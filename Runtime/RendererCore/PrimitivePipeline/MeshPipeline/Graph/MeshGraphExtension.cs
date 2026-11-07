using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal struct MeshGpuCullPassData
    {
        public MeshWorld world;
        public MeshView view;
        public int instanceCount;
        public Vector4[] frustumPlanes;
        public RGBufferHandle visibility;
        public RGBufferHandle boundsCenter;
        public RGBufferHandle boundsExtent;
        public RGBufferHandle instanceFlags;
        public RGBufferHandle instanceLayerMask;
        public RGBufferHandle instanceSceneCullingMask;
        public RGBufferHandle instanceRenderingLayer;
    }

    /// <summary>
    /// Per-builder mesh state. The render graph knows this object only as an <see cref="IRGGraphExtension"/>:
    /// it calls Contribute once per compile and Clear whenever the recorded graph is dropped.
    /// </summary>
    internal sealed partial class MeshGraphExtension : IRGGraphExtension
    {
        struct MeshDrawPreparePassData { public RGDrawListContext lists; public int index; }

        internal readonly MeshWorld world;
        internal readonly RGDrawListContext drawLists = new RGDrawListContext();
        readonly Dictionary<int, RGBufferHandle[]> m_PreparedDrawLists = new Dictionary<int, RGBufferHandle[]>();
        readonly Dictionary<MeshGpuVisibilityKey, RGBufferHandle> m_GpuCullVisibility = new Dictionary<MeshGpuVisibilityKey, RGBufferHandle>(8);
        readonly HashSet<MeshTransformVersion> m_ScheduledVersions = new HashSet<MeshTransformVersion>();

        internal MeshGraphExtension(MeshWorld world) => this.world = world;

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

        internal RGDrawListHandle CreateDrawList(RGBuilder builder, in MeshView view, MeshPassId passId)
        {
            MeshPassContext pass = world.BindPass(view, passId);
            RGDrawListHandle draws = drawLists.Declare(
                world.Processor, pass, MeshVisibilityHandle.Invalid, world.VisibilityShare,
                view, passId, world);
            RetainDrawListInputs(builder, draws);
            EnsureMeshGpuCull(builder, view, draws);
            return draws;
        }

        void RetainDrawListInputs(RGBuilder builder, RGDrawListHandle draws)
        {
            var record = drawLists.GetRecordCopy(draws.index);
            var residency = record.residency;
            if (residency.TransformBuffer.buffer != null) builder.ImportBuffer(residency.TransformBuffer);
            if (residency.RenderingLayerBuffer.buffer != null) builder.ImportBuffer(residency.RenderingLayerBuffer);
            if (residency.InstanceTransformIndexBuffer.buffer != null) builder.ImportBuffer(residency.InstanceTransformIndexBuffer);
            if (residency.version != null) builder.ImportBuffer(residency.version.generations);
            var previous = record.pass.previousTransforms.previous;
            if (previous != null) { builder.ImportBuffer(previous.matrices); builder.ImportBuffer(previous.generations); }
        }

        internal void EnsureMeshGpuCull(RGBuilder builder, in MeshView view, in RGDrawListHandle draws)
        {
            if (MeshDrawGPUBackend.SelectPolicy(world.SelectPolicy()) != EMeshBackendPolicy.GpuIndirect)
                return;

            MeshGpuVisibilityKey key = MeshGpuVisibilityCache.MakeKey(view, true);
            if (m_GpuCullVisibility.TryGetValue(key, out RGBufferHandle existing))
            {
                if (drawLists.IsLive(draws))
                    drawLists.BindGpuVisibility(draws.index, existing);
                return;
            }

            GpuScene residency = world.Residency;
            if (residency == null
                || residency.BoundsCenterBuffer.buffer == null
                || residency.BoundsExtentBuffer.buffer == null
                || residency.InstanceFlagsBuffer.buffer == null
                || residency.InstanceLayerMaskBuffer.buffer == null
                || residency.InstanceSceneCullingMaskBuffer.buffer == null
                || residency.InstanceRenderingLayerBuffer.buffer == null)
                throw new InvalidOperationException("MeshGpuCull requires uploaded GpuScene instance buffers.");
            if (!MeshDrawGPUBackend.TryGetCullKernel(out _, out _))
                throw new InvalidOperationException("MeshGpuCull requires CullFrustum.");

            int instanceCount = residency.Scene != null ? residency.Scene.InstanceHighWater : 0;
            BufferHandle visibility = world.GetGpuVisibilityBufferHandle(view, math.max(1, instanceCount));
            RGBufferHandle visibilityRef = builder.ImportBuffer(visibility);
            RGBufferHandle boundsCenter = builder.ImportBuffer(residency.BoundsCenterBuffer);
            RGBufferHandle boundsExtent = builder.ImportBuffer(residency.BoundsExtentBuffer);
            RGBufferHandle instanceFlags = builder.ImportBuffer(residency.InstanceFlagsBuffer);
            RGBufferHandle instanceLayerMask = builder.ImportBuffer(residency.InstanceLayerMaskBuffer);
            RGBufferHandle instanceSceneCullingMask = builder.ImportBuffer(residency.InstanceSceneCullingMaskBuffer);
            RGBufferHandle instanceRenderingLayer = builder.ImportBuffer(residency.InstanceRenderingLayerBuffer);
            Vector4[] frustumPlanes = builder.GetRecordingArray<Vector4>(6);
            view.CopyFrustumPlanes(frustumPlanes);

            using (RGComputePassHandle passRef = builder.AddComputePass<MeshGpuCullPassData>(MeshDrawProfiling.culling))
            {
                ref MeshGpuCullPassData passData = ref passRef.GetPassData<MeshGpuCullPassData>();
                passData.world = world;
                passData.view = view;
                passData.instanceCount = instanceCount;
                passData.frustumPlanes = frustumPlanes;
                passData.visibility = passRef.WriteBuffer(visibilityRef);
                passData.boundsCenter = passRef.ReadBuffer(boundsCenter);
                passData.boundsExtent = passRef.ReadBuffer(boundsExtent);
                passData.instanceFlags = passRef.ReadBuffer(instanceFlags);
                passData.instanceLayerMask = passRef.ReadBuffer(instanceLayerMask);
                passData.instanceSceneCullingMask = passRef.ReadBuffer(instanceSceneCullingMask);
                passData.instanceRenderingLayer = passRef.ReadBuffer(instanceRenderingLayer);
                passRef.KeepImportedWritesLocal();
                passRef.SetExecuteFunc((in MeshGpuCullPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    MeshDrawGPUBackend.DispatchCullFrustum(
                        cmdEncoder, passData.view, passData.instanceCount, passData.frustumPlanes,
                        passData.boundsCenter, passData.boundsExtent, passData.instanceFlags, passData.instanceLayerMask,
                        passData.instanceSceneCullingMask, passData.instanceRenderingLayer, passData.visibility);
                    passData.world?.MarkGpuCulled(passData.view);
                });
            }

            m_GpuCullVisibility[key] = visibilityRef;
            if (drawLists.IsLive(draws))
                drawLists.BindGpuVisibility(draws.index, visibilityRef);
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal static void UseDrawList(RGDrawListContext lists, IRGPass pass, in RGDrawListHandle draws)
        {
            pass.extensionUses.Add(draws.index);
            RGDrawListRecord record = lists.GetRecordCopy(draws.index);
            if (record.gpuVisibilityBuffer.IsValid())
                pass.AddResourceRead(record.gpuVisibilityBuffer.handle);
        }

        void CompileMeshDrawLists(RGBuilder builder)
        {
            drawLists.ClearConsumers();
            for (int passIndex = 0; passIndex < builder.passCount; ++passIndex)
            {
                if (builder.IsCulled(passIndex)) continue;
                List<int> used = builder.GetPass(passIndex).extensionUses;
                for (int i = 0; i < used.Count; ++i)
                    drawLists.MarkLiveConsumer(used[i], passIndex);
            }
            drawLists.PrepareLive(world);
        }

        static RGBufferHandle ImportMeshBuffer(RGBuilder builder, ComputeBuffer buffer, string name, ComputeBufferType type = ComputeBufferType.Structured)
            => builder.ImportBuffer(new BufferHandle(-1, new BufferDescriptor(buffer.count, buffer.stride, type) { name = name }, buffer));

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
                    var record = drawLists.GetRecordCopy(drawIndex);
                    var residency = record.residency;
                    if (!residency.isValid) continue;
                    residency.version.owner.Record(builder, residency.version, MeshDrawGPUBackend.shader, m_ScheduledVersions);
                    consumer.AddResourceRead(builder.ImportBuffer(residency.TransformBuffer).handle);
                    consumer.AddResourceRead(builder.ImportBuffer(residency.RenderingLayerBuffer).handle);
                    consumer.AddResourceRead(builder.ImportBuffer(residency.version.generations).handle);
                    var previous = record.pass.previousTransforms.previous;
                    if (previous == null) continue;
                    consumer.AddResourceRead(builder.ImportBuffer(previous.matrices).handle);
                    consumer.AddResourceRead(builder.ImportBuffer(previous.generations).handle);
                }
                foreach (int drawIndex in consumer.extensionUses)
                {
                    if (!prepared.TryGetValue(drawIndex, out RGBufferHandle[] outputs))
                    {
                        var record = drawLists.PrepareGraphRecord(drawIndex);
                        if (record.pipeline == null) continue;
                        bool gpu = record.gpuPayload != null;
                        RGBufferHandle metadata = gpu ? ImportMeshBuffer(builder, record.gpuPayload.commandMeta, "MeshDrawMetadata")
                            : record.cpuIndexBuffer.buffer != null ? builder.ImportBuffer(record.cpuIndexBuffer) : default;
                        if (!metadata.IsValid())
                        {
                            prepared.Add(drawIndex, Array.Empty<RGBufferHandle>());
                            continue;
                        }
                        using (RGTransferPassHandle upload = builder.AddTransferPass<MeshDrawPreparePassData>(MeshDrawProfiling.upload))
                        {
                            upload.WriteBuffer(metadata);
                            ref MeshDrawPreparePassData data = ref upload.GetPassData<MeshDrawPreparePassData>();
                            data.lists = drawLists;
                            data.index = drawIndex;
                            upload.SetExecuteFunc((in MeshDrawPreparePassData data, in RGTransferEncoder commands) => data.lists.Upload(data.index, commands.m_CommandBuffer));
                        }
                        if (gpu)
                        {
                            RGBufferHandle indices = ImportMeshBuffer(builder, record.gpuPayload.compactedIndices, "MeshDrawCompactedIndices");
                            RGBufferHandle counts = ImportMeshBuffer(builder, record.gpuPayload.visibleCounts, "MeshDrawVisibleCounts");
                            RGBufferHandle args = ImportMeshBuffer(builder, record.gpuPayload.argsBuffer, "MeshDrawIndirectArgs", ComputeBufferType.IndirectArguments);
                            RGBufferHandle candidates = ImportMeshBuffer(builder, record.gpuStaging.candidateTable, "MeshDrawCandidates");
                            RGBufferHandle transforms = builder.ImportBuffer(record.residency.InstanceTransformIndexBuffer);
                            using (RGComputePassHandle compact = builder.AddComputePass<MeshDrawPreparePassData>(MeshDrawProfiling.compaction))
                            {
                                compact.ReadBuffer(metadata);
                                compact.ReadBuffer(candidates);
                                compact.ReadBuffer(transforms);
                                compact.ReadBuffer(record.gpuVisibilityBuffer);
                                compact.WriteBuffer(indices);
                                compact.WriteBuffer(counts);
                                compact.WriteBuffer(args);
                                ref MeshDrawPreparePassData data = ref compact.GetPassData<MeshDrawPreparePassData>();
                                data.lists = drawLists;
                                data.index = drawIndex;
                                compact.SetExecuteFunc((in MeshDrawPreparePassData data, in RGComputeEncoder commands) => data.lists.Compact(data.index, commands.m_CommandBuffer));
                            }
                            outputs = builder.GetRecordingArray<RGBufferHandle>(2);
                            outputs[0] = indices;
                            outputs[1] = args;
                        }
                        else
                        {
                            outputs = builder.GetRecordingArray<RGBufferHandle>(1);
                            outputs[0] = metadata;
                        }
                        prepared.Add(drawIndex, outputs);
                    }
                    foreach (RGBufferHandle output in outputs) consumer.AddResourceRead(output.handle);
                }
                if (group != null) builder.EndProfileRange(group);
            }
            prepared.Clear();
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

        public static RGDrawListHandle CreateDrawList(this RGBuilder builder, in MeshView view, MeshPassId passId)
            => MeshGraphExtension.Of(builder).CreateDrawList(builder, view, passId);

        public static void EnsureMeshGpuCull(this RGBuilder builder, in MeshView view)
            => MeshGraphExtension.Of(builder).EnsureMeshGpuCull(builder, view, RGDrawListHandle.Invalid);

        public static RGDrawListHandle UseDrawList(this RGRasterPassHandle pass, in RGDrawListHandle draws)
        {
            // Only a live list of this pass's own builder becomes a consumer edge.
            if (!(pass.extension is MeshGraphExtension owner) || !owner.drawLists.IsLive(draws)) return RGDrawListHandle.Invalid;
            MeshGraphExtension.UseDrawList(owner.drawLists, pass.pass, draws);
            return draws;
        }

        public static void Draw(this in RGRasterEncoder encoder, in RGDrawListHandle draws)
        {
            // Submit rejects foreign, stale or out-of-range handles without throwing.
            (encoder.m_Extension as MeshGraphExtension)?.drawLists.Submit(encoder.m_CommandBuffer, draws);
        }

        internal static MeshOcclusionLists RecordMeshOcclusionFirst(this RGBuilder builder, MeshView view, ComputeShader shader, MeshMotionBinding motion,
            RGTextureHandle previousFurthest, Matrix4x4 previousProjection, int2 extent, ulong historyVersion)
            => MeshGraphExtension.Of(builder).RecordMeshOcclusionFirst(builder, view, shader, motion, previousFurthest, previousProjection, extent, historyVersion);

        internal static void RecordMeshOcclusionRecovery(this RGBuilder builder, in MeshOcclusionLists lists, ComputeShader shader, RGTextureHandle furthest,
            Matrix4x4 projection, int2 extent)
            => MeshGraphExtension.Of(builder).RecordMeshOcclusionRecovery(builder, lists, shader, furthest, projection, extent);
    }
}
