using System;
using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    public sealed partial class MeshWorld
    {
        MeshWorldGPU m_Gpu;
        ResourcePool m_ResourcePool;
        MeshDrawCPUBackend m_CPUBackend;
        MeshDrawGPUBackend m_GPUBackend;
        MeshVisibilityShare m_VisibilityShare;
        MeshGPUVisibilityCache m_GPUVisibility;
        readonly List<MeshPassCache> m_PassCaches = new List<MeshPassCache>(16);
        readonly Dictionary<ulong, MeshMotionBinding> m_PreviousTransforms = new Dictionary<ulong, MeshMotionBinding>(8);
        // Backend the owner requests for the current camera: a diagnostics override, then the Rendering Debugger, then the asset.
        internal EMeshDrawBackendPolicy requestedBackend;

        internal MeshWorldGPU gpu => m_Gpu;
        internal ResourcePool resourcePool => m_ResourcePool;
        internal MeshDrawCPUBackend cpuBackend => m_CPUBackend;
        internal MeshDrawGPUBackend gpuBackend => m_GPUBackend;
        internal MeshVisibilityShare visibilityShare => m_VisibilityShare;
        internal MeshGPUVisibilityCache gpuVisibility => m_GPUVisibility;
        internal MeshTransformVersionCache transformVersions => m_Gpu.transformVersions;
        internal IReadOnlyList<MeshPassCache> passCaches => m_PassCaches;

        void InternPresets()
        {
            foreach (MeshPassDesc preset in MeshPassTable.all)
            {
                CreatePassCache(preset);
            }
        }

        /// <summary>The nine pass caches created with the world. A desc that is not one of them throws.</summary>
        internal MeshPassCache GetPassCache(in MeshPassDesc desc)
        {
            foreach (MeshPassCache existing in m_PassCaches)
                if (existing.desc.Equals(desc))
                {
                    return existing;
                }
            throw new ArgumentException($"'{desc.lightMode}' is not one of the nine mesh passes.");
        }

        void CreatePassCache(in MeshPassDesc desc)
        {
            ThrowIfDisposed();
            var cache = new MeshPassCache(desc);
            try
            {
                cache.Build(this);
                if (m_GPUBackend.isSupported)
                {
                    cache.UploadCandidates(m_ResourcePool, stats);
                }
            }
            catch
            {
                cache.ReleaseGPU(m_ResourcePool);
                cache.Dispose();
                throw;
            }
            m_PassCaches.Add(cache);
            stats.passCacheBuilds++;
            PublishStats();
        }

        /// <summary>Entity slots with at least one member element in the pass (for example shadow casters).</summary>
        internal Unity.Collections.NativeArray<int> GetPassMembers(in MeshPassDesc desc) => GetPassCache(desc).entityMemberCount;

        public void BeginCamera(Camera camera)
        {
            m_PreviousTransforms.Clear();
            stats.BeginCamera(camera);
            m_VisibilityShare.BeginCamera();
        }

        internal void BindPreviousTransforms(ulong viewKey, MeshMotionBinding binding)
        {
            if (!binding.isValid)
            {
                m_PreviousTransforms.Remove(viewKey);
            }
            else
            {
                m_PreviousTransforms[viewKey] = binding;
            }
        }

        internal MeshMotionBinding GetPreviousTransforms(ulong viewKey)
        {
            m_PreviousTransforms.TryGetValue(viewKey, out MeshMotionBinding binding);
            return binding;
        }

        /// <summary>
        /// Backend for one draw list. Auto and GPU Indirect follow device capability. A pass with
        /// <see cref="MeshPassDesc.cpuBackend"/> records on the CPU.
        /// </summary>
        internal EMeshDrawBackendPolicy SelectBackend(in MeshPassDesc desc)
        {
            bool gpu = !desc.cpuBackend && requestedBackend != EMeshDrawBackendPolicy.CPUDirect && m_GPUBackend.isSupported;
            return gpu ? EMeshDrawBackendPolicy.GPUIndirect : EMeshDrawBackendPolicy.CPUDirect;
        }

        internal BufferHandle GetGPUVisibilityBuffer(in MeshView view) => m_GPUVisibility.GetBufferHandle(view, entityHighWater);

        internal void ReleaseView(ulong viewKey)
        {
            m_PreviousTransforms.Remove(viewKey);
            m_GPUVisibility.ReleaseView(viewKey);
        }

        /// <summary>Uploads dirty entity columns, the next transform version and stale pass candidate tables.</summary>
        public void UpdateResidency()
        {
            ThrowIfDisposed();
            m_Gpu.Update();
            if (!m_GPUBackend.isSupported)
            {
                return;
            }
            foreach (MeshPassCache cache in m_PassCaches)
            {
                cache.UploadCandidates(m_ResourcePool, stats);
            }
        }

        /// <summary>After native submit: return retired versions, payloads and buffers to the pool.</summary>
        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            m_Gpu.transformVersions.AfterSubmit(reuseFence);
            m_CPUBackend.FlushRetired(reuseFence);
            m_GPUBackend.FlushRetired(reuseFence);
            m_GPUVisibility.FlushRetired();
        }

        void DisposeFrameState()
        {
            m_PreviousTransforms.Clear();
            foreach (MeshPassCache cache in m_PassCaches)
            {
                cache.ReleaseGPU(m_ResourcePool);
            }
        }
    }
}
