using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Single Mesh Drawing world: scene, GPU residency, pass registry, visibility intern, one processor.
    /// RenderGraph talks only to this object via Create / Use / Draw.
    /// CandidateTable uploads stay on PassBin epoch, not per-frame.
    /// </summary>
    public sealed class MeshWorld : IDisposable
    {
        private readonly MeshScene m_Scene;
        private readonly GpuScene m_Residency;
        private readonly MeshVisibilityShare m_VisibilityShare;
        private readonly MeshDrawPipeline m_Processor;
        private readonly PassRegistry m_Registry;
        private readonly MeshGpuVisibilityCache m_GpuVisibility;
        private readonly Dictionary<ulong, ComputeBuffer> m_PreviousTransforms = new Dictionary<ulong, ComputeBuffer>(8);
        private Camera m_ActiveCamera;
        private EMeshBackendPolicy? m_BackendOverride;
        private bool m_Disposed;

        public MeshScene Scene => m_Scene;
        public GpuScene Residency => m_Residency;
        public MeshVisibilityShare VisibilityShare => m_VisibilityShare;
        public MeshDrawPipeline Processor => m_Processor;
        public PassRegistry Registry => m_Registry;

        public MeshWorld(MeshScene scene, ResourcePool resourcePool)
        {
            m_Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            if (resourcePool == null)
            {
                throw new ArgumentNullException(nameof(resourcePool));
            }

            m_Residency = new GpuScene(resourcePool, m_Scene);
            m_VisibilityShare = new MeshVisibilityShare();
            m_Registry = new PassRegistry();
            m_Processor = new MeshDrawPipeline(m_Scene, m_Residency, resourcePool, m_Registry);
            m_GpuVisibility = new MeshGpuVisibilityCache(resourcePool);
        }

        public void BeginCamera(Camera camera)
        {
            m_ActiveCamera = camera;
            m_PreviousTransforms.Clear();
            MeshPipelineDiagnostics.VisibilityProductionsPerFrame = 0;
            MeshPipelineDiagnostics.CompactDispatchesPerFrame = 0;
            MeshPipelineDiagnostics.GpuCullDispatchesPerFrame = 0;
            MeshPipelineDiagnostics.CandidateUploadsBytes = 0;
            m_GpuVisibility.BeginCamera();
            if (m_Scene != null)
            {
                m_VisibilityShare.BeginFrame(m_Scene.VisibilityRevision);
            }
        }

        public void BindPreviousTransforms(ulong viewKey, ComputeBuffer buffer)
        {
            if (buffer == null)
            {
                m_PreviousTransforms.Remove(viewKey);
                return;
            }

            m_PreviousTransforms[viewKey] = buffer;
        }

        public void SetBackendOverride(EMeshBackendPolicy? policy)
        {
            m_BackendOverride = policy;
        }

        public EMeshBackendPolicy SelectPolicy()
        {
            if (m_BackendOverride.HasValue)
            {
                return m_BackendOverride.Value;
            }

            if (m_ActiveCamera != null)
            {
                return RenderCaptureService.BackendFor(m_ActiveCamera);
            }

            return EMeshBackendPolicy.Auto;
        }

        public MeshPassContext BindPass(in MeshView view, MeshPassId passId)
        {
            MeshPassDefinition definition = m_Registry.Get(passId);
            m_PreviousTransforms.TryGetValue(view.viewKey, out ComputeBuffer previousTransforms);
            return new MeshPassContext
            {
                passId = passId,
                shaderPassIndex = definition.shaderPassIndex,
                lightModeTag = definition.lightModeTag,
                backendPolicy = SelectPolicy(),
                viewKey = view.viewKey,
                previousTransforms = previousTransforms
            };
        }

        public MeshVisibilityHandle AcquireVisibility(in MeshView view)
        {
            return m_VisibilityShare.Acquire(m_Scene, view);
        }

        public void RefineVisibilityHiZ(MeshVisibilityHandle handle)
        {
            m_VisibilityShare.RefineVisibilityHiZ(handle);
        }

        internal ComputeBuffer GetGpuVisibilityBuffer(in MeshView view, int instanceCount)
        {
            return m_GpuVisibility.GetBuffer(view, instanceCount);
        }

        internal FBufferRef GetGpuVisibilityBufferRef(in MeshView view, int instanceCount)
        {
            return m_GpuVisibility.GetBufferRef(view, instanceCount);
        }

        internal bool NeedsGpuCull(in MeshView view)
        {
            return m_GpuVisibility.NeedsCull(view);
        }

        internal void MarkGpuCulled(in MeshView view)
        {
            m_GpuVisibility.MarkCulled(view);
        }

        public void UpdateResidency()
        {
            m_Residency.Update();
        }

        public void ClearResidency()
        {
            m_Residency.Clear();
        }

        public void FlushRetired()
        {
            MeshDrawGPUBackend.FlushRetiredPayloads();
            m_Processor.FlushRetiredBuffers();
            m_GpuVisibility.FlushRetired();
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            m_PreviousTransforms.Clear();
            m_Processor.Dispose();
            m_VisibilityShare.Dispose();
            m_GpuVisibility.Dispose();
            m_Residency.Dispose();
        }
    }
}
