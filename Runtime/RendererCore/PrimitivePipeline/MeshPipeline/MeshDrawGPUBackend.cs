using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
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
        public Vector4[] frustumPlanes;
        public int commandCount;
        public int candidateCount;
        public int boundsInstanceCount;
        public bool isValid;

        public static MeshDrawGpuStaging Build(in MeshDrawList drawList, in MeshViewCullingResult culling, int boundsInstanceCount)
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
                Mesh mesh = Resources.InstanceIDToObject(command.meshUnityId) as Mesh;
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
        public ComputeBuffer candidateIndices;
        public ComputeBuffer compactedIndices;
        public ComputeBuffer cmdInstanceOffsets;
        public ComputeBuffer candidateOffsets;
        public ComputeBuffer candidateCounts;
        public ComputeBuffer instanceVisibility;

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
                cmdInstanceOffsets?.Release();
                candidateOffsets?.Release();
                candidateCounts?.Release();

                capacityCommands = math.max(commands, 64);
                argsBuffer = new ComputeBuffer(capacityCommands * 5, sizeof(uint), ComputeBufferType.IndirectArguments);
                commandMeta = new ComputeBuffer(capacityCommands * 4, sizeof(uint), ComputeBufferType.Structured);
                visibleCounts = new ComputeBuffer(capacityCommands, sizeof(uint), ComputeBufferType.Structured);
                cmdInstanceOffsets = new ComputeBuffer(capacityCommands, sizeof(uint), ComputeBufferType.Structured);
                candidateOffsets = new ComputeBuffer(capacityCommands, sizeof(uint), ComputeBufferType.Structured);
                candidateCounts = new ComputeBuffer(capacityCommands, sizeof(uint), ComputeBufferType.Structured);
            }

            if (candidateIndices == null || capacityInstances < instances)
            {
                candidateIndices?.Release();
                compactedIndices?.Release();
                instanceVisibility?.Release();
                capacityInstances = math.max(instances, 256);
                candidateIndices = new ComputeBuffer(capacityInstances, sizeof(uint), ComputeBufferType.Structured);
                compactedIndices = new ComputeBuffer(capacityInstances, sizeof(uint), ComputeBufferType.Structured);
                instanceVisibility = new ComputeBuffer(capacityInstances, sizeof(uint), ComputeBufferType.Structured);
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

            if (candidateIndices == null || capacityInstances < instances)
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
            candidateIndices?.Release();
            compactedIndices?.Release();
            cmdInstanceOffsets?.Release();
            candidateOffsets?.Release();
            candidateCounts?.Release();
            instanceVisibility?.Release();
            argsBuffer = null;
            commandMeta = null;
            visibleCounts = null;
            candidateIndices = null;
            compactedIndices = null;
            cmdInstanceOffsets = null;
            candidateOffsets = null;
            candidateCounts = null;
            instanceVisibility = null;
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
        private static int s_KernelPrefixSum = -1;
        private static int s_KernelScatter = -1;
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
            "CullInstances",
            "ClearCommandCounts",
            "CompactCommandInstances",
            "PrefixSumCommands",
            "ScatterVisibleInstances",
            "BuildIndirectArgs"
        };

        private static readonly int ID_InstanceCount = Shader.PropertyToID("_InstanceCount");
        private static readonly int ID_CommandCount = Shader.PropertyToID("_CommandCount");
        private static readonly int ID_CandidateCount = Shader.PropertyToID("_CandidateCount");
        private static readonly int ID_CommandIndex = Shader.PropertyToID("_CommandIndex");
        private static readonly int ID_CandidateOffset = Shader.PropertyToID("_CandidateOffset");
        private static readonly int ID_CandidateSpan = Shader.PropertyToID("_CandidateSpan");
        private static readonly int ID_FrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int ID_InstanceBoundsCenter = Shader.PropertyToID("_InstanceBoundsCenter");
        private static readonly int ID_InstanceBoundsExtent = Shader.PropertyToID("_InstanceBoundsExtent");
        private static readonly int ID_InstanceVisibility = Shader.PropertyToID("_InstanceVisibility");
        private static readonly int ID_CommandMeta = Shader.PropertyToID("_CommandMeta");
        private static readonly int ID_CommandCandidateOffsets = Shader.PropertyToID("_CommandCandidateOffsets");
        private static readonly int ID_CommandCandidateCounts = Shader.PropertyToID("_CommandCandidateCounts");
        private static readonly int ID_CandidateIndices = Shader.PropertyToID("_CandidateIndices");
        private static readonly int ID_CompactedIndices = Shader.PropertyToID("_CompactedIndices");
        private static readonly int ID_VisibleCounts = Shader.PropertyToID("_VisibleCounts");
        private static readonly int ID_CommandInstanceOffsets = Shader.PropertyToID("_CommandInstanceOffsets");
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

        internal static MeshDrawGpuStaging CreateStaging(in MeshDrawList drawList, in MeshViewCullingResult culling, int boundsInstanceCount)
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
        public static bool CanSubmitSinglePayload(in MeshDrawList drawList)
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

        /// <returns>False when GPU path cannot submit (caller should CpuDirect fallback).</returns>
        public static bool SubmitIndirect(
            CommandBuffer cmdBuffer,
            in MeshDrawList drawList,
            int shaderPassIndex,
            MeshSceneResidency residency,
            MaterialPropertyBlock propertyBlock,
            ProfilingSampler profiler,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging)
        {
            if (cmdBuffer == null || !drawList.isValid || drawList.commandCount == 0 || residency == null
                || residency.TransformBuffer.buffer == null
                || residency.PreviousTransformBuffer.buffer == null
                || residency.BoundsCenterBuffer.buffer == null
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

            using (new ProfilingScope(cmdBuffer, profiler))
            {
                if (!TryPlanBatches(staging.candidateCounts, staging.commandCount, s_BatchPlan))
                {
                    MeshPipelineDiagnostics.GpuOverflowCount++;
                    return false;
                }

                // Budget all batches before any dispatch; never grow ComputeBuffers mid-recording.
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

                // Require every batch before the first dispatch so a late capacity miss never
                // leaves a partially recorded GPU path that would double-draw on CPU fallback.
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
                    if (!SubmitBatch(
                        cmdBuffer,
                        drawList,
                        shaderPassIndex,
                        residency,
                        propertyBlock,
                        payload,
                        staging,
                        batch.commandBegin,
                        batch.batchCommands))
                    {
                        // Should be unreachable after the pre-Require sweep above.
                        MeshPipelineDiagnostics.GpuOverflowCount++;
                        return false;
                    }
                }

                return true;
            }
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

        private static bool SubmitBatch(
            CommandBuffer cmdBuffer,
            in MeshDrawList drawList,
            int shaderPassIndex,
            MeshSceneResidency residency,
            MaterialPropertyBlock propertyBlock,
            MeshDrawGpuPayload payload,
            MeshDrawGpuStaging staging,
            int commandBegin,
            int batchCommandCount)
        {
            if (!TryGetBatchInstanceCapacity(staging, commandBegin, batchCommandCount, out int instanceCapacity)
                || !payload.RequireCapacity(batchCommandCount, instanceCapacity))
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
            payload.commandCount = batchCommandCount;

            // Build batch-local staging slices.
            uint[] batchMeta = new uint[batchCommandCount * 4];
            uint[] batchCandOff = new uint[batchCommandCount];
            uint[] batchCandCount = new uint[batchCommandCount];
            uint[] batchCandidatesIndices = new uint[math.max(1, batchCandidates)];

            for (int i = 0; i < batchCommandCount; ++i)
            {
                int src = commandBegin + i;
                batchMeta[i * 4 + 0] = staging.commandMeta[src * 4 + 0];
                batchMeta[i * 4 + 1] = staging.commandMeta[src * 4 + 1];
                batchMeta[i * 4 + 2] = staging.commandMeta[src * 4 + 2];
                batchMeta[i * 4 + 3] = staging.commandMeta[src * 4 + 3];
                batchCandOff[i] = (uint)math.max(0, (int)staging.candidateOffsets[src] - candidateBegin);
                batchCandCount[i] = staging.candidateCounts[src];
            }

            for (int i = 0; i < batchCandidates; ++i)
            {
                batchCandidatesIndices[i] = (uint)staging.candidateIndices[candidateBegin + i];
            }

            cmdBuffer.SetBufferData(payload.commandMeta, batchMeta, 0, 0, batchMeta.Length);
            cmdBuffer.SetBufferData(payload.candidateOffsets, batchCandOff, 0, 0, batchCommandCount);
            cmdBuffer.SetBufferData(payload.candidateCounts, batchCandCount, 0, 0, batchCommandCount);
            if (batchCandidates > 0)
            {
                cmdBuffer.SetBufferData(payload.candidateIndices, batchCandidatesIndices, 0, 0, batchCandidates);
            }

            cmdBuffer.SetComputeVectorArrayParam(s_Shader, ID_FrustumPlanes, staging.frustumPlanes);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_InstanceCount, boundsCount);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CommandCount, batchCommandCount);
            cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateCount, batchCandidates);

            // 1) Cull all resident slots into per-payload visibility bits.
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceBoundsCenter, residency.BoundsCenterBuffer.buffer);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceBoundsExtent, residency.BoundsExtentBuffer.buffer);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCull, ID_InstanceVisibility, payload.instanceVisibility);
            int cullGroups = (boundsCount + 63) / 64;
            cmdBuffer.DispatchCompute(s_Shader, s_KernelCull, math.max(1, cullGroups), 1, 1);

            // 2) Clear per-command counts / args.
            if (s_KernelClearCounts >= 0)
            {
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_VisibleCounts, payload.visibleCounts);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_CommandInstanceOffsets, payload.cmdInstanceOffsets);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelClearCounts, ID_IndirectArgs, payload.argsBuffer);
                int clearGroups = (batchCommandCount + 63) / 64;
                cmdBuffer.DispatchCompute(s_Shader, s_KernelClearCounts, math.max(1, clearGroups), 1, 1);
            }

            // 3) Compact candidates per command into the payload index buffer.
            for (int i = 0; i < batchCommandCount; ++i)
            {
                uint span = batchCandCount[i];
                if (span == 0)
                {
                    continue;
                }

                cmdBuffer.SetComputeIntParam(s_Shader, ID_CommandIndex, i);
                cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateOffset, (int)batchCandOff[i]);
                cmdBuffer.SetComputeIntParam(s_Shader, ID_CandidateSpan, (int)span);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CandidateIndices, payload.candidateIndices);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CompactedIndices, payload.compactedIndices);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_InstanceVisibility, payload.instanceVisibility);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_InstanceTransformIndex, residency.InstanceTransformIndexBuffer.buffer);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_VisibleCounts, payload.visibleCounts);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelCompact, ID_CommandCandidateOffsets, payload.candidateOffsets);
                int groups = ((int)span + 63) / 64;
                cmdBuffer.DispatchCompute(s_Shader, s_KernelCompact, math.max(1, groups), 1, 1);
            }

            // 4) Prefix sum + scatter offsets (keeps shader InstanceIndexOffset = candidate base).
            if (s_KernelPrefixSum >= 0)
            {
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelPrefixSum, ID_VisibleCounts, payload.visibleCounts);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelPrefixSum, ID_CommandInstanceOffsets, payload.cmdInstanceOffsets);
                cmdBuffer.DispatchCompute(s_Shader, s_KernelPrefixSum, 1, 1, 1);
            }

            if (s_KernelScatter >= 0)
            {
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelScatter, ID_CommandInstanceOffsets, payload.cmdInstanceOffsets);
                cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelScatter, ID_CommandCandidateOffsets, payload.candidateOffsets);
                int scatterGroups = (batchCommandCount + 63) / 64;
                cmdBuffer.DispatchCompute(s_Shader, s_KernelScatter, math.max(1, scatterGroups), 1, 1);
            }

            // 5) Build indirect args from GPU visible counts.
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_CommandMeta, payload.commandMeta);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_VisibleCounts, payload.visibleCounts);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_KernelBuildArgs, ID_IndirectArgs, payload.argsBuffer);
            int argsGroups = (batchCommandCount + 63) / 64;
            cmdBuffer.DispatchCompute(s_Shader, s_KernelBuildArgs, math.max(1, argsGroups), 1, 1);

            // 6) Draw
            for (int i = 0; i < batchCommandCount; ++i)
            {
                MeshDrawCommand command = drawList.commands[commandBegin + i];
                Mesh mesh = Resources.InstanceIDToObject(command.meshUnityId) as Mesh;
                Material material = Resources.InstanceIDToObject(command.materialUnityId) as Material;
                if (mesh == null || material == null)
                {
                    continue;
                }

                propertyBlock.Clear();
                propertyBlock.SetInt(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.InstanceIndexOffset, (int)batchCandOff[i]);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.InstanceIndexBuffer, payload.compactedIndices);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.TransformBuffer, residency.TransformBuffer.buffer);
                propertyBlock.SetBuffer(InfinityTech.Rendering.Pipeline.InfinityShaderIDs.PreviousTransformBuffer, residency.PreviousTransformBuffer.buffer);

                int argsOffset = i * 5 * sizeof(uint);
                cmdBuffer.DrawMeshInstancedIndirect(mesh, command.sectionIndex, material, shaderPassIndex, payload.argsBuffer, argsOffset, propertyBlock);
            }

            return true;
        }

        private static void InvalidateKernels()
        {
            s_KernelCull = -1;
            s_KernelClearCounts = -1;
            s_KernelCompact = -1;
            s_KernelPrefixSum = -1;
            s_KernelScatter = -1;
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

            s_KernelCull = s_Shader.FindKernel("CullInstances");
            s_KernelClearCounts = s_Shader.FindKernel("ClearCommandCounts");
            s_KernelCompact = s_Shader.FindKernel("CompactCommandInstances");
            s_KernelPrefixSum = s_Shader.FindKernel("PrefixSumCommands");
            s_KernelScatter = s_Shader.FindKernel("ScatterVisibleInstances");
            s_KernelBuildArgs = s_Shader.FindKernel("BuildIndirectArgs");
            s_KernelsValid = true;
        }
    }
}
