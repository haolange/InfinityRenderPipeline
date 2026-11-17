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
        // One cache per entry of the pass table, at the same index.
        readonly List<MeshPassCache> m_PassCaches = new List<MeshPassCache>(16);
        readonly MeshPassDesc[] m_Passes;
        // Backend the owner requests for the current camera: a diagnostics override, then the Rendering Debugger, then the asset.
        internal EMeshDrawBackendPolicy requestedBackend;

        internal MeshWorldGPU gpu => m_Gpu;
        internal ResourcePool resourcePool => m_ResourcePool;
        internal MeshDrawCPUBackend cpuBackend => m_CPUBackend;
        internal MeshDrawGPUBackend gpuBackend => m_GPUBackend;
        internal MeshTransformVersionCache transformVersions => m_Gpu.transformVersions;
        internal IReadOnlyList<MeshPassCache> passCaches => m_PassCaches;
        /// <summary>The pass table the world was built with; a <see cref="MeshPassId"/> indexes it.</summary>
        internal ReadOnlySpan<MeshPassDesc> passes => m_Passes;

        // The highest Unity render queue.
        const int MaxRenderQueue = 5000;

        // Runs before any allocation, so an invalid table leaves nothing to release. The table is the pipeline's data; the
        // world reads it and never writes it.
        static MeshPassDesc[] ValidatePasses(MeshPassDesc[] passes)
        {
            if (passes == null || passes.Length == 0)
            {
                throw new ArgumentException("A mesh world draws at least one pass.", nameof(passes));
            }
            for (int i = 0; i < passes.Length; ++i)
            {
                MeshPassDesc desc = passes[i];
                if (string.IsNullOrEmpty(desc.lightMode) || string.IsNullOrEmpty(desc.token))
                {
                    throw new ArgumentException($"Mesh pass {i} has no LightMode or marker token.", nameof(passes));
                }
                if (desc.queueMin < 0 || desc.queueMin > desc.queueMax || desc.queueMax > MaxRenderQueue)
                {
                    throw new ArgumentException($"Mesh pass '{desc.lightMode}' has the queue range [{desc.queueMin}, {desc.queueMax}]; a range lies inside [0, {MaxRenderQueue}].", nameof(passes));
                }
                for (int j = 0; j < i; ++j)
                {
                    if (string.Equals(passes[j].lightMode, desc.lightMode, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Mesh pass '{desc.lightMode}' appears twice in the pass table.", nameof(passes));
                    }
                }
            }
            return passes;
        }

        void CreatePassCaches()
        {
            foreach (MeshPassDesc desc in m_Passes)
            {
                CreatePassCache(desc);
            }
        }

        /// <summary>The cache of one pass of the table, created with the world.</summary>
        internal MeshPassCache GetPassCache(MeshPassId pass) => m_PassCaches[pass.index];

        void CreatePassCache(in MeshPassDesc desc)
        {
            ThrowIfDisposed();
            var cache = new MeshPassCache(desc);
            try
            {
                cache.Build(this);
                if (m_GPUBackend.isSupported)
                {
                    cache.UploadTables(m_ResourcePool, stats);
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
        internal Unity.Collections.NativeArray<int> GetPassMembers(MeshPassId pass) => GetPassCache(pass).entityMemberCount;

        public void BeginCamera(Camera camera) => stats.BeginCamera(camera);

        /// <summary>Backend for the draw lists of one pass; see <see cref="SelectBackend(EMeshSortKey)"/>.</summary>
        internal EMeshDrawBackendPolicy SelectBackend(MeshPassId pass) => SelectBackend(m_Passes[pass.index].sortKey);

        /// <summary>
        /// Backend for passes with one sort key. A translucent color key needs strict back-to-front order, which the GPU
        /// backend's atomic append does not keep, so those passes record on the CPU. Every other key follows the requested
        /// policy and device capability (P6: the choice changes cost, not the image).
        /// </summary>
        internal EMeshDrawBackendPolicy SelectBackend(EMeshSortKey sortKey)
        {
            bool gpu = sortKey != EMeshSortKey.TranslucentColor && requestedBackend != EMeshDrawBackendPolicy.CPUDirect && m_GPUBackend.isSupported;
            return gpu ? EMeshDrawBackendPolicy.GPUIndirect : EMeshDrawBackendPolicy.CPUDirect;
        }

        /// <summary>Uploads dirty entity columns, the next transform version and stale pass candidate and command tables.</summary>
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
                cache.UploadTables(m_ResourcePool, stats);
            }
        }

        /// <summary>After native submit: return retired versions, payloads and buffers to the pool.</summary>
        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            m_Gpu.transformVersions.AfterSubmit(reuseFence);
            m_CPUBackend.FlushRetired(reuseFence);
            m_GPUBackend.FlushRetired(reuseFence);
        }

        void DisposeFrameState()
        {
            foreach (MeshPassCache cache in m_PassCaches)
            {
                cache.ReleaseGPU(m_ResourcePool);
            }
        }
    }
}
