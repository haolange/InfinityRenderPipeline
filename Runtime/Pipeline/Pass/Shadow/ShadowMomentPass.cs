using UnityEngine;
using InfinityTech.Core;
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
        internal const string CascadeTextureName = "CascadeShadowMoments";
        internal const string LocalTextureName = "LocalShadowMoments";
        internal const string CascadeBlurTextureName = CascadeTextureName + "BlurTemp";
        internal const string LocalBlurTextureName = LocalTextureName + "BlurTemp";
        // Smallest moment standard deviation in depth units (_ShadowMomentParams.z); hides acne on flat receivers.
        internal const float MinMomentDeviation = 0.001f;
        internal static readonly int MomentSliceRectID = Shader.PropertyToID("_MomentSliceRect");
        internal static readonly int MomentDepthParamsID = Shader.PropertyToID("_MomentDepthParams");
        internal static readonly int MomentParamsID = Shader.PropertyToID("_MomentParams");
        internal static readonly int SRV_ShadowDepthID = Shader.PropertyToID("SRV_ShadowDepth");
        internal static readonly int SRV_MomentInputID = Shader.PropertyToID("SRV_MomentInput");
        internal static readonly int UAV_MomentsID = Shader.PropertyToID("UAV_Moments");

        // _ShadowMomentParams of ShadowSampling.hlsl.
        internal static Vector4 SamplingParams(in ShadowFilterSettings settings) =>
            new Vector4(settings.evsmExponent, settings.lightBleedingReduction, MinMomentDeviation, 0);

        internal static int Mode(int shadowFilter) => shadowFilter == (int)EShadowFilter.EVSM ? 1 : 0;
    }

    internal sealed class ShadowMomentPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;

        internal ShadowMomentPass(RGBuilder graph, InfinityRenderPipelineResources resources, in InfinityRenderPipelineAsset.Settings settings)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_Settings = settings;
        }

        struct ShadowMomentPassData
        {
            public ComputeKernel fromDepth, blurH, blurV;
            public int sliceCount;
            // Per slice: rect in moment pixels, depth params, (mode, exponent, 1 / far, blur radius).
            public Vector4[] rects;
            public Vector4[] depthParams;
            public Vector4[] momentParams;
            public RGTextureHandle depthHandle;
            public RGTextureHandle momentsHandle;
            public RGTextureHandle blurTempHandle;
        }

        // Builds the VSM / EVSM moment atlases for the slices whose light needs them. Each atlas has half the resolution
        // of its depth atlas and the same UV layout; it exists only on frames where a visible shadowed light uses it.
        internal void ComputeShadowMoments(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.ShadowMoments))
            {
                return;
            }
            // A visible VSM / EVSM light makes the moments a required producer: a blocked feature fails at record time.
            context.features.ThrowIfCannotProduce(EFrameFeature.ShadowMoments);

            ShadowAllocator allocator = context.lights.ShadowAllocator;
            ShadowFilterSettings filter = m_Settings.shadowFilter;

            if (allocator.CascadeUsesMoments)
            {
                int mode = ShadowMomentPassUtilityData.Mode(allocator.CascadeShadowFilter);
                var rects = m_RGBuilder.GetRecordingArray<Vector4>(ShadowAllocator.CascadeCount);
                var depthParams = m_RGBuilder.GetRecordingArray<Vector4>(ShadowAllocator.CascadeCount);
                var momentParams = m_RGBuilder.GetRecordingArray<Vector4>(ShadowAllocator.CascadeCount);
                int count = 0;
                for (int cascade = 0; cascade < ShadowAllocator.CascadeCount; ++cascade)
                {
                    CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }
                    Vector4 cascadeParams = allocator.CascadeParams[cascade];
                    rects[count] = slice.atlasPixelRect * 0.5f;
                    depthParams[count] = new Vector4(cascadeParams.y, cascadeParams.z, cascadeParams.x, 1);
                    momentParams[count] = new Vector4(mode, filter.evsmExponent, 1, filter.momentBlurRadius);
                    count++;
                }
                int atlasSize = math.max(1, m_Settings.cascadeShadowMapResolution);
                AddShadowMomentPass(InfinityShaderIDs.CascadeShadowMap, InfinityShaderIDs.CascadeShadowMoments, ShadowMomentPassUtilityData.CascadeTextureName,
                    ShadowMomentPassUtilityData.CascadeBlurTextureName, atlasSize, count, rects, depthParams, momentParams);
            }

            if (allocator.LocalMomentSliceCount > 0)
            {
                int capacity = allocator.LocalMomentSliceCount;
                var rects = m_RGBuilder.GetRecordingArray<Vector4>(capacity);
                var depthParams = m_RGBuilder.GetRecordingArray<Vector4>(capacity);
                var momentParams = m_RGBuilder.GetRecordingArray<Vector4>(capacity);
                int count = 0;
                for (int i = 0; i < allocator.LocalSliceCount && count < capacity; ++i)
                {
                    LocalShadowSlice slice = allocator.LocalSlices[i];
                    if (!slice.valid || !ShadowAllocator.UsesMoments(slice.shadowFilter))
                    {
                        continue;
                    }
                    LocalShadowSliceGPU gpu = allocator.LocalGPU[i];
                    rects[count] = slice.atlasPixelRect * 0.5f;
                    depthParams[count] = gpu.depthParams;
                    momentParams[count] = new Vector4(ShadowMomentPassUtilityData.Mode(slice.shadowFilter), filter.evsmExponent, gpu.momentParams.x, filter.momentBlurRadius);
                    count++;
                }
                int atlasSize = math.max(1, ShadowAllocator.ResolveLocalAtlasResolution(m_Settings.localShadowMapResolution) / 2);
                AddShadowMomentPass(InfinityShaderIDs.LocalShadowMap, InfinityShaderIDs.LocalShadowMoments, ShadowMomentPassUtilityData.LocalTextureName,
                    ShadowMomentPassUtilityData.LocalBlurTextureName, atlasSize, count, rects, depthParams, momentParams);
            }

            context.features.MarkProduced(EFrameFeature.ShadowMoments);
        }

        void AddShadowMomentPass(RGResourceKey depthKey, RGResourceKey momentKey, string name, string blurName, int atlasSize, int sliceCount,
            Vector4[] rects, Vector4[] depthParams, Vector4[] momentParams)
        {
            var descriptor = new TextureDescriptor(atlasSize, atlasSize)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthStencilFormat = GraphicsFormat.None,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            RGTextureHandle momentsHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(momentKey, descriptor);

            {
                RGComputePassHandle<ShadowMomentPassData> passHandle = m_RGBuilder.AddComputePass<ShadowMomentPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeShadowMoments));
                ref ShadowMomentPassData passData = ref passHandle.GetPassData();
                passData.fromDepth = m_Resources.kernels.momentFromDepth;
                passData.blurH = m_Resources.kernels.momentBlurH;
                passData.blurV = m_Resources.kernels.momentBlurV;
                passData.sliceCount = sliceCount;
                passData.rects = rects;
                passData.depthParams = depthParams;
                passData.momentParams = momentParams;
                passData.depthHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(depthKey));
                passData.momentsHandle = passHandle.WriteTexture(momentsHandle);
                descriptor.name = blurName;
                passData.blurTempHandle = passHandle.CreatePassLocalTexture(descriptor);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in ShadowMomentPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeKernel fromDepth = passData.fromDepth;
                    ComputeKernel blurH = passData.blurH;
                    ComputeKernel blurV = passData.blurV;
                    ComputeShader shader = fromDepth.shader;
                    for (int i = 0; i < passData.sliceCount; ++i)
                    {
                        Vector4 rect = passData.rects[i];
                        cmdEncoder.SetComputeVectorParam(shader, ShadowMomentPassUtilityData.MomentSliceRectID, rect);
                        cmdEncoder.SetComputeVectorParam(shader, ShadowMomentPassUtilityData.MomentDepthParamsID, passData.depthParams[i]);
                        cmdEncoder.SetComputeVectorParam(shader, ShadowMomentPassUtilityData.MomentParamsID, passData.momentParams[i]);

                        cmdEncoder.SetComputeTextureParam(shader, fromDepth.index, ShadowMomentPassUtilityData.SRV_ShadowDepthID, passData.depthHandle);
                        cmdEncoder.SetComputeTextureParam(shader, fromDepth.index, ShadowMomentPassUtilityData.UAV_MomentsID, passData.momentsHandle);
                        cmdEncoder.DispatchCompute(shader, fromDepth.index, fromDepth.GroupsX((int)rect.z), fromDepth.GroupsY((int)rect.w), 1);

                        if (passData.momentParams[i].w < 1)
                        {
                            continue;
                        }
                        cmdEncoder.SetComputeTextureParam(shader, blurH.index, ShadowMomentPassUtilityData.SRV_MomentInputID, passData.momentsHandle);
                        cmdEncoder.SetComputeTextureParam(shader, blurH.index, ShadowMomentPassUtilityData.UAV_MomentsID, passData.blurTempHandle);
                        cmdEncoder.DispatchCompute(shader, blurH.index, blurH.GroupsX((int)rect.z), blurH.GroupsY((int)rect.w), 1);
                        cmdEncoder.SetComputeTextureParam(shader, blurV.index, ShadowMomentPassUtilityData.SRV_MomentInputID, passData.blurTempHandle);
                        cmdEncoder.SetComputeTextureParam(shader, blurV.index, ShadowMomentPassUtilityData.UAV_MomentsID, passData.momentsHandle);
                        cmdEncoder.DispatchCompute(shader, blurV.index, blurV.GroupsX((int)rect.z), blurV.GroupsY((int)rect.w), 1);
                    }
                });
            }
        }
    }
}
