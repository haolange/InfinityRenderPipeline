using UnityEngine;
using InfinityTech.Core;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Reflection;

namespace InfinityTech.Rendering.Pipeline
{
    internal struct ReflectionInputs
    {
        public RGBufferHandle probesHandle;
        public RGBufferHandle clusterHeadersHandle;
        public RGBufferHandle clusterIndicesHandle;
        public RGTextureHandle probeAtlasHandle;
        public bool hasProbeAtlas;

        public static ReflectionInputs Query(RGBuilder graph, in ViewContext context)
        {
            bool hasProbeAtlas = context.features.ShouldRecord(EFrameFeature.ReflectionProbes);
            RGTextureHandle probeAtlasHandle = hasProbeAtlas ? graph.scoper.QueryTexture(InfinityShaderIDs.ReflectionProbeAtlas) : default;
            return new ReflectionInputs
            {
                probesHandle = graph.scoper.QueryBuffer(ReflectionShaderIDs.ReflectionProbeBuffer),
                clusterHeadersHandle = graph.scoper.QueryBuffer(ReflectionShaderIDs.ProbeClusterHeaderBuffer),
                clusterIndicesHandle = graph.scoper.QueryBuffer(ReflectionShaderIDs.ProbeClusterIndexBuffer),
                probeAtlasHandle = probeAtlasHandle,
                hasProbeAtlas = hasProbeAtlas
            };
        }

        public void Read<THandle>(THandle passHandle) where THandle : struct, IPassHandle
        {
            passHandle.ReadBuffer(probesHandle);
            passHandle.ReadBuffer(clusterHeadersHandle);
            passHandle.ReadBuffer(clusterIndicesHandle);
            if (hasProbeAtlas)
            {
                passHandle.ReadTexture(probeAtlasHandle);
            }
        }

        public void Bind<TCommands>(in TCommands commands, ComputeShader shader, int kernel) where TCommands : struct, IComputeCommands
        {
            commands.SetKeyword(ReflectionShaderIDs.ReflectionProbesKeyword, hasProbeAtlas);
            commands.SetComputeBufferParam(shader, kernel, ReflectionShaderIDs.ReflectionProbes, probesHandle);
            commands.SetComputeBufferParam(shader, kernel, ReflectionShaderIDs.ProbeClusterHeaders, clusterHeadersHandle);
            commands.SetComputeBufferParam(shader, kernel, ReflectionShaderIDs.ProbeClusterIndices, clusterIndicesHandle);
            if (hasProbeAtlas)
            {
                commands.SetComputeTextureParam(shader, kernel, ReflectionShaderIDs.ReflectionProbeAtlas, probeAtlasHandle);
            }
        }

        public void BindGlobal<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
        {
            commands.SetKeyword(ReflectionShaderIDs.ReflectionProbesKeyword, hasProbeAtlas);
            commands.SetGlobalBuffer(ReflectionShaderIDs.ReflectionProbes, probesHandle);
            commands.SetGlobalBuffer(ReflectionShaderIDs.ProbeClusterHeaders, clusterHeadersHandle);
            commands.SetGlobalBuffer(ReflectionShaderIDs.ProbeClusterIndices, clusterIndicesHandle);
            if (hasProbeAtlas)
            {
                commands.SetGlobalTexture(ReflectionShaderIDs.ReflectionProbeAtlas, probeAtlasHandle);
            }
        }
    }

    internal static class ReflectionProbePassUtilityData
    {
        // Importance samples per texel of a probe mip; the source mips carry the low frequencies.
        internal const int FilterSampleCount = 32;
        internal static readonly int RasterReflectionResolutionID = Shader.PropertyToID("_RasterReflectionResolution");
    }

    internal sealed class ReflectionProbePass
    {
        static readonly ProfilingSampler s_UploadReflection = new ProfilingSampler("UploadReflectionData");

        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly ReflectionProbeAtlas m_ReflectionProbeAtlas;
        readonly ReflectionProbeProduction m_ReflectionProbeProduction;

        internal ReflectionProbePass(RGBuilder graph, InfinityRenderPipelineResources resources, ReflectionProbeAtlas reflectionProbeAtlas)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_ReflectionProbeAtlas = reflectionProbeAtlas;
            m_ReflectionProbeProduction = new ReflectionProbeProduction { atlas = reflectionProbeAtlas };
        }

        struct ReflectionUploadPassData
        {
            public ReflectionContext context;
        }

        struct ReflectionProbeFilterPassData
        {
            public ComputeKernel filterCubeToArray;
            public ProbeCubeUpload[] uploads;
            public int uploadCount;
            public int resolution;
            public int mipCount;
            public RGTextureHandle atlasHandle;
        }

        struct RasterReflectionPassData
        {
            public Vector4 resolution;
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle gBufferBHandle;
        }

        sealed class ReflectionProbeProduction : IRGPassQueueObserver
        {
            public ReflectionProbeAtlas atlas;
            public void OnQueueFailed() { }
            public void OnQueued() => atlas.MarkRecordedProduced();
        }

