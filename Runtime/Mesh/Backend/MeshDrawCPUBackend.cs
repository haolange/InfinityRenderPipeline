using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// CPU direct drawing: a draw list's sorted instance-to-entity table is uploaded once, then every merged run is one
    /// <c>DrawMeshInstancedProcedural</c>. The property block's buffers are set once per draw list.
    /// </summary>
    internal sealed class MeshDrawCPUBackend : IDisposable
    {
        static readonly GlobalKeyword s_LodDither = GlobalKeyword.Create("INFINITY_LOD_DITHER");
        readonly ResourcePool m_Pool;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        readonly List<BufferHandle> m_Retired = new List<BufferHandle>(16);

        internal MeshDrawCPUBackend(ResourcePool pool) => m_Pool = pool;

        /// <summary>Record-time allocation of the instance-to-entity and fade tables, sized by the pass's member count.</summary>
        internal void AllocateInstances(int memberCount, out BufferHandle instances, out BufferHandle fades)
        {
            instances = m_Pool.GetBuffer(new BufferDescriptor(math.max(16, memberCount), sizeof(uint)) { name = "MeshInstanceToEntity" });
            try
            {
                fades = m_Pool.GetBuffer(new BufferDescriptor(math.max(16, memberCount), sizeof(float)) { name = "MeshInstanceFade" });
            }
            catch
            {
                m_Pool.ReleaseBuffer(instances);
                instances = default;
                throw;
            }
        }

        internal void Upload(CommandBuffer commands, MeshPassCache cache, ref MeshDrawListBuild build, in BufferHandle instances, in BufferHandle fades, in MeshView view)
        {
            using (MeshDrawProfiling.Cpu("Complete", view, cache.desc.lightMode).Auto())
            {
                build.job.Complete();
            }
            int count = build.instanceCount;
            if (count > instances.buffer.count)
            {
                throw new InvalidOperationException("A CPU draw list outgrew its record-time instance capacity.");
            }
            if (count == 0)
            {
                return;
            }
            using (MeshDrawProfiling.Cpu("Upload", view, cache.desc.lightMode).Auto())
            {
                commands.SetBufferData(instances.buffer, build.instanceToEntity, 0, 0, count);
                commands.SetBufferData(fades.buffer, build.instanceFade, 0, 0, count);
            }
        }

        internal void SubmitMeshDrawList(CommandBuffer commands, MeshPassCache cache, in MeshDrawListBuild build, in BufferHandle instances, in BufferHandle fades,
            in MeshWorldGPUTables tables, in MeshMotionBinding motion, in MeshView view)
        {
            if (build.draws.Length == 0)
            {
                return;
            }
            using var cpuDraw = MeshDrawProfiling.Cpu("Draw", view, cache.desc.lightMode).Auto();
            ProfilingSampler marker = CachedSampler.Get(MeshDrawProfiling.Draw(view.kind, false));
            marker.Begin(commands);
            bool dithering = false;
            try
            {
                m_Properties.Clear();
                m_Properties.SetBuffer(MeshShaderIDs.InstanceToEntity, instances.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.InstanceFade, fades.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.EntityTransforms, tables.version.matrices.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.EntityRenderingLayers, tables.renderingLayers.buffer);
                (motion.isValid ? motion : new MeshMotionBinding(tables.version, null)).Bind(m_Properties);
                foreach (MeshDrawCall draw in build.draws)
                {
                    MeshDrawCommandBinding binding = cache.GetBinding(draw.command);
                    if (binding.mesh == null || binding.material == null)
                    {
                        cache.ReportMissing(draw.command);
                        continue;
                    }
                    if (draw.fading != dithering)
                    {
                        dithering = draw.fading;
                        commands.SetKeyword(s_LodDither, dithering);
                    }
                    m_Properties.SetInt(MeshShaderIDs.InstanceOffset, draw.instanceOffset);
                    commands.DrawMeshInstancedProcedural(binding.mesh, binding.submesh, binding.material, binding.passIndex, draw.instanceCount, m_Properties);
                }
            }
            finally
            {
                if (dithering)
                {
                    commands.SetKeyword(s_LodDither, false);
                }
                marker.End(commands);
            }
        }

        /// <summary>Logical release when the graph is dropped; the buffer returns to the pool after native submit.</summary>
        internal void Retire(in BufferHandle instances)
        {
            if (instances.buffer != null)
            {
                m_Retired.Add(instances);
            }
        }

        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            foreach (BufferHandle buffer in m_Retired)
            {
                m_Pool.ReleaseBuffer(buffer, reuseFence);
            }
            m_Retired.Clear();
        }

        public void Dispose() => FlushRetired(default);
    }
}
