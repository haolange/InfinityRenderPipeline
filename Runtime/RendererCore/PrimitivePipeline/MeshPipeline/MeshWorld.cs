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
    /// </summary>
    public sealed class MeshWorld : IDisposable
    {
        private readonly MeshScene m_Scene;
        private readonly MeshSceneResidency m_Residency;
        private readonly MeshVisibilityShare m_VisibilityShare;
        private readonly MeshDrawPipeline m_Processor;
        private readonly PassRegistry m_Registry;
        private readonly Dictionary<ulong, ComputeBuffer> m_PreviousTransforms = new Dictionary<ulong, ComputeBuffer>(8);
        private Camera m_ActiveCamera;
        private EMeshBackendPolicy? m_BackendOverride;
        private bool m_Disposed;

        public MeshScene Scene => m_Scene;
        public MeshSceneResidency Residency => m_Residency;
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

            m_Residency = new MeshSceneResidency(resourcePool, m_Scene);
            m_VisibilityShare = new MeshVisibilityShare();
            m_Processor = new MeshDrawPipeline(m_Scene, m_Residency, resourcePool);
            m_Registry = new PassRegistry();
        }

        public void BeginCamera(Camera camera)
        {
            m_ActiveCamera = camera;
            m_PreviousTransforms.Clear();
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

        public MeshDrawRequest BuildRequest(in MeshView view, MeshPassId passId)
        {
            MeshPassDefinition definition = m_Registry.Get(passId);
            MeshFilterProgram filter = definition.defaultFilter;
            filter.layerMask = view.layerMask;
            filter.renderingLayerMask = view.renderingLayerMask;
            filter.filterRenderingLayers = definition.filterRenderingLayers;
            filter.excludeCameraMotionOnly = definition.excludeCameraMotionOnly;
            m_PreviousTransforms.TryGetValue(view.viewKey, out ComputeBuffer previousTransforms);

            return new MeshDrawRequest
            {
                filter = filter,
                sort = definition.defaultSort,
                backendPolicy = SelectPolicy(),
                shaderPassIndex = definition.shaderPassIndex,
                lightModeTag = definition.lightModeTag,
                viewPosition = view.viewPosition,
                viewKey = view.viewKey,
                previousTransforms = previousTransforms
            };
        }

        public MeshVisibilityHandle AcquireVisibility(in MeshView view)
        {
            Plane[] planes = view.CopyPlanes();
            return m_VisibilityShare.Acquire(
                m_Scene,
                view.viewKey,
                planes,
                view.PolicyId,
                view.enableVisibility,
                view.subviewIndex);
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
            m_Residency.Dispose();
        }
    }
}
