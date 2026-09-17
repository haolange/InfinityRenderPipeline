using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// CPU-side staging for a GPU DrawList payload. Uploaded on CommandBuffer during Submit.
    /// </summary>
    internal sealed class MeshDrawGpuStaging
    {
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
        public ComputeBuffer argsBuffer;
        public ComputeBuffer commandMeta;
        public ComputeBuffer visibleCounts;
        public ComputeBuffer compactedIndices;

        public int capacityCommands;
        public int capacityInstances;
        public int commandCount;
        public bool isRented;

        /// <summary>
        /// Grow GPU buffers to fit the requested budget. Allowed only before CommandBuffer recording.
        /// </summary>
        public void EnsureCapacity(int commands, int instances)
        {
            TryEnsureCapacity(commands, instances);
        }

        /// <summary>
        /// Grow GPU buffers to fit the requested budget. Allowed only before CommandBuffer recording.
        /// </summary>
        public bool TryEnsureCapacity(int commands, int instances)
        {
            commands = math.max(1, commands);
            instances = math.max(1, instances);

            if (argsBuffer == null || capacityCommands < commands)
            {
                argsBuffer?.Release();
                commandMeta?.Release();
                visibleCounts?.Release();

                capacityCommands = math.max(commands, 64);
                argsBuffer = new ComputeBuffer(capacityCommands * 5, sizeof(uint), ComputeBufferType.IndirectArguments);
                commandMeta = new ComputeBuffer(capacityCommands * 4, sizeof(uint), ComputeBufferType.Structured);
                visibleCounts = new ComputeBuffer(capacityCommands, sizeof(uint), ComputeBufferType.Structured);
            }

            if (compactedIndices == null || capacityInstances < instances)
            {
                compactedIndices?.Release();
                capacityInstances = math.max(instances, 256);
                compactedIndices = new ComputeBuffer(capacityInstances, sizeof(uint), ComputeBufferType.Structured);
            }

            return true;
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
            argsBuffer?.Release();
            commandMeta?.Release();
            visibleCounts?.Release();
            compactedIndices?.Release();
            argsBuffer = null;
            commandMeta = null;
            visibleCounts = null;
            compactedIndices = null;
            capacityCommands = 0;
            capacityInstances = 0;
            commandCount = 0;
            isRented = false;
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
        private static readonly int ID_InstanceRenderingLayer = Shader.PropertyToID("_InstanceRenderingLayer");
        private static readonly int ID_ViewLayerMask = Shader.PropertyToID("_ViewLayerMask");
        private static readonly int ID_ViewRenderingLayerMask = Shader.PropertyToID("_ViewRenderingLayerMask");
        private static readonly int ID_FilterRenderingLayers = Shader.PropertyToID("_FilterRenderingLayers");
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

            payload.commandCount = 0;
            payload.isRented = false;
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
        /// Physical drain: return retired payloads to the pool after <c>ScriptableRenderContext.Submit</c>.
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
        /// Alias for <see cref="FlushRetiredPayloads"/>. Prefer the Flush name at frame-end call sites.
        /// </summary>
        public static void ReleaseFrameResources()
        {
            FlushRetiredPayloads();
        }

        /// <summary>
        /// Max command / instance capacities required across all planned batches.
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

            int safeBounds = math.max(1, boundsCount);
            for (int i = 0; i < s_BudgetPlan.Count; ++i)
            {
                (int commandBegin, int batchCommands) batch = s_BudgetPlan[i];
                int batchCandidates = 0;
                for (int c = 0; c < batch.batchCommands; ++c)
                {
                    batchCandidates += (int)candidateCounts[batch.commandBegin + c];
                }

                maxCommands = math.max(maxCommands, batch.batchCommands);
                maxInstances = math.max(maxInstances, math.max(batchCandidates, safeBounds));
            }
        }

        /// <summary>
        /// True when the draw list fits a single payload; false means split batches or CPU fallback.
        /// </summary>
        public static bool CanSubmitSinglePayload(in PassView drawList)
        {
            return drawList.isValid
                && drawList.commandCount > 0
                && drawList.commandCount <= MaxCommands
                && drawList.instanceCount <= MaxInstances;
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
        /// Upload + GPU cull/compact/args. Must run before BeginRenderPass (D3D12 forbids SetBufferData inside a pass).
        /// </summary>
        internal static bool PrepareIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            MeshSceneResidency residency,
            ProfilingSampler profiler,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            MeshWorld world = null,
            MeshView view = default,
            MeshPassId passId = default,
            MeshCandidateTableStore candidateTables = null)
        {
            if (!CanPrepareIndirect(cmdBuffer, drawList, residency, payload, staging))
            {
                return false;
            }

            using (new ProfilingScope(cmdBuffer, profiler))
            {
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
                if (maxCommands <= 0 || !payload.TryEnsureCapacity(maxCommands, maxInstances))
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
                    if (!PrepareBatch(cmdBuffer, residency, payload, staging, batch.commandBegin, batch.batchCommands, world, view, passId, candidateTables))
                    {
                        MeshPipelineDiagnostics.GpuOverflowCount++;
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Draw only. Safe inside a raster render pass.
        /// </summary>
        internal static void DrawIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            int shaderPassIndex,
            MeshSceneResidency residency,
            MaterialPropertyBlock propertyBlock,
            ProfilingSampler profiler,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            string lightModeTag = null, ComputeBuffer previousTransforms = null)
        {
            if (cmdBuffer == null || !drawList.isValid || payload == null || staging == null || !staging.isValid)
            {
                return;
            }

            using (new ProfilingScope(cmdBuffer, profiler))
            {
                if (!TryPlanBatches(staging.candidateCounts, staging.commandCount, s_BatchPlan))
                {
                    return;
                }

                for (int i = 0; i < s_BatchPlan.Count; ++i)
                {
                    (int commandBegin, int batchCommands) batch = s_BatchPlan[i];
                    DrawBatch(cmdBuffer, drawList, shaderPassIndex, residency, propertyBlock, payload, staging, batch.commandBegin, batch.batchCommands, lightModeTag, previousTransforms);
                }
            }
        }

        /// <returns>False when GPU path cannot submit (caller should CpuDirect fallback).</returns>
        internal static bool SubmitIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            int shaderPassIndex,
            MeshSceneResidency residency,
            MaterialPropertyBlock propertyBlock,
            ProfilingSampler profiler,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging)
        {
            if (!PrepareIndirect(cmdBuffer, drawList, residency, profiler, payload, staging))
            {
                return false;
            }

            DrawIndirect(cmdBuffer, drawList, shaderPassIndex, residency, propertyBlock, profiler, payload, staging);
            return true;
        }

        private static bool CanPrepareIndirect(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            MeshSceneResidency residency,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0 || residency == null
                || residency.TransformBuffer.buffer == null
                || residency.BoundsCenterBuffer.buffer == null
                || residency.InstanceTransformIndexBuffer.buffer == null
                || residency.InstanceFlagsBuffer.buffer == null
                || residency.InstanceLayerMaskBuffer.buffer == null
                || residency.InstanceRenderingLayerBuffer.buffer == null
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
            MeshSceneResidency residency,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            int commandBegin,
            int batchCommandCount,
            MeshWorld world,
            in MeshView view,
            MeshPassId passId,
            MeshCandidateTableStore candidateTables)
        {
            if (!TryGetBatchInstanceCapacity(staging, commandBegin, batchCommandCount, out int instanceCapacity)
                || !payload.RequireCapacity(batchCommandCount, instanceCapacity)
                || candidateTables == null)
            {
                return false;
            }

            ComputeBuffer candidateTable = candidateTables.GetCandidateBuffer(passId);
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

            uint[] batchMeta = new uint[batchCommandCount * 4];
            for (int i = 0; i < batchCommandCount; ++i)
            {
                int src = commandBegin + i;
                batchMeta[i * 4 + 0] = staging.commandMeta[src * 4 + 0];
                batchMeta[i * 4 + 1] = staging.commandMeta[src * 4 + 1];
                batchMeta[i * 4 + 2] = staging.commandMeta[src * 4 + 2];
                batchMeta[i * 4 + 3] = (uint)math.max(0, (int)staging.candidateOffsets[src] - candidateBegin);
            }

            cmdBuffer.SetBufferData(payload.commandMeta, batchMeta, 0, 0, batchMeta.Length);
            cmdBuffer.SetComputeVectorArrayParam(s_Shader, ID_FrustumPlanes, staging.frustumPlanes);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_InstanceCount, boundsCount);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CommandCount, batchCommandCount);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CommandBegin, commandBegin);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateOffset, candidateBegin);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateSpan, batchCandidates);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateCount, staging.candidateCount);

            ComputeBuffer visibility = null;
            bool dispatchCull = true;
            if (world != null)
            {
                visibility = world.GetGpuVisibilityBuffer(view, boundsCount);
                dispatchCull = world.NeedsGpuCull(view);
            }

            if (visibility == null)
            {
                return false;
            }

            if (dispatchCull)
            {
                int viewLayerMask = view.layerMask;
                int viewRenderingLayerMask = (int)view.renderingLayerMask;
                int filterLayers = view.FilterRenderingLayers ? 1 : 0;
                cmdBuffer.SetComputeIntParam(s_Shader, ID_ViewLayerMask, viewLayerMask);
                cmdBuffer.SetComputeIntParam(s_Shader, ID_ViewRenderingLayerMask, viewRenderingLayerMask);
                cmdBuffer.SetComputeIntParam(s_Shader, ID_FilterRenderingLayers, filterLayers);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceBoundsCenter, residency.BoundsCenterBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceBoundsExtent, residency.BoundsExtentBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceFlags, residency.InstanceFlagsBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceLayerMask, residency.InstanceLayerMaskBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceRenderingLayer, residency.InstanceRenderingLayerBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_Visibility, visibility);
                int cullGroups = (boundsCount + 63) / 64;
                cmdBuffer.DispatchCompute(s_Shader, s_KernelCull, math.max(1, cullGroups), 1, 1);
                world.MarkGpuCulled(view);
            }

            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_VisibleCounts, payload.visibleCounts);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_IndirectArgs, payload.argsBuffer);
            int clearGroups = (batchCommandCount + 63) / 64;
            cmdBuffer.DispatchCompute(s_Shader, s_KernelClearCounts, math.max(1, clearGroups), 1, 1);

            if (batchCandidates > 0)
            {
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CandidateTable, candidateTable);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_Visibility, visibility);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_InstanceToTransform, residency.InstanceTransformIndexBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_ShadingIndices, payload.compactedIndices);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_VisibleCounts, payload.visibleCounts);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CommandMeta, payload.commandMeta);
                int groups = (batchCandidates + 63) / 64;
                cmdBuffer.DispatchCompute(s_Shader, s_KernelCompact, math.max(1, groups), 1, 1);
                MeshPipelineDiagnostics.CompactDispatchesPerFrame++;
            }

            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_CommandMeta, payload.commandMeta);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_VisibleCounts, payload.visibleCounts);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_IndirectArgs, payload.argsBuffer);
            int argsGroups = (batchCommandCount + 63) / 64;
            cmdBuffer.DispatchCompute(s_Shader, s_KernelBuildArgs, math.max(1, argsGroups), 1, 1);
            return true;
        }

        private static void DrawBatch(
            CommandBuffer cmdBuffer,
            in PassView drawList,
            int shaderPassIndex,
            MeshSceneResidency residency,
            MaterialPropertyBlock propertyBlock,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            int commandBegin,
            int batchCommandCount,
            string lightModeTag = null, ComputeBuffer previousTransforms = null)
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

                int batchCandOff = math.max(0, (int)staging.candidateOffsets[commandBegin + i] - candidateBegin);
                propertyBlock.Clear();
                propertyBlock.SetInt(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.InstanceIndexOffset, batchCandOff);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.InstanceIndexBuffer, payload.compactedIndices);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.TransformBuffer, residency.TransformBuffer.buffer);
                if (previousTransforms != null) propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.PreviousTransformBuffer, previousTransforms);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.RenderingLayerBuffer, residency.RenderingLayerBuffer.buffer);

                int passIndex = MeshPassShaderUtility.ResolvePassIndex(material, lightModeTag, shaderPassIndex);
                if (passIndex < 0)
                {
                    continue;
                }

                int argsOffset = i * 5 * sizeof(uint);
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
