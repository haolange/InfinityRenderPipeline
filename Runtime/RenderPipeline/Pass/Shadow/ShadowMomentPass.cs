using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ShadowMomentPassUtilityData
    {
        internal const string KernelFromDepth = "MomentFromDepth";
        internal const string KernelBlurH = "MomentBlurH";
        internal const string KernelBlurV = "MomentBlurV";
        internal const string CascadeTextureName = "CascadeShadowMoments";
        internal const string LocalTextureName = "LocalShadowMoments";
        // Smallest moment standard deviation in depth units (_ShadowMomentParams.z); hides acne on flat receivers.
        internal const float MinMomentDeviation = 0.001f;
        internal static readonly int MomentSliceRectID = Shader.PropertyToID("_MomentSliceRect");
        internal static readonly int MomentDepthParamsID = Shader.PropertyToID("_MomentDepthParams");
        internal static readonly int MomentParamsID = Shader.PropertyToID("_MomentParams");
        internal static readonly int SRV_ShadowDepthID = Shader.PropertyToID("SRV_ShadowDepth");
        internal static readonly int SRV_MomentInputID = Shader.PropertyToID("SRV_MomentInput");
        internal static readonly int UAV_MomentsID = Shader.PropertyToID("UAV_Moments");

        // _ShadowMomentParams of ShadowSampling.hlsl.
        internal static Vector4 SamplingParams(in FShadowFilterSettings settings) =>
            new Vector4(settings.evsmExponent, settings.lightBleedingReduction, MinMomentDeviation, 0);

        internal static int Mode(int shadowFilter) => shadowFilter == (int)EShadowFilter.EVSM ? 1 : 0;
    }

    public partial class InfinityRenderPipeline
    {
        struct ShadowMomentPassData
        {
            public ComputeShader shader;
            public int sliceCount;
            // Per slice: rect in moment pixels, depth params, (mode, exponent, 1 / far, blur radius).
            public Vector4[] rects;
            public Vector4[] depthParams;
            public Vector4[] momentParams;
            public RGTextureRef depth;
            public RGTextureRef moments;
            public RGTextureRef blurTemp;
        }

        // Builds the VSM / EVSM moment atlases for the slices whose light needs them. Each atlas has half the resolution
        // of its depth atlas and the same UV layout; it exists only on frames where a visible shadowed light uses it.
        void ComputeShadowMoments(RenderContext renderContext)
        {
            if (!ShouldRecordFeature(EFrameFeature.ShadowMoments))
            {
                return;
            }
            // A visible VSM / EVSM light makes the moments a required producer: a blocked feature fails at record time.
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.ShadowMoments);

            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            FShadowFilterSettings filter = pipelineAsset.ResolveShadowFilter();

            if (allocator.CascadeUsesMoments)
            {
                int mode = ShadowMomentPassUtilityData.Mode(allocator.CascadeShadowFilter);
                var rects = new Vector4[ShadowAllocator.CascadeCount];
                var depthParams = new Vector4[ShadowAllocator.CascadeCount];
                var momentParams = new Vector4[ShadowAllocator.CascadeCount];
                int count = 0;
                for (int cascade = 0; cascade < ShadowAllocator.CascadeCount; ++cascade)
                {
                    FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid) continue;
                    Vector4 cascadeParams = allocator.CascadeParams[cascade];
                    rects[count] = slice.atlasPixelRect * 0.5f;
                    depthParams[count] = new Vector4(cascadeParams.y, cascadeParams.z, cascadeParams.x, 1);
                    momentParams[count] = new Vector4(mode, filter.evsmExponent, 1, filter.momentBlurRadius);
                    count++;
                }
                int atlasSize = math.max(1, pipelineAsset.cascadeShadowMapResolution);
                AddShadowMomentPass(InfinityShaderIDs.CascadeShadowMap, InfinityShaderIDs.CascadeShadowMoments, ShadowMomentPassUtilityData.CascadeTextureName,
                    atlasSize, count, rects, depthParams, momentParams);
            }

            if (allocator.LocalMomentSliceCount > 0)
            {
                int capacity = allocator.LocalMomentSliceCount;
                var rects = new Vector4[capacity];
                var depthParams = new Vector4[capacity];
                var momentParams = new Vector4[capacity];
                int count = 0;
                for (int i = 0; i < allocator.LocalSliceCount && count < capacity; ++i)
                {
                    FLocalShadowSlice slice = allocator.LocalSlices[i];
                    if (!slice.valid || !ShadowAllocator.UsesMoments(slice.shadowFilter)) continue;
                    FLocalShadowSliceGPU gpu = allocator.LocalGPU[i];
                    rects[count] = slice.atlasPixelRect * 0.5f;
                    depthParams[count] = gpu.depthParams;
                    momentParams[count] = new Vector4(ShadowMomentPassUtilityData.Mode(slice.shadowFilter), filter.evsmExponent, gpu.momentParams.x, filter.momentBlurRadius);
                    count++;
                }
                int atlasSize = math.max(1, pipelineAsset.localShadowMapResolution / 2);
                AddShadowMomentPass(InfinityShaderIDs.LocalShadowMap, InfinityShaderIDs.LocalShadowMoments, ShadowMomentPassUtilityData.LocalTextureName,
                    atlasSize, count, rects, depthParams, momentParams);
            }

            MarkFeatureProduced(EFrameFeature.ShadowMoments);
        }

        void AddShadowMomentPass(RGResourceKey depthKey, RGResourceKey momentKey, string name, int atlasSize, int sliceCount,
            Vector4[] rects, Vector4[] depthParams, Vector4[] momentParams)
        {
            var descriptor = new TextureDescriptor(atlasSize, atlasSize)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = EDepthBits.None,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            RGTextureRef moments = m_RGScoper.CreateAndRegisterTexture(momentKey, descriptor);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ShadowMomentPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeShadowMoments)))
            {
                ref ShadowMomentPassData passData = ref passRef.GetPassData<ShadowMomentPassData>();
                passData.shader = shaders.shadowMomentShader;
                passData.sliceCount = sliceCount;
                passData.rects = rects;
                passData.depthParams = depthParams;
                passData.momentParams = momentParams;
                passData.depth = passRef.ReadTexture(m_RGScoper.QueryTexture(depthKey));
                passData.moments = passRef.WriteTexture(moments);
                descriptor.name = name + "BlurTemp";
                passData.blurTemp = passRef.CreateTemporaryTexture(descriptor);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ShadowMomentPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.shader;
                    int fromDepth = shader.FindKernel(ShadowMomentPassUtilityData.KernelFromDepth);
                    int blurH = shader.FindKernel(ShadowMomentPassUtilityData.KernelBlurH);
                    int blurV = shader.FindKernel(ShadowMomentPassUtilityData.KernelBlurV);
                    for (int i = 0; i < passData.sliceCount; ++i)
                    {
                        Vector4 rect = passData.rects[i];
                        int groupsX = ((int)rect.z + 7) / 8;
                        int groupsY = ((int)rect.w + 7) / 8;
                        cmdEncoder.SetComputeVectorParam(shader, ShadowMomentPassUtilityData.MomentSliceRectID, rect);
                        cmdEncoder.SetComputeVectorParam(shader, ShadowMomentPassUtilityData.MomentDepthParamsID, passData.depthParams[i]);
                        cmdEncoder.SetComputeVectorParam(shader, ShadowMomentPassUtilityData.MomentParamsID, passData.momentParams[i]);

                        cmdEncoder.SetComputeTextureParam(shader, fromDepth, ShadowMomentPassUtilityData.SRV_ShadowDepthID, passData.depth);
                        cmdEncoder.SetComputeTextureParam(shader, fromDepth, ShadowMomentPassUtilityData.UAV_MomentsID, passData.moments);
                        cmdEncoder.DispatchCompute(shader, fromDepth, groupsX, groupsY, 1);

                        if (passData.momentParams[i].w < 1)
                        {
                            continue;
                        }
                        cmdEncoder.SetComputeTextureParam(shader, blurH, ShadowMomentPassUtilityData.SRV_MomentInputID, passData.moments);
                        cmdEncoder.SetComputeTextureParam(shader, blurH, ShadowMomentPassUtilityData.UAV_MomentsID, passData.blurTemp);
                        cmdEncoder.DispatchCompute(shader, blurH, groupsX, groupsY, 1);
                        cmdEncoder.SetComputeTextureParam(shader, blurV, ShadowMomentPassUtilityData.SRV_MomentInputID, passData.blurTemp);
                        cmdEncoder.SetComputeTextureParam(shader, blurV, ShadowMomentPassUtilityData.UAV_MomentsID, passData.moments);
                        cmdEncoder.DispatchCompute(shader, blurV, groupsX, groupsY, 1);
                    }
                });
            }
        }
    }
}
