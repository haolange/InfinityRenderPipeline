using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// A culled mesh view of one graph, the mesh counterpart of <c>CullingResults</c>. The registry reference binds it to one
    /// builder; the generation binds it to one graph lifetime (P2).
    /// </summary>
    internal readonly struct RGMeshViewHandle
    {
        internal readonly MeshViewRegistry registry;
        internal readonly int index;
        internal readonly int generation;

        internal RGMeshViewHandle(MeshViewRegistry registry, int index, int generation)
        {
            this.registry = registry;
            this.index = index;
            this.generation = generation;
        }
    }

    /// <summary>
    /// Logical draw-list handle: one view and one pass. Not an ERGResourceType; it lives in the mesh graph extension's
    /// registry. The registry reference binds it to one builder; the generation binds it to one graph lifetime.
    /// </summary>
    public readonly struct RGMeshDrawListHandle
    {
        internal readonly MeshViewRegistry registry;
        internal readonly int index;
        internal readonly int generation;

        internal RGMeshDrawListHandle(MeshViewRegistry registry, int index, int generation)
        {
            this.registry = registry;
            this.index = index;
            this.generation = generation;
        }

        /// <summary>Registry-only validity. Generation and index are checked by UseMeshDrawList and SubmitMeshDrawList.</summary>
        public bool IsValid => registry != null;

        public static RGMeshDrawListHandle InvalidHandle => default;
    }

    internal enum EMeshDrawListState : byte
    {
        Recorded = 0,
        Live = 1,
        Scheduled = 2,
        Released = 3,
        Empty = 4
    }

    /// <summary>
    /// One culled view: its descriptor, for a remainder view the history view whose rest it draws, and, once its first nonempty
    /// GPU list declares it, the view's producer state: the culling-context buffer it rents and the visibility it writes.
    /// </summary>
    internal struct MeshViewRecord
    {
        public MeshCullingContext cullingContext;
        public MeshMotionBinding motion;
        public MeshOccluder occluder;
        // The history view this view draws the rest of, or -1.
        public int drawnView;
        // A history view's remainder view, or -1. There is at most one, so an instance draws at most once per view and pass.
        public int remainderView;
        // GPU producer: null and invalid until the view's first nonempty GPU list, and for a view drawn only on the CPU.
        public MeshGPUViewPayload payload;
        public RGBufferHandle visibilityHandle;
        public int entityCount;
    }

    /// <summary>Graph handles every consumer of a list reads: the frozen tables and the producer's outputs.</summary>
    internal struct MeshDrawListReads
    {
        public RGBufferHandle matricesHandle, generationsHandle, layersHandle, previousMatricesHandle, previousGenerationsHandle;
        public bool hasPrevious;
        public RGBufferHandle[] producerOutputsHandle;
    }

    internal struct MeshDrawListRecord
    {
        public MeshPassCache cache;
        public EMeshDrawBackendPolicy backend;
        public MeshWorldGPUTables tables;
        public EMeshDrawListState state;
        public List<int> consumerPassIndices;
        // The pass cache revision the list was recorded against; execution rejects any other.
        public uint cacheRevision;
        public MeshDrawListReads reads;
        // CPU direct
        public MeshDrawListBuild build;
        public BufferHandle cpuInstancesHandle;
        public BufferHandle cpuFadesHandle;
        // GPU indirect
        public MeshGPUDrawPayload payload;
        public int commandCount;
        public int candidateCount;
    }

    /// <summary>
    /// Per-graph mesh state of one builder: the culled views, their draw lists and the transform versions this graph
    /// scheduled. Recording, compile, production and submit read it. <see cref="ReleaseAll"/> empties it when the graph is
    /// dropped and advances the generation, so every view and list handle of that graph is rejected afterwards.
    /// </summary>
    internal sealed class MeshViewRegistry
    {
        readonly MeshWorld m_World;
        // Sized for a camera's main views, four cascades and several shadowed local faces, so recording does not grow them.
        readonly List<MeshViewRecord> m_Views = new List<MeshViewRecord>(32);
        // Sized for a camera with every pass on those views.
        readonly List<MeshDrawListRecord> m_Lists = new List<MeshDrawListRecord>(64);
        // (view, pass index) of each list, in list order; the one record of which view and pass a list draws.
        readonly List<int2> m_ListKeys = new List<int2>(64);
        readonly Stack<List<int>> m_ConsumerLists = new Stack<List<int>>();
        readonly HashSet<MeshTransformVersion> m_ScheduledVersions = new HashSet<MeshTransformVersion>();
        // Start at 1 so a default handle never matches a live graph.
        int m_GraphGeneration = 1;

        internal MeshViewRegistry(MeshWorld world) => m_World = world;

        internal int viewCount => m_Views.Count;
        internal int listCount => m_Lists.Count;
        internal int graphGeneration => m_GraphGeneration;
        /// <summary>Transform versions whose production this graph recorded; each is recorded once per graph.</summary>
        internal HashSet<MeshTransformVersion> scheduledVersions => m_ScheduledVersions;

        internal bool IsLive(in RGMeshViewHandle viewHandle)
            => viewHandle.registry == this && viewHandle.generation == m_GraphGeneration && (uint)viewHandle.index < (uint)m_Views.Count;

        internal bool IsLive(in RGMeshDrawListHandle drawsHandle)
            => drawsHandle.registry == this && drawsHandle.generation == m_GraphGeneration && (uint)drawsHandle.index < (uint)m_Lists.Count;

        internal MeshDrawListRecord GetListCopy(int index) => m_Lists[index];

        internal MeshViewRecord GetViewCopy(int index) => m_Views[index];

        /// <summary>The view a list draws.</summary>
        internal int ViewOfList(int index) => m_ListKeys[index].x;

        /// <summary>The view of a live handle; a handle of another builder or an earlier graph fails here.</summary>
        internal MeshViewRecord RequireView(in RGMeshViewHandle viewHandle)
        {
            if (!IsLive(viewHandle))
            {
                throw new InvalidOperationException("The mesh view belongs to another builder or an earlier graph.");
            }
            return m_Views[viewHandle.index];
        }

        /// <summary>Records a view. A history occluder needs the view's accepted transform version.</summary>
        internal RGMeshViewHandle CullView(in MeshViewDesc desc)
        {
            if (desc.occluder.history && desc.motion.previous == null)
            {
                throw new InvalidOperationException("A history occluder needs the view's accepted transform version.");
            }
            int index = m_Views.Count;
            m_Views.Add(new MeshViewRecord
            {
                cullingContext = desc.cullingContext,
                motion = desc.motion,
                occluder = desc.occluder,
                drawnView = -1,
                remainderView = -1
            });
            return new RGMeshViewHandle(this, index, m_GraphGeneration);
        }

        /// <summary>
        /// Records the view that draws what the lists of a history view left: its culling context and motion against a newer
        /// occluder. A history view has one remainder view; a second would draw those instances twice.
        /// </summary>
        internal RGMeshViewHandle CullRemainder(in RGMeshViewHandle drawnViewHandle, in MeshOccluder occluder)
        {
            MeshViewRecord drawn = RequireView(drawnViewHandle);
            if (!drawn.occluder.history)
            {
                throw new InvalidOperationException("A remainder view draws what a view culled against the previous frame's pyramid left; this view has no history occluder.");
            }
            if (drawn.remainderView >= 0)
            {
                throw new InvalidOperationException("This history view already has a remainder view.");
            }
            if (!occluder.isValid || occluder.history)
            {
                throw new InvalidOperationException("A remainder view needs a newer occluder than the previous frame's pyramid.");
            }
            int index = m_Views.Count;
            m_Views.Add(new MeshViewRecord
            {
                cullingContext = drawn.cullingContext,
                motion = drawn.motion,
                occluder = occluder,
                drawnView = drawnViewHandle.index,
                remainderView = -1
            });
            drawn.remainderView = index;
            m_Views[drawnViewHandle.index] = drawn;
            return new RGMeshViewHandle(this, index, m_GraphGeneration);
        }

        /// <summary>The list of one view and pass, or -1. A camera has few lists, so a scan of the keys is enough.</summary>
        internal int FindList(int view, MeshPassId pass)
        {
            for (int i = 0; i < m_ListKeys.Count; ++i)
            {
                if (m_ListKeys[i].x == view && m_ListKeys[i].y == pass.index)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Records the list of one view and pass and acquires its capacity once: a GPU list rents its payload, a CPU list its
        /// instance tables. An empty list (no members, or nothing left of its view and pass) acquires nothing and draws nothing.
        /// </summary>
        internal RGMeshDrawListHandle CreateList(int view, MeshPassId pass, MeshPassCache cache, EMeshDrawBackendPolicy backend, bool empty)
        {
            MeshWorldGPUTables tables = m_World.gpu.Snapshot();
            if (!tables.isValid)
            {
                throw new InvalidOperationException("Draw lists require uploaded MeshWorld residency.");
            }
            bool gpu = backend == EMeshDrawBackendPolicy.GPUIndirect;
            if (gpu && !empty && (cache.candidateBufferHandle.buffer == null || cache.commandMetaBufferHandle.buffer == null))
            {
                throw new InvalidOperationException($"Mesh pass '{cache.desc.lightMode}' has no uploaded candidate and command tables.");
            }
            tables.version.Retain();
            int index = m_Lists.Count;
            // The record joins the registry before renting, so ReleaseAll returns whatever a failed rent left behind.
            m_ListKeys.Add(new int2(view, pass.index));
            m_Lists.Add(new MeshDrawListRecord
            {
                cache = cache,
                backend = backend,
                tables = tables,
                state = empty ? EMeshDrawListState.Empty : EMeshDrawListState.Recorded,
                consumerPassIndices = m_ConsumerLists.Count > 0 ? m_ConsumerLists.Pop() : new List<int>(4),
                cacheRevision = cache.revision
            });
            if (!empty)
            {
                MeshDrawListRecord record = m_Lists[index];
                if (gpu)
                {
                    record.commandCount = cache.commandHighWater;
                    record.candidateCount = cache.candidates.Length;
                    record.payload = m_World.gpuBackend.RentPayload(record.commandCount, cache.memberCount);
                }
                else
                {
                    m_World.cpuBackend.AllocateInstances(cache.memberCount, out record.cpuInstancesHandle, out record.cpuFadesHandle);
                }
                m_Lists[index] = record;
            }
            return new RGMeshDrawListHandle(this, index, m_GraphGeneration);
        }

        internal void SetReads(int index, in MeshDrawListReads reads)
        {
            MeshDrawListRecord record = m_Lists[index];
            record.reads = reads;
            m_Lists[index] = record;
        }

        internal void SetProducerOutputs(int index, RGBufferHandle[] outputsHandle)
        {
            MeshDrawListRecord record = m_Lists[index];
            record.reads.producerOutputsHandle = outputsHandle;
            m_Lists[index] = record;
        }

        /// <summary>
        /// Gives a view without a producer its GPU producer state: the culling-context buffer it rents, the visibility buffer its
        /// producer writes and the entity slots it covers. The payload joins the view before the producer pass is declared, so
        /// <see cref="ReleaseAll"/> retires it whatever fails after.
        /// </summary>
        internal void SetViewProducer(int view, MeshGPUViewPayload payload, RGBufferHandle visibilityHandle, int entityCount)
        {
            MeshViewRecord record = m_Views[view];
            record.payload = payload;
            record.visibilityHandle = visibilityHandle;
            record.entityCount = entityCount;
            m_Views[view] = record;
        }

        internal void ClearConsumers()
        {
            for (int i = 0; i < m_Lists.Count; ++i)
            {
                MeshDrawListRecord record = m_Lists[i];
                record.consumerPassIndices.Clear();
                if (record.state != EMeshDrawListState.Released && record.state != EMeshDrawListState.Empty)
                {
                    record.state = EMeshDrawListState.Recorded;
                }
                m_Lists[i] = record;
            }
        }

        internal void MarkLiveConsumer(int drawListIndex, int passIndex)
        {
            MeshDrawListRecord record = m_Lists[drawListIndex];
            record.consumerPassIndices.Add(passIndex);
            if (record.state == EMeshDrawListState.Recorded)
            {
                record.state = EMeshDrawListState.Live;
            }
            m_Lists[drawListIndex] = record;
        }

        /// <summary>
        /// Live CPU lists schedule their build jobs; lists without a live consumer are released without producing anything
        /// (the graph culls a GPU list's producer with its consumer).
        /// </summary>
        internal void PrepareLive()
        {
            for (int i = 0; i < m_Lists.Count; ++i)
            {
                MeshDrawListRecord record = m_Lists[i];
                if (record.state == EMeshDrawListState.Empty)
                {
                    continue;
                }
                if (record.state != EMeshDrawListState.Live)
                {
                    record.state = EMeshDrawListState.Released;
                    m_Lists[i] = record;
                    continue;
                }
                RequireUnchanged(record);
                if (record.backend == EMeshDrawBackendPolicy.CPUDirect && !record.build.isCreated)
                {
                    MeshViewRecord view = m_Views[ViewOfList(i)];
                    using (record.cache.scheduleMarker.Auto())
                    {
                        record.build = MeshDrawListBuild.Schedule(record.cache, m_World, view.cullingContext);
                    }
                }
                record.state = EMeshDrawListState.Scheduled;
                m_Lists[i] = record;
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
            MeshDrawListRecord record = m_Lists[index];
            RequireUnchanged(record);
            ProfilingSampler consumer = record.cache.passSampler;
            consumer.Begin(commands);
            try
            {
                m_World.cpuBackend.Upload(commands, record.cache, ref record.build, record.cpuInstancesHandle, record.cpuFadesHandle);
            }
            finally
            {
                consumer.End(commands);
            }
            m_Lists[index] = record;
        }

        /// <summary>The view's producer: its visibility of every entity slot, read by every GPU list of the view.</summary>
        internal void ProduceView(int view, in RGComputeEncoder encoder, in MeshCullInputs inputs, RGBufferHandle visibilityHandle, bool occluded,
            in MeshOccluderInputs occluder, bool remainder, RGBufferHandle drawnHandle)
        {
            MeshViewRecord record = m_Views[view];
            m_World.gpuBackend.ProduceView(encoder, record.payload, record.entityCount, record.cullingContext, inputs, visibilityHandle,
                occluded, occluder, remainder, drawnHandle, m_World.stats);
        }

        internal void ProduceDrawList(int index, in RGComputeEncoder encoder, RGBufferHandle visibilityHandle)
        {
            MeshDrawListRecord record = m_Lists[index];
            RequireUnchanged(record);
            ProfilingSampler consumer = record.cache.passSampler;
            encoder.BeginSample(consumer);
            try
            {
                m_World.gpuBackend.ProduceDrawList(encoder, record.payload, record.cache, record.commandCount, record.candidateCount,
                    visibilityHandle, m_World.stats);
            }
            finally
            {
                encoder.EndSample(consumer);
            }
        }

        internal void SubmitMeshDrawList(CommandBuffer commands, in RGMeshDrawListHandle drawsHandle)
        {
            // Submit is only reachable from a live consumer declared with UseMeshDrawList; anything else is a broken pass.
            if (!IsLive(drawsHandle))
            {
                throw new InvalidOperationException("SubmitMeshDrawList received a draw list of another builder or an earlier graph.");
            }
            MeshDrawListRecord record = m_Lists[drawsHandle.index];
            if (record.state != EMeshDrawListState.Scheduled && record.state != EMeshDrawListState.Empty)
            {
                throw new InvalidOperationException("SubmitMeshDrawList received a draw list that its pass did not declare with UseMeshDrawList.");
            }
            RequireUnchanged(record);
            if (record.state == EMeshDrawListState.Empty)
            {
                return;
            }
            MeshMotionBinding motion = m_Views[ViewOfList(drawsHandle.index)].motion;
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect)
                m_World.gpuBackend.SubmitMeshDrawList(commands, record.payload, record.cache, record.commandCount, record.tables, motion);
            else
                m_World.cpuBackend.SubmitMeshDrawList(commands, record.cache, record.build, record.cpuInstancesHandle, record.cpuFadesHandle, record.tables, motion);
        }

        /// <summary>
        /// Logical cleanup only: completes and frees CPU build jobs and retires list and view payloads, instance buffers and
        /// transform-version references, then drops the views. Physical return happens after native submit.
        /// </summary>
        internal void ReleaseAll()
        {
            List<Exception> failures = null;
            for (int i = 0; i < m_Views.Count; ++i)
            {
                try
                {
                    m_World.gpuBackend.RetireViewPayload(m_Views[i].payload);
                }
                catch (Exception error)
                {
                    (failures ??= new List<Exception>()).Add(error);
                }
            }
            for (int i = 0; i < m_Lists.Count; ++i)
            {
                MeshDrawListRecord record = m_Lists[i];
                try
                {
                    record.build.Dispose();
                    m_World.gpuBackend.RetirePayload(record.payload);
                    m_World.cpuBackend.Retire(record.cpuInstancesHandle);
                    m_World.cpuBackend.Retire(record.cpuFadesHandle);
                    record.tables.version?.Release();
                }
                catch (Exception error)
                {
                    (failures ??= new List<Exception>()).Add(error);
                }
                record.consumerPassIndices.Clear();
                m_ConsumerLists.Push(record.consumerPassIndices);
            }
            m_Lists.Clear();
            m_ListKeys.Clear();
            m_Views.Clear();
            m_ScheduledVersions.Clear();
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
