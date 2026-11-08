using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal struct MeshFrustumBatchData
    {
        public MeshWorld world;
        public MeshFrustumView[] views;
        public int viewCount;
        public int stride;
        public MeshCullInputs inputs;
        public RGBufferHandle visibility;
        public ComputeBuffer viewTable;
    }

    /// <summary>
    /// Per-builder mesh state. The render graph knows this object only as an <see cref="IRGGraphExtension"/>:
    /// it calls Contribute once per compile and Clear whenever the recorded graph is dropped.
    /// </summary>
    internal sealed partial class MeshGraphExtension : IRGGraphExtension
    {
        struct MeshDrawPreparePassData { public MeshDrawListRegistry lists; public int index; }

        internal readonly MeshWorld world;
        internal readonly MeshDrawListRegistry drawLists;
        readonly Dictionary<int, RGBufferHandle[]> m_PreparedDrawLists = new Dictionary<int, RGBufferHandle[]>();
        readonly Dictionary<MeshVisibilityKey, RGBufferHandle> m_GPUCullVisibility = new Dictionary<MeshVisibilityKey, RGBufferHandle>(8);
        readonly Dictionary<MeshVisibilityKey, GpuVisibilityBinding> m_OcclusionFinal = new Dictionary<MeshVisibilityKey, GpuVisibilityBinding>(4);
        readonly List<MeshVisibilityKey> m_FrustumOrder = new List<MeshVisibilityKey>(8);
        readonly List<MeshView> m_FrustumSources = new List<MeshView>(8);
        readonly Dictionary<MeshVisibilityKey, int> m_FrustumSlice = new Dictionary<MeshVisibilityKey, int>(8);
        MeshFrustumView[] m_FrustumRecords = System.Array.Empty<MeshFrustumView>();
        readonly HashSet<MeshTransformVersion> m_ScheduledVersions = new HashSet<MeshTransformVersion>();

        struct GpuVisibilityBinding
        {
            public RGBufferHandle handle;
            public BufferHandle buffer;
        }

        int m_BoundFrustumStride;

        internal MeshGraphExtension(MeshWorld world)
        {
            this.world = world;
            drawLists = new MeshDrawListRegistry(world);
        }

        internal static MeshGraphExtension Of(RGBuilder builder)
            => builder.extension as MeshGraphExtension ?? throw new InvalidOperationException("This RGBuilder has no MeshWorld; call SetMeshWorld first.");

        public void Clear()
        {
            m_ScheduledVersions.Clear();
            m_GPUCullVisibility.Clear();
            m_OcclusionFinal.Clear();
            drawLists.ReleaseAll();
        }

        public void Contribute(RGBuilder builder)
        {
            int recorded = builder.passCount;
            CompileMeshDrawLists(builder);
            InsertDrawListPreparation(builder, recorded);
        }

        internal RGMeshDrawListHandle CreateMeshDrawList(RGBuilder builder, in MeshView view, in MeshPassDesc desc)
        {
            view.lodProjection.ThrowIfInvalid();
            RGMeshDrawListHandle draws = drawLists.Declare(view, desc);
            MeshDrawListRecord record = drawLists.GetRecordCopy(draws.index);
            // Recording references keep the frozen tables alive for this graph.
            builder.ImportBuffer(record.tables.renderingLayers);
            builder.ImportBuffer(record.tables.version.matrices);
            builder.ImportBuffer(record.tables.version.generations);
            MeshTransformVersion previous = record.motion.previous;
            if (previous != null) { builder.ImportBuffer(previous.matrices); builder.ImportBuffer(previous.generations); }
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect) RequestMeshGPUCulling(builder, view, draws);
            return draws;
        }

        MeshCullInputs CullInputs(RGBuilder builder, RGComputePassHandle pass, bool boundsOnly = false)
        {
            MeshWorldGPU gpu = world.gpu;
            var inputs = new MeshCullInputs
            {
                entityCount = world.entityHighWater,
                boundsCenter = pass.ReadBuffer(builder.ImportBuffer(gpu.boundsCenter)),
                boundsExtent = pass.ReadBuffer(builder.ImportBuffer(gpu.boundsExtent)),
                lodChains = pass.ReadBuffer(builder.ImportBuffer(gpu.lodChains))
            };
            if (boundsOnly) return inputs;
            inputs.flags = pass.ReadBuffer(builder.ImportBuffer(gpu.flags));
            inputs.layerMask = pass.ReadBuffer(builder.ImportBuffer(gpu.layerMask));
            inputs.sceneCullingMask = pass.ReadBuffer(builder.ImportBuffer(gpu.sceneCullingMask));
            inputs.renderingLayers = pass.ReadBuffer(builder.ImportBuffer(gpu.renderingLayers));
            return inputs;
        }

        /// <summary>Registers a view for compile-time culling. Occlusion coverage and frustum producers are resolved in <see cref="BindGpuCulling"/>.</summary>
        internal void RequestMeshGPUCulling(RGBuilder builder, in MeshView view, in RGMeshDrawListHandle draws)
        {
            view.lodProjection.ThrowIfInvalid();
        }

        static MeshVisibilityKey CoverageKey(in MeshView view)
        {
            var plain = view;
            plain.visibilityPhase = EMeshVisibilityPhase.Final;
            plain.visibilityHistory = 0;
            return new MeshVisibilityKey(plain, false);
        }

        internal bool TryOcclusionFinal(in MeshView view, out RGBufferHandle handle, out BufferHandle buffer)
        {
            if (m_OcclusionFinal.TryGetValue(CoverageKey(view), out GpuVisibilityBinding binding))
            {
                handle = binding.handle;
                buffer = binding.buffer;
                return true;
            }
            handle = default;
            buffer = default;
            return false;
        }

        bool OcclusionPhase(in MeshDrawListRecord record, out GpuVisibilityBinding binding)
        {
            binding = default;
            if (record.view.visibilityPhase == EMeshVisibilityPhase.Final && TryOcclusionFinal(record.view, out RGBufferHandle finalHandle, out BufferHandle finalBuffer))
            {
                binding = new GpuVisibilityBinding { handle = finalHandle, buffer = finalBuffer };
                return true;
            }
            foreach (var pair in m_GPUCullVisibility)
            {
                if (pair.Key.viewKey != record.view.viewKey || pair.Key.phase != record.view.visibilityPhase) continue;
                binding = new GpuVisibilityBinding { handle = pair.Value, buffer = world.GetGPUVisibilityBuffer(record.view) };
                return true;
            }
            return false;
        }

        GpuVisibilityBinding RecordFrustumBatch(RGBuilder builder, int viewCount)
        {
            if (!world.gpuBackend.isSupported) throw new InvalidOperationException("GPU mesh culling requires Compute_MeshDrawPipeline.");
            if (m_FrustumRecords.Length < viewCount)
            {
                int grown = Mathf.NextPowerOfTwo(Mathf.Max(4, viewCount));
                m_FrustumRecords = new MeshFrustumView[grown];
            }
            for (int i = 0; i < viewCount; ++i) m_FrustumRecords[i] = MeshFrustumView.FromView(m_FrustumSources[i]);
            BufferHandle batch = world.gpuVisibility.EnsureFrustumBatch(world.entityHighWater, viewCount, out int stride);
            m_BoundFrustumStride = stride;
            RGBufferHandle visibility = builder.ImportBuffer(batch);
            using (RGComputePassHandle pass = builder.AddComputePass<MeshFrustumBatchData>(MeshDrawProfiling.culling))
            {
                ref MeshFrustumBatchData data = ref pass.GetPassData<MeshFrustumBatchData>();
                data.world = world;
                data.views = m_FrustumRecords;
                data.viewCount = viewCount;
                data.stride = stride;
                data.inputs = CullInputs(builder, pass);
                data.visibility = pass.WriteBuffer(visibility);
                data.viewTable = world.gpuVisibility.frustumViews.buffer;
                pass.KeepImportedWritesLocal();
                pass.SetExecuteFunc(static (in MeshFrustumBatchData data, in RGComputeEncoder commands) =>
                {
                    if (data.viewCount > 0) commands.SetBufferData(data.viewTable, data.views, 0, 0, data.viewCount);
                    data.world.gpuBackend.DispatchCullFrustumBatch(commands, data.inputs, data.viewCount, data.stride, data.viewTable, data.visibility, data.world.stats);
                });
            }
            return new GpuVisibilityBinding { handle = visibility, buffer = batch };
        }

        void BindVisibilityRead(RGBuilder builder, in MeshDrawListRecord record, RGBufferHandle visibility)
        {
            for (int c = 0; c < record.consumerPassIndices.Count; ++c)
                builder.GetPass(record.consumerPassIndices[c]).AddResourceRead(visibility.handle);
        }

        void BindGpuCulling(RGBuilder builder)
        {
            m_FrustumOrder.Clear();
            m_FrustumSources.Clear();
            m_FrustumSlice.Clear();
            m_BoundFrustumStride = 0;
            for (int i = 0; i < drawLists.Count; ++i)
            {
                MeshDrawListRecord record = drawLists.GetRecordCopy(i);
                if (record.state != EMeshDrawListState.Live || record.backend != EMeshDrawBackendPolicy.GPUIndirect || OcclusionPhase(record, out _)) continue;
                var key = new MeshVisibilityKey(record.view, true);
                if (m_FrustumSlice.ContainsKey(key)) continue;
                m_FrustumSlice.Add(key, m_FrustumOrder.Count);
                m_FrustumOrder.Add(key);
                m_FrustumSources.Add(record.view);
            }
            GpuVisibilityBinding batch = default;
            if (m_FrustumOrder.Count > 0) batch = RecordFrustumBatch(builder, m_FrustumOrder.Count);
            for (int i = 0; i < drawLists.Count; ++i)
            {
                MeshDrawListRecord record = drawLists.GetRecordCopy(i);
                if (record.state != EMeshDrawListState.Live || record.backend != EMeshDrawBackendPolicy.GPUIndirect) continue;
                int visibilityBase = 0;
                GpuVisibilityBinding binding;
                if (OcclusionPhase(record, out binding)) { }
                else
                {
                    int slice = m_FrustumSlice[new MeshVisibilityKey(record.view, true)];
                    binding = batch;
                    visibilityBase = slice * m_BoundFrustumStride;
                }
                drawLists.BindGPUVisibility(i, binding.handle, binding.buffer, visibilityBase);
                BindVisibilityRead(builder, record, binding.handle);
            }
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal static void UseMeshDrawList(MeshDrawListRegistry lists, IRGPass pass, in RGMeshDrawListHandle draws)
        {
            pass.extensionUses.Add(draws.index);
            MeshDrawListRecord record = lists.GetRecordCopy(draws.index);
            if (record.gpuVisibility.IsValid()) pass.AddResourceRead(record.gpuVisibility.handle);
        }

        void CompileMeshDrawLists(RGBuilder builder)
        {
            drawLists.ClearConsumers();
            for (int passIndex = 0; passIndex < builder.passCount; ++passIndex)
            {
                if (builder.IsCulled(passIndex)) continue;
                List<int> used = builder.GetPass(passIndex).extensionUses;
                for (int i = 0; i < used.Count; ++i) drawLists.MarkLiveConsumer(used[i], passIndex);
            }
            BindGpuCulling(builder);
            drawLists.PrepareLive();
        }

        // Upload and compaction are appended here and then moved to the earliest pass whose inputs exist,
        // so they do not sit between adjacent raster passes.
        void InsertDrawListPreparation(RGBuilder builder, int originalCount)
        {
            var prepared = m_PreparedDrawLists;
            prepared.Clear();
            for (int passIndex = 0; passIndex < originalCount; ++passIndex)
            {
                IRGPass consumer = builder.GetPass(passIndex);
                if (builder.IsCulled(passIndex) || consumer.extensionUses.Count == 0) continue;
                foreach (int drawIndex in consumer.extensionUses)
                {
                    MeshDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
                    MeshTransformVersion version = record.tables.version;
                    version.owner.Record(builder, version, world.meshDrawShader, m_ScheduledVersions);
                    consumer.AddResourceRead(builder.ImportBuffer(version.matrices).handle);
                    consumer.AddResourceRead(builder.ImportBuffer(version.generations).handle);
                    consumer.AddResourceRead(builder.ImportBuffer(record.tables.renderingLayers).handle);
                    MeshTransformVersion previous = record.motion.previous;
                    if (previous == null) continue;
                    consumer.AddResourceRead(builder.ImportBuffer(previous.matrices).handle);
                    consumer.AddResourceRead(builder.ImportBuffer(previous.generations).handle);
                }
                foreach (int drawIndex in consumer.extensionUses)
                {
                    if (!prepared.TryGetValue(drawIndex, out RGBufferHandle[] outputs))
                    {
                        outputs = RecordPreparation(builder, drawIndex);
                        prepared.Add(drawIndex, outputs);
                    }
                    foreach (RGBufferHandle output in outputs) consumer.AddResourceRead(output.handle);
                }
            }
            prepared.Clear();
        }

        // One upload producer per live list and, for GPU indirect, one compute producer.
        RGBufferHandle[] RecordPreparation(RGBuilder builder, int drawIndex)
        {
            MeshDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
            bool gpu = record.backend == EMeshDrawBackendPolicy.GPUIndirect;
            RGBufferHandle uploaded = builder.ImportBuffer(gpu ? record.payload.commandMeta : record.cpuInstances);
            RGBufferHandle cpuFades = gpu ? default : builder.ImportBuffer(record.cpuFades);
            using (RGTransferPassHandle upload = builder.AddTransferPass<MeshDrawPreparePassData>(MeshDrawProfiling.upload))
            {
                upload.WriteBuffer(uploaded);
                if (gpu && record.gpuVisibility.IsValid()) upload.ReadBuffer(record.gpuVisibility);
                if (!gpu) upload.WriteBuffer(cpuFades);
                ref MeshDrawPreparePassData data = ref upload.GetPassData<MeshDrawPreparePassData>();
                data.lists = drawLists;
                data.index = drawIndex;
                upload.SetExecuteFunc((in MeshDrawPreparePassData data, in RGTransferEncoder commands) => data.lists.Upload(data.index, commands.m_CommandBuffer));
            }
            if (!gpu)
            {
                RGBufferHandle[] cpuOutputs = builder.GetRecordingArray<RGBufferHandle>(2);
                cpuOutputs[0] = uploaded;
                cpuOutputs[1] = cpuFades;
                return cpuOutputs;
            }

            RGBufferHandle instances = builder.ImportBuffer(record.payload.instanceToEntity);
            RGBufferHandle fades = builder.ImportBuffer(record.payload.instanceFade);
            RGBufferHandle counts = builder.ImportBuffer(record.payload.visibleCounts);
            RGBufferHandle args = builder.ImportBuffer(record.payload.indirectArgs);
            RGBufferHandle candidates = builder.ImportBuffer(record.cache.candidateBuffer);
            using (RGComputePassHandle compact = builder.AddComputePass<MeshDrawPreparePassData>(MeshDrawProfiling.compaction))
            {
                compact.ReadBuffer(uploaded);
                compact.ReadBuffer(candidates);
                compact.ReadBuffer(record.gpuVisibility);
                compact.WriteBuffer(instances);
                compact.WriteBuffer(fades);
                compact.WriteBuffer(counts);
                compact.WriteBuffer(args);
                ref MeshDrawPreparePassData data = ref compact.GetPassData<MeshDrawPreparePassData>();
                data.lists = drawLists;
                data.index = drawIndex;
                compact.SetExecuteFunc((in MeshDrawPreparePassData data, in RGComputeEncoder commands) => data.lists.Compact(data.index, commands.m_CommandBuffer));
            }
            RGBufferHandle[] outputs = builder.GetRecordingArray<RGBufferHandle>(3);
            outputs[0] = instances;
            outputs[1] = fades;
            outputs[2] = args;
            return outputs;
        }
    }

    /// <summary>
    /// One view and one mesh pass. The constructor only stores the two fields. <see cref="MeshGraphRecording.CreateMeshDrawList"/>
    /// checks that the tag is one of the nine mesh passes and that the view's LOD projection is valid.
    /// </summary>
    public readonly struct MeshDrawListDesc
    {
        public readonly MeshView view;
        public readonly ShaderTagId passTag;

        public MeshDrawListDesc(MeshView view, ShaderTagId passTag)
        {
            this.view = view;
            this.passTag = passTag;
        }
    }

    /// <summary>
    /// Pass-facing mesh recording API. Submitting a mesh draw list records its draw calls into the raster encoder.
    /// Native command-buffer submission keeps the words native submit and submission segment.
    /// </summary>
    public static class MeshGraphRecording
    {
        /// <summary>Binds the builder to its mesh world once; the extension and its draw-list registry live as long as the builder.</summary>
        public static void SetMeshWorld(this RGBuilder builder, MeshWorld meshWorld)
        {
            if (meshWorld == null) throw new ArgumentNullException(nameof(meshWorld));
            if (builder.extension != null) throw new InvalidOperationException("This RGBuilder already owns a graph extension.");
            builder.extension = new MeshGraphExtension(meshWorld);
        }

        public static RGMeshDrawListHandle CreateMeshDrawList(this RGBuilder builder, in MeshDrawListDesc desc)
        {
            if (!MeshPassTable.TryGet(desc.passTag, out MeshPassDesc pass))
                throw new ArgumentException($"'{desc.passTag.name}' is not a mesh pass.", nameof(desc));
            desc.view.lodProjection.ThrowIfInvalid();
            return MeshGraphExtension.Of(builder).CreateMeshDrawList(builder, desc.view, pass);
        }

        /// <summary>Records GPU frustum culling for a view ahead of its draw lists when this camera draws on the GPU backend.</summary>
        public static void RequestMeshGPUCulling(this RGBuilder builder, in MeshView view)
        {
            MeshGraphExtension extension = MeshGraphExtension.Of(builder);
            if (extension.world.SelectBackend(MeshPassTable.Depth) != EMeshDrawBackendPolicy.GPUIndirect) return;
            view.lodProjection.ThrowIfInvalid();
            extension.RequestMeshGPUCulling(builder, view, RGMeshDrawListHandle.Invalid);
        }

        /// <summary>Declares the pass as a consumer. A handle from another builder or an earlier graph fails here.</summary>
        public static RGMeshDrawListHandle UseMeshDrawList(this RGRasterPassHandle pass, in RGMeshDrawListHandle draws)
        {
            if (!(pass.extension is MeshGraphExtension owner)) throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            if (!owner.drawLists.IsLive(draws)) throw new InvalidOperationException("The draw list belongs to another builder or an earlier graph.");
            MeshGraphExtension.UseMeshDrawList(owner.drawLists, pass.pass, draws);
            return draws;
        }

        public static void SubmitMeshDrawList(this in RGRasterEncoder encoder, in RGMeshDrawListHandle draws)
        {
            if (!(encoder.m_Extension is MeshGraphExtension owner)) throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            owner.drawLists.SubmitMeshDrawList(encoder.m_CommandBuffer, draws);
        }

        internal static MeshOcclusionLists RecordMeshOcclusionPrediction(this RGBuilder builder, MeshView view, MeshMotionBinding motion,
            RGTextureHandle previousFurthest, Matrix4x4 previousProjection, Unity.Mathematics.int2 extent, ulong historyVersion)
            => MeshGraphExtension.Of(builder).RecordMeshOcclusionPrediction(builder, view, motion, previousFurthest, previousProjection, extent, historyVersion);

        internal static void RecordMeshOcclusionRecovery(this RGBuilder builder, in MeshOcclusionLists lists, RGTextureHandle furthest,
            Matrix4x4 projection, Unity.Mathematics.int2 extent)
            => MeshGraphExtension.Of(builder).RecordMeshOcclusionRecovery(builder, lists, furthest, projection, extent);
    }
}
