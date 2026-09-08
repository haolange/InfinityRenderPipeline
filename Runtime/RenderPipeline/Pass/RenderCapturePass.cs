using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
        struct CaptureTransferPassData
        {
            public RGTextureRef source, destination;
            public RenderCaptureSession.Staging staging;
            public RGBuilder graph;
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
            foreach (string buffer in session.request.buffers)
            {
                if (buffer == "Lighting") continue;
                int id;
                switch (buffer)
                {
                    case "DisplayColor": id = InfinityShaderIDs.DisplayColorBuffer; break;
                    case "PostProcess": id = InfinityShaderIDs.PostProcessBuffer; break;
                    case "Motion": id = InfinityShaderIDs.MotionBuffer; break;
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

        void RecordCaptureTransfer(RenderCaptureSession session, string semantic, int shaderId)
        {
            RGTextureRef source = m_RGScoper.QueryTexture(shaderId);
            if (!source.IsValid()) throw new InvalidOperationException("Capture source producer is missing: " + semantic);
            var descriptor = m_RGBuilder.GetTextureDescriptor(source);
            string producer = m_RGBuilder.GetTextureProducer(source, out string sourceQueue);
            RenderCaptureSession.Staging staging = session.Reserve(semantic, producer, descriptor, m_CameraUniform.historyReset);
            staging.evidence.sourceQueue = sourceQueue;
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
                pass.SetExecuteFunc((in CaptureTransferPassData parameters, in RGTransferEncoder commands, RGObjectPool pool) =>
                {
                    parameters.staging.owner.RecordGraph(parameters.graph, Time.frameCount);
                    commands.CopyTexture(parameters.source, parameters.destination);
                    commands.RequestAsyncReadback(parameters.staging.texture, parameters.staging.Complete);
                });
            }
        }
    }
}
