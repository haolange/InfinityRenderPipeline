using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Graph handles of the entity columns a draw-list producer reads.</summary>
    internal struct MeshCullInputs
    {
        public RGBufferHandle boundsCenter, boundsExtent, flags, layerMask, sceneCullingMask, renderingLayers, lodChains;
    }

    /// <summary>
    /// The occluder a producer tests against: a furthest-depth pyramid, the view-projection it was built with, and, for a
    /// history occluder, the current and previous transform versions that decide which entities did not move.
    /// </summary>
    internal struct MeshOccluderInputs
    {
        public RGTextureHandle furthest;
        public Matrix4x4 viewProjection;
        public Vector4 extent;
        public int mipCount;
        public bool history;
        public RGBufferHandle currentTransforms, currentGenerations, previousTransforms, previousGenerations;
        public int currentCount, previousCount;
    }

    /// <summary>
    /// Per-draw-list GPU buffers. The object is an empty CPU shell between uses; the buffers belong to the resource pool
    /// and are rented once, when the list is created, at its capacity.
    /// </summary>
    internal sealed class MeshGPUDrawPayload
    {
        internal BufferHandle commandMeta, indirectArgs, instanceToEntity, instanceFade, cullingContext;
        internal ResourceReuseFence metaWait, argsWait, instancesWait, fadeWait, contextWait;
        internal int commandCapacity, instanceCapacity;
        internal readonly MeshCullingContext[] contextStaging = new MeshCullingContext[1];

        internal void WaitForReuse(CommandBuffer commands)
        {
            metaWait.Wait(commands, false);
            argsWait.Wait(commands, false);
            instancesWait.Wait(commands, false);
            fadeWait.Wait(commands, false);
            contextWait.Wait(commands, false);
        }
    }

    /// <summary>
    /// GPU indirect drawing: per draw list, one compute producer initializes arguments and appends the visible members of
    /// the candidate table to per-command stable and fading instance ranges, testing each against the list's culling
    /// context (and occluder) itself; the raster consumer issues one <c>DrawMeshInstancedIndirect</c> per live command and
    /// range. There is no batching and no CPU fallback.
    /// </summary>
    internal sealed class MeshDrawGPUBackend : IDisposable
    {
        static readonly int ID_EntityBoundsCenter = Shader.PropertyToID("_EntityBoundsCenter");
        static readonly int ID_EntityBoundsExtent = Shader.PropertyToID("_EntityBoundsExtent");
        static readonly int ID_EntityFlags = Shader.PropertyToID("_EntityFlags");
        static readonly int ID_EntityLayerMask = Shader.PropertyToID("_EntityLayerMask");
        static readonly int ID_EntitySceneCullingMask = Shader.PropertyToID("_EntitySceneCullingMask");
        static readonly int ID_EntityRenderingLayer = Shader.PropertyToID("_EntityRenderingLayer");
        static readonly int ID_EntityLodChains = Shader.PropertyToID("_EntityLodChains");
        static readonly int ID_CullingContext = Shader.PropertyToID("_CullingContext");
        static readonly int ID_CommandCount = Shader.PropertyToID("_CommandCount");
        static readonly int ID_CandidateCount = Shader.PropertyToID("_CandidateCount");
        static readonly int ID_CommandMeta = Shader.PropertyToID("_CommandMeta");
        static readonly int ID_CandidateTable = Shader.PropertyToID("_CandidateTable");
        static readonly int ID_InstanceToEntity = Shader.PropertyToID("_InstanceToEntity");
        static readonly int ID_IndirectArgs = Shader.PropertyToID("_IndirectArgs");
        static readonly int ID_InstanceFade = Shader.PropertyToID("_InstanceFade");
        static readonly int ID_DitherLod = Shader.PropertyToID("_DitherLod");
        static readonly int ID_DrawnCandidates = Shader.PropertyToID("_DrawnCandidates");
        static readonly int ID_Furthest = Shader.PropertyToID("_MeshCullFurthest");
        static readonly int ID_Projection = Shader.PropertyToID("_MeshCullProjection");
        static readonly int ID_Extent = Shader.PropertyToID("_MeshCullFullExtent");
        static readonly int ID_MipCount = Shader.PropertyToID("_MeshCullMipCount");
        static readonly int ID_CurrentCount = Shader.PropertyToID("_MeshCullCurrentCount");
        static readonly int ID_PreviousCount = Shader.PropertyToID("_MeshCullPreviousCount");
        static readonly int ID_CurrentTransforms = Shader.PropertyToID("_MeshCullCurrentTransforms");
        static readonly int ID_PreviousTransforms = Shader.PropertyToID("_MeshCullPreviousTransforms");
        static readonly int ID_CurrentGenerations = Shader.PropertyToID("_MeshCullCurrentGenerations");
        static readonly int ID_PreviousGenerations = Shader.PropertyToID("_MeshCullPreviousGenerations");
        static readonly GlobalKeyword s_LodDither = GlobalKeyword.Create("INFINITY_LOD_DITHER");
        static readonly int s_ContextStride = Marshal.SizeOf<MeshCullingContext>();
        const int k_ArgsStride = 5;

        readonly ResourcePool m_Pool;
        readonly ComputeShader m_Shader;
        readonly ComputeKernel m_InitializeArgsKernel, m_BuildKernel, m_BuildOccludedKernel, m_BuildOccludedHistoryKernel, m_BuildRemainderKernel;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        readonly Stack<MeshGPUDrawPayload> m_FreePayloads = new Stack<MeshGPUDrawPayload>(8);
        readonly List<MeshGPUDrawPayload> m_Retired = new List<MeshGPUDrawPayload>(8);

        /// <summary>Device capability; the compute shader and its kernels are required on every backend.</summary>
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
            const string field = "GlobalSettings RuntimeShaders.meshDrawPipelineCS";
            m_InitializeArgsKernel = ComputeKernel.Resolve(shader, field, "InitializeIndirectArgs");
            m_BuildKernel = ComputeKernel.Resolve(shader, field, "BuildDrawList");
            m_BuildOccludedKernel = ComputeKernel.Resolve(shader, field, "BuildDrawListOccluded");
            m_BuildOccludedHistoryKernel = ComputeKernel.Resolve(shader, field, "BuildDrawListOccludedHistory");
            m_BuildRemainderKernel = ComputeKernel.Resolve(shader, field, "BuildDrawListRemainder");
            // The shader is required on every backend; a missing kernel is a broken install, not a device limit.
            foreach (ComputeKernel kernel in new[] { m_InitializeArgsKernel, m_BuildKernel, m_BuildOccludedKernel, m_BuildOccludedHistoryKernel, m_BuildRemainderKernel })
            {
                if (!kernel.isValid)
                {
                    throw new InvalidOperationException("Mesh drawing requires every Compute_MeshDrawPipeline kernel: " + kernel.missingReason);
                }
            }
            isSupported = DeviceSupportsIndirect;
        }

        void RequireSupported()
        {
            if (!isSupported)
            {
                throw new InvalidOperationException("GPU indirect mesh drawing requires a device with compute shaders and instancing.");
            }
        }

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
                payload.indirectArgs = m_Pool.GetBuffer(new BufferDescriptor(commands * 2 * k_ArgsStride, sizeof(uint), GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured) { name = "MeshDrawIndirectArgs" }, true, false, out payload.argsWait);
                payload.instanceToEntity = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(uint)) { name = "MeshInstanceToEntity" }, true, false, out payload.instancesWait);
                payload.instanceFade = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(float)) { name = "MeshInstanceFade" }, true, false, out payload.fadeWait);
                payload.cullingContext = m_Pool.GetBuffer(new BufferDescriptor(1, s_ContextStride) { name = "MeshCullingContext" }, true, false, out payload.contextWait);
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

        /// <summary>
        /// The list's producer: uploads the command metadata and the culling context, initializes both argument records of
        /// every command slot, then appends every passing candidate. <paramref name="occluder"/> is read only when
        /// <paramref name="occluded"/>; a history occluder writes the drawn table and a remainder list reads it.
        /// </summary>
        internal void Produce(in RGComputeEncoder encoder, MeshGPUDrawPayload payload, MeshPassCache cache, int commandCount, int candidateCount,
            in MeshCullingContext context, in MeshCullInputs inputs, bool occluded, in MeshOccluderInputs occluder, RGBufferHandle drawn,
            bool remainder, MeshWorldStats stats)
        {
            RequireSupported();
            if (commandCount > payload.commandCapacity)
            {
                throw new InvalidOperationException("A GPU draw list outgrew its record-time command capacity.");
            }
            CommandBuffer commands = encoder.m_Core.commands;
            payload.WaitForReuse(commands);
            if (commandCount > 0)
            {
                commands.SetBufferData(payload.commandMeta.buffer, cache.GetCommandMeta(), 0, 0, commandCount * MeshPassCache.MetaStride);
            }
            payload.contextStaging[0] = context;
            commands.SetBufferData(payload.cullingContext.buffer, payload.contextStaging, 0, 0, 1);

            int initialize = m_InitializeArgsKernel.index;
            int dither = cache.desc.ditherLODTransitions ? 1 : 0;
            encoder.BeginSample(MeshDrawProfiling.initializeIndirectArgs);
            encoder.SetComputeIntParam(m_Shader, ID_CommandCount, commandCount);
            encoder.SetComputeBufferParam(m_Shader, initialize, ID_CommandMeta, payload.commandMeta.buffer);
            encoder.SetComputeBufferParam(m_Shader, initialize, ID_IndirectArgs, payload.indirectArgs.buffer);
            encoder.DispatchCompute(m_Shader, initialize, math.max(1, (commandCount + 63) / 64), 1, 1);
            encoder.EndSample(MeshDrawProfiling.initializeIndirectArgs);
            stats.drawListKernelDispatches++;

            ComputeKernel build = !occluded ? m_BuildKernel : occluder.history ? m_BuildOccludedHistoryKernel
                : remainder ? m_BuildRemainderKernel : m_BuildOccludedKernel;
            int kernel = build.index;
            encoder.BeginSample(MeshDrawProfiling.buildDrawList);
            encoder.SetComputeIntParam(m_Shader, ID_CandidateCount, candidateCount);
            encoder.SetComputeIntParam(m_Shader, ID_DitherLod, dither);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_CullingContext, payload.cullingContext.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsCenter, inputs.boundsCenter);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsExtent, inputs.boundsExtent);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityLodChains, inputs.lodChains);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityFlags, inputs.flags);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityLayerMask, inputs.layerMask);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntitySceneCullingMask, inputs.sceneCullingMask);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityRenderingLayer, inputs.renderingLayers);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_CandidateTable, cache.candidateBuffer.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_CommandMeta, payload.commandMeta.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_IndirectArgs, payload.indirectArgs.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_InstanceToEntity, payload.instanceToEntity.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_InstanceFade, payload.instanceFade.buffer);
            if (occluded)
            {
                if (occluder.history || remainder)
                {
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_DrawnCandidates, drawn);
                }
                encoder.SetComputeTextureParam(m_Shader, kernel, ID_Furthest, occluder.furthest);
                encoder.SetComputeMatrixParam(m_Shader, ID_Projection, occluder.viewProjection);
                encoder.SetComputeVectorParam(m_Shader, ID_Extent, occluder.extent);
                encoder.SetComputeIntParam(m_Shader, ID_MipCount, occluder.mipCount);
                if (occluder.history)
                {
                    encoder.SetComputeIntParam(m_Shader, ID_CurrentCount, occluder.currentCount);
                    encoder.SetComputeIntParam(m_Shader, ID_PreviousCount, occluder.previousCount);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_CurrentTransforms, occluder.currentTransforms);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_CurrentGenerations, occluder.currentGenerations);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_PreviousTransforms, occluder.previousTransforms);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_PreviousGenerations, occluder.previousGenerations);
                }
            }
            encoder.DispatchCompute(m_Shader, kernel, math.max(1, build.GroupsX(candidateCount)), 1, 1);
            encoder.EndSample(MeshDrawProfiling.buildDrawList);
            stats.drawListKernelDispatches++;
            stats.drawListProductions++;
        }

        internal void SubmitMeshDrawList(CommandBuffer commands, MeshGPUDrawPayload payload, MeshPassCache cache, int commandCount,
            in MeshWorldGPUTables tables, in MeshMotionBinding motion)
        {
            ProfilingSampler marker = MeshDrawProfiling.DrawSampler(cache.desc, true);
            marker.Begin(commands);
            bool dithering = false;
            try
            {
                NativeArray<uint> meta = cache.GetCommandMeta();
                NativeArray<int> order = cache.GetCommandOrder();
                m_Properties.Clear();
                m_Properties.SetBuffer(MeshShaderIDs.InstanceToEntity, payload.instanceToEntity.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.InstanceFade, payload.instanceFade.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.EntityTransforms, tables.version.matrices.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.EntityRenderingLayers, tables.renderingLayers.buffer);
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
                marker.End(commands);
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
            m_Properties.SetInt(MeshShaderIDs.InstanceOffset, (int)meta[command * MeshPassCache.MetaStride + 3 + range]);
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
            if (payload.cullingContext.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.cullingContext, reuseFence);
            }
            payload.commandMeta = payload.indirectArgs = payload.instanceToEntity = payload.instanceFade = payload.cullingContext = default;
            payload.metaWait = payload.argsWait = payload.instancesWait = payload.fadeWait = payload.contextWait = default;
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
