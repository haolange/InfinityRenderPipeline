using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct CaptureStageSnapshot
        {
            public bool enabled;
            public ComputeShader shader;
            public RGTextureRef texture;
            public int width, height;
            public void Record(in RGComputeEncoder commands, RGTextureRef source)
            {
                if (!enabled) return;
                int kernel = shader.FindKernel("CaptureTexture");
                commands.SetComputeVectorParam(shader, Shader.PropertyToID("CaptureSize"), new Vector4(width, height, 0, 0));
                commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("SRV_Capture"), source);
                commands.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("UAV_Capture"), texture);
                commands.DispatchCompute(shader, kernel, (width + 7) / 8, (height + 7) / 8, 1);
            }
        }

        CaptureStageSnapshot PrepareStageSnapshot(RGComputePassRef pass, string semantic, RGTextureRef source, TextureDescriptor? sourceDescriptor = null)
        {
            var session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || Array.IndexOf(session.request.buffers, semantic) < 0) return default;
            var shader = Resources.Load<ComputeShader>("Compute_CaptureTexture");
            if (shader == null || !shader.HasKernel("CaptureTexture")) throw new InvalidOperationException("Stage capture shader is required.");
            var descriptor = sourceDescriptor ?? m_RGBuilder.GetTextureDescriptor(source);
            descriptor.name = "Capture_" + semantic;
            descriptor.enableRandomWrite = true;
            var snapshot = m_RGScoper.CreateAndRegisterTexture(Shader.PropertyToID(descriptor.name), descriptor);
            return new CaptureStageSnapshot { enabled = true, shader = shader, width = descriptor.width, height = descriptor.height, texture = pass.WriteTexture(snapshot) };
        }

        struct CaptureTransferPassData
        {
            public RGTextureRef source, destination;
            public RenderCaptureSession.Staging staging;
            public RGBuilder graph;
            public int sourceMip;
        }
        static readonly ProfilingSampler s_CaptureTransferSampler = new ProfilingSampler("CaptureNormalFrame");

        void RecordLightingCapture()
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || Array.IndexOf(session.request.buffers, "Lighting") < 0) return;
            // Capture the final opaque lighting before ownership moves to scene-color stages.
            RecordCaptureTransfer(session, "Lighting", InfinityShaderIDs.LightingBuffer);
        }

        void RecordNormalFrameCapture(Camera camera)
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame) return;
            RecordZBinOverflowCapture();
            if (Array.IndexOf(session.request.buffers, "CascadeShadow") >= 0) session.RecordShadowState(renderContext.lightContext.ShadowAllocator);
            foreach (string buffer in session.request.buffers)
            {
                if (buffer == "Lighting") continue;
                if (buffer == "HiZ")
                {
                    var descriptor = m_RGBuilder.GetTextureDescriptor(m_RGScoper.QueryTexture(InfinityShaderIDs.HiZBuffer));
                    for (int mip = 0; mip < PyramidMipBatch.MipCount(descriptor.width, descriptor.height); mip++)
                        RecordCaptureTransfer(session, "HiZ_Mip" + mip, InfinityShaderIDs.HiZBuffer, mip);
                    continue;
                }
                int id;
                switch (buffer)
                {
                    case "TAAInput": case "TAAHistoryColor": case "TAAHistoryDepth":
                    case "AOTrace": case "AOSpatialX": case "AOSpatialY": case "AOTemporal":
                    case "SSRSpatial": case "SSGISpatial": id = Shader.PropertyToID("Capture_" + buffer); break;
                    case "BakedDiffuse": id = InfinityShaderIDs.BakedDiffuseBuffer; break;
                    case "BakedOcclusion": id = InfinityShaderIDs.BakedOcclusionBuffer; break;
                    case "IndirectDiffuse": id = InfinityShaderIDs.IndirectDiffuseBuffer; break;
                    case "IndirectSpecular": id = InfinityShaderIDs.IndirectSpecularBuffer; break;
                    case "HalfResDepth": id = InfinityShaderIDs.HalfResDepthBuffer; break;
                    case "HalfResNormal": id = InfinityShaderIDs.HalfResNormalBuffer; break;
                    case "CascadeShadow": id = InfinityShaderIDs.CascadeShadowMap; break;
                    case "SSRHit": id = InfinityShaderIDs.SSRHitPDFBuffer; break;
                    case "SSRRadiance": id = Shader.PropertyToID("Capture_SSRRadiance"); break;
                    case "SSRTemporal": id = InfinityShaderIDs.SSRTemporalBuffer; break;
                    case "SSGIRadiance": id = Shader.PropertyToID("Capture_SSGIRadiance"); break;
                    case "SSGITemporal": id = InfinityShaderIDs.SSGITemporalBuffer; break;
                    case "DisplayColor": id = InfinityShaderIDs.DisplayColorBuffer; break;
                    case "PostProcess": id = InfinityShaderIDs.PostProcessBuffer; break;
                    case "Motion": id = InfinityShaderIDs.MotionBuffer; break;
                    case "MotionMetadata": id = InfinityShaderIDs.MotionMetadataBuffer; break;
                    case "TAAAccumulation": id = InfinityShaderIDs.TAAAccumulationBuffer; break;
                    case "TAADepth": id = InfinityShaderIDs.TAADepthBuffer; break;
                    case "TAAReprojection": id = InfinityShaderIDs.TAAReprojectionBuffer; break;
                    case "Depth": id = InfinityShaderIDs.DepthBuffer; break;
                    case "GBufferA": id = InfinityShaderIDs.GBufferA; break;
                    case "GBufferB": id = InfinityShaderIDs.GBufferB; break;
                    case "GBufferC": id = InfinityShaderIDs.GBufferC; break;
                    case "Occlusion": id = InfinityShaderIDs.OcclusionBuffer; break;
                    case "SSR": id = InfinityShaderIDs.SSRBuffer; break;
                    case "SSGI": id = InfinityShaderIDs.SSGIBuffer; break;
                    case "SceneColor": id = InfinityShaderIDs.FoggedSceneColorBuffer; break;
                    case "AntiAliasing": id = InfinityShaderIDs.AntiAliasingBuffer; break;
                    default: throw new InvalidOperationException("Unknown capture buffer: " + buffer);
                }
                RecordCaptureTransfer(session, buffer, id);
            }
            if (session.request.includeConfidence)
                RecordCaptureTransfer(session, "TAAConfidence", InfinityShaderIDs.TAAConfidenceBuffer);
        }

        struct CaptureCounterPassData
        {
            public RGBufferRef source, destination;
            public RenderCaptureSession.Staging staging;
        }

        void RecordZBinOverflowCapture()
        {
            RenderCaptureSession session = RenderCaptureService.current;
            if (session == null || !session.CaptureThisFrame || !session.request.includeZBinOverflow) return;
            RGBufferRef source = m_RGScoper.QueryBuffer(LightShaderIDs.ZBinOverflowBuffer);
            string producer = m_RGBuilder.GetBufferProducer(source, out string queue);
            var staging = session.ReserveCounter("ZBinOverflow", renderContext.lightContext.ZBinOverflowBuffer, producer, queue);
            using (RGTransferPassRef pass = m_RGBuilder.AddTransferPass<CaptureCounterPassData>(s_CaptureTransferSampler))
            {
                pass.EnablePassCulling(false);
                ref CaptureCounterPassData data = ref pass.GetPassData<CaptureCounterPassData>();
                data.source = pass.ReadBuffer(source);
                data.destination = pass.WriteBuffer(m_RGBuilder.ImportBuffer(staging.buffer, "ZBinOverflowStaging"));
                data.staging = staging;
                pass.SetQueueObserver(staging);
                pass.SetExecuteFunc((in CaptureCounterPassData parameters, in RGTransferEncoder commands, RGObjectPool pool) =>
                {
                    commands.CopyBuffer(parameters.source.Resolve().graphicsResource, parameters.destination.Resolve().graphicsResource);
                    commands.RequestAsyncReadback(parameters.staging.buffer, parameters.staging.Complete);
                });
            }
        }

        struct CaptureDepthPassData
        {
            public RGTextureRef source, destination;
            public ComputeShader shader;
            public int width, height;
        }

        RGTextureRef ConvertCaptureDepth(RGTextureRef source, TextureDescriptor descriptor)
        {
            var shader = Resources.Load<ComputeShader>("Compute_CaptureDepth");
            if (shader == null || !shader.HasKernel("CaptureDepth"))
                throw new InvalidOperationException("Depth diagnostics require the CaptureDepth kernel.");
            descriptor.depthBufferBits = EDepthBits.None;
            descriptor.colorFormat = GraphicsFormat.R32_SFloat;
            descriptor.isShadowMap = false;
            descriptor.enableRandomWrite = true;
            descriptor.name = "CaptureDepthValues";
            RGTextureRef destination = m_RGBuilder.CreateTexture(descriptor);
            using (RGComputePassRef pass = m_RGBuilder.AddComputePass<CaptureDepthPassData>(s_CaptureTransferSampler))
            {
                ref var data = ref pass.GetPassData<CaptureDepthPassData>();
                data.source = pass.ReadTexture(source);
                data.destination = pass.WriteTexture(destination);
                data.shader = shader; data.width = descriptor.width; data.height = descriptor.height;
                pass.SetExecuteFunc((in CaptureDepthPassData data, in RGComputeEncoder commands, RGObjectPool pool) =>
                {
                    int kernel = data.shader.FindKernel("CaptureDepth");
                    commands.SetComputeVectorParam(data.shader, Shader.PropertyToID("CaptureSize"), new Vector4(data.width, data.height, 0, 0));
                    commands.SetComputeTextureParam(data.shader, kernel, Shader.PropertyToID("SRV_Depth"), data.source);
                    commands.SetComputeTextureParam(data.shader, kernel, Shader.PropertyToID("UAV_Depth"), data.destination);
                    commands.DispatchCompute(data.shader, kernel, (data.width + 7) / 8, (data.height + 7) / 8, 1);
                });
            }
            return destination;
        }

        void RecordCaptureTransfer(RenderCaptureSession session, string semantic, int shaderId, int sourceMip = 0)
        {
            RGTextureRef source = m_RGScoper.QueryTexture(shaderId);
            if (!source.IsValid()) throw new InvalidOperationException("Capture source producer is missing: " + semantic);
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            var sourceDescriptor = descriptor;
            descriptor.width = PyramidMipBatch.MipSize(descriptor.width, sourceMip);
            descriptor.height = PyramidMipBatch.MipSize(descriptor.height, sourceMip);
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            string producer = m_RGBuilder.GetTextureProducer(source, out string sourceQueue);
            if (descriptor.depthBufferBits != EDepthBits.None)
            {
                source = ConvertCaptureDepth(source, descriptor);
                descriptor = m_RGBuilder.GetTextureDescriptor(source);
            }
            RenderCaptureSession.Staging staging = session.Reserve(semantic, producer, descriptor, m_CameraUniform.historyReset);
            staging.evidence.sourceQueue = sourceQueue;
            staging.evidence.sourceDescriptor = sourceDescriptor;
            staging.evidence.sourceMip = sourceMip;
            RGTextureRef destination = m_RGBuilder.ImportTexture(staging.handle);
            using (RGTransferPassRef pass = m_RGBuilder.AddTransferPass<CaptureTransferPassData>(s_CaptureTransferSampler))
            {
                pass.EnablePassCulling(false);
                ref CaptureTransferPassData data = ref pass.GetPassData<CaptureTransferPassData>();
                data.source = pass.ReadTexture(source);
                data.destination = pass.WriteTexture(destination);
                data.staging = staging;
                pass.SetQueueObserver(staging);
                data.graph = m_RGBuilder;
                data.sourceMip = sourceMip;
                pass.SetExecuteFunc((in CaptureTransferPassData parameters, in RGTransferEncoder commands, RGObjectPool pool) =>
                {
                    parameters.staging.owner.RecordGraph(parameters.graph, Time.frameCount);
                    commands.CopyTexture(parameters.source, 0, parameters.sourceMip, parameters.destination, 0, 0);
                    commands.RequestAsyncReadback(parameters.staging.texture, parameters.staging.Complete);
                });
            }
        }
    }
}
