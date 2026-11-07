using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal struct MeshGpuCullPassData
    {
        public MeshWorld world;
        public MeshView view;
        public Vector4[] frustumPlanes;
        public MeshCullInputs inputs;
        public RGBufferHandle visibility;
    }

    /// <summary>
    /// Per-builder mesh state. The render graph knows this object only as an <see cref="IRGGraphExtension"/>:
    /// it calls Contribute once per compile and Clear whenever the recorded graph is dropped.
    /// </summary>
    internal sealed partial class MeshGraphExtension : IRGGraphExtension
    {
        struct MeshDrawPreparePassData { public RGDrawListContext lists; public int index; }

        internal readonly MeshWorld world;
        internal readonly RGDrawListContext drawLists;
        readonly Dictionary<int, RGBufferHandle[]> m_PreparedDrawLists = new Dictionary<int, RGBufferHandle[]>();
        readonly Dictionary<MeshVisibilityKey, RGBufferHandle> m_GpuCullVisibility = new Dictionary<MeshVisibilityKey, RGBufferHandle>(8);
        readonly HashSet<MeshTransformVersion> m_ScheduledVersions = new HashSet<MeshTransformVersion>();

        internal MeshGraphExtension(MeshWorld world)
        {
            this.world = world;
            drawLists = new RGDrawListContext(world);
        }

        internal static MeshGraphExtension Of(RGBuilder builder)
            => builder.extension as MeshGraphExtension ?? throw new InvalidOperationException("This RGBuilder has no MeshWorld; call SetMeshWorld first.");

        public void Clear()
        {
            m_ScheduledVersions.Clear();
            m_GpuCullVisibility.Clear();
            drawLists.ReleaseAll();
        }

        public void Contribute(RGBuilder builder)
        {
            CompileMeshDrawLists(builder);
            InsertDrawListPreparation(builder);
        }

        internal RGDrawListHandle CreateDrawList(RGBuilder builder, in MeshView view, in MeshPassDesc desc)
        {
            view.lodProjection.ThrowIfInvalid();
            RGDrawListHandle draws = drawLists.Declare(view, desc);
            RGDrawListRecord record = drawLists.GetRecordCopy(draws.index);
            // Recording references keep the frozen tables alive for this graph.
            builder.ImportBuffer(record.tables.renderingLayers);
            builder.ImportBuffer(record.tables.version.matrices);
            builder.ImportBuffer(record.tables.version.generations);
            MeshTransformVersion previous = record.motion.previous;
            if (previous != null) { builder.ImportBuffer(previous.matrices); builder.ImportBuffer(previous.generations); }
            if (record.backend == EMeshBackendPolicy.GpuIndirect) EnsureMeshGpuCull(builder, view, draws);
            return draws;
        }

        MeshCullInputs CullInputs(RGBuilder builder, RGComputePassHandle pass, bool boundsOnly = false)
        {
            MeshWorldGpu gpu = world.gpu;
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

        /// <summary>One frustum-culling producer per visibility key and camera render; later lists of the same view only bind it.</summary>
        internal void EnsureMeshGpuCull(RGBuilder builder, in MeshView view, in RGDrawListHandle draws)
        {
            var key = new MeshVisibilityKey(view, true);
            BufferHandle buffer = world.GetGpuVisibilityBuffer(view);
            if (m_GpuCullVisibility.TryGetValue(key, out RGBufferHandle existing))
            {
                if (drawLists.IsLive(draws)) drawLists.BindGpuVisibility(draws.index, existing, buffer);
                return;
            }

            if (!world.gpuBackend.isSupported) throw new InvalidOperationException("GPU mesh culling requires Compute_MeshDrawPipeline.");
            RGBufferHandle visibility = builder.ImportBuffer(buffer);
            Vector4[] frustumPlanes = builder.GetRecordingArray<Vector4>(6);
            view.CopyFrustumPlanes(frustumPlanes);
            using (RGComputePassHandle pass = builder.AddComputePass<MeshGpuCullPassData>(MeshDrawProfiling.culling))
            {
                ref MeshGpuCullPassData data = ref pass.GetPassData<MeshGpuCullPassData>();
                data.world = world;
                data.view = view;
                data.frustumPlanes = frustumPlanes;
                data.inputs = CullInputs(builder, pass);
                data.visibility = pass.WriteBuffer(visibility);
                pass.KeepImportedWritesLocal();
                pass.SetExecuteFunc((in MeshGpuCullPassData data, in RGComputeEncoder commands) =>
                {
                    data.world.gpuBackend.DispatchCullFrustum(commands, data.view, data.frustumPlanes, data.inputs, data.visibility, data.world.stats);
                });
            }

            m_GpuCullVisibility[key] = visibility;
            if (drawLists.IsLive(draws)) drawLists.BindGpuVisibility(draws.index, visibility, buffer);
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal static void UseDrawList(RGDrawListContext lists, IRGPass pass, in RGDrawListHandle draws)
        {
            pass.extensionUses.Add(draws.index);
            RGDrawListRecord record = lists.GetRecordCopy(draws.index);
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
            drawLists.PrepareLive();
        }

        // Upload and compaction are appended here and then moved to the earliest pass whose inputs exist,
        // so they do not sit between adjacent raster passes.
        void InsertDrawListPreparation(RGBuilder builder)
        {
            int originalCount = builder.passCount;
            var prepared = m_PreparedDrawLists;
            prepared.Clear();
            for (int passIndex = 0; passIndex < originalCount; ++passIndex)
            {
                IRGPass consumer = builder.GetPass(passIndex);
                if (builder.IsCulled(passIndex) || consumer.extensionUses.Count == 0) continue;
                ProfilingSampler group = consumer.customSampler;
                if (group != null) builder.BeginProfileRange(group);
                foreach (int drawIndex in consumer.extensionUses)
                {
                    RGDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
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
                if (group != null) builder.EndProfileRange(group);
            }
            prepared.Clear();
        }

        // One upload producer per live list and, for GPU indirect, one compute producer.
        RGBufferHandle[] RecordPreparation(RGBuilder builder, int drawIndex)
        {
            RGDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
            bool gpu = record.backend == EMeshBackendPolicy.GpuIndirect;
            RGBufferHandle uploaded = builder.ImportBuffer(gpu ? record.payload.commandMeta : record.cpuInstances);
            RGBufferHandle cpuFades = gpu ? default : builder.ImportBuffer(record.cpuFades);
            using (RGTransferPassHandle upload = builder.AddTransferPass<MeshDrawPreparePassData>(MeshDrawProfiling.upload))
            {
                upload.WriteBuffer(uploaded);
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
    /// Pass-facing mesh recording API. The calls read like render-graph members
    /// (<c>graph.CreateDrawList</c>, <c>pass.UseDrawList</c>, <c>encoder.Draw</c>) while the render graph itself
    /// has no dependency on the mesh pipeline.
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

        public static RGDrawListHandle CreateDrawList(this RGBuilder builder, in MeshView view, in MeshPassDesc desc)
            => MeshGraphExtension.Of(builder).CreateDrawList(builder, view, desc);

        /// <summary>Records GPU frustum culling for a view ahead of its draw lists when this camera draws on the GPU backend.</summary>
        public static void EnsureMeshGpuCull(this RGBuilder builder, in MeshView view)
        {
            MeshGraphExtension extension = MeshGraphExtension.Of(builder);
            if (extension.world.SelectBackend(MeshPassPresets.Depth) != EMeshBackendPolicy.GpuIndirect) return;
            view.lodProjection.ThrowIfInvalid();
            extension.EnsureMeshGpuCull(builder, view, RGDrawListHandle.Invalid);
        }

        /// <summary>Declares the pass as a consumer. A handle from another builder or an earlier graph fails here.</summary>
        public static RGDrawListHandle UseDrawList(this RGRasterPassHandle pass, in RGDrawListHandle draws)
        {
            if (!(pass.extension is MeshGraphExtension owner)) throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            if (!owner.drawLists.IsLive(draws)) throw new InvalidOperationException("The draw list belongs to another builder or an earlier graph.");
            MeshGraphExtension.UseDrawList(owner.drawLists, pass.pass, draws);
            return draws;
        }

        public static void Draw(this in RGRasterEncoder encoder, in RGDrawListHandle draws)
        {
            if (!(encoder.m_Extension is MeshGraphExtension owner)) throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            owner.drawLists.Submit(encoder.m_CommandBuffer, draws);
        }

        internal static MeshOcclusionLists RecordMeshOcclusionFirst(this RGBuilder builder, MeshView view, MeshMotionBinding motion,
            RGTextureHandle previousFurthest, Matrix4x4 previousProjection, Unity.Mathematics.int2 extent, ulong historyVersion)
            => MeshGraphExtension.Of(builder).RecordMeshOcclusionFirst(builder, view, motion, previousFurthest, previousProjection, extent, historyVersion);

        internal static void RecordMeshOcclusionRecovery(this RGBuilder builder, in MeshOcclusionLists lists, RGTextureHandle furthest,
            Matrix4x4 projection, Unity.Mathematics.int2 extent)
            => MeshGraphExtension.Of(builder).RecordMeshOcclusionRecovery(builder, lists, furthest, projection, extent);
    }
}
