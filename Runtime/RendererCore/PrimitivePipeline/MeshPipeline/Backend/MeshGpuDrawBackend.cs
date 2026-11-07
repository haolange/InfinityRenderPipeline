using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Graph handles of the entity columns a culling dispatch reads.</summary>
    internal struct MeshCullInputs
    {
        public RGBufferHandle boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers;
        public int entityCount;
    }

    /// <summary>
    /// Per-draw-list GPU buffers. The object is an empty CPU shell between uses; the buffers belong to the resource pool
    /// and are rented once, before recording, at the draw list's capacity.
    /// </summary>
    internal sealed class MeshGpuDrawPayload
    {
        internal BufferHandle commandMeta, visibleCounts, indirectArgs, instanceToEntity;
        internal ResourceReuseFence metaWait, countsWait, argsWait, instancesWait;
        internal int commandCapacity, instanceCapacity;

        internal void WaitForReuse(CommandBuffer commands)
        {
            metaWait.Wait(commands, false); countsWait.Wait(commands, false);
            argsWait.Wait(commands, false); instancesWait.Wait(commands, false);
        }
    }

    /// <summary>
    /// GPU indirect drawing: per draw list, one compute producer clears counts, compacts visible members of the pass's
    /// candidate table into per-command instance ranges and writes indirect arguments; the raster consumer issues one
    /// <c>DrawMeshInstancedIndirect</c> per live command. There is no batching and no CPU fallback.
    /// </summary>
    internal sealed class MeshGpuDrawBackend : IDisposable
    {
        static readonly int ID_EntityCount = Shader.PropertyToID("_EntityCount");
        static readonly int ID_FrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        static readonly int ID_EntityBoundsCenter = Shader.PropertyToID("_EntityBoundsCenter");
        static readonly int ID_EntityBoundsExtent = Shader.PropertyToID("_EntityBoundsExtent");
        static readonly int ID_EntityFlags = Shader.PropertyToID("_EntityFlags");
        static readonly int ID_EntityLayerMask = Shader.PropertyToID("_EntityLayerMask");
        static readonly int ID_EntitySceneCullingMask = Shader.PropertyToID("_EntitySceneCullingMask");
        static readonly int ID_EntityRenderingLayer = Shader.PropertyToID("_EntityRenderingLayer");
        static readonly int ID_ViewLayerMask = Shader.PropertyToID("_ViewLayerMask");
        static readonly int ID_ViewSceneCullingMaskLow = Shader.PropertyToID("_ViewSceneCullingMaskLow");
        static readonly int ID_ViewSceneCullingMaskHigh = Shader.PropertyToID("_ViewSceneCullingMaskHigh");
        static readonly int ID_ViewRenderingLayerMask = Shader.PropertyToID("_ViewRenderingLayerMask");
        static readonly int ID_FilterRenderingLayers = Shader.PropertyToID("_FilterRenderingLayers");
        static readonly int ID_Visibility = Shader.PropertyToID("_Visibility");
        static readonly int ID_CommandCount = Shader.PropertyToID("_CommandCount");
        static readonly int ID_CandidateCount = Shader.PropertyToID("_CandidateCount");
        static readonly int ID_CommandMeta = Shader.PropertyToID("_CommandMeta");
        static readonly int ID_CandidateTable = Shader.PropertyToID("_CandidateTable");
        static readonly int ID_InstanceToEntity = Shader.PropertyToID("_InstanceToEntity");
        static readonly int ID_VisibleCounts = Shader.PropertyToID("_VisibleCounts");
        static readonly int ID_IndirectArgs = Shader.PropertyToID("_IndirectArgs");

        readonly ResourcePool m_Pool;
        readonly ComputeShader m_Shader;
        readonly int m_CullKernel = -1, m_ClearKernel = -1, m_CompactKernel = -1, m_ArgsKernel = -1;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        readonly Stack<MeshGpuDrawPayload> m_FreePayloads = new Stack<MeshGpuDrawPayload>(8);
        readonly List<MeshGpuDrawPayload> m_Retired = new List<MeshGpuDrawPayload>(8);

        /// <summary>Device capability only; the backend additionally needs its compute shader.</summary>
        public static bool DeviceSupportsIndirect => SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing;

        public bool isSupported { get; }
        internal ComputeShader shader => m_Shader;

        internal MeshGpuDrawBackend(ResourcePool pool, ComputeShader shader)
        {
            m_Pool = pool;
            m_Shader = shader;
            if (shader == null || !DeviceSupportsIndirect) return;
            m_CullKernel = Find(shader, "CullFrustum");
            m_ClearKernel = Find(shader, "ClearCounts");
            m_CompactKernel = Find(shader, "Compact");
            m_ArgsKernel = Find(shader, "BuildIndirectArgs");
            isSupported = m_CullKernel >= 0 && m_ClearKernel >= 0 && m_CompactKernel >= 0 && m_ArgsKernel >= 0;
        }

        static int Find(ComputeShader shader, string kernel) => shader.HasKernel(kernel) ? shader.FindKernel(kernel) : -1;

        void RequireSupported()
        {
            if (!isSupported) throw new InvalidOperationException("GPU indirect mesh drawing requires Compute_MeshDrawPipeline with its culling and compaction kernels.");
        }

        internal int FindKernel(string kernel)
        {
            RequireSupported();
            int index = Find(m_Shader, kernel);
            if (index < 0) throw new InvalidOperationException($"Compute_MeshDrawPipeline has no kernel '{kernel}'.");
            return index;
        }

        #region Culling
        internal void BindCullInputs<TCommands>(TCommands commands, int kernel, in MeshView view, Vector4[] frustumPlanes,
            in MeshCullInputs inputs, RGBufferHandle visibility) where TCommands : struct, IComputeCommands
        {
            commands.SetComputeVectorArrayParam(m_Shader, ID_FrustumPlanes, frustumPlanes);
            BindCullBounds(commands, kernel, inputs);
            commands.SetComputeIntParam(m_Shader, ID_ViewLayerMask, view.layerMask);
            commands.SetComputeIntParam(m_Shader, ID_ViewSceneCullingMaskLow, unchecked((int)view.sceneCullingMask));
            commands.SetComputeIntParam(m_Shader, ID_ViewSceneCullingMaskHigh, unchecked((int)(view.sceneCullingMask >> 32)));
            commands.SetComputeIntParam(m_Shader, ID_ViewRenderingLayerMask, (int)view.renderingLayerMask);
            commands.SetComputeIntParam(m_Shader, ID_FilterRenderingLayers, view.FilterRenderingLayers ? 1 : 0);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityFlags, inputs.flags);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityLayerMask, inputs.layerMask);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntitySceneCullingMask, inputs.sceneCullingMask);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityRenderingLayer, inputs.renderingLayers);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_Visibility, visibility);
        }

        internal void BindCullBounds<TCommands>(TCommands commands, int kernel, in MeshCullInputs inputs) where TCommands : struct, IComputeCommands
        {
            commands.SetComputeIntParam(m_Shader, ID_EntityCount, inputs.entityCount);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsCenter, inputs.boundsCenter);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsExtent, inputs.boundsExtent);
        }

        internal void DispatchCullFrustum<TCommands>(TCommands commands, in MeshView view, Vector4[] frustumPlanes, in MeshCullInputs inputs,
            RGBufferHandle visibility, MeshWorldStats stats) where TCommands : struct, IComputeCommands
        {
            RequireSupported();
            BindCullInputs(commands, m_CullKernel, view, frustumPlanes, inputs, visibility);
            commands.DispatchCompute(m_Shader, m_CullKernel, math.max(1, (inputs.entityCount + 63) / 64), 1, 1);
            stats.gpuCullDispatches++;
        }
        #endregion

        #region Draw lists
        internal MeshGpuDrawPayload RentPayload(int commandCount, int instanceCount)
        {
            RequireSupported();
            int commands = math.ceilpow2(math.max(64, commandCount));
            int instances = math.ceilpow2(math.max(256, instanceCount));
            MeshGpuDrawPayload payload = m_FreePayloads.Count > 0 ? m_FreePayloads.Pop() : new MeshGpuDrawPayload();
            try
            {
                payload.commandMeta = m_Pool.GetBuffer(new BufferDescriptor(commands * 4, sizeof(uint)) { name = "MeshDrawCommandMeta" }, true, false, out payload.metaWait);
                payload.visibleCounts = m_Pool.GetBuffer(new BufferDescriptor(commands, sizeof(uint)) { name = "MeshDrawVisibleCounts" }, true, false, out payload.countsWait);
                payload.indirectArgs = m_Pool.GetBuffer(new BufferDescriptor(commands * 5, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "MeshDrawIndirectArgs" }, true, false, out payload.argsWait);
                payload.instanceToEntity = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(uint)) { name = "MeshInstanceToEntity" }, true, false, out payload.instancesWait);
            }
            catch
            {
                ReleaseBuffers(payload, default);
                m_FreePayloads.Push(payload);
                throw;
            }
            payload.commandCapacity = commands;
            payload.instanceCapacity = instances;
            return payload;
        }

        internal void Upload(CommandBuffer commands, MeshGpuDrawPayload payload, MeshPassCache cache, int commandCount)
        {
            if (commandCount > payload.commandCapacity) throw new InvalidOperationException("A GPU draw list outgrew its record-time command capacity.");
            payload.WaitForReuse(commands);
            NativeArray<uint> meta = cache.GetCommandMeta();
            if (commandCount > 0) commands.SetBufferData(payload.commandMeta.buffer, meta, 0, 0, commandCount * 4);
        }

        internal void Compact(CommandBuffer commands, MeshGpuDrawPayload payload, MeshPassCache cache, int commandCount, int candidateCount,
            ComputeBuffer visibility, MeshWorldStats stats)
        {
            RequireSupported();
            commands.SetComputeIntParam(m_Shader, ID_CommandCount, commandCount);
            commands.SetComputeIntParam(m_Shader, ID_CandidateCount, candidateCount);
            commands.SetComputeBufferParam(m_Shader, m_ClearKernel, ID_VisibleCounts, payload.visibleCounts.buffer);
            commands.SetComputeBufferParam(m_Shader, m_ClearKernel, ID_IndirectArgs, payload.indirectArgs.buffer);
            commands.DispatchCompute(m_Shader, m_ClearKernel, math.max(1, (commandCount + 63) / 64), 1, 1);

            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_CandidateTable, cache.candidateBuffer.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_Visibility, visibility);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_CommandMeta, payload.commandMeta.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_VisibleCounts, payload.visibleCounts.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_InstanceToEntity, payload.instanceToEntity.buffer);
            commands.DispatchCompute(m_Shader, m_CompactKernel, math.max(1, (candidateCount + 63) / 64), 1, 1);

            commands.SetComputeBufferParam(m_Shader, m_ArgsKernel, ID_CommandMeta, payload.commandMeta.buffer);
            commands.SetComputeBufferParam(m_Shader, m_ArgsKernel, ID_VisibleCounts, payload.visibleCounts.buffer);
            commands.SetComputeBufferParam(m_Shader, m_ArgsKernel, ID_IndirectArgs, payload.indirectArgs.buffer);
            commands.DispatchCompute(m_Shader, m_ArgsKernel, math.max(1, (commandCount + 63) / 64), 1, 1);
            stats.compactDispatches++;
        }

        internal void Submit(CommandBuffer commands, MeshGpuDrawPayload payload, MeshPassCache cache, int commandCount,
            in MeshWorldGpuTables tables, in MeshMotionBinding motion, EMeshViewKind viewKind)
        {
            string marker = MeshDrawProfiling.Draw(viewKind, true);
            commands.BeginSample(marker);
            try
            {
                NativeArray<uint> meta = cache.GetCommandMeta();
                NativeArray<MeshDrawCommand> records = cache.commands;
                m_Properties.Clear();
                m_Properties.SetBuffer(InfinityShaderIDs.InstanceToEntity, payload.instanceToEntity.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.EntityTransforms, tables.version.matrices.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.EntityRenderingLayers, tables.renderingLayers.buffer);
                (motion.isValid ? motion : new MeshMotionBinding(tables.version, null)).Bind(m_Properties);
                for (int command = 0; command < commandCount; ++command)
                {
                    if (records[command].refCount == 0) continue;
                    MeshDrawCommandBinding binding = cache.GetBinding(command);
                    if (binding.mesh == null || binding.material == null)
                    {
                        cache.ReportMissing(command);
                        continue;
                    }
                    m_Properties.SetInt(InfinityShaderIDs.InstanceOffset, (int)meta[command * 4 + 3]);
                    commands.DrawMeshInstancedIndirect(binding.mesh, binding.submesh, binding.material, binding.passIndex,
                        payload.indirectArgs.buffer, command * 5 * sizeof(uint), m_Properties);
                }
            }
            finally { commands.EndSample(marker); }
        }

        internal void RetirePayload(MeshGpuDrawPayload payload)
        {
            if (payload != null) m_Retired.Add(payload);
        }

        void ReleaseBuffers(MeshGpuDrawPayload payload, ResourceReuseFence reuseFence)
        {
            if (payload.commandMeta.buffer != null) m_Pool.ReleaseBuffer(payload.commandMeta, reuseFence);
            if (payload.visibleCounts.buffer != null) m_Pool.ReleaseBuffer(payload.visibleCounts, reuseFence);
            if (payload.indirectArgs.buffer != null) m_Pool.ReleaseBuffer(payload.indirectArgs, reuseFence);
            if (payload.instanceToEntity.buffer != null) m_Pool.ReleaseBuffer(payload.instanceToEntity, reuseFence);
            payload.commandMeta = payload.visibleCounts = payload.indirectArgs = payload.instanceToEntity = default;
            payload.metaWait = payload.countsWait = payload.argsWait = payload.instancesWait = default;
            payload.commandCapacity = payload.instanceCapacity = 0;
        }

        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            foreach (MeshGpuDrawPayload payload in m_Retired)
            {
                ReleaseBuffers(payload, reuseFence);
                m_FreePayloads.Push(payload);
            }
            m_Retired.Clear();
        }
        #endregion

        public void Dispose()
        {
            FlushRetired(default);
            m_FreePayloads.Clear();
        }
    }
}
