using System;
using InfinityTech.Core;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.Temporal;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class TemporalShaderIDs
    {
        internal static readonly int SRV_ReactiveMaskTexture = Shader.PropertyToID("SRV_ReactiveMaskTexture");
        internal static readonly int UAV_TemporalHistoryTexture = Shader.PropertyToID("UAV_TemporalHistoryTexture");
        internal static readonly int UAV_TemporalOutputTexture = Shader.PropertyToID("UAV_TemporalOutputTexture");
    }

    internal static class TemporalReconstructionUtility
    {
        internal const string HistoryColorTextureName = "TemporalHistoryColor";
        internal static readonly int HistoryColorTextureID = Shader.PropertyToID(HistoryColorTextureName);

        internal static TextureDescriptor TemporalColorDescriptor(
            int width,
            int height,
            string name,
            bool enableRandomWrite,
            ETextureSizingDomain domain = ETextureSizingDomain.Display)
        {
            return new TextureDescriptor(math.max(1, width), math.max(1, height), domain)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                enableRandomWrite = enableRandomWrite,
            };
        }

        internal static TextureDescriptor CreateScratchDescriptor(
            int width,
            int height,
            string name,
            GraphicsFormat format,
            FilterMode filterMode,
            ETextureSizingDomain domain)
        {
            return new TextureDescriptor(math.max(1, width), math.max(1, height), domain)
            {
                name = name,
                dimension = TextureDimension.Tex2D,
                colorFormat = format,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                enableRandomWrite = true,
            };
        }

        internal static void RequireKernel(in ComputeKernel kernel)
        {
            if (!kernel.isValid)
            {
                throw new InvalidOperationException("InfinityRP: " + kernel.missingReason);
            }
        }

        internal static HistoryRead ReadPingPongHistory(
            HistoryCache historyCache,
            RGBuilder graph,
            int historyId,
            in TextureDescriptor descriptor,
            bool resetRequested,
            out RGTextureHandle priorHandle,
            out RGTextureHandle writeHandle,
            out bool historyValid)
        {
            HistoryRead read = historyCache.Read(historyId, descriptor, resetRequested);
            priorHandle = graph.ImportTexture(read.textureHandle);
            writeHandle = graph.ImportTexture(historyCache.GetWriteTexture(historyId, descriptor));
            historyCache.MarkProduced(historyId);
            historyValid = !resetRequested && read.valid;
            return read;
        }
    }

    internal readonly struct TemporalExecutionContext
    {
        internal readonly ViewContext view;
        internal readonly RGTextureHandle sceneColorHandle;
        internal readonly RGTextureHandle depthHandle;
        internal readonly RGTextureHandle motionHandle;
        internal readonly RGTextureHandle reactiveMaskHandle;
        internal readonly RGTextureHandle historyColorReadHandle;
        internal readonly RGTextureHandle historyColorWriteHandle;
        internal readonly RGTextureHandle outputHandle;
        internal readonly bool hasReactiveMask;
        internal readonly bool resetHistory;

        internal TemporalExecutionContext(
            in ViewContext view,
            RGTextureHandle sceneColorHandle,
            RGTextureHandle depthHandle,
            RGTextureHandle motionHandle,
            RGTextureHandle reactiveMaskHandle,
            RGTextureHandle historyColorReadHandle,
            RGTextureHandle historyColorWriteHandle,
            RGTextureHandle outputHandle,
            bool hasReactiveMask,
            bool resetHistory)
        {
            this.view = view;
            this.sceneColorHandle = sceneColorHandle;
            this.depthHandle = depthHandle;
            this.motionHandle = motionHandle;
            this.reactiveMaskHandle = reactiveMaskHandle;
            this.historyColorReadHandle = historyColorReadHandle;
            this.historyColorWriteHandle = historyColorWriteHandle;
            this.outputHandle = outputHandle;
            this.hasReactiveMask = hasReactiveMask;
            this.resetHistory = resetHistory;
        }
    }

    internal interface ITemporalExecutionBackend
    {
        ETemporalBackend Backend { get; }
        void Record(RGBuilder graph, InfinityRenderPipelineResources resources, in TemporalExecutionContext executionContext);
    }

    /// <summary>
    /// Unified RHI-style Temporal Reconstruction pass (PRD §9.1 & §10.2). Dispatches the active
    /// ITemporalExecutionBackend through a fixed backend table with shared graph IO and unified history ownership.
    /// </summary>
    internal sealed class TemporalReconstructionPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly ITemporalExecutionBackend[] m_Backends;

        internal TemporalReconstructionPass(
            RGBuilder graph,
            InfinityRenderPipelineResources resources
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            , RenderCapturePass renderCapturePass = null
#endif
        )
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            int slotCount = Enum.GetValues(typeof(ETemporalBackend)).Length;
            m_Backends = new ITemporalExecutionBackend[slotCount];
            RegisterBackend(new NativeTemporalBackend(
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                renderCapturePass
#endif
            ));
            RegisterBackend(new StpTemporalBackend());
            RegisterBackend(new FsrTemporalBackend());
            RegisterBackend(new SgsrTemporalBackend());
        }

        void RegisterBackend(ITemporalExecutionBackend backend)
        {
            m_Backends[(int)backend.Backend] = backend;
        }

        internal void ComputeTemporalReconstruction(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.TemporalReconstruction))
            {
                return;
            }

            TemporalFrameRequirements requirements = context.temporalRequirements;
            ETemporalBackend activeBackend = requirements.activeBackend;
            int backendSlot = (int)activeBackend;
            ITemporalExecutionBackend backend = (backendSlot >= 0 && backendSlot < m_Backends.Length)
                ? m_Backends[backendSlot]
                : null;
            if (backend == null)
            {
                throw new InvalidOperationException("InfinityRP: No temporal execution backend registered for " + activeBackend + ".");
            }

            using (PipelineCpuMarkers.s_RecordComputeTemporalReconstruction.Auto())
            {
                RGTextureHandle sceneColorHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.fogged);
                RGTextureHandle depthHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
                RGTextureHandle motionHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
                RGTextureHandle reactiveMaskHandle = default;
                bool hasReactiveMask = requirements.requiresReactiveMask
                    && m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.ReactiveMaskBuffer, out reactiveMaskHandle);

                TextureDescriptor outputDesc = TemporalReconstructionUtility.TemporalColorDescriptor(
                    requirements.displaySize.x,
                    requirements.displaySize.y,
                    "TemporalOutput",
                    enableRandomWrite: true,
                    ETextureSizingDomain.Display);
                RGTextureHandle outputHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TemporalOutputBuffer, outputDesc);

                TextureDescriptor historyDesc = TemporalReconstructionUtility.TemporalColorDescriptor(
                    requirements.displaySize.x,
                    requirements.displaySize.y,
                    TemporalReconstructionUtility.HistoryColorTextureName,
                    enableRandomWrite: true,
                    ETextureSizingDomain.Display);
                bool resetRequested = requirements.requiresHistoryReset || context.resetsHistory;
                HistoryRead historyRead = context.historyCache.Read(
                    TemporalReconstructionUtility.HistoryColorTextureID,
                    historyDesc,
                    resetRequested);
                RGTextureHandle historyReadHandle = m_RGBuilder.ImportTexture(historyRead.textureHandle);
                RGTextureHandle historyWriteHandle = m_RGBuilder.ImportTexture(
                    context.historyCache.GetWriteTexture(TemporalReconstructionUtility.HistoryColorTextureID, historyDesc));
                context.historyCache.MarkProduced(TemporalReconstructionUtility.HistoryColorTextureID);

                bool resetHistory = resetRequested || !historyRead.valid;
                var executionContext = new TemporalExecutionContext(
                    in context,
                    sceneColorHandle,
                    depthHandle,
                    motionHandle,
                    reactiveMaskHandle,
                    historyReadHandle,
                    historyWriteHandle,
                    outputHandle,
                    hasReactiveMask,
                    resetHistory);

                backend.Record(m_RGBuilder, m_Resources, in executionContext);
                context.features.MarkProduced(EFrameFeature.TemporalReconstruction);
            }
        }
    }
}
