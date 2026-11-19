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
    /// <summary>Graph handles of the entity columns a view producer reads.</summary>
    internal struct MeshCullInputs
    {
        public RGBufferHandle boundsCenterHandle, boundsExtentHandle, flagsHandle, layerMaskHandle, sceneCullingMaskHandle, renderingLayersHandle, lodChainsHandle;
    }

    /// <summary>
    /// The occluder a view producer tests against: a furthest-depth pyramid, the view-projection it was built with, and, for a
    /// history occluder, the current and previous transform versions that decide which entities did not move.
    /// </summary>
    internal struct MeshOccluderInputs
    {
        public RGTextureHandle furthestHandle;
        public Matrix4x4 viewProjection;
        public Vector4 extent;
        public int mipCount;
        public bool history;
        public RGBufferHandle currentTransformsHandle, currentGenerationsHandle, previousTransformsHandle, previousGenerationsHandle;
        public int currentCount, previousCount;
    }

    /// <summary>
    /// Per-view GPU buffer: the culling context the view's producer uploads and its kernel reads. The object is an empty CPU
    /// shell between uses; the buffer belongs to the resource pool and is rented when the view declares its producer.
    /// </summary>
    internal sealed class MeshGPUViewPayload
    {
        internal BufferHandle cullingContextHandle;
        internal ResourceReuseFence contextWait;
        // SetBufferData takes a managed array; the view payload owns its one-element staging (R17).
        internal readonly MeshCullingContext[] contextStaging = new MeshCullingContext[1];
    }

    /// <summary>
    /// Per-draw-list GPU buffers. The object is an empty CPU shell between uses; the buffers belong to the resource pool
    /// and are rented once, when the list is created, at its capacity.
    /// </summary>
    internal sealed class MeshGPUDrawPayload
    {
        internal BufferHandle indirectArgsHandle, instanceToEntityHandle, instanceFadeHandle;
        internal ResourceReuseFence argsWait, instancesWait, fadeWait;
        internal int commandCapacity, instanceCapacity;

        internal void WaitForReuse(CommandBuffer commands)
        {
            argsWait.Wait(commands, false);
            instancesWait.Wait(commands, false);
            fadeWait.Wait(commands, false);
        }
    }

    /// <summary>
    /// GPU indirect drawing: per view, one compute producer writes every entity's packed visibility, testing it against the
    /// view's culling context (and occluder); per draw list of the view, one compute producer initializes arguments and
    /// appends the members of the candidate table whose entity the view sees to per-command stable and fading instance
    /// ranges. The raster consumer issues one <c>DrawMeshInstancedIndirect</c> per live command and range. There is no
    /// batching and no CPU fallback.
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
        static readonly int ID_EntityCount = Shader.PropertyToID("_EntityCount");
        static readonly int ID_ViewVisibilityOutput = Shader.PropertyToID("UAV_MeshViewVisibility");
        static readonly int ID_ViewVisibility = Shader.PropertyToID("SRV_MeshViewVisibility");
        static readonly int ID_ViewDrawn = Shader.PropertyToID("SRV_MeshViewDrawn");
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
        readonly ComputeKernel m_ViewCullingKernel, m_ViewCullingOccludedKernel, m_ViewCullingOccludedHistoryKernel, m_ViewCullingRemainderKernel;
        readonly ComputeKernel m_InitializeArgsKernel, m_BuildDrawListKernel;
        readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        readonly Stack<MeshGPUViewPayload> m_FreeViewPayloads = new Stack<MeshGPUViewPayload>(8);
        readonly List<MeshGPUViewPayload> m_RetiredViews = new List<MeshGPUViewPayload>(8);
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
            m_ViewCullingKernel = ComputeKernel.Resolve(shader, field, "MeshViewCulling");
            m_ViewCullingOccludedKernel = ComputeKernel.Resolve(shader, field, "MeshViewCullingOccluded");
            m_ViewCullingOccludedHistoryKernel = ComputeKernel.Resolve(shader, field, "MeshViewCullingOccludedHistory");
            m_ViewCullingRemainderKernel = ComputeKernel.Resolve(shader, field, "MeshViewCullingRemainder");
            m_InitializeArgsKernel = ComputeKernel.Resolve(shader, field, "InitializeIndirectArgs");
            m_BuildDrawListKernel = ComputeKernel.Resolve(shader, field, "BuildDrawList");
            // The shader is required on every backend; a missing kernel is a broken install, not a device limit.
            foreach (ComputeKernel kernel in new[] { m_ViewCullingKernel, m_ViewCullingOccludedKernel, m_ViewCullingOccludedHistoryKernel, m_ViewCullingRemainderKernel,
                m_InitializeArgsKernel, m_BuildDrawListKernel })
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

        #region Views
        /// <summary>The culling-context buffer of a view that declares its producer.</summary>
        internal MeshGPUViewPayload RentViewPayload()
        {
            RequireSupported();
            MeshGPUViewPayload payload = m_FreeViewPayloads.Count > 0 ? m_FreeViewPayloads.Pop() : new MeshGPUViewPayload();
            try
            {
                payload.cullingContextHandle = m_Pool.GetBuffer(new BufferDescriptor(1, s_ContextStride) { name = "MeshCullingContext" }, true, false, out payload.contextWait);
            }
            catch
            {
                m_FreeViewPayloads.Push(payload);
                throw;
            }
            return payload;
        }

        /// <summary>
        /// The view's producer: uploads its culling context, then writes one packed visibility word per entity slot below
        /// <paramref name="entityCount"/> (one thread each). <paramref name="occluder"/> is read only when
        /// <paramref name="occluded"/>; a history occluder also compares the transform versions, and a remainder view reads
        /// the words its history view wrote and leaves every entity that view saw culled.
        /// </summary>
        internal void ProduceView(in RGComputeEncoder encoder, MeshGPUViewPayload payload, int entityCount, in MeshCullingContext context,
            in MeshCullInputs inputs, RGBufferHandle visibilityHandle, bool occluded, in MeshOccluderInputs occluder, bool remainder,
            RGBufferHandle drawnHandle, MeshWorldStats stats)
        {
            RequireSupported();
            CommandBuffer commands = encoder.m_Core.commands;
            payload.contextWait.Wait(commands, false);
            payload.contextStaging[0] = context;
            commands.SetBufferData(payload.cullingContextHandle.buffer, payload.contextStaging, 0, 0, 1);

            ComputeKernel cull = !occluded ? m_ViewCullingKernel : occluder.history ? m_ViewCullingOccludedHistoryKernel
                : remainder ? m_ViewCullingRemainderKernel : m_ViewCullingOccludedKernel;
            int kernel = cull.index;
            encoder.BeginSample(MeshDrawProfiling.viewCulling);
            encoder.SetComputeIntParam(m_Shader, ID_EntityCount, entityCount);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_CullingContext, payload.cullingContextHandle.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsCenter, inputs.boundsCenterHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityBoundsExtent, inputs.boundsExtentHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityLodChains, inputs.lodChainsHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityFlags, inputs.flagsHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityLayerMask, inputs.layerMaskHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntitySceneCullingMask, inputs.sceneCullingMaskHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_EntityRenderingLayer, inputs.renderingLayersHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_ViewVisibilityOutput, visibilityHandle);
            if (occluded)
            {
                if (remainder)
                {
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_ViewDrawn, drawnHandle);
                }
                encoder.SetComputeTextureParam(m_Shader, kernel, ID_Furthest, occluder.furthestHandle);
                encoder.SetComputeMatrixParam(m_Shader, ID_Projection, occluder.viewProjection);
                encoder.SetComputeVectorParam(m_Shader, ID_Extent, occluder.extent);
                encoder.SetComputeIntParam(m_Shader, ID_MipCount, occluder.mipCount);
                if (occluder.history)
                {
                    encoder.SetComputeIntParam(m_Shader, ID_CurrentCount, occluder.currentCount);
                    encoder.SetComputeIntParam(m_Shader, ID_PreviousCount, occluder.previousCount);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_CurrentTransforms, occluder.currentTransformsHandle);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_CurrentGenerations, occluder.currentGenerationsHandle);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_PreviousTransforms, occluder.previousTransformsHandle);
                    encoder.SetComputeBufferParam(m_Shader, kernel, ID_PreviousGenerations, occluder.previousGenerationsHandle);
                }
            }
            encoder.DispatchCompute(m_Shader, kernel, math.max(1, cull.GroupsX(entityCount)), 1, 1);
            encoder.EndSample(MeshDrawProfiling.viewCulling);
            stats.viewProductions++;
        }

        internal void RetireViewPayload(MeshGPUViewPayload payload)
        {
            if (payload != null)
            {
                m_RetiredViews.Add(payload);
            }
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
                payload.indirectArgsHandle = m_Pool.GetBuffer(new BufferDescriptor(commands * 2 * k_ArgsStride, sizeof(uint), GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured) { name = "MeshDrawIndirectArgs" }, true, false, out payload.argsWait);
                payload.instanceToEntityHandle = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(uint)) { name = "MeshInstanceToEntity" }, true, false, out payload.instancesWait);
                payload.instanceFadeHandle = m_Pool.GetBuffer(new BufferDescriptor(instances, sizeof(float)) { name = "MeshInstanceFade" }, true, false, out payload.fadeWait);
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
        /// The list's producer: initializes both argument records of every command slot, then appends every candidate whose
        /// entity its view sees, reading the visibility the view's producer wrote. The pass decides only whether LOD
        /// transitions dither and which level each element draws.
        /// </summary>
        internal void ProduceDrawList(in RGComputeEncoder encoder, MeshGPUDrawPayload payload, MeshPassCache cache, int commandCount, int candidateCount,
            RGBufferHandle visibilityHandle, MeshWorldStats stats)
        {
            RequireSupported();
            if (commandCount > payload.commandCapacity)
            {
                throw new InvalidOperationException("A GPU draw list outgrew its record-time command capacity.");
            }
            payload.WaitForReuse(encoder.m_Core.commands);

            int initialize = m_InitializeArgsKernel.index;
            encoder.BeginSample(MeshDrawProfiling.initializeIndirectArgs);
            encoder.SetComputeIntParam(m_Shader, ID_CommandCount, commandCount);
            encoder.SetComputeBufferParam(m_Shader, initialize, ID_CommandMeta, cache.commandMetaBufferHandle.buffer);
            encoder.SetComputeBufferParam(m_Shader, initialize, ID_IndirectArgs, payload.indirectArgsHandle.buffer);
            encoder.DispatchCompute(m_Shader, initialize, math.max(1, (commandCount + 63) / 64), 1, 1);
            encoder.EndSample(MeshDrawProfiling.initializeIndirectArgs);
            stats.drawListKernelDispatches++;

            int kernel = m_BuildDrawListKernel.index;
            encoder.BeginSample(MeshDrawProfiling.buildDrawList);
            encoder.SetComputeIntParam(m_Shader, ID_CandidateCount, candidateCount);
            encoder.SetComputeIntParam(m_Shader, ID_DitherLod, cache.desc.ditherLODTransitions ? 1 : 0);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_ViewVisibility, visibilityHandle);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_CandidateTable, cache.candidateBufferHandle.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_CommandMeta, cache.commandMetaBufferHandle.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_IndirectArgs, payload.indirectArgsHandle.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_InstanceToEntity, payload.instanceToEntityHandle.buffer);
            encoder.SetComputeBufferParam(m_Shader, kernel, ID_InstanceFade, payload.instanceFadeHandle.buffer);
            encoder.DispatchCompute(m_Shader, kernel, math.max(1, m_BuildDrawListKernel.GroupsX(candidateCount)), 1, 1);
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
                m_Properties.SetBuffer(MeshShaderIDs.InstanceToEntity, payload.instanceToEntityHandle.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.InstanceFade, payload.instanceFadeHandle.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.EntityTransforms, tables.version.matricesHandle.buffer);
                m_Properties.SetBuffer(MeshShaderIDs.EntityRenderingLayers, tables.renderingLayersHandle.buffer);
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
                payload.indirectArgsHandle.buffer, (command * 2 + range) * k_ArgsStride * sizeof(uint), m_Properties);
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
            if (payload.indirectArgsHandle.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.indirectArgsHandle, reuseFence);
            }
            if (payload.instanceToEntityHandle.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.instanceToEntityHandle, reuseFence);
            }
            if (payload.instanceFadeHandle.buffer != null)
            {
                m_Pool.ReleaseBuffer(payload.instanceFadeHandle, reuseFence);
            }
            payload.indirectArgsHandle = payload.instanceToEntityHandle = payload.instanceFadeHandle = default;
            payload.argsWait = payload.instancesWait = payload.fadeWait = default;
            payload.commandCapacity = payload.instanceCapacity = 0;
        }
        #endregion

        /// <summary>Returns retired view and list buffers to the pool behind <paramref name="reuseFence"/>; the shells become free.</summary>
        internal void FlushRetired(ResourceReuseFence reuseFence)
        {
            foreach (MeshGPUViewPayload payload in m_RetiredViews)
            {
                if (payload.cullingContextHandle.buffer != null)
                {
                    m_Pool.ReleaseBuffer(payload.cullingContextHandle, reuseFence);
                }
                payload.cullingContextHandle = default;
                payload.contextWait = default;
                m_FreeViewPayloads.Push(payload);
            }
            m_RetiredViews.Clear();
            foreach (MeshGPUDrawPayload payload in m_Retired)
            {
                ReleaseBuffers(payload, reuseFence);
                m_FreePayloads.Push(payload);
            }
            m_Retired.Clear();
        }

        public void Dispose()
        {
            FlushRetired(default);
            m_FreeViewPayloads.Clear();
            m_FreePayloads.Clear();
        }
    }
}
