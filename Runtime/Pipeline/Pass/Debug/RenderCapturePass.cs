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
            public RGTextureHandle textureHandle;
            public int width, height;
            public void Record(in RGComputeEncoder commands, RGTextureHandle sourceHandle)
            {
                if (!enabled)
                {
                    return;
                }
                ComputeShader shader = kernel.shader;
                commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureSizeID, new Vector4(width, height, 0, 0));
                commands.SetComputeTextureParam(shader, kernel.index, RenderCapturePassUtilityData.SRV_CaptureID, sourceHandle);
                commands.SetComputeTextureParam(shader, kernel.index, RenderCapturePassUtilityData.UAV_CaptureID, textureHandle);
                commands.DispatchCompute(shader, kernel.index, kernel.GroupsX(width), kernel.GroupsY(height), 1);
            }
        }

        CaptureStageSnapshot PrepareStageSnapshot<TPassData>(RGComputePassHandle<TPassData> passHandle, ECaptureBuffer buffer, RGTextureHandle sourceHandle) where TPassData : struct
        {
            var session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || !session.Captures(buffer))
            {
                return default;
            }
            string semantic = buffer.ToString();
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.texture);
            var descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            descriptor.name = "Capture_" + semantic;
            descriptor.enableRandomWrite = true;
            var snapshot = m_RGBuilder.scoper.CreateAndRegisterTexture(Shader.PropertyToID(descriptor.name), descriptor);
            return new CaptureStageSnapshot { enabled = true, kernel = kernel, width = descriptor.width, height = descriptor.height, textureHandle = passHandle.WriteTexture(snapshot) };
        }

        struct CaptureTransferPassData
        {
            public RGTextureHandle sourceHandle, destinationHandle;
            public RenderCaptureSession.Staging staging;
            public RGBuilder graph;
            public int frame, sourceMip, width, height;
        }
        static readonly ProfilingSampler s_CaptureLightMaskSampler = new ProfilingSampler("ComputeCaptureLightMask");
        static readonly ProfilingSampler s_CaptureMetadataSampler = new ProfilingSampler("ComputeCaptureSurfaceMetadata");
        static readonly ProfilingSampler s_CaptureDepthSampler = new ProfilingSampler("ComputeCaptureDepth");
        static readonly ProfilingSampler s_CaptureTransferSampler = new ProfilingSampler("CopyCaptureNormalFrame");

        void RecordLightingCapture(in ViewContext context)
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || !session.Captures(ECaptureBuffer.Lighting))
            {
                return;
            }
            // Capture the final opaque lighting before ownership moves to scene-color stages.
            RecordCaptureTransfer(context, session, "Lighting", context.sceneColor.opaque);
        }

        void RecordDeferredLightingCapture(in ViewContext context)
        {
            var session = RenderCaptureService.current;
            if (session != null && session.CaptureThisFrame && session.Captures(ECaptureBuffer.DeferredLighting))
                RecordCaptureTransfer(context, session, "DeferredLighting", InfinityShaderIDs.LightingBuffer);
        }

        struct CaptureLightMaskPassData
        {
            public RGBufferHandle sourceHandle;
            public RGTextureHandle destinationHandle;
            public ComputeKernel kernel;
            public int width, height;
        }

        int RecordLightMaskCapture(in ViewContext context)
        {
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.bufferUIntBytes);
            var lightContext = context.lights;
            bool local = lightContext.HasLocalLightList();
            int width = local ? lightContext.TileCount.x * lightContext.TileWordCount : 1;
            int height = local ? lightContext.TileCount.y : 1;
            int id = RenderCapturePassUtilityData.Capture_LightTileMaskID;
            var descriptor = new TextureDescriptor(width, height)
            {
                name = "Capture_LightTileMask", dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R32G32B32A32_SFloat, enableRandomWrite = true
            };
            var destination = m_RGBuilder.scoper.CreateAndRegisterTexture(id, descriptor);
            {
                RGComputePassHandle<CaptureLightMaskPassData> passHandle = m_RGBuilder.AddComputePass<CaptureLightMaskPassData>(s_CaptureLightMaskSampler);
                ref CaptureLightMaskPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = passHandle.ReadBuffer(QueryLightLoop(context).tileMasksHandle);
                passData.destinationHandle = passHandle.WriteTexture(destination);
                passData.kernel = kernel;
                passData.width = width;
                passData.height = height;
                passHandle.SetExecuteFunc((in CaptureLightMaskPassData passData, in RGComputeEncoder commands) =>
                {
                    ComputeShader shader = passData.kernel.shader;
                    int kernel = passData.kernel.index;
                    commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureBufferSizeID, new Vector4(passData.width, passData.height, 0, 0));
                    commands.SetComputeBufferParam(shader, kernel, RenderCapturePassUtilityData.SRV_CaptureUIntID, passData.sourceHandle);
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.UAV_CaptureUIntBytesID, passData.destinationHandle);
                    commands.DispatchCompute(shader, kernel, passData.kernel.GroupsX(passData.width), passData.kernel.GroupsY(passData.height), 1);
                });
            }
            return id;
        }

        void RecordNormalFrameCapture(in ViewContext context)
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame)
            {
                return;
            }
            if (session.Captures(ECaptureBuffer.CascadeShadow))
            {
                session.RecordShadowState(context.lights.ShadowAllocator);
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
                    var descriptor = m_RGBuilder.GetTextureDescriptor(m_RGBuilder.scoper.QueryTexture(key));
                    for (int mip = 0; mip < PyramidMipBatch.MipCount(descriptor.width, descriptor.height); mip++)
                        RecordCaptureTransfer(context, session, buffer + "_Mip" + mip, key, mip);
                    continue;
                }
                int id;
                switch (buffer)
                {
                    case "TemporalColorSource": case "TemporalColorDepth":
                        if (!context.state.temporalColorView.valid)
                            throw new InvalidOperationException("Temporal color source capture requires an accepted history.");
                        id = buffer == "TemporalColorSource" ? InfinityShaderIDs.TemporalColorSourceBuffer : InfinityShaderIDs.TemporalColorDepthBuffer;
                        break;
                    case "LightTileMask":
                        id = RecordLightMaskCapture(context);
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
                        id = context.sceneColor.fogged;
                        break;
                    case "AntiAliasing":
                        id = InfinityShaderIDs.AntiAliasingBuffer;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown capture buffer: " + buffer);
                }
                RecordCaptureTransfer(context, session, buffer, id);
            }
            if (session.request.includeConfidence)
                RecordCaptureTransfer(context, session, "TAAConfidence", InfinityShaderIDs.TAAConfidenceBuffer);
        }

        struct CaptureDepthPassData
        {
            public RGTextureHandle sourceHandle, destinationHandle;
            public ComputeKernel kernel;
            public int width, height;
        }

        RGTextureHandle ConvertCaptureDepth(RGTextureHandle sourceHandle, TextureDescriptor descriptor)
        {
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.depth);
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.colorFormat = GraphicsFormat.R32_SFloat;
            descriptor.isShadowMap = false;
            descriptor.enableRandomWrite = true;
            descriptor.name = "CaptureDepthValues";
            RGTextureHandle destinationHandle = m_RGBuilder.CreateTexture(descriptor);
            {
                RGComputePassHandle<CaptureDepthPassData> passHandle = m_RGBuilder.AddComputePass<CaptureDepthPassData>(s_CaptureDepthSampler);
                ref CaptureDepthPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = passHandle.ReadTexture(sourceHandle);
                passData.destinationHandle = passHandle.WriteTexture(destinationHandle);
                passData.kernel = kernel;
                passData.width = descriptor.width;
                passData.height = descriptor.height;
                passHandle.SetExecuteFunc((in CaptureDepthPassData passData, in RGComputeEncoder commands) =>
                {
                    ComputeShader shader = passData.kernel.shader;
                    int kernel = passData.kernel.index;
                    commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureSizeID, new Vector4(passData.width, passData.height, 0, 0));
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.SRV_DepthID, passData.sourceHandle);
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.UAV_DepthID, passData.destinationHandle);
                    commands.DispatchCompute(shader, kernel, passData.kernel.GroupsX(passData.width), passData.kernel.GroupsY(passData.height), 1);
                });
            }
            return destinationHandle;
        }

        RGTextureHandle ConvertCaptureMetadata(RGTextureHandle sourceHandle, TextureDescriptor descriptor)
        {
            ComputeKernel kernel = CaptureKernels.Require(resources.captureKernels.surfaceMetadata);
            descriptor.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            descriptor.enableRandomWrite = true;
            descriptor.name = "CaptureSurfaceValues";
            var destination = m_RGBuilder.CreateTexture(descriptor);
            {
                RGComputePassHandle<CaptureDepthPassData> passHandle = m_RGBuilder.AddComputePass<CaptureDepthPassData>(s_CaptureMetadataSampler);
                ref CaptureDepthPassData passData = ref passHandle.GetPassData();
                passData.sourceHandle = passHandle.ReadTexture(sourceHandle);
                passData.destinationHandle = passHandle.WriteTexture(destination);
                passData.kernel = kernel;
                passData.width = descriptor.width;
                passData.height = descriptor.height;
                passHandle.SetExecuteFunc((in CaptureDepthPassData passData, in RGComputeEncoder commands) =>
                {
                    ComputeShader shader = passData.kernel.shader;
                    int kernel = passData.kernel.index;
                    commands.SetComputeVectorParam(shader, RenderCapturePassUtilityData.CaptureSizeID, new Vector4(passData.width, passData.height, 0, 0));
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.SRV_CaptureMetadataID, passData.sourceHandle);
                    commands.SetComputeTextureParam(shader, kernel, RenderCapturePassUtilityData.UAV_CaptureID, passData.destinationHandle);
                    commands.DispatchCompute(shader, kernel, passData.kernel.GroupsX(passData.width), passData.kernel.GroupsY(passData.height), 1);
                });
            }
            return destination;
        }

        void RecordCaptureTransfer(in ViewContext context, RenderCaptureSession session, string semantic, int shaderId, int sourceMip = 0)
        {
            RGTextureHandle sourceHandle = m_RGBuilder.scoper.QueryTexture(shaderId);
            if (!sourceHandle.IsValid())
            {
                throw new InvalidOperationException("Capture source producer is missing: " + semantic);
            }
            var descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            var sourceDescriptor = descriptor;
            descriptor.width = PyramidMipBatch.MipSize(descriptor.width, sourceMip);
            descriptor.height = PyramidMipBatch.MipSize(descriptor.height, sourceMip);
            descriptor.sizingDomain = ETextureSizingDomain.Exact;
            descriptor.allocationWidth = descriptor.allocationHeight = 0;
            descriptor.viewDivisor = 0;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            string producer = m_RGBuilder.GetTextureProducer(sourceHandle, out string sourceQueue);
            if (descriptor.depthBufferBits != EDepthBits.None)
            {
                sourceHandle = ConvertCaptureDepth(sourceHandle, descriptor);
                descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            }
            if (descriptor.colorFormat == GraphicsFormat.R32G32_UInt)
            {
                sourceHandle = ConvertCaptureMetadata(sourceHandle, descriptor);
                descriptor = m_RGBuilder.GetTextureDescriptor(sourceHandle);
            }
            RenderCaptureSession.Staging staging = session.Reserve(semantic, producer, descriptor, context.resetsHistory);
            staging.evidence.sourceQueue = sourceQueue;
            // External contents predate this graph; their production frame is unknown to this capture.
            if (sourceQueue == "External")
            {
                staging.evidence.producerFrame = -1;
            }
            staging.evidence.sourceDescriptor = sourceDescriptor;
            staging.evidence.sourceMip = sourceMip;
            RGTextureHandle destinationHandle = m_RGBuilder.ImportTexture(staging.handle);
            {
                RGTransferPassHandle<CaptureTransferPassData> passHandle = m_RGBuilder.AddTransferPass<CaptureTransferPassData>(s_CaptureTransferSampler);
                passHandle.EnablePassCulling(false);
                ref CaptureTransferPassData data = ref passHandle.GetPassData();
                data.sourceHandle = passHandle.ReadTexture(sourceHandle);
                data.destinationHandle = passHandle.WriteTexture(destinationHandle);
                data.staging = staging;
                passHandle.SetQueueObserver(staging);
                data.graph = m_RGBuilder;
                data.frame = context.frame;
                data.sourceMip = sourceMip;
                data.width = descriptor.width;
                data.height = descriptor.height;
                passHandle.SetExecuteFunc((in CaptureTransferPassData parameters, in RGTransferEncoder commands) =>
                {
                    parameters.staging.owner.RecordGraph(parameters.graph, parameters.frame);
                    commands.CopyTexture(parameters.sourceHandle, 0, parameters.sourceMip, 0, 0, parameters.width, parameters.height, parameters.destinationHandle, 0, 0, 0, 0);
                    commands.RequestAsyncReadback(parameters.staging.texture, parameters.staging.Complete);
                });
            }
        }
    }
}
#endif