        internal void UploadReflectionData(ReflectionContext context)
        {
            RGTransferPassHandle<ReflectionUploadPassData> passHandle = m_RGBuilder.AddTransferPass<ReflectionUploadPassData>(s_UploadReflection);
            passHandle.GetPassData().context = context;
            m_RGBuilder.scoper.RegisterBuffer(ReflectionShaderIDs.ReflectionProbeBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.ProbeBuffer, "ReflectionProbeBuffer")));
            m_RGBuilder.scoper.RegisterBuffer(ReflectionShaderIDs.ProbeClusterHeaderBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.ClusterHeaderBuffer, "ProbeClusterHeaderBuffer")));
            m_RGBuilder.scoper.RegisterBuffer(ReflectionShaderIDs.ProbeClusterIndexBuffer, passHandle.WriteBuffer(m_RGBuilder.ImportBuffer(context.ClusterIndexBuffer, "ProbeClusterIndexBuffer")));
            passHandle.SetExecuteFunc((in ReflectionUploadPassData data, in RGTransferEncoder commands) => data.context.Upload(commands));
        }

        // Imports the probe atlas when probes reach this camera's screen and GGX-filters the cubes that are new, changed
        // or realtime (once per frame across cameras). Cluster headers/indices are uploaded by UploadReflectionData.
        internal void ComputeReflectionProbes(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.ReflectionProbes))
            {
                return;
            }
            context.features.ThrowIfCannotProduce(EFrameFeature.ReflectionProbes);

            RGTextureHandle atlasHandle = m_RGBuilder.ImportTexture(m_ReflectionProbeAtlas.GetAtlas());
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.ReflectionProbeAtlas, atlasHandle);

            if (m_ReflectionProbeAtlas.TakeUploads(out ProbeCubeUpload[] uploads, out int uploadCount))
            {
                RGComputePassHandle<ReflectionProbeFilterPassData> passHandle = m_RGBuilder.AddComputePass<ReflectionProbeFilterPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeReflectionProbes));
                ref ReflectionProbeFilterPassData passData = ref passHandle.GetPassData();
                passData.filterCubeToArray = m_Resources.kernels.filterCubeToArray;
                passData.uploads = uploads;
                passData.uploadCount = uploadCount;
                passData.resolution = m_ReflectionProbeAtlas.Resolution;
                passData.mipCount = m_ReflectionProbeAtlas.MipCount;
                passData.atlasHandle = passHandle.WriteTexture(atlasHandle);
                passHandle.EnablePassCulling(false);
                passHandle.SetQueueObserver(m_ReflectionProbeProduction);
                passHandle.SetExecuteFunc((in ReflectionProbeFilterPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    int count = passData.uploadCount;
                    ProbeCubeUpload[] recorded = passData.uploads;
                    for (int i = 0; i < count; ++i)
                    {
                        ProbeCubeUpload upload = recorded[i];
                        if (upload.source == null)
                        {
                            continue;
                        }
                        // Intensity and HDR decode stay in the record (params.x), so a changed intensity needs no refilter.
                        for (int mip = 0; mip < passData.mipCount; ++mip)
                        {
                            CubeFilterUtilityData.FilterCubeToArray(cmdEncoder, passData.filterCubeToArray, upload.source, 1.0f, passData.atlasHandle,
                                passData.resolution, mip, CubeFilterUtilityData.MipRoughness(mip, passData.mipCount), upload.slot * 6,
                                ReflectionProbePassUtilityData.FilterSampleCount);
                        }
                    }
                });
            }

            context.features.MarkProduced(EFrameFeature.ReflectionProbes);
        }

        // Raster reflection path: executes only when programmable RasterReflectionVolumes are active in the scene.
        internal void RasterReflections(in ViewContext context)
        {
            if (context.reflections.RasterReflectionCount == 0 || !context.features.ShouldRecord(EFrameFeature.ReflectionProbes))
            {
                return;
            }

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            var rasterDescriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            {
                name = "RasterReflectionTexture",
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                clearColor = Color.clear
            };
            RGTextureHandle rasterReflectionHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.RasterReflectionBuffer, rasterDescriptor);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);

            RGRendererListHandle rendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(
                context.cullingResults, context.camera, InfinityPassIDs.RasterReflectionPass,
                new RenderQueueRange(2000, 2999), SortingCriteria.RendererPriority | SortingCriteria.CommonOpaque,
                PerObjectData.None, false));

            {
                RGRasterPassHandle<RasterReflectionPassData> passHandle = m_RGBuilder.AddRasterPass<RasterReflectionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterReflections));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(rasterReflectionHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref RasterReflectionPassData passData = ref passHandle.GetPassData();
                passData.resolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
                passData.rendererListHandle = passHandle.UseRendererList(rendererListHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);

                passHandle.SetExecuteFunc((in RasterReflectionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalVector(ReflectionProbePassUtilityData.RasterReflectionResolutionID, passData.resolution);
                    cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }
        }
    }
}
