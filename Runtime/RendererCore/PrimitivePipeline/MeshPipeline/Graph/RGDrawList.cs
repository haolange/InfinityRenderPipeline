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
    public readonly struct RGDrawListHandle
    {
        internal readonly RGDrawListContext context;
        internal readonly int index;
        internal readonly int generation;

        internal RGDrawListHandle(RGDrawListContext context, int index, int generation)
        {
            this.context = context;
            this.index = index;
            this.generation = generation;
        }

        /// <summary>Registry-only validity. Generation and index are checked by UseDrawList and Draw.</summary>
        public bool IsValid => context != null;

        public static RGDrawListHandle Invalid => default;
    }

    internal enum ERGDrawListCompileState : byte
    {
        Declared = 0,
        Live = 1,
        Scheduled = 2,
        Released = 3
    }

    internal struct RGDrawListRecord
    {
        public MeshView view;
        public MeshPassCache cache;
        public EMeshBackendPolicy backend;
        public MeshWorldGpuTables tables;
        public MeshMotionBinding motion;
        public ERGDrawListCompileState state;
        public List<int> consumerPassIndices;
        // CPU direct
        public MeshVisibilityHandle visibility;
        public MeshDrawListBuild build;
        public BufferHandle cpuInstances;
        // GPU indirect
        public RGBufferHandle gpuVisibility;
        public BufferHandle gpuVisibilityBuffer;
        public MeshGpuDrawPayload payload;
        public int commandCount;
        public int candidateCount;
        public uint cacheRevision;
    }

    /// <summary>Per-graph draw-list registry used by compile, preparation and submit.</summary>
    internal sealed class RGDrawListContext
    {
        readonly MeshWorld m_World;
        readonly List<RGDrawListRecord> m_Records = new List<RGDrawListRecord>(16);
        readonly Stack<List<int>> m_ConsumerLists = new Stack<List<int>>();
        // Start at 1 so default(RGDrawListHandle) never matches a live graph.
        int m_GraphGeneration = 1;

        internal RGDrawListContext(MeshWorld world) => m_World = world;

        public int Count => m_Records.Count;
        public int GraphGeneration => m_GraphGeneration;

        public bool IsLive(in RGDrawListHandle draws)
            => draws.context == this && draws.generation == m_GraphGeneration && draws.index >= 0 && draws.index < m_Records.Count;

        public RGDrawListRecord GetRecordCopy(int index) => m_Records[index];

        public RGDrawListHandle Declare(in MeshView view, in MeshPassDesc desc)
        {
            MeshPassCache cache = m_World.GetPassCache(desc);
            cache.EnsureShaderBindings();
            MeshMotionBinding motion = m_World.GetPreviousTransforms(view.viewKey);
            if (desc.requirePreviousTransforms && !motion.isValid)
                throw new InvalidOperationException($"Mesh pass '{desc.lightMode}' requires the view's previous-transform producer.");
            MeshWorldGpuTables tables = m_World.gpu.Snapshot();
            if (!tables.isValid) throw new InvalidOperationException("Draw lists require uploaded MeshWorld residency.");
            tables.version.Retain();
            int index = m_Records.Count;
            m_Records.Add(new RGDrawListRecord
            {
                view = view,
                cache = cache,
                backend = m_World.SelectBackend(desc),
                tables = tables,
                motion = motion,
                state = ERGDrawListCompileState.Declared,
                consumerPassIndices = m_ConsumerLists.Count > 0 ? m_ConsumerLists.Pop() : new List<int>(4),
                visibility = MeshVisibilityHandle.Invalid
            });
            return new RGDrawListHandle(this, index, m_GraphGeneration);
        }

        public void ClearConsumers()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                record.consumerPassIndices.Clear();
                if (record.state != ERGDrawListCompileState.Released) record.state = ERGDrawListCompileState.Declared;
                m_Records[i] = record;
            }
        }

        public void MarkLiveConsumer(int drawListIndex, int passIndex)
        {
            RGDrawListRecord record = m_Records[drawListIndex];
            record.consumerPassIndices.Add(passIndex);
            if (record.state == ERGDrawListCompileState.Declared) record.state = ERGDrawListCompileState.Live;
            m_Records[drawListIndex] = record;
        }

        public void BindGpuVisibility(int index, in RGBufferHandle visibility, in BufferHandle buffer)
        {
            RGDrawListRecord record = m_Records[index];
            record.gpuVisibility = visibility;
            record.gpuVisibilityBuffer = buffer;
            m_Records[index] = record;
        }

        public bool HasLiveView(in MeshView view)
        {
            var key = new MeshVisibilityKey(view, true);
            foreach (RGDrawListRecord record in m_Records)
            {
                if (record.state == ERGDrawListCompileState.Declared || record.state == ERGDrawListCompileState.Released) continue;
                if (new MeshVisibilityKey(record.view, true).Equals(key)) return true;
            }
            return false;
        }

        /// <summary>
        /// Live lists acquire their capacity once: CPU lists acquire visibility and schedule their build jobs, GPU lists
        /// rent their payload. Lists without a live consumer are released without producing anything.
        /// </summary>
        public void PrepareLive()
        {
            for (int i = 0; i < m_Records.Count; ++i)
            {
                RGDrawListRecord record = m_Records[i];
                if (record.state != ERGDrawListCompileState.Live)
                {
                    record.state = ERGDrawListCompileState.Released;
                    m_Records[i] = record;
                    continue;
                }

                MeshPassCache cache = record.cache;
                record.cacheRevision = cache.revision;
                if (record.backend == EMeshBackendPolicy.GpuIndirect)
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
                    record.visibility = m_World.visibilityShare.Acquire(m_World, record.view);
                    record.build = MeshDrawListBuild.Schedule(cache, m_World, m_World.visibilityShare.Get(record.visibility));
                    record.cpuInstances = m_World.cpuBackend.AllocateInstances(cache.memberCount);
                }
                record.state = ERGDrawListCompileState.Scheduled;
                m_Records[i] = record;
            }
            JobHandle.ScheduleBatchedJobs();
        }

        void RequireUnchanged(in RGDrawListRecord record)
        {
            if (record.cache.revision != record.cacheRevision)
                throw new InvalidOperationException("A pass cache changed between draw-list record and execution.");
        }

        internal void Upload(int index, CommandBuffer commands)
        {
            RGDrawListRecord record = m_Records[index];
            RequireUnchanged(record);
            if (record.backend == EMeshBackendPolicy.GpuIndirect) m_World.gpuBackend.Upload(commands, record.payload, record.cache, record.commandCount);
            else m_World.cpuBackend.Upload(commands, ref record.build, record.cpuInstances);
            m_Records[index] = record;
        }

        internal void Compact(int index, CommandBuffer commands)
        {
            RGDrawListRecord record = m_Records[index];
            RequireUnchanged(record);
            string slice = SliceSampleName(record.view);
            if (slice != null) commands.BeginSample(slice);
            try
            {
                m_World.gpuBackend.Compact(commands, record.payload, record.cache, record.commandCount, record.candidateCount,
                    record.gpuVisibilityBuffer.buffer, m_World.stats);
            }
            finally { if (slice != null) commands.EndSample(slice); }
        }

        public void Submit(CommandBuffer commands, in RGDrawListHandle draws)
        {
            // A foreign, stale or released handle draws nothing; UseDrawList already rejected it at record.
            if (!IsLive(draws)) return;
            RGDrawListRecord record = m_Records[draws.index];
            if (record.state != ERGDrawListCompileState.Scheduled) return;
            RequireUnchanged(record);
            if (record.backend == EMeshBackendPolicy.GpuIndirect)
                m_World.gpuBackend.Submit(commands, record.payload, record.cache, record.commandCount, record.tables, record.motion, record.view.kind);
            else
                m_World.cpuBackend.Submit(commands, record.cache, record.build, record.cpuInstances, record.tables, record.motion, record.view.kind);
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
                RGDrawListRecord record = m_Records[i];
                try
                {
                    record.build.Dispose();
                    if (record.visibility.IsValid) m_World.visibilityShare.Release(record.visibility);
                    m_World.gpuBackend.RetirePayload(record.payload);
                    m_World.cpuBackend.Retire(record.cpuInstances);
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
