using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// CPU direct drawing: a draw list's sorted instance-to-entity table is uploaded once, then every merged run is one
    /// <c>DrawMeshInstancedProcedural</c>. The property block's buffers are set once per draw list.
    /// </summary>
    internal sealed class MeshCpuDrawBackend : IDisposable
    {
        readonly ResourcePool m_Pool;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        readonly List<BufferHandle> m_Retired = new List<BufferHandle>(16);

        internal MeshCpuDrawBackend(ResourcePool pool) => m_Pool = pool;

        /// <summary>Record-time allocation sized by the pass's member count; never grown during execution.</summary>
        internal BufferHandle AllocateInstances(int memberCount)
            => m_Pool.GetBuffer(new BufferDescriptor(math.max(16, memberCount), sizeof(uint)) { name = "MeshInstanceToEntity" });

        internal void Upload(CommandBuffer commands, ref MeshDrawListBuild build, in BufferHandle instances)
        {
            build.job.Complete();
            int count = build.instanceCount;
            if (count > instances.buffer.count) throw new InvalidOperationException("A CPU draw list outgrew its record-time instance capacity.");
            if (count > 0) commands.SetBufferData(instances.buffer, build.instanceToEntity, 0, 0, count);
        }

        internal void Submit(CommandBuffer commands, MeshPassCache cache, in MeshDrawListBuild build, in BufferHandle instances,
            in MeshWorldGpuTables tables, in MeshMotionBinding motion, EMeshViewKind viewKind)
        {
            if (build.draws.Length == 0) return;
            string marker = MeshDrawProfiling.Draw(viewKind, false);
            commands.BeginSample(marker);
            try
            {
                m_Properties.Clear();
                m_Properties.SetBuffer(InfinityShaderIDs.InstanceToEntity, instances.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.EntityTransforms, tables.version.matrices.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.EntityRenderingLayers, tables.renderingLayers.buffer);
                (motion.isValid ? motion : new MeshMotionBinding(tables.version, null)).Bind(m_Properties);
                foreach (MeshInstancedDraw draw in build.draws)
                {
                    MeshDrawCommandBinding binding = cache.GetBinding(draw.command);
                    if (binding.mesh == null || binding.material == null)
                    {
                        cache.ReportMissing(draw.command);
                        continue;
                    }
                    m_Properties.SetInt(InfinityShaderIDs.InstanceOffset, draw.instanceOffset);
                    commands.DrawMeshInstancedProcedural(binding.mesh, binding.submesh, binding.material, binding.passIndex, draw.instanceCount, m_Properties);
                }
            }
            finally { commands.EndSample(marker); }
        }

        /// <summary>Logical release when the graph is dropped; the buffer returns to the pool after native submit.</summary>
        internal void Retire(in BufferHandle instances)
        {
            if (instances.buffer != null) m_Retired.Add(instances);
        }

        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            foreach (BufferHandle buffer in m_Retired) m_Pool.ReleaseBuffer(buffer, reuseFence);
            m_Retired.Clear();
        }

        public void Dispose() => FlushRetired(default);
    }
}
