using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering;

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
        Recorded = 0,
        Live = 1,
        Scheduled = 2,
        Released = 3,
        Empty = 4
    }

    internal struct MeshDrawListRecord
    {
        public MeshCullingContext cullingContext;
        public MeshPassCache cache;
        public EMeshDrawBackendPolicy backend;
        public MeshWorldGPUTables tables;
        public MeshMotionBinding motion;
        public EMeshDrawListState state;
        public List<int> consumerPassIndices;
        // The pass cache revision the list was recorded against; execution rejects any other.
        public uint cacheRevision;
        // CPU direct
        public MeshDrawListBuild build;
        public BufferHandle cpuInstances;
        public BufferHandle cpuFades;
        // GPU indirect
        public MeshGPUDrawPayload payload;
        public int commandCount;
        public int candidateCount;
    }

    /// <summary>Per-graph draw-list registry used by recording, compile, production and submit.</summary>
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

        /// <summary>
        /// Records a list and acquires its capacity once: a GPU list rents its payload, a CPU list its instance tables. An
        /// empty list (no members, or nothing left of its context and pass) acquires nothing and draws nothing.
        /// </summary>
        public RGMeshDrawListHandle Create(in MeshCullingContext context, MeshPassCache cache, EMeshDrawBackendPolicy backend,
            in MeshMotionBinding motion, bool empty)
        {
            MeshWorldGPUTables tables = m_World.gpu.Snapshot();
            if (!tables.isValid)
            {
                throw new InvalidOperationException("Draw lists require uploaded MeshWorld residency.");
            }
            bool gpu = backend == EMeshDrawBackendPolicy.GPUIndirect;
            if (gpu && !empty && cache.candidateBuffer.buffer == null)
            {
                throw new InvalidOperationException($"Mesh pass '{cache.desc.lightMode}' has no uploaded candidate table.");
            }
            tables.version.Retain();
            int index = m_Records.Count;
            // The record joins the registry before renting, so ReleaseAll returns whatever a failed rent left behind.
            m_Records.Add(new MeshDrawListRecord
            {
                cullingContext = context,
                cache = cache,
                backend = backend,
                tables = tables,
                motion = motion,
                state = empty ? EMeshDrawListState.Empty : EMeshDrawListState.Recorded,
                consumerPassIndices = m_ConsumerLists.Count > 0 ? m_ConsumerLists.Pop() : new List<int>(4),
                cacheRevision = cache.revision
            });
            if (!empty)
            {
                MeshDrawListRecord record = m_Records[index];
                if (gpu)
                {
                    record.commandCount = cache.commandHighWater;
                    record.candidateCount = cache.candidates.Length;
                    record.payload = m_World.gpuBackend.RentPayload(record.commandCount, cache.memberCount);
                }
                else
                {
                    m_World.cpuBackend.AllocateInstances(cache.memberCount, out record.cpuInstances, out record.cpuFades);
                }
                m_Records[index] = record;
            }
            return new RGMeshDrawListHandle(this, index, m_GraphGeneration);
        }

        public void ClearConsumers()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                MeshDrawListRecord record = m_Records[i];
                record.consumerPassIndices.Clear();
                if (record.state != EMeshDrawListState.Released && record.state != EMeshDrawListState.Empty)
                {
                    record.state = EMeshDrawListState.Recorded;
                }
                m_Records[i] = record;
            }
        }

        public void MarkLiveConsumer(int drawListIndex, int passIndex)
        {
            MeshDrawListRecord record = m_Records[drawListIndex];
            record.consumerPassIndices.Add(passIndex);
            if (record.state == EMeshDrawListState.Recorded)
            {
                record.state = EMeshDrawListState.Live;
            }
            m_Records[drawListIndex] = record;
        }

        /// <summary>
        /// Live CPU lists schedule their build jobs; lists without a live consumer are released without producing anything
        /// (the graph culls a GPU list's producer with its consumer).
        /// </summary>
        public void PrepareLive()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                MeshDrawListRecord record = m_Records[i];
                if (record.state == EMeshDrawListState.Empty)
                {
                    continue;
                }
                if (record.state != EMeshDrawListState.Live)
                {
                    record.state = EMeshDrawListState.Released;
                    m_Records[i] = record;
                    continue;
                }
                RequireUnchanged(record);
                if (record.backend == EMeshDrawBackendPolicy.CPUDirect && !record.build.isCreated)
                {
                    using (record.cache.scheduleMarker.Auto())
                    {
                        record.build = MeshDrawListBuild.Schedule(record.cache, m_World, record.cullingContext);
                    }
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
            ProfilingSampler consumer = record.cache.passSampler;
            consumer.Begin(commands);
            try
            {
                m_World.cpuBackend.Upload(commands, record.cache, ref record.build, record.cpuInstances, record.cpuFades);
            }
            finally
            {
                consumer.End(commands);
            }
            m_Records[index] = record;
        }

        internal void Produce(int index, in RGComputeEncoder encoder, in MeshCullInputs inputs, bool occluded, in MeshOccluderInputs occluder,
            RGBufferHandle drawn, bool readDrawn)
        {
            MeshDrawListRecord record = m_Records[index];
            RequireUnchanged(record);
            ProfilingSampler consumer = record.cache.passSampler;
            encoder.BeginSample(consumer);
            try
            {
                m_World.gpuBackend.Produce(encoder, record.payload, record.cache, record.commandCount, record.candidateCount,
                    record.cullingContext, inputs, occluded, occluder, drawn, readDrawn, m_World.stats);
            }
            finally
            {
                encoder.EndSample(consumer);
            }
        }

        public void SubmitMeshDrawList(CommandBuffer commands, in RGMeshDrawListHandle draws)
        {
            // Draw is only reachable from a live consumer declared with UseMeshDrawList; anything else is a broken pass.
            if (!IsLive(draws))
            {
                throw new InvalidOperationException("Draw received a draw list of another builder or an earlier graph.");
            }
            MeshDrawListRecord record = m_Records[draws.index];
            if (record.state != EMeshDrawListState.Scheduled && record.state != EMeshDrawListState.Empty)
            {
                throw new InvalidOperationException("Draw received a draw list that its pass did not declare with UseMeshDrawList.");
            }
            RequireUnchanged(record);
            if (record.state == EMeshDrawListState.Empty)
            {
                return;
            }
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect)
                m_World.gpuBackend.SubmitMeshDrawList(commands, record.payload, record.cache, record.commandCount, record.tables, record.motion);
            else
                m_World.cpuBackend.SubmitMeshDrawList(commands, record.cache, record.build, record.cpuInstances, record.cpuFades, record.tables, record.motion);
        }

        /// <summary>
        /// Logical cleanup only: completes and frees CPU build jobs and retires payloads, instance buffers and
        /// transform-version references. Physical return happens after native submit.
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
            unchecked
            {
                ++m_GraphGeneration;
            }
            if (failures == null)
            {
                return;
            }
            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            throw new AggregateException(failures);
        }
    }
}
