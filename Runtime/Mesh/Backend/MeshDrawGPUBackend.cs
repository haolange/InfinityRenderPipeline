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
        public RGBufferHandle boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains;
        public int entityCount;
    }

    /// <summary>
    /// Per-draw-list GPU buffers. The object is an empty CPU shell between uses; the buffers belong to the resource pool
    /// and are rented once, before recording, at the draw list's capacity.
    /// </summary>
    internal sealed class MeshGPUDrawPayload
    {
        internal BufferHandle commandMeta, indirectArgs, instanceToEntity, instanceFade;
        internal ResourceReuseFence metaWait, argsWait, instancesWait, fadeWait;
        internal int commandCapacity, instanceCapacity;

        internal void WaitForReuse(CommandBuffer commands)
        {
            metaWait.Wait(commands, false);
            argsWait.Wait(commands, false);
            instancesWait.Wait(commands, false);
            fadeWait.Wait(commands, false);
        }
    }

    /// <summary>
    /// GPU indirect drawing: per draw list, one compute producer initializes arguments and compacts visible members of the
    /// candidate table into per-command stable and fading instance ranges; the raster consumer
    /// issues one <c>DrawMeshInstancedIndirect</c> per live command and range. There is no batching and no CPU fallback.
    /// </summary>
    internal sealed class MeshDrawGPUBackend : IDisposable
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
        static readonly int ID_VisibilityBase = Shader.PropertyToID("_VisibilityBase");
        static readonly int ID_ViewCount = Shader.PropertyToID("_ViewCount");
        static readonly int ID_EntityStride = Shader.PropertyToID("_EntityStride");
        static readonly int ID_FrustumViews = Shader.PropertyToID("_FrustumViews");
        static readonly int ID_CommandCount = Shader.PropertyToID("_CommandCount");
        static readonly int ID_CandidateCount = Shader.PropertyToID("_CandidateCount");
        static readonly int ID_CommandMeta = Shader.PropertyToID("_CommandMeta");
        static readonly int ID_CandidateTable = Shader.PropertyToID("_CandidateTable");
        static readonly int ID_InstanceToEntity = Shader.PropertyToID("_InstanceToEntity");
        static readonly int ID_IndirectArgs = Shader.PropertyToID("_IndirectArgs");
        static readonly int ID_InstanceFade = Shader.PropertyToID("_InstanceFade");
        static readonly int ID_DitherLod = Shader.PropertyToID("_DitherLod");
        static readonly int ID_EntityLodChains = Shader.PropertyToID("_EntityLodChains");
        static readonly int ID_LodOrigin = Shader.PropertyToID("_LodOrigin");
        static readonly int ID_LodScale = Shader.PropertyToID("_LodScale");
        static readonly int ID_LodBias = Shader.PropertyToID("_LodBias");
        static readonly int ID_LodOrthographic = Shader.PropertyToID("_LodOrthographic");
        static readonly GlobalKeyword s_LodDither = GlobalKeyword.Create("INFINITY_LOD_DITHER");
        const int k_ArgsStride = 5;

        readonly ResourcePool m_Pool;
        readonly ComputeShader m_Shader;
        readonly int m_CullKernel = -1, m_InitializeArgsKernel = -1, m_CompactKernel = -1;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        readonly Stack<MeshGPUDrawPayload> m_FreePayloads = new Stack<MeshGPUDrawPayload>(8);
        readonly List<MeshGPUDrawPayload> m_Retired = new List<MeshGPUDrawPayload>(8);

        /// <summary>Device capability only; the backend additionally needs its compute shader.</summary>
        public static bool DeviceSupportsIndirect => SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing;

        public bool isSupported
        {
            get;
        }
        internal ComputeShader shader => m_Shader;

        internal MeshDrawGPUBackend(ResourcePool pool, ComputeShader shader)
        {
            m_Pool = pool;
            m_Shader = shader;
            if (shader == null || !DeviceSupportsIndirect)
            {
                return;
            }
            m_CullKernel = Find(shader, "CullFrustum");
            m_InitializeArgsKernel = Find(shader, "InitializeIndirectArgs");
            m_CompactKernel = Find(shader, "Compact");
            isSupported = m_CullKernel >= 0 && m_InitializeArgsKernel >= 0 && m_CompactKernel >= 0;
        }

        static int Find(ComputeShader shader, string kernel) => shader.HasKernel(kernel) ? shader.FindKernel(kernel) : -1;

        void RequireSupported()
        {
            if (!isSupported)
            {
                throw new InvalidOperationException("GPU indirect mesh drawing requires Compute_MeshDrawPipeline with its culling and compaction kernels.");
            }
        }

        internal int FindKernel(string kernel)
        {
            RequireSupported();
            int index = Find(m_Shader, kernel);
            if (index < 0)
            {
                throw new InvalidOperationException($"Compute_MeshDrawPipeline has no kernel '{kernel}'.");
            }
            return index;
        }

        #region Culling
        internal void BindCullInputs<TCommands>(TCommands commands, int kernel, in MeshView view, Vector4[] frustumPlanes,
            in MeshCullInputs inputs, RGBufferHandle visibility) where TCommands : struct, IComputeCommands
        {
            commands.SetComputeVectorArrayParam(m_Shader, ID_FrustumPlanes, frustumPlanes);
            BindCullBounds(commands, kernel, view, inputs);
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

        /// <summary>Bounds and LOD inputs every culling kernel reads; the packed visibility carries the selected level.</summary>
        internal void BindCullBounds<TCommands>(TCommands commands, int kernel, in MeshView view, in MeshCullInputs inputs) where TCommands : struct, IComputeCommands
        {
            MeshLODProjection lod = view.lodProjection;
            commands.SetComputeIntParam(m_Shader, ID_EntityCount, inputs.entityCount);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsCenter, inputs.boundsCenter);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsExtent, inputs.boundsExtent);
            commands.SetComputeBufferParam(m_Shader, kernel, ID_EntityLodChains, inputs.lodChains);
            commands.SetComputeVectorParam(m_Shader, ID_LodOrigin, new Vector4(lod.origin.x, lod.origin.y, lod.origin.z, 0));
            commands.SetComputeFloatParam(m_Shader, ID_LodScale, lod.scale);
            commands.SetComputeFloatParam(m_Shader, ID_LodBias, lod.bias);
            commands.SetComputeIntParam(m_Shader, ID_LodOrthographic, lod.orthographic ? 1 : 0);
        }

        internal void DispatchCullFrustumBatch<TCommands>(TCommands commands, in MeshCullInputs inputs, int viewCount, int stride,
            ComputeBuffer viewTable, RGBufferHandle visibility, MeshWorldStats stats) where TCommands : struct, IComputeCommands
        {
            RequireSupported();
            commands.SetComputeIntParam(m_Shader, ID_EntityCount, inputs.entityCount);
            commands.SetComputeIntParam(m_Shader, ID_ViewCount, viewCount);
            commands.SetComputeIntParam(m_Shader, ID_EntityStride, stride);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_FrustumViews, viewTable);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntityBoundsCenter, inputs.boundsCenter);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntityBoundsExtent, inputs.boundsExtent);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntityLodChains, inputs.lodChains);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntityFlags, inputs.flags);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntityLayerMask, inputs.layerMask);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntitySceneCullingMask, inputs.sceneCullingMask);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_EntityRenderingLayer, inputs.renderingLayers);
            commands.SetComputeBufferParam(m_Shader, m_CullKernel, ID_Visibility, visibility);
            commands.DispatchCompute(m_Shader, m_CullKernel, math.max(1, (inputs.entityCount + 63) / 64), math.max(1, viewCount), 1);
            stats.gpuCullDispatches++;
        }
        #endregion

        #region Draw lists
        /// <summary>Capacity for every command's stable and fading range: two argument records per command, two instances per member.</summary>
        internal MeshGPUDrawPayload RentPayload(int commandCount, int memberCount)
        {
            RequireSupported();
            int commands = math.ceilpow2(math.max(64, commandCount));
            int instances = math.ceilpow2(math.max(256, memberCount * 2));
            MeshGPUDrawPayload payload = m_FreePayloads.Count > 0 ? m_FreePayloads.Pop() : new MeshGPUDrawPayload();
            try
            {
                payload.commandMeta = m_Pool.GetBuffer(new BufferDescriptor(commands * MeshPassCache.MetaStride, sizeof(uint)) { name = "MeshDrawCommandMeta" }, true, false, out payload.metaWait);
                payload.indirectArgs = m_Pool.GetBuffer(new BufferDescriptor(commands * 2 * k_ArgsStride, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "MeshDrawIndirectArgs" }, true, false, out payload.argsWait);
                payload.instanceToEntity = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(uint)) { name = "MeshInstanceToEntity" }, true, false, out payload.instancesWait);
                payload.instanceFade = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(float)) { name = "MeshInstanceFade" }, true, false, out payload.fadeWait);
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

        internal void Upload(CommandBuffer commands, MeshGPUDrawPayload payload, MeshPassCache cache, int commandCount)
        {
            if (commandCount > payload.commandCapacity)
            {
                throw new InvalidOperationException("A GPU draw list outgrew its record-time command capacity.");
            }
            payload.WaitForReuse(commands);
            NativeArray<uint> meta = cache.GetCommandMeta();
            if (commandCount > 0)
            {
                commands.SetBufferData(payload.commandMeta.buffer, meta, 0, 0, commandCount * MeshPassCache.MetaStride);
            }
        }

        internal void Compact(CommandBuffer commands, MeshGPUDrawPayload payload, MeshPassCache cache, int commandCount, int candidateCount,
            ComputeBuffer visibility, int visibilityBase, MeshWorldStats stats)
        {
            RequireSupported();
            commands.SetComputeIntParam(m_Shader, ID_VisibilityBase, visibilityBase);
            commands.SetComputeIntParam(m_Shader, ID_CommandCount, commandCount);
            commands.SetComputeIntParam(m_Shader, ID_CandidateCount, candidateCount);
            commands.SetComputeIntParam(m_Shader, ID_DitherLod, cache.desc.ditherLODTransitions ? 1 : 0);
            commands.SetComputeBufferParam(m_Shader, m_InitializeArgsKernel, ID_CommandMeta, payload.commandMeta.buffer);
            commands.SetComputeBufferParam(m_Shader, m_InitializeArgsKernel, ID_IndirectArgs, payload.indirectArgs.buffer);
            commands.DispatchCompute(m_Shader, m_InitializeArgsKernel, math.max(1, (commandCount + 63) / 64), 1, 1);
            stats.compactKernelDispatches++;

            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_CandidateTable, cache.candidateBuffer.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_Visibility, visibility);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_CommandMeta, payload.commandMeta.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_IndirectArgs, payload.indirectArgs.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_InstanceToEntity, payload.instanceToEntity.buffer);
            commands.SetComputeBufferParam(m_Shader, m_CompactKernel, ID_InstanceFade, payload.instanceFade.buffer);
            commands.DispatchCompute(m_Shader, m_CompactKernel, math.max(1, (candidateCount + 63) / 64), 1, 1);
            stats.compactKernelDispatches++;

            stats.compactDispatches++;
        }

        internal void SubmitMeshDrawList(CommandBuffer commands, MeshGPUDrawPayload payload, MeshPassCache cache, int commandCount,
            in MeshWorldGPUTables tables, in MeshMotionBinding motion, EMeshViewKind viewKind)
        {
            string marker = MeshDrawProfiling.Draw(viewKind, true);
            commands.BeginSample(marker);
            bool dithering = false;
            try
            {
                NativeArray<uint> meta = cache.GetCommandMeta();
                NativeArray<int> order = cache.GetCommandOrder();
                m_Properties.Clear();
                m_Properties.SetBuffer(InfinityShaderIDs.InstanceToEntity, payload.instanceToEntity.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.InstanceFade, payload.instanceFade.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.EntityTransforms, tables.version.matrices.buffer);
                m_Properties.SetBuffer(InfinityShaderIDs.EntityRenderingLayers, tables.renderingLayers.buffer);
                (motion.isValid ? motion : new MeshMotionBinding(tables.version, null)).Bind(m_Properties);
                NativeArray<MeshDrawCommand> records = cache.commands;
                bool anyFading = false;
                foreach (int command in order)
                {
                    if (command >= commandCount)
                    {
                        throw new InvalidOperationException("A pass cache grew between draw-list record and execution.");
                    }
                    anyFading |= records[command].fadingMemberCount > 0;
                    DrawRange(commands, payload, cache, meta, command, 0);
                }
                // Fading ranges exist only for commands with members that can fade, and only in passes that dither.
                if (anyFading && cache.desc.ditherLODTransitions)
                {
                    dithering = true;
                    commands.SetKeyword(s_LodDither, true);
                    foreach (int command in order)
                        if (records[command].fadingMemberCount > 0)
                        {
                            DrawRange(commands, payload, cache, meta, command, 1);
                        }
                }
            }
            finally
            {
                if (dithering)
                {
                    commands.SetKeyword(s_LodDither, false);
                }
                commands.EndSample(marker);
            }
        }

        // range 0 = stable, 1 = fading; arguments are (command, range) records of k_ArgsStride uints.
        void DrawRange(CommandBuffer commands, MeshGPUDrawPayload payload, MeshPassCache cache, NativeArray<uint> meta, int command, int range)
        {
            MeshDrawCommandBinding binding = cache.GetBinding(command);
            if (binding.mesh == null || binding.material == null)
            {
                cache.ReportMissing(command);
                return;
            }
            m_Properties.SetInt(InfinityShaderIDs.InstanceOffset, (int)meta[command * MeshPassCache.MetaStride + 3 + range]);
            commands.DrawMeshInstancedIndirect(binding.mesh, binding.submesh, binding.material, binding.passIndex,
                payload.indirectArgs.buffer, (command * 2 + range) * k_ArgsStride * sizeof(uint), m_Properties);
        }

        internal void RetirePayload(MeshGPUDrawPayload payload)
        {
            if (payload != null)
            {
                m_Retired.Add(payload);
            }
        }

        void ReleaseBuffers(MeshGPUDrawPayload payload, ResourceReuseFence reuseFence)
        {
            if (payload.commandMeta.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.commandMeta, reuseFence);
            }
            if (payload.indirectArgs.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.indirectArgs, reuseFence);
            }
            if (payload.instanceToEntity.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.instanceToEntity, reuseFence);
            }
            if (payload.instanceFade.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.instanceFade, reuseFence);
            }
            payload.commandMeta = payload.indirectArgs = payload.instanceToEntity = payload.instanceFade = default;
            payload.metaWait = payload.argsWait = payload.instancesWait = payload.fadeWait = default;
            payload.commandCapacity = payload.instanceCapacity = 0;
        }

        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            foreach (MeshGPUDrawPayload payload in m_Retired)
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
