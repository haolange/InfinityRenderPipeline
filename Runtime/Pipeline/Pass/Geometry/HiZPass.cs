using System;
using UnityEngine;
using InfinityTech.Core;
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

    internal sealed class HiZPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal HiZPass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        struct HiZPassData
        {
            public int mipCount;
            public int2 fullSize, baseSize;
            public ComputeKernel kernel;
            public RGTextureHandle depthHandle, closestHandle, furthestHandle;
        }
        internal static TextureDescriptor HiZDescriptor(int width, int height, string name)
        {
            var descriptor = TextureDescriptor.Create2D(name, (width + 1) / 2, (height + 1) / 2, GraphicsFormat.R32_SFloat, ETextureSizingDomain.Internal, 2);
            descriptor.useMipMap = true;
            descriptor.filterMode = FilterMode.Point;
            return descriptor;
        }

        internal void ComputeHiZ(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.HiZ))
            {
                return;
            }
            var depth = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var size = m_RGBuilder.GetTextureDescriptor(depth);
            var closest = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.HiZClosestBuffer, HiZDescriptor(size.width, size.height, "HiZClosest"));
            var descriptor = HiZDescriptor(size.width, size.height, "HiZFurthest");
            var history = context.historyCache;
            var furthest = m_RGBuilder.ImportTexture(history.GetWriteTexture(HiZPassUtilityData.HistoryFurthest, descriptor));
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.HiZFurthestBuffer, furthest);
            history.MarkProduced(HiZPassUtilityData.HistoryFurthest);
            RecordHiZ(depth, closest, furthest, size.width, size.height, true, ProfilingSampler.Get(CustomSamplerId.ComputeHiZ));
            context.features.MarkProduced(EFrameFeature.HiZ);
        }

        internal RGTextureHandle ComputeEarlyFurthestHiZ()
        {
            var depth = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var size = m_RGBuilder.GetTextureDescriptor(depth);
            var far = m_RGBuilder.CreateTexture(HiZDescriptor(size.width, size.height, "EarlyFurthestHiZ"));
            RecordHiZ(depth, default, far, size.width, size.height, false, ProfilingSampler.Get(CustomSamplerId.ComputeEarlyFurthestHiZ));
            return far;
        }

        void RecordHiZ(RGTextureHandle depthHandle, RGTextureHandle closestHandle, RGTextureHandle furthestHandle, int width, int height, bool async, ProfilingSampler sampler)
        {
            ComputeKernel kernel = closestHandle.IsValid() ? m_Resources.kernels.hiZClosest : m_Resources.kernels.hiZFurthest;
            {
                RGComputePassHandle<HiZPassData> passHandle = m_RGBuilder.AddComputePass<HiZPassData>(sampler);
                ref HiZPassData passData = ref passHandle.GetPassData();
                passData = default;
                passData.kernel = kernel;
                passData.fullSize = new int2(width, height);
                passData.baseSize = (passData.fullSize + 1) / 2;
                passData.mipCount = PyramidMipBatch.MipCount(passData.baseSize.x, passData.baseSize.y);
                passData.depthHandle = passHandle.ReadTexture(depthHandle);
                passData.furthestHandle = passHandle.WriteTexture(furthestHandle);
                if (closestHandle.IsValid())
                {
                    passData.closestHandle = passHandle.WriteTexture(closestHandle);
                }
                passHandle.EnableAsyncCompute(async);
                passHandle.SetExecuteFunc((in HiZPassData passData, in RGComputeEncoder commands) =>
                {
                    for (int first = 0; first < passData.mipCount; first += PyramidMipBatch.HiZMipsPerDispatch)
                    {
                        bool raw = first == 0;
                        int sourceMip = raw ? 0 : first - 1;
                        int2 source = raw ? passData.fullSize : math.max(1, passData.baseSize >> sourceMip);
                        int2 destination = math.max(1, passData.baseSize >> first);
                        int writes = math.min(PyramidMipBatch.HiZMipsPerDispatch, passData.mipCount - first);
                        commands.SetComputeIntParam(passData.kernel.shader, HiZPassUtilityData.SourceMip, sourceMip);
                        commands.SetComputeIntParam(passData.kernel.shader, HiZPassUtilityData.RawSource, raw ? 1 : 0);
                        commands.SetComputeIntParam(passData.kernel.shader, HiZPassUtilityData.WriteCount, writes);
                        commands.SetComputeVectorParam(passData.kernel.shader, HiZPassUtilityData.SourceSize, new Vector4(source.x, source.y, 1f / source.x, 1f / source.y));
                        commands.SetComputeVectorParam(passData.kernel.shader, HiZPassUtilityData.DestinationSize, new Vector4(destination.x, destination.y, 1f / destination.x, 1f / destination.y));
                        commands.SetComputeTextureParam(passData.kernel.shader, passData.kernel.index, HiZPassUtilityData.RawDepth, passData.depthHandle);
                        commands.SetComputeTextureParam(passData.kernel.shader, passData.kernel.index, HiZPassUtilityData.SourceFurthest, raw ? passData.depthHandle : passData.furthestHandle);
                        if (passData.closestHandle.IsValid())
                        {
                            commands.SetComputeTextureParam(passData.kernel.shader, passData.kernel.index, HiZPassUtilityData.SourceClosest, raw ? passData.depthHandle : passData.closestHandle);
                        }
                        for (int i = 0; i < PyramidMipBatch.HiZMipsPerDispatch; ++i)
                        {
                            int mip = first + math.min(i, writes - 1);
                            commands.SetComputeTextureParam(passData.kernel.shader, passData.kernel.index, HiZPassUtilityData.Furthest[i], passData.furthestHandle, mip);
                            if (passData.closestHandle.IsValid())
                            {
                                commands.SetComputeTextureParam(passData.kernel.shader, passData.kernel.index, HiZPassUtilityData.Closest[i], passData.closestHandle, mip);
                            }
                        }
                        commands.DispatchCompute(passData.kernel.shader, passData.kernel.index, passData.kernel.GroupsX(destination.x), passData.kernel.GroupsY(destination.y), 1);
                    }
                });
            }
        }
    }
}
