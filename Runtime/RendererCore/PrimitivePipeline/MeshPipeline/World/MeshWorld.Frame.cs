using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline
{
    public sealed partial class MeshWorld
    {
        const int k_MaxPassCaches = 64;

        MeshWorldGpu m_Gpu;
        ResourcePool m_ResourcePool;
        MeshCpuDrawBackend m_CpuBackend;
        MeshGpuDrawBackend m_GpuBackend;
        MeshVisibilityShare m_VisibilityShare;
        MeshGpuVisibilityCache m_GpuVisibility;
        readonly List<MeshPassCache> m_PassCaches = new List<MeshPassCache>(16);
        readonly Dictionary<ulong, MeshMotionBinding> m_PreviousTransforms = new Dictionary<ulong, MeshMotionBinding>(8);
        Camera m_ActiveCamera;
        EMeshBackendPolicy? m_BackendOverride;
        internal EMeshBackendPolicy assetBackendPolicy;

        internal MeshWorldGpu gpu => m_Gpu;
        internal ResourcePool resourcePool => m_ResourcePool;
        internal MeshCpuDrawBackend cpuBackend => m_CpuBackend;
        internal MeshGpuDrawBackend gpuBackend => m_GpuBackend;
        internal MeshVisibilityShare visibilityShare => m_VisibilityShare;
        internal MeshGpuVisibilityCache gpuVisibility => m_GpuVisibility;
        internal MeshTransformVersionCache transformVersions => m_Gpu.transformVersions;
        internal IReadOnlyList<MeshPassCache> passCaches => m_PassCaches;

        void InternPresets()
        {
            foreach (MeshPassDesc preset in MeshPassPresets.all) GetPassCache(preset);
        }

        /// <summary>
        /// The interned cache of a pass desc. The presets exist from construction; another desc is built from the world
        /// on its first request. A world interns at most 64 descs and never evicts one.
        /// </summary>
        internal MeshPassCache GetPassCache(in MeshPassDesc desc)
        {
            foreach (MeshPassCache existing in m_PassCaches)
                if (existing.desc.Equals(desc)) return existing;
            ThrowIfDisposed();
            if (m_TransactionOpen) throw new InvalidOperationException("A pass cache cannot be interned while a MeshWorldUpdate is open.");
            if (m_PassCaches.Count >= k_MaxPassCaches) throw new InvalidOperationException($"A MeshWorld interns at most {k_MaxPassCaches} pass descs.");
            var cache = new MeshPassCache(desc);
            try
            {
                cache.Build(this);
                if (m_GpuBackend.isSupported) cache.UploadCandidates(m_ResourcePool, stats);
            }
            catch
            {
                cache.ReleaseGpu(m_ResourcePool);
                cache.Dispose();
                throw;
            }
            m_PassCaches.Add(cache);
            stats.passCacheBuilds++;
            PublishStats();
            return cache;
        }

        /// <summary>Entity slots with at least one member element in the pass (for example shadow casters).</summary>
        internal Unity.Collections.NativeArray<int> GetPassMembers(in MeshPassDesc desc) => GetPassCache(desc).entityMemberCount;

        public void BeginCamera(Camera camera)
        {
            m_ActiveCamera = camera;
            m_PreviousTransforms.Clear();
            stats.BeginCamera();
            var debug = InfinityDebugDisplaySettings.current.mesh;
            debug.entityCount = entityCount;
            debug.elementCount = elementCount;
            debug.passCacheCount = stats.passCacheCount;
            debug.commandCount = stats.commandCount;
            m_GpuVisibility.BeginCamera();
            m_VisibilityShare.BeginCamera();
        }

        internal void BindPreviousTransforms(ulong viewKey, MeshMotionBinding binding)
        {
            if (!binding.isValid) m_PreviousTransforms.Remove(viewKey);
            else m_PreviousTransforms[viewKey] = binding;
        }

        internal MeshMotionBinding GetPreviousTransforms(ulong viewKey)
        {
            m_PreviousTransforms.TryGetValue(viewKey, out MeshMotionBinding binding);
            return binding;
        }

        public void SetBackendOverride(EMeshBackendPolicy? policy) => m_BackendOverride = policy;

        /// <summary>
        /// Backend for one draw list. Order: explicit camera or test override, then the Rendering Debugger, then the asset.
        /// Auto and GPU Indirect follow device capability. Back-to-front passes record on the CPU backend.
        /// </summary>
        internal EMeshBackendPolicy SelectBackend(in MeshPassDesc desc)
        {
            var debug = InfinityDebugDisplaySettings.current.mesh;
            EMeshBackendPolicy requested = m_BackendOverride ??
                (m_ActiveCamera != null && RenderCaptureService.HasBackendOverride(m_ActiveCamera)
                    ? RenderCaptureService.BackendFor(m_ActiveCamera) : debug.backendOverride ?? assetBackendPolicy);
            EMeshBackendPolicy actual;
            if (requested == EMeshBackendPolicy.CpuDirect)
            {
                actual = EMeshBackendPolicy.CpuDirect;
                debug.backendReason = "Requested CPU Direct";
            }
            else if (!m_GpuBackend.isSupported)
            {
                actual = EMeshBackendPolicy.CpuDirect;
                debug.backendReason = "Indirect drawing unsupported; CPU Direct";
            }
            else
            {
                actual = EMeshBackendPolicy.GpuIndirect;
                debug.backendReason = "Indirect drawing supported; back-to-front passes use CPU Direct";
            }
            debug.requestedBackend = requested;
            debug.actualBackend = actual;
            return desc.sortMode == ESortMode.BackToFront ? EMeshBackendPolicy.CpuDirect : actual;
        }

        internal BufferHandle GetGpuVisibilityBuffer(in MeshView view) => m_GpuVisibility.GetBufferHandle(view, entityHighWater);
        internal bool NeedsGpuCull(in MeshView view) => m_GpuVisibility.NeedsCull(view);
        internal void MarkGpuCulled(in MeshView view) => m_GpuVisibility.MarkCulled(view);

        internal void ReleaseView(ulong viewKey)
        {
            m_PreviousTransforms.Remove(viewKey);
            m_GpuVisibility.ReleaseView(viewKey);
        }

        /// <summary>Uploads dirty entity columns, the next transform version and stale pass candidate tables.</summary>
        public void UpdateResidency()
        {
            ThrowIfDisposed();
            m_Gpu.Update();
            if (!m_GpuBackend.isSupported) return;
            foreach (MeshPassCache cache in m_PassCaches) cache.UploadCandidates(m_ResourcePool, stats);
        }

        /// <summary>After native submit: return retired versions, payloads and buffers to the pool.</summary>
        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            m_Gpu.transformVersions.AfterSubmit(reuseFence);
            m_CpuBackend.FlushRetired(reuseFence);
            m_GpuBackend.FlushRetired(reuseFence);
            m_GpuVisibility.FlushRetired();
        }

        void DisposeFrameState()
        {
            m_PreviousTransforms.Clear();
            foreach (MeshPassCache cache in m_PassCaches) cache.ReleaseGpu(m_ResourcePool);
        }
    }
}
