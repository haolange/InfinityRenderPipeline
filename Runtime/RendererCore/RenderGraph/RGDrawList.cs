using System.Collections.Generic;
using System.Threading;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.RenderGraph
{
    /// <summary>
    /// Logical DrawList handle. Not an ERGResourceType; lives in an independent registry.
    /// contextId binds to a registry instance; generation binds to a single graph lifetime.
    /// </summary>
    public readonly struct RGDrawListRef
    {
        internal readonly int contextId;
        internal readonly int index;
        internal readonly int generation;

        internal RGDrawListRef(int contextId, int index, int generation)
        {
            this.contextId = contextId;
            this.index = index;
            this.generation = generation;
        }

        /// <summary>
        /// Context-only validity. Generation/index match is enforced at UseDrawList / Draw / EnsureResolved.
        /// </summary>
        public bool IsValid => contextId > 0;

        public static RGDrawListRef Invalid => new RGDrawListRef(-1, 0, 0);
    }

    internal enum ERGDrawListCompileState : byte
    {
        Declared = 0,
        Live = 1,
        Scheduled = 2,
        Resolved = 3,
        Released = 4
    }

    internal struct RGDrawListRecord
    {
        public MeshDrawPipeline pipeline;
        public MeshDrawRequest request;
        public MeshViewCullingResult culling;
        public MeshVisibilityHandle visibilityHandle;
        public MeshVisibilityShare visibilityShare;
        public bool ownsCulling;
        public List<int> consumerPassIndices;
        public ERGDrawListCompileState state;
        public EMeshBackendPolicy selectedBackend;
        public MeshDrawBuild build;
        public MeshDrawList resolvedList;
        public MeshDrawGpuStaging gpuStaging;
        public MeshDrawGpuPayload gpuPayload;
        public FBufferRef cpuIndexBuffer;
        public bool hasSideEffect;
    }

    /// <summary>
    /// Per-graph DrawList registry used by compile / resolve / submit.
    /// </summary>
    internal sealed class RGDrawListContext
    {
        private static int s_NextContextId;

        private readonly int m_ContextId;
        private readonly List<RGDrawListRecord> m_Records = new List<RGDrawListRecord>(16);
        // Start at 1 so default(RGDrawListRef) {index=0, generation=0} never matches a live graph.
        private int m_GraphGeneration = 1;

        public RGDrawListContext()
        {
            m_ContextId = Interlocked.Increment(ref s_NextContextId);
        }

        public int Count => m_Records.Count;
        public int GraphGeneration => m_GraphGeneration;
        public int ContextId => m_ContextId;

        public bool IsLiveRef(in RGDrawListRef draws)
        {
            return draws.contextId == m_ContextId
                && draws.generation == m_GraphGeneration
                && draws.index >= 0
                && draws.index < m_Records.Count;
        }

        public RGDrawListRef Declare(MeshDrawPipeline pipeline, in MeshDrawRequest request, in MeshViewCullingResult culling)
        {
            // Value-copy path: first record owns NativeArrays; later declares sharing the same arrays must not double-free.
            // Prefer Declare(..., MeshVisibilityHandle) for shared visibility.
            bool owns = culling.isValid;
            if (owns)
            {
                for (int i = 0; i < m_Records.Count; ++i)
                {
                    RGDrawListRecord existing = m_Records[i];
                    if (existing.ownsCulling && existing.culling.isValid
                        && existing.culling.instanceVisibility.Equals(culling.instanceVisibility))
                    {
                        owns = false;
                        break;
                    }
                }
            }

            int index = m_Records.Count;
            m_Records.Add(new RGDrawListRecord
            {
                pipeline = pipeline,
                request = request,
                culling = culling,
                visibilityHandle = MeshVisibilityHandle.Invalid,
                visibilityShare = null,
                ownsCulling = owns,
                consumerPassIndices = new List<int>(4),
                state = ERGDrawListCompileState.Declared,
                selectedBackend = EMeshBackendPolicy.CpuDirect,
                build = default,
                resolvedList = MeshDrawList.Invalid,
                gpuStaging = null,
                gpuPayload = null,
                hasSideEffect = false
            });
            return new RGDrawListRef(m_ContextId, index, m_GraphGeneration);
        }

        public RGDrawListRef Declare(
            MeshDrawPipeline pipeline,
            in MeshDrawRequest request,
            MeshVisibilityHandle visibilityHandle,
            MeshVisibilityShare visibilityShare)
        {
            MeshViewCullingResult culling = default;
            if (visibilityShare != null && visibilityHandle.IsValid)
            {
                visibilityShare.AddRef(visibilityHandle);
                culling = visibilityShare.GetResult(visibilityHandle);
            }

            int index = m_Records.Count;
            m_Records.Add(new RGDrawListRecord
            {
                pipeline = pipeline,
                request = request,
                culling = culling,
                visibilityHandle = visibilityHandle,
                visibilityShare = visibilityShare,
                ownsCulling = false,
                consumerPassIndices = new List<int>(4),
                state = ERGDrawListCompileState.Declared,
                selectedBackend = EMeshBackendPolicy.CpuDirect,
                build = default,
                resolvedList = MeshDrawList.Invalid,
                gpuStaging = null,
                gpuPayload = null,
                hasSideEffect = false
            });
            return new RGDrawListRef(m_ContextId, index, m_GraphGeneration);
        }

        public RGDrawListRecord GetRecordCopy(int index) => m_Records[index];
        public void SetRecord(int index, in RGDrawListRecord record)
        {
            m_Records[index] = record;
        }

        public void ClearConsumers()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                record.consumerPassIndices.Clear();
                if (record.state != ERGDrawListCompileState.Released)
                {
                    record.state = ERGDrawListCompileState.Declared;
                }
                m_Records[i] = record;
            }
        }

        public void MarkLiveConsumer(int drawListIndex, int passIndex)
        {
            RGDrawListRecord record = m_Records[drawListIndex];
            record.consumerPassIndices.Add(passIndex);
            if (record.state == ERGDrawListCompileState.Declared)
            {
                record.state = ERGDrawListCompileState.Live;
            }
            m_Records[drawListIndex] = record;
        }

        public void ScheduleLive()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                if (record.state != ERGDrawListCompileState.Live)
                {
                    // Culled / unused: zero schedule, zero TempJob alloc.
                    // Visibility ownership is still released in ReleaseAll.
                    record.state = ERGDrawListCompileState.Released;
                    m_Records[i] = record;
                    continue;
                }

                record.selectedBackend = MeshDrawGPUBackend.SelectPolicy(record.request.backendPolicy);
                if (record.pipeline != null)
                {
                    record.build = record.pipeline.Schedule(record.request, record.culling);
                    record.state = ERGDrawListCompileState.Scheduled;
                }
                else
                {
                    record.state = ERGDrawListCompileState.Released;
                }
                m_Records[i] = record;
            }
        }

        public void EnsureResolved(in RGDrawListRef draws)
        {
            if (!IsLiveRef(draws))
            {
                return;
            }

            EnsureResolved(draws.index);
        }

        public void EnsureResolved(int index)
        {
            if (index < 0 || index >= m_Records.Count)
            {
                return;
            }

            RGDrawListRecord record = m_Records[index];
            if (record.state == ERGDrawListCompileState.Resolved || record.state != ERGDrawListCompileState.Scheduled)
            {
                return;
            }

            if (record.pipeline != null)
            {
                record.resolvedList = record.pipeline.Resolve(ref record.build);
                if (record.selectedBackend == EMeshBackendPolicy.GpuIndirect)
                {
                    // Overflow splits into multiple Submit batches; single-command overflow falls back to CpuDirect.
                    int boundsCount = record.pipeline.GetBoundsCullCount();
                    record.gpuStaging = MeshDrawGPUBackend.CreateStaging(record.resolvedList, record.culling, boundsCount);
                    if (record.gpuStaging != null)
                    {
                        MeshDrawGPUBackend.ComputePayloadBudget(
                            record.gpuStaging.candidateCounts,
                            record.gpuStaging.commandCount,
                            boundsCount,
                            out int maxCommands,
                            out int maxInstances);
                        if (maxCommands > 0)
                        {
                            record.gpuPayload = MeshDrawGPUBackend.RentPayload();
                            record.gpuPayload.EnsureCapacity(maxCommands, maxInstances);
                        }
                        else
                        {
                            record.gpuStaging = null;
                            record.selectedBackend = EMeshBackendPolicy.CpuDirect;
                            MeshPipelineDiagnostics.GpuOverflowCount++;
                        }
                    }
                    else
                    {
                        record.selectedBackend = EMeshBackendPolicy.CpuDirect;
                        MeshPipelineDiagnostics.GpuOverflowCount++;
                    }
                }
            }

            record.state = ERGDrawListCompileState.Resolved;
            m_Records[index] = record;
        }

        public void PrepareSubmit(CommandBuffer cmdBuffer, in RGDrawListRef draws)
        {
            if (cmdBuffer == null || !IsLiveRef(draws))
            {
                return;
            }

            RGDrawListRecord record = m_Records[draws.index];
            if (record.state != ERGDrawListCompileState.Resolved || record.pipeline == null)
            {
                return;
            }

            if (record.selectedBackend == EMeshBackendPolicy.GpuIndirect
                && record.gpuPayload != null
                && record.gpuStaging != null
                && record.pipeline.PrepareGpu(cmdBuffer, record.resolvedList, record.gpuPayload, record.gpuStaging))
            {
                m_Records[draws.index] = record;
                return;
            }

            record.selectedBackend = EMeshBackendPolicy.CpuDirect;
            record.cpuIndexBuffer = record.pipeline.PrepareCpuDirect(cmdBuffer, record.resolvedList);
            m_Records[draws.index] = record;
        }

        public void Submit(CommandBuffer cmdBuffer, in RGDrawListRef draws)
        {
            if (cmdBuffer == null || !IsLiveRef(draws))
            {
                return;
            }

            RGDrawListRecord record = m_Records[draws.index];
            if (record.state != ERGDrawListCompileState.Resolved || record.pipeline == null)
            {
                return;
            }

            if (record.selectedBackend == EMeshBackendPolicy.GpuIndirect
                && record.gpuPayload != null
                && record.gpuStaging != null)
            {
                record.pipeline.SubmitGpu(
                    cmdBuffer,
                    record.resolvedList,
                    record.request.shaderPassIndex,
                    record.gpuPayload,
                    record.gpuStaging);
                return;
            }

            record.pipeline.SubmitCpuDirect(cmdBuffer, record.resolvedList, record.request.shaderPassIndex, record.cpuIndexBuffer);
        }

        /// <summary>
        /// Logical cleanup only: retire GPU payloads / CPU rented buffers and free visibility ownership.
        /// Physical ReturnPayload / ReleaseBuffer runs after <c>ScriptableRenderContext.Submit</c>
        /// via <see cref="MeshDrawGPUBackend.FlushRetiredPayloads"/> / <see cref="MeshDrawPipeline.FlushRetiredBuffers"/>.
        /// </summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                if (record.pipeline != null && record.build.isCreated)
                {
                    record.pipeline.Release(ref record.build);
                }

                if (record.gpuPayload != null)
                {
                    MeshDrawGPUBackend.RetirePayload(record.gpuPayload);
                    record.gpuPayload = null;
                }

                record.gpuStaging = null;

                if (record.visibilityHandle.IsValid && record.visibilityShare != null)
                {
                    record.visibilityShare.Release(record.visibilityHandle);
                    record.visibilityHandle = MeshVisibilityHandle.Invalid;
                    record.visibilityShare = null;
                }
                else if (record.ownsCulling)
                {
                    record.culling.Release();
                    record.ownsCulling = false;
                }

                // Idempotent: same pipeline may appear on multiple records.
                // Moves frame-rented CPU buffers into the retirement queue (not pool Return).
                record.pipeline?.ReleaseFrameBuffers();

                record.resolvedList = MeshDrawList.Invalid;
                record.state = ERGDrawListCompileState.Released;
                m_Records[i] = record;
            }

            m_Records.Clear();
            // Invalidate any RGDrawListRef captured from this graph lifetime.
            unchecked { ++m_GraphGeneration; }
        }
    }
}
