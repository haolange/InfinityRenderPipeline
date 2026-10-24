using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// CPU-side staging for a GPU DrawList payload. Uploaded on CommandBuffer during Submit.
    /// </summary>
    internal sealed class MeshDrawGpuStaging
    {
        public ComputeBuffer candidateTable;
        public uint[] commandMeta;
        public uint[] candidateOffsets;
        public uint[] candidateCounts;
        public int[] candidateIndices;
        public MeshDrawCommand[] drawCommands;
        public Vector4[] frustumPlanes;
        public int commandCount;
        public int candidateCount;
        public int boundsInstanceCount;
        public bool isValid;

        public static MeshDrawGpuStaging Build(in PassView drawList, in MeshViewCullingResult culling, int boundsInstanceCount)
        {
            if (!drawList.isValid || drawList.commandCount <= 0)
            {
                return null;
            }

            var staging = new MeshDrawGpuStaging
            {
                commandCount = drawList.commandCount,
                candidateCount = drawList.instanceCount,
                boundsInstanceCount = math.max(1, boundsInstanceCount),
                commandMeta = new uint[drawList.commandCount * 4],
                candidateOffsets = new uint[drawList.commandCount],
                candidateCounts = new uint[drawList.commandCount],
                candidateIndices = new int[math.max(1, drawList.instanceCount)],
                frustumPlanes = new Vector4[6],
                isValid = true
            };

            for (int i = 0; i < drawList.commandCount; ++i)
            {
                MeshDrawCommand command = drawList.commands[i];
                Mesh mesh = UnityEntityId.ToObject<Mesh>(command.meshUnityId);
                int indexCount = 0;
                int startIndex = 0;
                int baseVertex = 0;
                if (mesh != null && command.sectionIndex >= 0 && command.sectionIndex < mesh.subMeshCount)
                {
                    SubMeshDescriptor subMesh = mesh.GetSubMesh(command.sectionIndex);
                    indexCount = subMesh.indexCount;
                    startIndex = subMesh.indexStart;
                    baseVertex = subMesh.baseVertex;
                }

                int meta = i * 4;
                staging.commandMeta[meta + 0] = (uint)math.max(0, indexCount);
                staging.commandMeta[meta + 1] = (uint)math.max(0, startIndex);
                staging.commandMeta[meta + 2] = (uint)math.max(0, baseVertex);
                staging.commandMeta[meta + 3] = (uint)math.max(0, command.countOffset.y);
                staging.candidateOffsets[i] = (uint)math.max(0, command.countOffset.y);
                staging.candidateCounts[i] = (uint)math.max(0, command.countOffset.x);
            }

            // Candidate stream is instance-indexed for GPU cull; Compact remaps via InstanceTransformIndex.
            if (drawList.instanceCount > 0 && drawList.instanceSlotIndices.IsCreated)
            {
                for (int i = 0; i < drawList.instanceCount; ++i)
                {
                    staging.candidateIndices[i] = drawList.instanceSlotIndices[i];
                }
            }

            if (culling.isValid && culling.frustum.IsCreated)
            {
                int planeCount = math.min(6, culling.frustum.Length);
                for (int i = 0; i < planeCount; ++i)
                {
                    FPlane plane = culling.frustum[i];
                    staging.frustumPlanes[i] = new Vector4(plane.normalDist.x, plane.normalDist.y, plane.normalDist.z, plane.normalDist.w);
                }
            }

            return staging;
        }
    }

    /// <summary>
    /// Per-DrawList GPU buffers. Never shared across concurrent DrawLists.
    /// </summary>
    internal sealed class MeshDrawGpuPayload : IDisposable
    {
        ResourcePool m_Pool;
        FBufferRef m_Args, m_Metadata, m_Counts, m_Indices;
        ResourceReuseFence m_ArgsWait, m_MetadataWait, m_CountsWait, m_IndicesWait;
        public ComputeBuffer argsBuffer => m_Args.buffer;
        public ComputeBuffer commandMeta => m_Metadata.buffer;
        public ComputeBuffer visibleCounts => m_Counts.buffer;
        public ComputeBuffer compactedIndices => m_Indices.buffer;

        public int capacityCommands;
        public int capacityInstances;
        public int commandCount;
        public bool isRented;

        // Pre-record only. ResourcePool owns physical storage, including cancelled and
        // unsubmitted references; the payload pool retains empty CPU objects only.
        public void EnsureCapacity(ResourcePool pool, int commands, int instances)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            commands = math.max(1, commands); instances = math.max(1, instances);
            if (m_Pool != null && m_Pool != pool) throw new InvalidOperationException("A live payload cannot change its resource owner.");
            if (RequireCapacity(commands, instances)) return;
            int nextCommands = math.max(commands, 64), nextInstances = math.max(instances, 256);
            FBufferRef args = default, metadata = default, counts = default, indices = default;
            ResourceReuseFence argsWait = default, metadataWait = default, countsWait = default, indicesWait = default;
            try
            {
                args = pool.GetBuffer(new BufferDescriptor(checked(nextCommands * 5), 4, ComputeBufferType.IndirectArguments), true, false, out argsWait);
                metadata = pool.GetBuffer(new BufferDescriptor(checked(nextCommands * 4), 4, ComputeBufferType.Structured), true, false, out metadataWait);
                counts = pool.GetBuffer(new BufferDescriptor(nextCommands, 4, ComputeBufferType.Structured), true, false, out countsWait);
                indices = pool.GetBuffer(new BufferDescriptor(nextInstances, 4, ComputeBufferType.Structured), true, false, out indicesWait);
            }
            catch
            {
                if (args.buffer != null) pool.ReleaseBuffer(args);
                if (metadata.buffer != null) pool.ReleaseBuffer(metadata);
                if (counts.buffer != null) pool.ReleaseBuffer(counts);
                if (indices.buffer != null) pool.ReleaseBuffer(indices);
                throw;
            }
            ReturnBuffers();
            m_Pool = pool; m_Args = args; m_Metadata = metadata; m_Counts = counts; m_Indices = indices;
            m_ArgsWait = argsWait; m_MetadataWait = metadataWait; m_CountsWait = countsWait; m_IndicesWait = indicesWait;
            capacityCommands = nextCommands; capacityInstances = nextInstances;
        }
        internal void WaitForReuse(CommandBuffer commands)
        {
            m_ArgsWait.Wait(commands, false); m_MetadataWait.Wait(commands, false);
            m_CountsWait.Wait(commands, false); m_IndicesWait.Wait(commands, false);
        }
        void ReturnBuffers()
        {
            if (m_Pool != null)
            {
                if (m_Args.buffer != null) m_Pool.ReleaseBuffer(m_Args);
                if (m_Metadata.buffer != null) m_Pool.ReleaseBuffer(m_Metadata);
                if (m_Counts.buffer != null) m_Pool.ReleaseBuffer(m_Counts);
                if (m_Indices.buffer != null) m_Pool.ReleaseBuffer(m_Indices);
            }
            m_Args = m_Metadata = m_Counts = m_Indices = default;
            m_ArgsWait = m_MetadataWait = m_CountsWait = m_IndicesWait = default;
            m_Pool = null; capacityCommands = capacityInstances = 0;
        }

        /// <summary>
        /// Recording-time capacity check. Never releases or recreates ComputeBuffers.
        /// </summary>
        public bool RequireCapacity(int commands, int instances)
        {
            commands = math.max(1, commands);
            instances = math.max(1, instances);
            if (argsBuffer == null || capacityCommands < commands)
            {
                return false;
            }

            if (compactedIndices == null || capacityInstances < instances)
            {
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            ReturnBuffers();
            commandCount = 0; isRented = false;
        }

    }

    /// <summary>
    /// GPU-driven mesh draw backend with per-payload buffers and Auto fallback.
    /// </summary>
    public sealed class MeshDrawGPUBackend
    {
        public const int MaxCommands = 1024;
        public const int MaxInstances = 65536;

        private static ComputeShader s_Shader;
        internal static ComputeShader shader => s_Shader;
        private static int s_KernelCull = -1;
        private static int s_KernelClearCounts = -1;
        private static int s_KernelCompact = -1;
        private static int s_KernelBuildArgs = -1;
        private static bool s_KernelsResolved;
        private static bool s_KernelsValid;

        private static readonly Stack<MeshDrawGpuPayload> s_PayloadPool = new Stack<MeshDrawGpuPayload>(8);
        private static readonly List<MeshDrawGpuPayload> s_RetiredPayloads = new List<MeshDrawGpuPayload>(8);
        private static readonly List<(int commandBegin, int batchCommands)> s_BatchPlan =
            new List<(int commandBegin, int batchCommands)>(16);
        private static readonly List<(int commandBegin, int batchCommands)> s_BudgetPlan =
            new List<(int commandBegin, int batchCommands)>(16);

        private static readonly string[] s_RequiredKernelNames =
        {
            "CullFrustum",
            "ClearCounts",
            "Compact",
            "BuildIndirectArgs"
        };

        private static readonly int ID_InstanceCount = Shader.PropertyToID("_InstanceCount");
        private static readonly int ID_CommandCount = Shader.PropertyToID("_CommandCount");
        private static readonly int ID_CandidateCount = Shader.PropertyToID("_CandidateCount");
        private static readonly int ID_CommandBegin = Shader.PropertyToID("_CommandBegin");
        private static readonly int ID_CandidateOffset = Shader.PropertyToID("_CandidateOffset");
        private static readonly int ID_CandidateSpan = Shader.PropertyToID("_CandidateSpan");
        private static readonly int ID_FrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int ID_InstanceBoundsCenter = Shader.PropertyToID("_InstanceBoundsCenter");
        private static readonly int ID_InstanceBoundsExtent = Shader.PropertyToID("_InstanceBoundsExtent");
        private static readonly int ID_InstanceFlags = Shader.PropertyToID("_InstanceFlags");
        private static readonly int ID_InstanceLayerMask = Shader.PropertyToID("_InstanceLayerMask");
        private static readonly int ID_InstanceSceneCullingMask = Shader.PropertyToID("_InstanceSceneCullingMask");
        private static readonly int ID_ViewSceneCullingMaskLow = Shader.PropertyToID("_ViewSceneCullingMaskLow");
        private static readonly int ID_ViewSceneCullingMaskHigh = Shader.PropertyToID("_ViewSceneCullingMaskHigh");
        private static readonly int ID_InstanceRenderingLayer = Shader.PropertyToID("_InstanceRenderingLayer");
        private static readonly int ID_ViewLayerMask = Shader.PropertyToID("_ViewLayerMask");
        private static readonly int ID_ViewRenderingLayerMask = Shader.PropertyToID("_ViewRenderingLayerMask");
        private static readonly int ID_FilterRenderingLayers = Shader.PropertyToID("_FilterRenderingLayers");
        private static readonly int ID_ViewExcludeFlags = Shader.PropertyToID("_ViewExcludeFlags");
        private static readonly int ID_CommandMeta = Shader.PropertyToID("_CommandMeta");
        private static readonly int ID_CommandBases = Shader.PropertyToID("_CommandBases");
        private static readonly int ID_CandidateTable = Shader.PropertyToID("_CandidateTable");
        private static readonly int ID_Visibility = Shader.PropertyToID("_Visibility");
        private static readonly int ID_ShadingIndices = Shader.PropertyToID("_ShadingIndices");
        private static readonly int ID_VisibleCounts = Shader.PropertyToID("_VisibleCounts");
        private static readonly int ID_InstanceToTransform = Shader.PropertyToID("_InstanceToTransform");
        private static readonly int ID_IndirectArgs = Shader.PropertyToID("_IndirectArgs");

        public static void SetShader(ComputeShader shader)
        {
            s_Shader = shader;
            s_KernelsResolved = false;
            s_KernelsValid = false;
        }

        public static bool SupportsCompute
        {
            get
            {
                ResolveKernels();
                return SystemInfo.supportsComputeShaders && s_Shader != null && s_KernelsValid;
            }
        }

        public static bool SupportsIndirect
        {
            get
            {
                return SystemInfo.supportsInstancing && SupportsCompute;
            }
        }

        public static EMeshBackendPolicy SelectPolicy(EMeshBackendPolicy requested)
        {
            if (requested == EMeshBackendPolicy.CpuDirect)
            {
                return EMeshBackendPolicy.CpuDirect;
            }

            if (requested == EMeshBackendPolicy.GpuIndirect)
            {
                return SupportsIndirect ? EMeshBackendPolicy.GpuIndirect : EMeshBackendPolicy.CpuDirect;
            }

            return SupportsIndirect ? EMeshBackendPolicy.GpuIndirect : EMeshBackendPolicy.CpuDirect;
        }

        internal static bool TryGetCullKernel(out ComputeShader shader, out int kernel)
        {
            ResolveKernels();
            shader = s_Shader;
            kernel = s_KernelCull;
            return SupportsIndirect && shader != null && kernel >= 0;
        }

        internal static void DispatchCullFrustum(
            IComputeCommands commands,
            in MeshView view,
            int instanceCount,
            Vector4[] frustumPlanes,
            RGBufferRef boundsCenter,
            RGBufferRef boundsExtent,
            RGBufferRef instanceFlags,
            RGBufferRef instanceLayerMask,
            RGBufferRef instanceSceneCullingMask,
            RGBufferRef instanceRenderingLayer,
            RGBufferRef visibility)
        {
            if (!TryGetCullKernel(out ComputeShader shader, out int kernel))
                throw new InvalidOperationException("MeshGpuCull requires CullFrustum.");
            BindCullInputs(commands, shader, kernel, view, instanceCount, frustumPlanes, boundsCenter, boundsExtent,
                instanceFlags, instanceLayerMask, instanceSceneCullingMask, instanceRenderingLayer, visibility);
            commands.DispatchCompute(shader, kernel, math.max(1, (instanceCount + 63) / 64), 1, 1);
            MeshPipelineDiagnostics.GpuCullDispatchesPerFrame++;
        }

        internal static void BindCullBounds(IComputeCommands commands, ComputeShader shader, int kernel, int instanceCount,
            RGBufferRef center, RGBufferRef extent)
        {
            commands.SetComputeIntParam(shader, ID_InstanceCount, instanceCount);
            commands.SetComputeBufferParam(shader, kernel, ID_InstanceBoundsCenter, center);
            commands.SetComputeBufferParam(shader, kernel, ID_InstanceBoundsExtent, extent);
        }
        internal static void BindCullInputs(IComputeCommands commands, ComputeShader shader, int kernel,
            in MeshView view, int instanceCount, Vector4[] frustumPlanes, RGBufferRef boundsCenter, RGBufferRef boundsExtent,
            RGBufferRef instanceFlags, RGBufferRef instanceLayerMask, RGBufferRef instanceSceneCullingMask,
            RGBufferRef instanceRenderingLayer, RGBufferRef visibility)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (!visibility.IsValid()
                || !boundsCenter.IsValid()
                || !boundsExtent.IsValid()
                || !instanceFlags.IsValid()
                || !instanceLayerMask.IsValid()
                || !instanceSceneCullingMask.IsValid()
                || !instanceRenderingLayer.IsValid())
            {
                throw new InvalidOperationException("MeshGpuCull requires GpuScene bounds/flags/layers and a visibility UAV.");
            }

            commands.SetComputeVectorArrayParam(shader, ID_FrustumPlanes, frustumPlanes);
            BindCullBounds(commands, shader, kernel, instanceCount, boundsCenter, boundsExtent);
            commands.SetComputeIntParam(shader, ID_ViewLayerMask, view.layerMask);
            commands.SetComputeIntParam(shader, ID_ViewSceneCullingMaskLow, unchecked((int)view.sceneCullingMask));
            commands.SetComputeIntParam(shader, ID_ViewSceneCullingMaskHigh, unchecked((int)(view.sceneCullingMask >> 32)));
            commands.SetComputeIntParam(shader, ID_ViewRenderingLayerMask, (int)view.renderingLayerMask);
            commands.SetComputeIntParam(shader, ID_FilterRenderingLayers, view.FilterRenderingLayers ? 1 : 0);
            commands.SetComputeIntParam(shader, ID_ViewExcludeFlags, unchecked((int)view.excludeInstanceFlags));
            commands.SetComputeBufferParam(shader, kernel, ID_InstanceFlags, instanceFlags);
            commands.SetComputeBufferParam(shader, kernel, ID_InstanceLayerMask, instanceLayerMask);
            commands.SetComputeBufferParam(shader, kernel, ID_InstanceSceneCullingMask, instanceSceneCullingMask);
            commands.SetComputeBufferParam(shader, kernel, ID_InstanceRenderingLayer, instanceRenderingLayer);
            commands.SetComputeBufferParam(shader, kernel, ID_Visibility, visibility);
        }

        internal static MeshDrawGpuStaging CreateStaging(in PassView drawList, in MeshViewCullingResult culling, int boundsInstanceCount)
        {
            return MeshDrawGpuStaging.Build(drawList, culling, boundsInstanceCount);
        }

        internal static MeshDrawGpuPayload RentPayload()
        {
            MeshDrawGpuPayload payload = s_PayloadPool.Count > 0 ? s_PayloadPool.Pop() : new MeshDrawGpuPayload();
            payload.isRented = true;
            return payload;
        }

        internal static void ReturnPayload(MeshDrawGpuPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            if (!payload.isRented) throw new InvalidOperationException("Mesh payload returned twice.");
            payload.Dispose();
            s_PayloadPool.Push(payload);
        }

        /// <summary>
        /// Logical release: enqueue for physical Return after GPU work for this frame has been submitted.
        /// Does not return the payload to the pool immediately.
        /// </summary>
        internal static void RetirePayload(MeshDrawGpuPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            payload.commandCount = 0;
            s_RetiredPayloads.Add(payload);
        }

        /// <summary>
        /// Release physical ownership to ResourcePool and retain empty CPU payloads after <c>ScriptableRenderContext.Submit</c>.
        /// </summary>
        public static void FlushRetiredPayloads()
        {
            for (int i = 0; i < s_RetiredPayloads.Count; ++i)
            {
                ReturnPayload(s_RetiredPayloads[i]);
            }

            s_RetiredPayloads.Clear();
        }

        /// <summary>
        /// Disjoint command / instance capacities required by all planned batches.
        /// <paramref name="maxCommands"/> is 0 when planning fails (caller should CPU-fallback).
        /// Instances per batch = max(batchCandidates, boundsCount).
        /// </summary>
        internal static void ComputePayloadBudget(
            uint[] candidateCounts,
            int commandCount,
            int boundsCount,
            out int maxCommands,
            out int maxInstances)
        {
            maxCommands = 0;
            maxInstances = 0;
            if (!TryPlanBatches(candidateCounts, commandCount, s_BudgetPlan))
            {
                return;
            }

            long totalCandidates = 0;
            for (int i = 0; i < commandCount; ++i) totalCandidates += candidateCounts[i];
            if (totalCandidates > int.MaxValue || commandCount > int.MaxValue / 5) return;
            maxCommands = commandCount;
            maxInstances = math.max((int)totalCandidates, math.max(1, boundsCount));
        }

        /// <summary>
        /// Plan payload batch splits before any GPU dispatch.
        /// Returns false when a single command's candidates exceed <see cref="MaxInstances"/>.
        /// </summary>
        internal static bool TryPlanBatches(
            uint[] candidateCounts,
            int totalCommands,
            List<(int commandBegin, int batchCommands)> batches)
        {
            if (batches == null || candidateCounts == null || totalCommands <= 0
                || candidateCounts.Length < totalCommands)
            {
                return false;
            }

            batches.Clear();
            int commandBegin = 0;
            while (commandBegin < totalCommands)
            {
                int batchCommands = 0;
                int batchCandidates = 0;
                int commandEnd = commandBegin;
                while (commandEnd < totalCommands)
                {
                    int cmdCandidates = (int)candidateCounts[commandEnd];
                    if (cmdCandidates > MaxInstances)
                    {
                        batches.Clear();
                        return false;
                    }

                    if (batchCommands > 0
                        && (batchCommands + 1 > MaxCommands || batchCandidates + cmdCandidates > MaxInstances))
                    {
                        break;
                    }

                    batchCommands += 1;
                    batchCandidates += cmdCandidates;
                    commandEnd += 1;
                }

                if (batchCommands == 0)
                {
                    batches.Clear();
                    return false;
                }

                batches.Add((commandBegin, batchCommands));
                commandBegin = commandEnd;
            }

            return batches.Count > 0;
        }

        /// <summary>
        /// Compact + BuildIndirectArgs only. Must run before BeginRenderPass (D3D12 forbids SetBufferData inside a pass).
        /// </summary>
        internal static bool PrepareIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            in MeshGpuSceneSnapshot residency,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            MeshWorld world = null,
            MeshView view = default,
            MeshPassId passId = default)
        {
            if (!CanPrepareIndirect(cmdBuffer, drawList, residency, payload, staging))
            {
                return false;
            }

            if (!TryPlanBatches(staging.candidateCounts, staging.commandCount, s_BatchPlan))
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
                return false;
            }

            ComputePayloadBudget(
                staging.candidateCounts,
                staging.commandCount,
                staging.boundsInstanceCount,
                out int maxCommands,
                out int maxInstances);
            if (maxCommands <= 0 || !payload.RequireCapacity(maxCommands, maxInstances))
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
                return false;
            }

            for (int i = 0; i < s_BatchPlan.Count; ++i)
            {
                (int commandBegin, int batchCommands) batch = s_BatchPlan[i];
                if (!TryGetBatchInstanceCapacity(staging, batch.commandBegin, batch.batchCommands, out int instanceCapacity)
                    || !payload.RequireCapacity(batch.batchCommands, instanceCapacity))
                {
                    MeshPipelineDiagnostics.GpuOverflowCount++;
                    return false;
                }
            }

            for (int i = 0; i < s_BatchPlan.Count; ++i)
            {
                (int commandBegin, int batchCommands) batch = s_BatchPlan[i];
                if (!PrepareBatch(cmdBuffer, residency, payload, staging, batch.commandBegin, batch.batchCommands, world, view, passId))
                {
                    MeshPipelineDiagnostics.GpuOverflowCount++;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Draw only. Safe inside a raster render pass.
        /// </summary>
        internal static void DrawIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            int shaderPassIndex,
            in MeshGpuSceneSnapshot residency,
            MaterialPropertyBlock propertyBlock,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            EMeshViewKind viewKind,
            string lightModeTag = null, MeshMotionBinding previousTransforms = default)
        {
            if (cmdBuffer == null || !drawList.isValid || payload == null || staging == null || !staging.isValid)
            {
                return;
            }

            if (!TryPlanBatches(staging.candidateCounts, staging.commandCount, s_BatchPlan))
            {
                return;
            }

            string marker = MeshDrawProfiling.Draw(viewKind, true);
            cmdBuffer.BeginSample(marker);
            try
            {
                for (int i = 0; i < s_BatchPlan.Count; ++i)
                {
                    (int commandBegin, int batchCommands) batch = s_BatchPlan[i];
                    DrawBatch(cmdBuffer, drawList, shaderPassIndex, residency, propertyBlock, payload, staging, batch.commandBegin, batch.batchCommands, lightModeTag, previousTransforms);
                }
            }
            finally
            {
                cmdBuffer.EndSample(marker);
            }
        }

        private static bool CanPrepareIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            in MeshGpuSceneSnapshot residency,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0 || !residency.isValid
                || residency.TransformBuffer.buffer == null
                || residency.InstanceTransformIndexBuffer.buffer == null
                || payload == null || staging == null || !staging.isValid)
            {
                return false;
            }

            ResolveKernels();
            if (!SupportsIndirect)
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
                return false;
            }

            return true;
        }

        public static void Dispose()
        {
            FlushRetiredPayloads();

            while (s_PayloadPool.Count > 0)
            {
                s_PayloadPool.Pop().Dispose();
            }

            s_Shader = null;
            s_KernelsResolved = false;
            InvalidateKernels();
        }

        private static bool TryGetBatchInstanceCapacity(
            MeshDrawGpuStaging staging,
            int commandBegin,
            int batchCommandCount,
            out int instanceCapacity)
        {
            instanceCapacity = 0;
            if (staging == null || batchCommandCount <= 0
                || commandBegin < 0 || commandBegin + batchCommandCount > staging.commandCount)
            {
                return false;
            }

            int candidateBegin = (int)staging.candidateOffsets[commandBegin];
            int candidateEnd = candidateBegin;
            for (int i = 0; i < batchCommandCount; ++i)
            {
                int cmd = commandBegin + i;
                int cmdEnd = (int)staging.candidateOffsets[cmd] + (int)staging.candidateCounts[cmd];
                candidateEnd = math.max(candidateEnd, cmdEnd);
            }

            int batchCandidates = math.max(0, candidateEnd - candidateBegin);
            int boundsCount = math.max(staging.boundsInstanceCount, 1);
            instanceCapacity = math.max(batchCandidates, boundsCount);
            return true;
        }

        private static bool PrepareBatch(
            CommandBuffer cmdBuffer,
            in MeshGpuSceneSnapshot residency,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            int commandBegin,
            int batchCommandCount,
            MeshWorld world,
            in MeshView view,
            MeshPassId passId)
        {
            if (!TryGetBatchInstanceCapacity(staging, commandBegin, batchCommandCount, out int instanceCapacity)
                || !payload.RequireCapacity(batchCommandCount, instanceCapacity)
                || staging.candidateTable == null)
            {
                return false;
            }

            ComputeBuffer candidateTable = staging.candidateTable;
            if (candidateTable == null)
            {
                return false;
            }

            int candidateBegin = (int)staging.candidateOffsets[commandBegin];
            int candidateEnd = candidateBegin;
            for (int i = 0; i < batchCommandCount; ++i)
            {
                int cmd = commandBegin + i;
                int cmdEnd = (int)staging.candidateOffsets[cmd] + (int)staging.candidateCounts[cmd];
                candidateEnd = math.max(candidateEnd, cmdEnd);
            }

            int batchCandidates = math.max(0, candidateEnd - candidateBegin);
            int boundsCount = math.max(staging.boundsInstanceCount, 1);
            payload.commandCount = batchCommandCount;

            cmdBuffer.SetComputeIntParam(s_Shader, ID_InstanceCount, boundsCount);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CommandCount, batchCommandCount);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CommandBegin, commandBegin);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateOffset, candidateBegin);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateSpan, batchCandidates);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateCount, staging.candidateCount);

            ComputeBuffer visibility = world != null ? world.GetGpuVisibilityBuffer(view, boundsCount) : null;
            if (visibility == null)
            {
                return false;
            }

            cmdBuffer.BeginSample("ClearCounts");
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_VisibleCounts, payload.visibleCounts);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_IndirectArgs, payload.argsBuffer);
            int clearGroups = (batchCommandCount + 63) / 64;
            cmdBuffer.DispatchCompute(s_Shader, s_KernelClearCounts, math.max(1, clearGroups), 1, 1);
            cmdBuffer.EndSample("ClearCounts");

            if (batchCandidates > 0)
            {
                cmdBuffer.BeginSample("CompactIndices");
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CandidateTable, candidateTable);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_Visibility, visibility);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_InstanceToTransform, residency.InstanceTransformIndexBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_ShadingIndices, payload.compactedIndices);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_VisibleCounts, payload.visibleCounts);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CommandMeta, payload.commandMeta);
                int groups = (batchCandidates + 63) / 64;
                cmdBuffer.DispatchCompute(s_Shader, s_KernelCompact, math.max(1, groups), 1, 1);
                MeshPipelineDiagnostics.CompactDispatchesPerFrame++;
                cmdBuffer.EndSample("CompactIndices");
            }

            cmdBuffer.BeginSample("BuildIndirectArgs");
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_CommandMeta, payload.commandMeta);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_VisibleCounts, payload.visibleCounts);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_IndirectArgs, payload.argsBuffer);
            int argsGroups = (batchCommandCount + 63) / 64;
            cmdBuffer.DispatchCompute(s_Shader, s_KernelBuildArgs, math.max(1, argsGroups), 1, 1);
            cmdBuffer.EndSample("BuildIndirectArgs");
            return true;
        }

        private static void DrawBatch(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            int shaderPassIndex,
            in MeshGpuSceneSnapshot residency,
            MaterialPropertyBlock propertyBlock,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            int commandBegin,
            int batchCommandCount,
            string lightModeTag = null, MeshMotionBinding previousTransforms = default)
        {
            int candidateBegin = (int)staging.candidateOffsets[commandBegin];
            for (int i = 0; i < batchCommandCount; ++i)
            {
                MeshDrawCommand command = staging.drawCommands != null
                    ? staging.drawCommands[commandBegin + i]
                    : drawList.commands[commandBegin + i];
                Mesh mesh = UnityEntityId.ToObject<Mesh>(command.meshUnityId);
                Material material = UnityEntityId.ToObject<Material>(command.materialUnityId);
                if (mesh == null || material == null)
                {
                    continue;
                }

                int batchCandOff = (int)staging.candidateOffsets[commandBegin + i];
                propertyBlock.Clear();
                propertyBlock.SetInt(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.InstanceIndexOffset, batchCandOff);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.InstanceIndexBuffer, payload.compactedIndices);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.TransformBuffer, residency.TransformBuffer.buffer);
                (previousTransforms.isValid ? previousTransforms : new MeshMotionBinding(residency.version, null)).Bind(propertyBlock);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.RenderingLayerBuffer, residency.RenderingLayerBuffer.buffer);

                int passIndex = MeshPassShaderUtility.ResolvePassIndex(material, lightModeTag, shaderPassIndex);
                if (passIndex < 0)
                {
                    continue;
                }

                int argsOffset = (commandBegin + i) * 5 * sizeof(uint);
                MeshBakedLighting.Bind(cmdBuffer, propertyBlock, command.bakedTextureSet, residency.BakedLightingBuffer.buffer);
                cmdBuffer.DrawMeshInstancedIndirect(mesh, command.sectionIndex, material, passIndex, payload.argsBuffer, argsOffset, propertyBlock);
                MeshBakedLighting.ClearKeywords(cmdBuffer);
            }
        }

        private static void InvalidateKernels()
        {
            s_KernelCull = -1;
            s_KernelClearCounts = -1;
            s_KernelCompact = -1;
            s_KernelBuildArgs = -1;
            s_KernelsValid = false;
        }

        private static void ResolveKernels()
        {
            if (s_KernelsResolved)
            {
                return;
            }

            s_KernelsResolved = true;
            InvalidateKernels();

            if (s_Shader == null)
            {
                return;
            }

            for (int i = 0; i < s_RequiredKernelNames.Length; ++i)
            {
                if (!s_Shader.HasKernel(s_RequiredKernelNames[i]))
                {
                    InvalidateKernels();
                    return;
                }
            }

            s_KernelCull = s_Shader.FindKernel("CullFrustum");
            s_KernelClearCounts = s_Shader.FindKernel("ClearCounts");
            s_KernelCompact = s_Shader.FindKernel("Compact");
            s_KernelBuildArgs = s_Shader.FindKernel("BuildIndirectArgs");
            s_KernelsValid = true;
        }
    }
}
