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
        public string sampleName;
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
        struct MeshDrawPreparePassData
        {
            public MeshDrawListRegistry lists;
            public int index;
        }

        internal readonly MeshWorld world;
        internal readonly MeshDrawListRegistry drawLists;
        readonly Dictionary<int, RGBufferHandle[]> m_PreparedDrawLists = new Dictionary<int, RGBufferHandle[]>();
        readonly Dictionary<int, BufferPair> m_TableReads = new Dictionary<int, BufferPair>();
        readonly Dictionary<MeshVisibilityKey, RGBufferHandle> m_GPUCullVisibility = new Dictionary<MeshVisibilityKey, RGBufferHandle>(8);
        readonly Dictionary<MeshVisibilityKey, GpuVisibilityBinding> m_OcclusionFinal = new Dictionary<MeshVisibilityKey, GpuVisibilityBinding>(4);
        readonly List<MeshView> m_FrustumSources = new List<MeshView>(8);
        readonly List<MeshView> m_PendingFrustum = new List<MeshView>(8);
        readonly HashSet<MeshVisibilityKey> m_PendingFrustumKeys = new HashSet<MeshVisibilityKey>();
        GpuVisibilityBinding m_FrustumBinding;
        readonly Dictionary<MeshVisibilityKey, int> m_FrustumSlice = new Dictionary<MeshVisibilityKey, int>(8);
        MeshFrustumView[] m_FrustumRecords = System.Array.Empty<MeshFrustumView>();
        readonly HashSet<MeshTransformVersion> m_ScheduledVersions = new HashSet<MeshTransformVersion>();

        struct BufferPair
        {
            public RGBufferHandle matrices, generations, layers, previousMatrices, previousGenerations;
            public bool hasPrevious;
        }

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
            m_PendingFrustum.Clear();
            m_PendingFrustumKeys.Clear();
            m_FrustumSlice.Clear();
            m_FrustumBinding = default;
            m_PreparedDrawLists.Clear();
            m_TableReads.Clear();
            drawLists.ReleaseAll();
        }

        public void Contribute(RGBuilder builder)
        {
            CompileMeshDrawLists(builder);
        }

        internal RGMeshDrawListHandle CreateMeshDrawList(RGBuilder builder, in MeshView view, in MeshPassDesc desc)
        {
            view.lodProjection.ThrowIfInvalid();
            RGMeshDrawListHandle draws = drawLists.Declare(view, desc);
            MeshDrawListRecord record = drawLists.GetRecordCopy(draws.index);
            // Recording references keep the frozen tables alive for this graph.
            var tables = new BufferPair
            {
                matrices = builder.ImportBuffer(record.tables.version.matrices),
                generations = builder.ImportBuffer(record.tables.version.generations),
                layers = builder.ImportBuffer(record.tables.renderingLayers)
            };
            MeshTransformVersion previous = record.motion.previous;
            if (previous != null)
            {
                tables.previousMatrices = builder.ImportBuffer(previous.matrices);
                tables.previousGenerations = builder.ImportBuffer(previous.generations);
                tables.hasPrevious = true;
            }
            m_TableReads[draws.index] = tables;
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect)
            {
                RequestMeshGPUCulling(builder, view, draws);
            }
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
            if (boundsOnly)
            {
                return inputs;
            }
            inputs.flags = pass.ReadBuffer(builder.ImportBuffer(gpu.flags));
            inputs.layerMask = pass.ReadBuffer(builder.ImportBuffer(gpu.layerMask));
            inputs.sceneCullingMask = pass.ReadBuffer(builder.ImportBuffer(gpu.sceneCullingMask));
            inputs.renderingLayers = pass.ReadBuffer(builder.ImportBuffer(gpu.renderingLayers));
            return inputs;
        }

        internal void RequestMeshGPUCulling(RGBuilder builder, in MeshView view, in RGMeshDrawListHandle draws)
        {
            view.lodProjection.ThrowIfInvalid();
            if (OcclusionCovers(view))
                return;
            var key = new MeshVisibilityKey(view, true);
            if (m_FrustumSlice.ContainsKey(key) || !m_PendingFrustumKeys.Add(key))
                return;
            m_PendingFrustum.Add(view);
        }

        bool OcclusionCovers(in MeshView view)
        {
            if (view.visibilityPhase == EMeshVisibilityPhase.Final && m_OcclusionFinal.ContainsKey(CoverageKey(view)))
                return true;
            return m_GPUCullVisibility.ContainsKey(new MeshVisibilityKey(view, true));
        }

        internal void FlushFrustumCulling(RGBuilder builder)
        {
            if (m_PendingFrustum.Count == 0)
                return;
            m_FrustumSources.Clear();
            bool depth = false, shadows = false;
            for (int i = 0; i < m_PendingFrustum.Count; ++i)
            {
                MeshView view = m_PendingFrustum[i];
                var key = new MeshVisibilityKey(view, true);
                m_FrustumSlice[key] = m_FrustumSources.Count;
                m_FrustumSources.Add(view);
                if (view.isShadow)
                    shadows = true;
                else
                    depth = true;
            }
            m_FrustumBinding = RecordFrustumBatch(builder, m_FrustumSources.Count, MeshDrawProfiling.FrustumGroup(depth, shadows));
            m_PendingFrustum.Clear();
            m_PendingFrustumKeys.Clear();
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
                if (pair.Key.viewKey != record.view.viewKey || pair.Key.phase != record.view.visibilityPhase)
                {
                    continue;
                }
                binding = new GpuVisibilityBinding { handle = pair.Value, buffer = world.GetGPUVisibilityBuffer(record.view) };
                return true;
            }
            return false;
        }

        GpuVisibilityBinding RecordFrustumBatch(RGBuilder builder, int viewCount, string sampleName)
        {
            if (!world.gpuBackend.isSupported)
            {
                throw new InvalidOperationException("GPU mesh culling requires Compute_MeshDrawPipeline.");
            }
            if (m_FrustumRecords.Length < viewCount)
            {
                int grown = Mathf.NextPowerOfTwo(Mathf.Max(4, viewCount));
                m_FrustumRecords = new MeshFrustumView[grown];
            }
            for (int i = 0; i < viewCount; ++i)
            {
                m_FrustumRecords[i] = MeshFrustumView.FromView(m_FrustumSources[i]);
            }
            BufferHandle batch = world.gpuVisibility.EnsureFrustumBatch(world.entityHighWater, viewCount, out int stride);
            m_BoundFrustumStride = stride;
            RGBufferHandle visibility = builder.ImportBuffer(batch);
            using (RGComputePassHandle pass = builder.AddComputePass<MeshFrustumBatchData>(MeshDrawProfiling.culling))
            {
                ref MeshFrustumBatchData data = ref pass.GetPassData<MeshFrustumBatchData>();
                data.world = world;
                MeshFrustumView[] stored = builder.GetRecordingArray<MeshFrustumView>(viewCount);
                System.Array.Copy(m_FrustumRecords, stored, viewCount);
                data.views = stored;
                data.viewCount = viewCount;
                data.stride = stride;
                data.sampleName = sampleName;
                data.inputs = CullInputs(builder, pass);
                data.visibility = pass.WriteBuffer(visibility);
                data.viewTable = world.gpuVisibility.frustumViews.buffer;
                pass.KeepImportedWritesLocal();
                pass.SetExecuteFunc(static (in MeshFrustumBatchData data, in RGComputeEncoder commands) =>
                {
                    if (data.viewCount > 0)
                    {
                        commands.SetBufferData(data.viewTable, data.views, 0, 0, data.viewCount);
                    }
                    commands.BeginSample(data.sampleName);
                    try
                    {
                        data.world.gpuBackend.DispatchCullFrustumBatch(commands, data.inputs, data.viewCount, data.stride, data.viewTable, data.visibility, data.world.stats);
                    }
                    finally
                    {
                        commands.EndSample(data.sampleName);
                    }
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
            for (int i = 0; i < drawLists.Count; ++i)
            {
                MeshDrawListRecord record = drawLists.GetRecordCopy(i);
                if (record.state != EMeshDrawListState.Live || record.backend != EMeshDrawBackendPolicy.GPUIndirect)
                    continue;
                if (!record.gpuVisibility.IsValid())
                    throw new InvalidOperationException("A GPU draw list was not prepared after its culling pass.");
                BindVisibilityRead(builder, record, record.gpuVisibility);
            }
        }

        internal void PrepareMeshDrawList(RGBuilder builder, in RGMeshDrawListHandle draws)
        {
            MeshDrawListRecord record = drawLists.GetRecordCopy(draws.index);
            record.cacheRevision = record.cache.revision;
            if (record.cache.memberCount == 0)
            {
                record.state = EMeshDrawListState.Empty;
                drawLists.SetRecord(draws.index, record);
                return;
            }
            record.tables.version.owner.Record(builder, record.tables.version, world.meshDrawShader, m_ScheduledVersions);
            if (record.backend == EMeshDrawBackendPolicy.GPUIndirect)
            {
                int visibilityBase = 0;
                GpuVisibilityBinding binding;
                if (!OcclusionPhase(record, out binding))
                {
                    RequestMeshGPUCulling(builder, record.view, draws);
                    var key = new MeshVisibilityKey(record.view, true);
                    if (!m_FrustumSlice.ContainsKey(key))
                        FlushFrustumCulling(builder);
                    visibilityBase = m_FrustumSlice[key] * m_BoundFrustumStride;
                    binding = m_FrustumBinding;
                }
                drawLists.BindGPUVisibility(draws.index, binding.handle, binding.buffer, visibilityBase);
                record = drawLists.GetRecordCopy(draws.index);
                record.cacheRevision = record.cache.revision;
                drawLists.EnsureGpuPayload(ref record);
            }
            else if (record.cpuInstances.buffer == null)
            {
                world.cpuBackend.AllocateInstances(record.cache.memberCount, out record.cpuInstances, out record.cpuFades);
            }
            drawLists.SetRecord(draws.index, record);
            if (!m_PreparedDrawLists.ContainsKey(draws.index))
            {
                m_PreparedDrawLists.Add(draws.index, RecordPreparation(builder, draws.index));
            }
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal void UsePreparedList(IRGPass pass, in RGMeshDrawListHandle draws)
        {
            pass.extensionUses.Add(draws.index);
            MeshDrawListRecord record = drawLists.GetRecordCopy(draws.index);
            if (record.gpuVisibility.IsValid())
                pass.AddResourceRead(record.gpuVisibility.handle);
            if (m_TableReads.TryGetValue(draws.index, out BufferPair tables))
            {
                pass.AddResourceRead(tables.matrices.handle);
                pass.AddResourceRead(tables.generations.handle);
                pass.AddResourceRead(tables.layers.handle);
                if (tables.hasPrevious)
                {
                    pass.AddResourceRead(tables.previousMatrices.handle);
                    pass.AddResourceRead(tables.previousGenerations.handle);
                }
            }
            if (m_PreparedDrawLists.TryGetValue(draws.index, out RGBufferHandle[] outputs))
            {
                for (int i = 0; i < outputs.Length; ++i)
                    pass.AddResourceRead(outputs[i].handle);
            }
        }

        void CompileMeshDrawLists(RGBuilder builder)
        {
            drawLists.ClearConsumers();
            for (int passIndex = 0; passIndex < builder.passCount; ++passIndex)
            {
                if (builder.IsCulled(passIndex))
                {
                    continue;
                }
                List<int> used = builder.GetPass(passIndex).extensionUses;
                for (int i = 0; i < used.Count; ++i)
                {
                    drawLists.MarkLiveConsumer(used[i], passIndex);
                }
            }
            BindGpuCulling(builder);
            drawLists.PrepareLive();
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
                upload.KeepImportedWritesLocal();
                upload.WriteBuffer(uploaded);
                if (gpu && record.gpuVisibility.IsValid())
                {
                    upload.ReadBuffer(record.gpuVisibility);
                }
                if (!gpu)
                {
                    upload.WriteBuffer(cpuFades);
                }
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
            RGBufferHandle args = builder.ImportBuffer(record.payload.indirectArgs);
            RGBufferHandle candidates = builder.ImportBuffer(record.cache.candidateBuffer);
            using (RGComputePassHandle compact = builder.AddComputePass<MeshDrawPreparePassData>(MeshDrawProfiling.compaction))
            {
                compact.KeepImportedWritesLocal();
                compact.ReadBuffer(uploaded);
                compact.ReadBuffer(candidates);
                compact.ReadBuffer(record.gpuVisibility);
                compact.WriteBuffer(instances);
                compact.WriteBuffer(fades);
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
            if (meshWorld == null)
            {
                throw new ArgumentNullException(nameof(meshWorld));
            }
            if (builder.extension != null)
            {
                throw new InvalidOperationException("This RGBuilder already owns a graph extension.");
            }
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
            if (extension.world.SelectBackend(MeshPassTable.Depth) != EMeshDrawBackendPolicy.GPUIndirect)
            {
                return;
            }
            view.lodProjection.ThrowIfInvalid();
            extension.RequestMeshGPUCulling(builder, view, RGMeshDrawListHandle.Invalid);
        }

        public static void FlushMeshFrustumCulling(this RGBuilder builder)
            => MeshGraphExtension.Of(builder).FlushFrustumCulling(builder);

        public static void PrepareMeshDrawList(this RGBuilder builder, in RGMeshDrawListHandle draws)
            => MeshGraphExtension.Of(builder).PrepareMeshDrawList(builder, draws);

        /// <summary>Declares the pass as a consumer. A handle from another builder or an earlier graph fails here.</summary>
        public static RGMeshDrawListHandle UseMeshDrawList(this RGRasterPassHandle pass, in RGMeshDrawListHandle draws)
        {
            if (!(pass.extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
            if (!owner.drawLists.IsLive(draws))
            {
                throw new InvalidOperationException("The draw list belongs to another builder or an earlier graph.");
            }
            owner.UsePreparedList(pass.pass, draws);
            return draws;
        }

        public static void SubmitMeshDrawList(this in RGRasterEncoder encoder, in RGMeshDrawListHandle draws)
        {
            if (!(encoder.m_Extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
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
