#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class RenderCapturePassUtilityData
    {
        internal static readonly int CaptureBufferSizeID = Shader.PropertyToID("CaptureBufferSize");
        internal static readonly int CaptureSizeID = Shader.PropertyToID("CaptureSize");
        internal static readonly int Capture_LightTileMaskID = Shader.PropertyToID("Capture_LightTileMask");
        internal static readonly int Capture_SSGITraceRadianceID = Shader.PropertyToID("Capture_SSGITraceRadiance");
        internal static readonly int SRV_CaptureID = Shader.PropertyToID("SRV_Capture");
        internal static readonly int SRV_CaptureMetadataID = Shader.PropertyToID("SRV_CaptureMetadata");
        internal static readonly int SRV_CaptureUIntID = Shader.PropertyToID("SRV_CaptureUInt");
        internal static readonly int SRV_DepthID = Shader.PropertyToID("SRV_Depth");
        internal static readonly int UAV_CaptureID = Shader.PropertyToID("UAV_Capture");
        internal static readonly int UAV_CaptureUIntBytesID = Shader.PropertyToID("UAV_CaptureUIntBytes");
        internal static readonly int UAV_DepthID = Shader.PropertyToID("UAV_Depth");
    }

    public partial class InfinityRenderPipeline
    {
        struct CaptureStageSnapshot
        {
            public bool enabled;
            public ComputeKernel kernel;
            public RGTextureHandle texture;
            public int width, height;
            public void Record(in RGComputeEncoder commands, RGTextureHandle source)
            {
                if (!enabled)
                {
                    return;
                }
                ComputeShader shader = kernel.shader;
                commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureSizeID, new Vector4(width, height, 0, 0));
                commands.SetComputeTextureParam(shader, kernel.index, RenderCapturePassUtilityData.SRV_CaptureID, source);
                commands.SetComputeTextureParam(shader, kernel.index, RenderCapturePassUtilityData.UAV_CaptureID, texture);
                commands.DispatchCompute(shader, kernel.index, kernel.GroupsX(width), kernel.GroupsY(height), 1);
            }
        }

        CaptureStageSnapshot PrepareStageSnapshot<TPassData>(RGComputePassHandle<TPassData> pass, string semantic, RGTextureHandle source) where TPassData : struct
        {
            var session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || Array.IndexOf(session.request.buffers, semantic) < 0)
            {
                return default;
            }
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.texture);
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            descriptor.name = "Capture_" + semantic;
            descriptor.enableRandomWrite = true;
            var snapshot = m_RGScoper.CreateAndRegisterTexture(Shader.PropertyToID(descriptor.name), descriptor);
            return new CaptureStageSnapshot { enabled = true, kernel = kernel, width = descriptor.width, height = descriptor.height, texture = pass.WriteTexture(snapshot) };
        }

        struct CaptureTransferPassData
        {
            public RGTextureHandle source, destination;
            public RenderCaptureSession.Staging staging;
            public RGBuilder graph;
            public int sourceMip, width, height;
        }
        static readonly ProfilingSampler s_CaptureLightMaskSampler = new ProfilingSampler("ComputeCaptureLightMask");
        static readonly ProfilingSampler s_CaptureMetadataSampler = new ProfilingSampler("ComputeCaptureSurfaceMetadata");
        static readonly ProfilingSampler s_CaptureDepthSampler = new ProfilingSampler("ComputeCaptureDepth");
        static readonly ProfilingSampler s_CaptureTransferSampler = new ProfilingSampler("CopyCaptureNormalFrame");

        void RecordLightingCapture()
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || Array.IndexOf(session.request.buffers, "Lighting") < 0)
            {
                return;
            }
            // Capture the final opaque lighting before ownership moves to scene-color stages.
            RecordCaptureTransfer(session, "Lighting", InfinityShaderIDs.OpaqueSceneColorBuffer);
        }

        void RecordDeferredLightingCapture()
        {
            var session = RenderCaptureService.current;
            if (session != null && session.CaptureThisFrame && Array.IndexOf(session.request.buffers, "DeferredLighting") >= 0)
                RecordCaptureTransfer(session, "DeferredLighting", InfinityShaderIDs.LightingBuffer);
        }

        struct CaptureLightMaskPassData
        {
            public RGBufferHandle source;
            public RGTextureHandle destination;
            public ComputeKernel kernel;
            public int width, height;
        }

        int RecordLightMaskCapture()
        {
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.bufferUIntBytes);
            var context = renderContext.lightContext;
            bool local = context.HasLocalLightList();
            int width = local ? context.TileCount.x * context.TileWordCount : 1;
            int height = local ? context.TileCount.y : 1;
            int id = RenderCapturePassUtilityData.Capture_LightTileMaskID;
            var descriptor = new TextureDescriptor(width, height)
            {
                name = "Capture_LightTileMask", dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R32G32B32A32_SFloat, enableRandomWrite = true
            };
            var destination = m_RGScoper.CreateAndRegisterTexture(id, descriptor);
            using (var pass = m_RGBuilder.AddComputePass<CaptureLightMaskPassData>(s_CaptureLightMaskSampler))
            {
                ref var data = ref pass.GetPassData();
                data.source = pass.ReadBuffer(QueryLightLoop(renderContext).tileMasks);
                data.destination = pass.WriteTexture(destination);
                data.kernel = kernel;
                data.width = width;
                data.height = height;
                pass.SetExecuteFunc((in CaptureLightMaskPassData data, in RGComputeEncoder commands) =>
                {
                    ComputeShader shader = data.kernel.shader;
                    int kernel = data.kernel.index;
                    commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureBufferSizeID, new Vector4(data.width, data.height, 0, 0));
                    commands.SetComputeBufferParam(shader, kernel, RenderCapturePassUtilityData.SRV_CaptureUIntID, data.source);
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.UAV_CaptureUIntBytesID, data.destination);
                    commands.DispatchCompute(shader, kernel, data.kernel.GroupsX(data.width), data.kernel.GroupsY(data.height), 1);
                });
            }
            return id;
        }

        void RecordNormalFrameCapture(Camera camera)
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame)
            {
                return;
            }
            if (Array.IndexOf(session.request.buffers, "CascadeShadow") >= 0)
            {
                session.RecordShadowState(renderContext.lightContext.ShadowAllocator);
            }
            foreach (string buffer in session.request.buffers)
            {
                if (buffer == "Lighting" || buffer == "DeferredLighting")
                {
                    continue;
                }
                if (buffer == "HiZClosest" || buffer == "HiZFurthest" || buffer == "TemporalColorMips")
                {
                    var key = buffer == "TemporalColorMips" ? InfinityShaderIDs.TemporalColorMipBuffer :
                        buffer == "HiZClosest" ? InfinityShaderIDs.HiZClosestBuffer : InfinityShaderIDs.HiZFurthestBuffer;
                    var descriptor = m_RGBuilder.GetTextureDescriptor(m_RGScoper.QueryTexture(key));
                    for (int mip = 0; mip < PyramidMipBatch.MipCount(descriptor.width, descriptor.height); mip++)
                        RecordCaptureTransfer(session, buffer + "_Mip" + mip, key, mip);
                    continue;
                }
                int id;
                switch (buffer)
                {
                    case "TemporalColorSource": case "TemporalColorDepth":
                        if (!m_ActiveFrameState.temporalColorView.valid)
                            throw new InvalidOperationException("Temporal color source capture requires an accepted history.");
                        id = buffer == "TemporalColorSource" ? InfinityShaderIDs.TemporalColorSourceBuffer : InfinityShaderIDs.TemporalColorDepthBuffer;
                        break;
                    case "LightTileMask":
                        id = RecordLightMaskCapture();
                        break;
                    case "TAAInput": case "TAAHistoryColor": case "TAAHistoryDepth":
                    case "AOTrace": case "AOSpatialX": case "AOSpatialY": case "AOTemporal": id = Shader.PropertyToID("Capture_" + buffer);
                    break;
                    case "BakedDiffuse":
                        id = InfinityShaderIDs.BakedDiffuseBuffer;
                        break;
                    case "BakedOcclusion":
                        id = InfinityShaderIDs.BakedOcclusionBuffer;
                        break;
                    case "IndirectDiffuse":
                        id = InfinityShaderIDs.IndirectDiffuseBuffer;
                        break;
                    case "IndirectSpecular":
                        id = InfinityShaderIDs.IndirectSpecularBuffer;
                        break;
                    case "HalfResDepth":
                        id = InfinityShaderIDs.HalfResDepthBuffer;
                        break;
                    case "HalfResNormal":
                        id = InfinityShaderIDs.HalfResNormalBuffer;
                        break;
                    case "VolumetricCloud":
                        id = InfinityShaderIDs.VolumetricCloudBuffer;
                        break;
                    case "ContactShadowSun":
                        id = InfinityShaderIDs.ContactShadowBuffer;
                        break;
                    case "ContactShadowMask":
                        id = InfinityShaderIDs.ContactShadowMaskBuffer;
                        break;
                    case "CascadeShadow":
                        id = InfinityShaderIDs.CascadeShadowMap;
                        break;
                    case "LocalShadow":
                        id = InfinityShaderIDs.LocalShadowMap;
                        break;
                    case "CachedShadow":
                        id = InfinityShaderIDs.CachedLocalShadowMap;
                        break;
                    case "CascadeShadowMoments":
                        id = InfinityShaderIDs.CascadeShadowMoments;
                        break;
                    case "LocalShadowMoments":
                        id = InfinityShaderIDs.LocalShadowMoments;
                        break;
                    case "SSRRayHit":
                        id = InfinityShaderIDs.SSRRayHitBuffer;
                        break;
                    case "SSRResolved":
                        id = InfinityShaderIDs.SSRResolvedBuffer;
                        break;
                    case "SSRTemporal":
                        id = InfinityShaderIDs.SSRTemporalBuffer;
                        break;
                    case "SSRMeta":
                        id = InfinityShaderIDs.SSRMetaBuffer;
                        break;
                    case "SSRTileClass":
                        id = PassShaderIDs.Capture_SSRTileClass;
                        break;
                    case "SSGIRayHit":
                        id = InfinityShaderIDs.SSGIRayHitBuffer;
                        break;
                    case "SSGITraceRadiance":
                        id = RenderCapturePassUtilityData.Capture_SSGITraceRadianceID;
                        break;
                    case "SSGITemporal":
                        id = InfinityShaderIDs.SSGITemporalBuffer;
                        break;
                    case "SSGIMeta":
                        id = InfinityShaderIDs.SSGIMetaBuffer;
                        break;
                    case "SSGIHitDistance":
                        id = InfinityShaderIDs.SSGIHitDistanceBuffer;
                        break;
                    case "ScatterDiffuse":
                        id = InfinityShaderIDs.ScatterDiffuseBuffer;
                        break;
                    case "SubsurfaceDiffuse":
                        id = InfinityShaderIDs.SubsurfaceBuffer;
                        break;
                    case "ReactiveMask":
                        id = InfinityShaderIDs.ReactiveMaskBuffer;
                        break;
                    case "DisplayColor":
                        id = InfinityShaderIDs.DisplayColorBuffer;
                        break;
                    case "PostProcess":
                        id = InfinityShaderIDs.PostProcessBuffer;
                        break;
                    case "Motion":
                        id = InfinityShaderIDs.MotionBuffer;
                        break;
                    case "MotionMetadata":
                        id = InfinityShaderIDs.MotionMetadataBuffer;
                        break;
                    case "SRAccumulation":
                        id = InfinityShaderIDs.SRAccumulationBuffer;
                        break;
                    case "SRDepth":
                        id = InfinityShaderIDs.SRDepthBuffer;
                        break;
                    case "TAAAccumulation":
                        id = InfinityShaderIDs.TAAAccumulationBuffer;
                        break;
                    case "TAADepth":
                        id = InfinityShaderIDs.TAADepthBuffer;
                        break;
                    case "TAAReprojection":
                        id = InfinityShaderIDs.TAAReprojectionBuffer;
                        break;
                    case "Depth":
                        id = InfinityShaderIDs.DepthBuffer;
                        break;
                    case "DBufferA":
                        id = InfinityShaderIDs.DBufferA;
                        break;
                    case "DBufferB":
                        id = InfinityShaderIDs.DBufferB;
                        break;
                    case "DBufferC":
                        id = InfinityShaderIDs.DBufferC;
                        break;
                    case "GBufferA":
                        id = InfinityShaderIDs.GBufferA;
                        break;
                    case "GBufferB":
                        id = InfinityShaderIDs.GBufferB;
                        break;
                    case "GBufferC":
                        id = InfinityShaderIDs.GBufferC;
                        break;
                    case "Occlusion":
                        id = InfinityShaderIDs.OcclusionBuffer;
                        break;
                    case "SSR":
                        id = InfinityShaderIDs.SSRBuffer;
                        break;
                    case "SSGI":
                        id = InfinityShaderIDs.SSGIBuffer;
                        break;
                    case "SceneColor":
                        id = InfinityShaderIDs.FoggedSceneColorBuffer;
                        break;
                    case "AntiAliasing":
                        id = InfinityShaderIDs.AntiAliasingBuffer;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown capture buffer: " + buffer);
                }
                RecordCaptureTransfer(session, buffer, id);
            }
            if (session.request.includeConfidence)
                RecordCaptureTransfer(session, "TAAConfidence", InfinityShaderIDs.TAAConfidenceBuffer);
        }

        struct CaptureDepthPassData
        {
            public RGTextureHandle source, destination;
            public ComputeKernel kernel;
            public int width, height;
        }

        RGTextureHandle ConvertCaptureDepth(RGTextureHandle source, TextureDescriptor descriptor)
        {
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.depth);
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.colorFormat = GraphicsFormat.R32_SFloat;
            descriptor.isShadowMap = false;
            descriptor.enableRandomWrite = true;
            descriptor.name = "CaptureDepthValues";
            RGTextureHandle destination = m_RGBuilder.CreateTexture(descriptor);
            using (var pass = m_RGBuilder.AddComputePass<CaptureDepthPassData>(s_CaptureDepthSampler))
            {
                ref var data = ref pass.GetPassData();
                data.source = pass.ReadTexture(source);
                data.destination = pass.WriteTexture(destination);
                data.kernel = kernel;
                data.width = descriptor.width;
                data.height = descriptor.height;
                pass.SetExecuteFunc((in CaptureDepthPassData data, in RGComputeEncoder commands) =>
                {
                    ComputeShader shader = data.kernel.shader;
                    int kernel = data.kernel.index;
                    commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureSizeID, new Vector4(data.width, data.height, 0, 0));
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.SRV_DepthID, data.source);
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.UAV_DepthID, data.destination);
                    commands.DispatchCompute(shader, kernel, data.kernel.GroupsX(data.width), data.kernel.GroupsY(data.height), 1);
                });
            }
            return destination;
        }

        RGTextureHandle ConvertCaptureMetadata(RGTextureHandle source, TextureDescriptor descriptor)
        {
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.surfaceMetadata);
            descriptor.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            descriptor.enableRandomWrite = true;
            descriptor.name = "CaptureSurfaceValues";
            var destination = m_RGBuilder.CreateTexture(descriptor);
            using (var pass = m_RGBuilder.AddComputePass<CaptureDepthPassData>(s_CaptureMetadataSampler))
            {
                ref var data = ref pass.GetPassData();
                data.source = pass.ReadTexture(source);
                data.destination = pass.WriteTexture(destination);
                data.kernel = kernel;
                data.width = descriptor.width;
                data.height = descriptor.height;
                pass.SetExecuteFunc((in CaptureDepthPassData data, in RGComputeEncoder commands) =>
                {
                    ComputeShader shader = data.kernel.shader;
                    int kernel = data.kernel.index;
                    commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureSizeID, new Vector4(data.width, data.height, 0, 0));
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.SRV_CaptureMetadataID, data.source);
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.UAV_CaptureID, data.destination);
                    commands.DispatchCompute(shader, kernel, data.kernel.GroupsX(data.width), data.kernel.GroupsY(data.height), 1);
                });
            }
            return destination;
        }

        void RecordCaptureTransfer(RenderCaptureSession session, string semantic, int shaderId, int sourceMip = 0)
        {
            RGTextureHandle source = m_RGScoper.QueryTexture(shaderId);
            if (!source.IsValid())
            {
                throw new InvalidOperationException("Capture source producer is missing: " + semantic);
            }
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            var sourceDescriptor = descriptor;
            descriptor.width = PyramidMipBatch.MipSize(descriptor.width, sourceMip);
            descriptor.height = PyramidMipBatch.MipSize(descriptor.height, sourceMip);
            descriptor.sizingDomain = ETextureSizingDomain.Exact;
            descriptor.allocationWidth = descriptor.allocationHeight = 0;
            descriptor.viewDivisor = 0;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            string producer = m_RGBuilder.GetTextureProducer(source, out string sourceQueue);
            if (descriptor.depthBufferBits != EDepthBits.None)
            {
                source = ConvertCaptureDepth(source, descriptor);
                descriptor = m_RGBuilder.GetTextureDescriptor(source);
            }
            if (descriptor.colorFormat == GraphicsFormat.R32G32_UInt)
            {
                source = ConvertCaptureMetadata(source, descriptor);
                descriptor = m_RGBuilder.GetTextureDescriptor(source);
            }
            RenderCaptureSession.Staging staging = session.Reserve(semantic, producer, descriptor, m_CameraUniform.historyReset);
            staging.evidence.sourceQueue = sourceQueue;
            // External contents predate this graph; their production frame is unknown to this capture.
            if (sourceQueue == "External")
            {
                staging.evidence.producerFrame = -1;
            }
            staging.evidence.sourceDescriptor = sourceDescriptor;
            staging.evidence.sourceMip = sourceMip;
            RGTextureHandle destination = m_RGBuilder.ImportTexture(staging.handle);
            using (var pass = m_RGBuilder.AddTransferPass<CaptureTransferPassData>(s_CaptureTransferSampler))
            {
                pass.EnablePassCulling(false);
                ref CaptureTransferPassData data = ref pass.GetPassData();
                data.source = pass.ReadTexture(source);
                data.destination = pass.WriteTexture(destination);
                data.staging = staging;
                pass.SetQueueObserver(staging);
                data.graph = m_RGBuilder;
                data.sourceMip = sourceMip;
                data.width = descriptor.width;
                data.height = descriptor.height;
                pass.SetExecuteFunc((in CaptureTransferPassData parameters, in RGTransferEncoder commands) =>
                {
                    parameters.staging.owner.RecordGraph(parameters.graph, Time.frameCount);
                    commands.CopyTexture(parameters.source, 0, parameters.sourceMip, 0, 0, parameters.width, parameters.height, parameters.destination, 0, 0, 0, 0);
                    commands.RequestAsyncReadback(parameters.staging.texture, parameters.staging.Complete);
                });
            }
        }
    }
}
#endif
