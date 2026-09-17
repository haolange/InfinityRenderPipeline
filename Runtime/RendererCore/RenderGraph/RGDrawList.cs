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
        public MeshView view;
        public MeshPassId passId;
        public MeshDrawPipeline pipeline;
        public MeshPassContext pass;
        public MeshViewCullingResult culling;
        public MeshVisibilityHandle visibilityHandle;
        public MeshVisibilityShare visibilityShare;
        public MeshWorld meshWorld;
        public bool ownsCulling;
        public List<int> consumerPassIndices;
        public ERGDrawListCompileState state;
        public EMeshBackendPolicy selectedBackend;
        public MeshDrawExtract extract;
        public PassView resolvedList;
        public MeshDrawGpuStaging gpuStaging;
        public MeshDrawGpuPayload gpuPayload;
        public FBufferRef cpuIndexBuffer;
        public RGBufferRef gpuVisibilityBuffer;
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

        static void ValidateMotionInput(in MeshPassContext pass)
        {
            if (pass.lightModeTag == "MotionPass" && pass.previousTransforms == null)
                throw new System.InvalidOperationException("Motion draws require the selected view's previous-transform producer.");
        }

        public RGDrawListRef Declare(
            MeshDrawPipeline pipeline,
            in MeshPassContext pass,
            in MeshViewCullingResult culling,
            in MeshView view = default,
            MeshPassId passId = default,
            MeshWorld meshWorld = null)
        {
            ValidateMotionInput(pass);
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
                view = view,
                passId = passId,
                pipeline = pipeline,
                pass = pass,
                culling = culling,
                visibilityHandle = MeshVisibilityHandle.Invalid,
                visibilityShare = null,
                meshWorld = meshWorld,
                ownsCulling = owns,
                consumerPassIndices = new List<int>(4),
                state = ERGDrawListCompileState.Declared,
                selectedBackend = EMeshBackendPolicy.CpuDirect,
                extract = default,
                resolvedList = PassView.Invalid,
                gpuStaging = null,
                gpuPayload = null,
                hasSideEffect = false
            });
            return new RGDrawListRef(m_ContextId, index, m_GraphGeneration);
        }

        public RGDrawListRef Declare(
            MeshDrawPipeline pipeline,
            in MeshPassContext pass,
            MeshVisibilityHandle visibilityHandle,
            MeshVisibilityShare visibilityShare,
            in MeshView view = default,
            MeshPassId passId = default,
            MeshWorld meshWorld = null)
        {
            ValidateMotionInput(pass);
            MeshViewCullingResult culling = default;
            if (visibilityShare != null && visibilityHandle.IsValid)
            {
                visibilityShare.AddRef(visibilityHandle);
                culling = visibilityShare.GetResult(visibilityHandle);
            }

            int index = m_Records.Count;
            m_Records.Add(new RGDrawListRecord
            {
                view = view,
                passId = passId,
                pipeline = pipeline,
                pass = pass,
                culling = culling,
                visibilityHandle = visibilityHandle,
                visibilityShare = visibilityShare,
                meshWorld = meshWorld,
                ownsCulling = false,
                consumerPassIndices = new List<int>(4),
                state = ERGDrawListCompileState.Declared,
                selectedBackend = EMeshBackendPolicy.CpuDirect,
                extract = default,
                resolvedList = PassView.Invalid,
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

        public void BindGpuVisibility(int index, in RGBufferRef visibility)
        {
            if (index < 0 || index >= m_Records.Count)
            {
                return;
            }

            RGDrawListRecord record = m_Records[index];
            record.gpuVisibilityBuffer = visibility;
            m_Records[index] = record;
        }

        public bool HasLiveView(in MeshView view)
        {
            ulong key = MeshGpuVisibilityCache.MakeKey(view);
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                if (record.state == ERGDrawListCompileState.Declared
                    || record.state == ERGDrawListCompileState.Released)
                {
                    continue;
                }

                if (MeshGpuVisibilityCache.MakeKey(record.view) == key)
                {
                    return true;
                }
            }

            return false;
        }

        public void PrepareLive(MeshWorld world)
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                if (record.state != ERGDrawListCompileState.Live)
                {
                    // Unused lists never produce Visibility or Compact.
                    record.state = ERGDrawListCompileState.Released;
                    m_Records[i] = record;
                    continue;
                }

                if (record.pipeline == null)
                {
                    record.state = ERGDrawListCompileState.Released;
                    m_Records[i] = record;
                    continue;
                }

                record.selectedBackend = MeshDrawGPUBackend.SelectPolicy(record.pass.backendPolicy);
                if (record.selectedBackend != EMeshBackendPolicy.GpuIndirect)
                {
                    AcquireCpuVisibility(ref record, record.meshWorld ?? world);
                    record.extract = record.pipeline.Extract(record.passId, record.culling);
                }

                record.state = ERGDrawListCompileState.Scheduled;
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
                if (record.selectedBackend == EMeshBackendPolicy.GpuIndirect)
                {
                    int boundsCount = record.pipeline.GetBoundsCullCount();
                    record.resolvedList = record.pipeline.CandidateTables.BuildPassView(record.passId);
                    record.gpuStaging = record.pipeline.CandidateTables.CreateStaging(record.passId, record.view, boundsCount);
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
                            FallbackCpuDirect(ref record, countOverflow: true);
                        }
                    }
                    else
                    {
                        FallbackCpuDirect(ref record, countOverflow: true);
                    }
                }
                else
                {
                    record.resolvedList = record.pipeline.Resolve(ref record.extract);
                }
            }

            record.state = ERGDrawListCompileState.Resolved;
            m_Records[index] = record;
        }

        public bool NeedsGpuPrepare(in RGDrawListRef draws)
        {
            if (!IsLiveRef(draws))
            {
                return false;
            }

            RGDrawListRecord record = m_Records[draws.index];
            return record.state == ERGDrawListCompileState.Resolved
                && record.pipeline != null
                && record.selectedBackend == EMeshBackendPolicy.GpuIndirect
                && record.gpuPayload != null
                && record.gpuStaging != null;
        }

        public string SliceSampleName(in RGDrawListRef draws)
        {
            if (!IsLiveRef(draws))
            {
                return null;
            }

            MeshView view = m_Records[draws.index].view;
            switch (view.kind)
            {
                case EMeshViewKind.CascadeShadow:
                    return $"CascadeSlice{view.subviewIndex}";
                case EMeshViewKind.LocalShadow:
                    return $"LocalShadowSlice{view.subviewIndex}";
                default:
                    return null;
            }
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
                && record.pipeline.PrepareGpu(cmdBuffer, record.resolvedList, record.gpuPayload, record.gpuStaging, record.meshWorld, record.view, record.passId))
            {
                m_Records[draws.index] = record;
                return;
            }

            FallbackCpuDirect(ref record, countOverflow: false);
            record.cpuIndexBuffer = record.pipeline.PrepareCpuDirect(cmdBuffer, record.resolvedList);
            m_Records[draws.index] = record;
        }

        static void AcquireCpuVisibility(ref RGDrawListRecord record, MeshWorld producer)
        {
            if (record.visibilityHandle.IsValid || producer == null)
            {
                return;
            }

            record.visibilityHandle = producer.AcquireVisibility(record.view);
            record.visibilityShare = producer.VisibilityShare;
            record.culling = record.visibilityShare.GetResult(record.visibilityHandle);
            producer.RefineVisibilityHiZ(record.visibilityHandle);
        }

        static void FallbackCpuDirect(ref RGDrawListRecord record, bool countOverflow)
        {
            if (countOverflow)
            {
                MeshPipelineDiagnostics.GpuOverflowCount++;
            }
            record.selectedBackend = EMeshBackendPolicy.CpuDirect;
            record.gpuStaging = null;
            if (record.gpuPayload != null)
            {
                MeshDrawGPUBackend.RetirePayload(record.gpuPayload);
                record.gpuPayload = null;
            }

            AcquireCpuVisibility(ref record, record.meshWorld);
            if (record.pipeline != null && !record.extract.isCreated)
            {
                record.extract = record.pipeline.Extract(record.passId, record.culling);
            }

            if (record.pipeline != null)
            {
                record.resolvedList = record.pipeline.Resolve(ref record.extract);
            }
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
                    record.pass.shaderPassIndex,
                    record.gpuPayload,
                    record.gpuStaging,
                    record.pass.lightModeTag, record.pass.previousTransforms);
                return;
            }

            record.pipeline.SubmitCpuDirect(cmdBuffer, record.resolvedList, record.pass.shaderPassIndex, record.cpuIndexBuffer, record.pass.lightModeTag, record.pass.previousTransforms);
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
                if (record.pipeline != null && record.extract.isCreated)
                {
                    record.pipeline.Release(ref record.extract);
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

                record.resolvedList = PassView.Invalid;
                record.state = ERGDrawListCompileState.Released;
                m_Records[i] = record;
            }

            m_Records.Clear();
            // Invalidate any RGDrawListRef captured from this graph lifetime.
            unchecked { ++m_GraphGeneration; }
        }
    }
}
