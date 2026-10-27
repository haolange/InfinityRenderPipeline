using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class HiZPassUtilityData
    {
        internal static readonly int HistoryFurthest = Shader.PropertyToID("HistoryHiZFurthest");
        internal static readonly int RawDepth = Shader.PropertyToID("_HiZRawDepth");
        internal static readonly int SourceClosest = Shader.PropertyToID("_HiZSourceClosest");
        internal static readonly int SourceFurthest = Shader.PropertyToID("_HiZSourceFurthest");
        internal static readonly int SourceMip = Shader.PropertyToID("_HiZSourceMip");
        internal static readonly int SourceSize = Shader.PropertyToID("_HiZSourceSize");
        internal static readonly int DestinationSize = Shader.PropertyToID("_HiZDestinationSize");
        internal static readonly int RawSource = Shader.PropertyToID("_HiZRawSource");
        internal static readonly int WriteCount = Shader.PropertyToID("_HiZMipWriteCount");
        internal static readonly int[] Closest = { Shader.PropertyToID("_HiZClosest0"), Shader.PropertyToID("_HiZClosest1"), Shader.PropertyToID("_HiZClosest2"), Shader.PropertyToID("_HiZClosest3") };
        internal static readonly int[] Furthest = { Shader.PropertyToID("_HiZFurthest0"), Shader.PropertyToID("_HiZFurthest1"), Shader.PropertyToID("_HiZFurthest2"), Shader.PropertyToID("_HiZFurthest3") };
    }

    public partial class InfinityRenderPipeline
    {
        struct HiZPassData
        {
            public int mipCount, kernel;
            public int2 fullSize, baseSize;
            public ComputeShader shader;
            public RGTextureHandle depth, closest, furthest;
        }
        internal static TextureDescriptor HiZDescriptor(int width, int height, string name)
            => new TextureDescriptor((width + 1) / 2, (height + 1) / 2, ETextureSizingDomain.Internal, 2)
            {
                name = name, dimension = TextureDimension.Tex2D, colorFormat = GraphicsFormat.R32_SFloat,
                enableRandomWrite = true, useMipMap = true, autoGenerateMips = false,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };

        void ComputeHiZ(RenderContext renderContext, Camera camera)
        {
            if (!ShouldRecordFeature(EFrameFeature.HiZ)) return;
            var depth = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var size = m_RGBuilder.GetTextureDescriptor(depth);
            var closest = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.HiZClosestBuffer, HiZDescriptor(size.width, size.height, "HiZClosest"));
            var descriptor = HiZDescriptor(size.width, size.height, "HiZFurthest");
            var history = m_ActiveFrameState.historyCache;
            var furthest = m_RGBuilder.ImportTexture(history.GetWriteTexture(HiZPassUtilityData.HistoryFurthest, descriptor));
            m_RGScoper.RegisterTexture(InfinityShaderIDs.HiZFurthestBuffer, furthest);
            history.MarkProduced(HiZPassUtilityData.HistoryFurthest);
            RecordHiZ(depth, closest, furthest, size.width, size.height, true, ProfilingSampler.Get(CustomSamplerId.ComputeHiZ));
            MarkFeatureProduced(EFrameFeature.HiZ);
        }

        RGTextureHandle ComputeEarlyFurthestHiZ()
        {
            var depth = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var size = m_RGBuilder.GetTextureDescriptor(depth);
            var far = m_RGBuilder.CreateTexture(HiZDescriptor(size.width, size.height, "EarlyFurthestHiZ"));
            RecordHiZ(depth, default, far, size.width, size.height, false, ProfilingSampler.Get(CustomSamplerId.ComputeEarlyFurthestHiZ));
            return far;
        }

        void RecordHiZ(RGTextureHandle depth, RGTextureHandle closest, RGTextureHandle furthest, int width, int height, bool async, ProfilingSampler sampler)
        {
            var shader = shaders.hiZShader;
            string name = closest.IsValid() ? "HiZ_Generation" : "HiZ_FurthestGeneration";
            if (shader == null || !shader.HasKernel(name)) throw new InvalidOperationException("HiZ requires kernel " + name);
            using (var pass = m_RGBuilder.AddComputePass<HiZPassData>(sampler))
            {
                ref var data = ref pass.GetPassData<HiZPassData>(); data = default;
                data.shader = shader; data.kernel = shader.FindKernel(name);
                data.fullSize = new int2(width, height); data.baseSize = (data.fullSize + 1) / 2;
                data.mipCount = PyramidMipBatch.MipCount(data.baseSize.x, data.baseSize.y);
                data.depth = pass.ReadTexture(depth); data.furthest = pass.WriteTexture(furthest);
                if (closest.IsValid()) data.closest = pass.WriteTexture(closest);
                pass.EnableAsyncCompute(async);
                pass.SetExecuteFunc((in HiZPassData data, in RGComputeEncoder commands) =>
                {
                    for (int first = 0; first < data.mipCount; first += PyramidMipBatch.HiZMipsPerDispatch)
                    {
                        bool raw = first == 0;
                        int sourceMip = raw ? 0 : first - 1;
                        int2 source = raw ? data.fullSize : math.max(1, data.baseSize >> sourceMip);
                        int2 destination = math.max(1, data.baseSize >> first);
                        int writes = math.min(PyramidMipBatch.HiZMipsPerDispatch, data.mipCount - first);
                        commands.SetComputeIntParam(data.shader, HiZPassUtilityData.SourceMip, sourceMip);
                        commands.SetComputeIntParam(data.shader, HiZPassUtilityData.RawSource, raw ? 1 : 0);
                        commands.SetComputeIntParam(data.shader, HiZPassUtilityData.WriteCount, writes);
                        commands.SetComputeVectorParam(data.shader, HiZPassUtilityData.SourceSize, new Vector4(source.x, source.y, 1f / source.x, 1f / source.y));
                        commands.SetComputeVectorParam(data.shader, HiZPassUtilityData.DestinationSize, new Vector4(destination.x, destination.y, 1f / destination.x, 1f / destination.y));
                        commands.SetComputeTextureParam(data.shader, data.kernel, HiZPassUtilityData.RawDepth, data.depth);
                        commands.SetComputeTextureParam(data.shader, data.kernel, HiZPassUtilityData.SourceFurthest, raw ? data.depth : data.furthest);
                        if (data.closest.IsValid()) commands.SetComputeTextureParam(data.shader, data.kernel, HiZPassUtilityData.SourceClosest, raw ? data.depth : data.closest);
                        for (int i = 0; i < PyramidMipBatch.HiZMipsPerDispatch; ++i)
                        {
                            int mip = first + math.min(i, writes - 1);
                            commands.SetComputeTextureParam(data.shader, data.kernel, HiZPassUtilityData.Furthest[i], data.furthest, mip);
                            if (data.closest.IsValid()) commands.SetComputeTextureParam(data.shader, data.kernel, HiZPassUtilityData.Closest[i], data.closest, mip);
                        }
                        commands.DispatchCompute(data.shader, data.kernel, (destination.x + 7) / 8, (destination.y + 7) / 8, 1);
                    }
                });
            }
        }
    }
}
