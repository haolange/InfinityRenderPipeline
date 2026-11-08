using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Logical draw-list handle. Not an ERGResourceType; it lives in the mesh graph extension's registry.
    /// The registry reference binds it to one builder; the generation binds it to one graph lifetime.
    /// </summary>
    public readonly struct RGMeshDrawListHandle
    {
        internal readonly MeshDrawListRegistry context;
        internal readonly int index;
        internal readonly int generation;

        internal RGMeshDrawListHandle(MeshDrawListRegistry context, int index, int generation)
        {
            this.context = context;
            this.index = index;
            this.generation = generation;
        }

        /// <summary>Registry-only validity. Generation and index are checked by UseMeshDrawList and Draw.</summary>
        public bool IsValid => context != null;

        public static RGMeshDrawListHandle Invalid => default;
    }

    internal enum EMeshDrawListState : byte
    {
        Declared = 0,
        Live = 1,
        Scheduled = 2,
        Released = 3
    }

    internal struct MeshDrawListRecord
    {
        public MeshView view;
        public MeshPassCache cache;
        public EMeshDrawBackendPolicy backend;
        public MeshWorldGPUTables tables;
        public MeshMotionBinding motion;
        public EMeshDrawListState state;
        public List<int> consumerPassIndices;
        // CPU direct
        public MeshVisibilityHandle visibility;
        public MeshDrawListBuild build;
        public BufferHandle cpuInstances;
        public BufferHandle cpuFades;
        // GPU indirect
        public RGBufferHandle gpuVisibility;
        public BufferHandle gpuVisibilityBuffer;
        public int gpuVisibilityBase;
        public MeshGPUDrawPayload payload;
        public int commandCount;
        public int candidateCount;
        public uint cacheRevision;
    }

    /// <summary>Per-graph draw-list registry used by compile, preparation and submit.</summary>
    internal sealed class MeshDrawListRegistry
    {
        readonly MeshWorld m_World;
        readonly List<MeshDrawListRecord> m_Records = new List<MeshDrawListRecord>(16);
        readonly Stack<List<int>> m_ConsumerLists = new Stack<List<int>>();
        // Start at 1 so default(RGMeshDrawListHandle) never matches a live graph.
        int m_GraphGeneration = 1;

        internal MeshDrawListRegistry(MeshWorld world) => m_World = world;

        public int Count => m_Records.Count;
        public int GraphGeneration => m_GraphGeneration;

        public bool IsLive(in RGMeshDrawListHandle draws)
            => draws.context == this && draws.generation == m_GraphGeneration && draws.index >= 0 && draws.index < m_Records.Count;

        public MeshDrawListRecord GetRecordCopy(int index) => m_Records[index];

        public RGMeshDrawListHandle Declare(in MeshView view, in MeshPassDesc desc)
        {
            MeshPassCache cache = m_World.GetPassCache(desc);
            cache.EnsureShaderBindings();
            MeshMotionBinding motion = m_World.GetPreviousTransforms(view.viewKey);
            if (desc.requirePreviousTransforms && !motion.isValid)
                throw new InvalidOperationException($"Mesh pass '{desc.lightMode}' requires the view's previous-transform producer.");
            MeshWorldGPUTables tables = m_World.gpu.Snapshot();
            if (!tables.isValid) throw new InvalidOperationException("Draw lists require uploaded MeshWorld residency.");
            EMeshDrawBackendPolicy backend = m_World.SelectBackend(desc);
            tables.version.Retain();
            int index = m_Records.Count;
            m_Records.Add(new MeshDrawListRecord
            {
                view = view,
                cache = cache,
                backend = backend,
                tables = tables,
                motion = motion,
                state = EMeshDrawListState.Declared,
                consumerPassIndices = m_ConsumerLists.Count > 0 ? m_ConsumerLists.Pop() : new List<int>(4),
                visibility = MeshVisibilityHandle.Invalid
            });
            return new RGMeshDrawListHandle(this, index, m_GraphGeneration);
        }

        public void ClearConsumers()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                MeshDrawListRecord record = m_Records[i];
                record.consumerPassIndices.Clear();
                if (record.state != EMeshDrawListState.Released) record.state = EMeshDrawListState.Declared;
                m_Records[i] = record;
            }
        }

        public void MarkLiveConsumer(int drawListIndex, int passIndex)
        {
            MeshDrawListRecord record = m_Records[drawListIndex];
            record.consumerPassIndices.Add(passIndex);
            if (record.state == EMeshDrawListState.Declared) record.state = EMeshDrawListState.Live;
            m_Records[drawListIndex] = record;
        }

        public void BindGPUVisibility(int index, in RGBufferHandle visibility, in BufferHandle buffer, int visibilityBase = 0)
        {
            MeshDrawListRecord record = m_Records[index];
            record.gpuVisibility = visibility;
            record.gpuVisibilityBuffer = buffer;
            record.gpuVisibilityBase = visibilityBase;
            m_Records[index] = record;
        }

        /// <summary>
        /// Live lists acquire their capacity once: CPU lists acquire visibility and schedule their build jobs, GPU lists
        /// rent their payload. Lists without a live consumer are released without producing anything.
        /// </summary>
        public void PrepareLive()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                MeshDrawListRecord record = m_Records[i];
                if (record.state != EMeshDrawListState.Live)
                {
                    record.state = EMeshDrawListState.Released;
                    m_Records[i] = record;
                    continue;
                }

                MeshPassCache cache = record.cache;
                record.cacheRevision = cache.revision;
                if (record.backend == EMeshDrawBackendPolicy.GPUIndirect)
                {
                    if (!record.gpuVisibility.IsValid())
                        throw new InvalidOperationException("A GPU draw list requires its view's GPU culling producer.");
                    if (cache.candidateBuffer.buffer == null)
                        throw new InvalidOperationException($"Mesh pass '{cache.desc.lightMode}' has no uploaded candidate table.");
                    record.commandCount = cache.commandHighWater;
                    record.candidateCount = cache.candidates.Length;
                    record.payload = m_World.gpuBackend.RentPayload(record.commandCount, cache.memberCount);
                }
                else
                {
                    // Stored before the next step can throw, so ReleaseAll releases what was acquired.
                    record.visibility = m_World.visibilityShare.Acquire(m_World, record.view);
                    m_Records[i] = record;
                    record.build = MeshDrawListBuild.Schedule(cache, m_World, m_World.visibilityShare.Get(record.visibility));
                    m_Records[i] = record;
                    m_World.cpuBackend.AllocateInstances(cache.memberCount, out record.cpuInstances, out record.cpuFades);
                }
                record.state = EMeshDrawListState.Scheduled;
                m_Records[i] = record;
            }
            JobHandle.ScheduleBatchedJobs();
        }

        void RequireUnchanged(in MeshDrawListRecord record)
        {
            if (record.cache.revision != record.cacheRevision)
                throw new InvalidOperationException("A pass cache changed between draw-list record and execution.");
        }

        internal void Upload(int index, CommandBuffer commands)
        {
            MeshDrawListRecord record = m_Records[index];
            RequireUnchanged(record);
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect) m_World.gpuBackend.Upload(commands, record.payload, record.cache, record.commandCount);
            else m_World.cpuBackend.Upload(commands, ref record.build, record.cpuInstances, record.cpuFades);
            m_Records[index] = record;
        }

        internal void Compact(int index, CommandBuffer commands)
        {
            MeshDrawListRecord record = m_Records[index];
            RequireUnchanged(record);
            string slice = SliceSampleName(record.view);
            if (slice != null) commands.BeginSample(slice);
            try
            {
                m_World.gpuBackend.Compact(commands, record.payload, record.cache, record.commandCount, record.candidateCount,
                    record.gpuVisibilityBuffer.buffer, record.gpuVisibilityBase, m_World.stats);
            }
            finally { if (slice != null) commands.EndSample(slice); }
        }

        public void SubmitMeshDrawList(CommandBuffer commands, in RGMeshDrawListHandle draws)
        {
            // Draw is only reachable from a live consumer declared with UseMeshDrawList; anything else is a broken pass.
            if (!IsLive(draws)) throw new InvalidOperationException("Draw received a draw list of another builder or an earlier graph.");
            MeshDrawListRecord record = m_Records[draws.index];
            if (record.state != EMeshDrawListState.Scheduled) throw new InvalidOperationException("Draw received a draw list that its pass did not declare with UseMeshDrawList.");
            RequireUnchanged(record);
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect)
                m_World.gpuBackend.SubmitMeshDrawList(commands, record.payload, record.cache, record.commandCount, record.tables, record.motion, record.view.kind);
            else
                m_World.cpuBackend.SubmitMeshDrawList(commands, record.cache, record.build, record.cpuInstances, record.cpuFades, record.tables, record.motion, record.view.kind);
        }

        static string SliceSampleName(in MeshView view)
            => view.isShadow ? MeshDrawProfiling.Slice(view.kind, view.subviewIndex) : null;

        /// <summary>
        /// Logical cleanup only: completes and frees CPU build jobs, releases visibility references and retires payloads,
        /// instance buffers and transform-version references. Physical return happens after native submit.
        /// </summary>
        public void ReleaseAll()
        {
            List<Exception> failures = null;
            for (int i = 0; i < m_Records.Count; ++i)
            {
                MeshDrawListRecord record = m_Records[i];
                try
                {
                    record.build.Dispose();
                    if (record.visibility.IsValid) m_World.visibilityShare.Release(record.visibility);
                    m_World.gpuBackend.RetirePayload(record.payload);
                    m_World.cpuBackend.Retire(record.cpuInstances);
                    m_World.cpuBackend.Retire(record.cpuFades);
                    record.tables.version?.Release();
                }
                catch (Exception error)
                {
                    (failures ??= new List<Exception>()).Add(error);
                }
                record.consumerPassIndices.Clear();
                m_ConsumerLists.Push(record.consumerPassIndices);
            }
            m_Records.Clear();
            unchecked { ++m_GraphGeneration; }
            if (failures == null) return;
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(failures);
        }
    }
}
