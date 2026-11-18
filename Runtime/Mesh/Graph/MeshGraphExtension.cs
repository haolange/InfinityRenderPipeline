using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// The mesh module's layer in one builder (P5: the render graph knows it only as an <see cref="IRGGraphExtension"/> and
    /// calls Contribute once per compile and Clear whenever the recorded graph is dropped). It declares each view's and each
    /// list's producer, contributes live consumers at compile and records consumer reads; the per-graph state lives in its
    /// <see cref="MeshViewRegistry"/>.
    /// </summary>
    internal sealed class MeshGraphExtension : IRGGraphExtension
    {
        struct MeshViewProducerData
        {
            public MeshViewRegistry registry;
            public int view;
            public MeshCullInputs inputs;
            public RGBufferHandle visibilityHandle;
            public bool occluded;
            public MeshOccluderInputs occluder;
            public bool remainder;
            public RGBufferHandle drawnHandle;
        }

        struct MeshDrawProducerData
        {
            public MeshViewRegistry registry;
            public int index;
            public RGBufferHandle visibilityHandle;
        }

        struct MeshDrawUploadData
        {
            public MeshViewRegistry registry;
            public int index;
        }

        static readonly RGComputePassExecuteAction<MeshViewProducerData> s_ProduceView = static (in MeshViewProducerData data, in RGComputeEncoder encoder)
            => data.registry.ProduceView(data.view, encoder, data.inputs, data.visibilityHandle, data.occluded, data.occluder, data.remainder, data.drawnHandle);
        static readonly RGComputePassExecuteAction<MeshDrawProducerData> s_Produce = static (in MeshDrawProducerData data, in RGComputeEncoder encoder)
            => data.registry.ProduceDrawList(data.index, encoder, data.visibilityHandle);
        static readonly RGTransferPassExecuteAction<MeshDrawUploadData> s_Upload = static (in MeshDrawUploadData data, in RGTransferEncoder encoder)
            => data.registry.Upload(data.index, encoder.m_Core.commands);

        internal readonly MeshWorld world;
        internal readonly MeshViewRegistry registry;

        internal MeshGraphExtension(MeshWorld world)
        {
            this.world = world;
            registry = new MeshViewRegistry(world);
        }

        internal static MeshGraphExtension Of(RGBuilder builder)
            => builder.extension as MeshGraphExtension ?? throw new InvalidOperationException("This RGBuilder has no MeshWorld; call SetMeshWorld first.");

        public void Clear() => registry.ReleaseAll();

        public void Contribute(RGBuilder builder)
        {
            registry.ClearConsumers();
            for (int passIndex = 0; passIndex < builder.passCount; ++passIndex)
            {
                if (builder.IsCulled(passIndex))
                {
                    continue;
                }
                List<int> used = builder.GetPass(passIndex).extensionUses;
                for (int i = 0; i < used.Count; ++i)
                {
                    registry.MarkLiveConsumer(used[i], passIndex);
                }
            }
            registry.PrepareLive();
        }

        /// <summary>
        /// Records the list of one view and pass and declares its producer; the view's first nonempty GPU list also declares
        /// the view's producer, so a view with no such list dispatches nothing. A view holds one list per pass. A list of a
        /// remainder view draws what the list of the same pass on its history view left, so that list must exist; on the CPU,
        /// which tests no occluder, the history list drew everything and the remainder is empty.
        /// </summary>
        internal RGMeshDrawListHandle CreateDrawList(RGBuilder builder, in RGMeshViewHandle viewHandle, MeshPassId pass)
        {
            using (MeshCpuMarkers.s_DeclareList.Auto())
            {
                MeshViewRecord view = registry.RequireView(viewHandle);
                MeshPassCache cache = world.GetPassCache(pass);
                if (registry.FindList(viewHandle.index, pass) >= 0)
                {
                    throw new InvalidOperationException($"This mesh view already has a list for '{cache.desc.lightMode}'; a view holds one list per pass.");
                }
                if (cache.desc.requirePreviousTransforms && !view.motion.isValid)
                {
                    throw new InvalidOperationException($"Mesh pass '{cache.desc.lightMode}' requires its view's mesh motion.");
                }
                cache.EnsureShaderBindings();
                EMeshDrawBackendPolicy backend = world.SelectBackend(pass);
                bool gpu = backend == EMeshDrawBackendPolicy.GPUIndirect;
                bool remainder = view.drawnView >= 0;
                if (remainder && registry.FindList(view.drawnView, pass) < 0)
                {
                    throw new InvalidOperationException($"The remainder list of '{cache.desc.lightMode}' needs a list of the same pass on the view whose rest it draws.");
                }
                bool empty = cache.memberCount == 0 || (remainder && !gpu);
                RGMeshDrawListHandle drawsHandle = registry.CreateList(viewHandle.index, pass, cache, backend, empty);
                MeshDrawListRecord record = registry.GetListCopy(drawsHandle.index);
                // Recording references keep the frozen tables alive for this graph.
                var reads = new MeshDrawListReads
                {
                    matricesHandle = builder.ImportBuffer(record.tables.version.matricesHandle),
                    generationsHandle = builder.ImportBuffer(record.tables.version.generationsHandle),
                    layersHandle = builder.ImportBuffer(record.tables.renderingLayersHandle)
                };
                MeshTransformVersion previous = view.motion.previous;
                if (previous != null)
                {
                    reads.previousMatricesHandle = builder.ImportBuffer(previous.matricesHandle);
                    reads.previousGenerationsHandle = builder.ImportBuffer(previous.generationsHandle);
                    reads.hasPrevious = true;
                }
                registry.SetReads(drawsHandle.index, reads);

                if (!empty)
                {
                    record.tables.version.owner.Record(builder, record.tables.version, registry.scheduledVersions);
                    if (gpu)
                    {
                        RGBufferHandle visibilityHandle = view.payload != null
                            ? view.visibilityHandle
                            : RecordViewProducer(builder, viewHandle.index, view, record.tables.entityCount);
                        RecordProducer(builder, drawsHandle.index, visibilityHandle);
                    }
                    else
                    {
                        RecordUpload(builder, drawsHandle.index);
                    }
                }
                return drawsHandle;
            }
        }

        MeshCullInputs CullInputs<TPassData>(RGBuilder builder, RGComputePassHandle<TPassData> passHandle) where TPassData : struct
        {
            MeshWorldGPU gpu = world.gpu;
            return new MeshCullInputs
            {
                boundsCenterHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.boundsCenterHandle)),
                boundsExtentHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.boundsExtentHandle)),
                lodChainsHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.lodChainsHandle)),
                flagsHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.flagsHandle)),
                layerMaskHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.layerMaskHandle)),
                sceneCullingMaskHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.sceneCullingMaskHandle)),
                renderingLayersHandle = passHandle.ReadBuffer(builder.ImportBuffer(gpu.renderingLayersHandle))
            };
        }

        // One compute producer per view with a nonempty GPU list, declared by the first such list. It evaluates eligibility,
        // LOD, frustum and occluder once per entity slot; the view decides the kernel. A remainder view reads the visibility
        // of its history view, whose list of the same pass exists and declared that view's producer first; that view's
        // producer sized SRV_MeshViewDrawn, so the remainder covers exactly its entity slots (P1: one source for the count).
        RGBufferHandle RecordViewProducer(RGBuilder builder, int viewIndex, in MeshViewRecord view, int entityHighWater)
        {
            using (MeshCpuMarkers.s_RecordViewProducer.Auto())
            {
                MeshOccluder occluder = view.occluder;
                bool occluded = occluder.isValid;
                bool history = occluded && occluder.history;
                bool remainder = view.drawnView >= 0;
                int entityCount = math.max(1, entityHighWater);
                RGBufferHandle drawnHandle = default;
                if (remainder)
                {
                    MeshViewRecord drawn = registry.GetViewCopy(view.drawnView);
                    if (!drawn.visibilityHandle.IsValid())
                    {
                        throw new InvalidOperationException("A remainder view reads the visibility of its history view, which has no GPU producer.");
                    }
                    drawnHandle = drawn.visibilityHandle;
                    entityCount = drawn.entityCount;
                }
                // MeshViewVisibility: one uint per entity slot, 0 when culled, else 1 | level << 8 | fade << 16 (the word
                // MeshPackLODVisibility returns in Shaders/Library/MeshLOD.hlsl; fade 65535 is stable).
                RGBufferHandle visibilityHandle = builder.CreateBuffer(new BufferDescriptor(entityCount, sizeof(uint)) { name = "MeshViewVisibility" });
                MeshGPUViewPayload payload = world.gpuBackend.RentViewPayload();
                registry.SetViewProducer(viewIndex, payload, visibilityHandle, entityCount);
                {
                    RGComputePassHandle<MeshViewProducerData> passHandle = builder.DeclareComputePass<MeshViewProducerData>(MeshDrawProfiling.viewProduction);
                    passHandle.KeepImportedWritesLocal();
                    ref MeshViewProducerData data = ref passHandle.GetPassData();
                    data = default;
                    data.registry = registry;
                    data.view = viewIndex;
                    data.inputs = CullInputs(builder, passHandle);
                    passHandle.WriteBuffer(builder.ImportBuffer(payload.cullingContextHandle));
                    data.visibilityHandle = passHandle.WriteBuffer(visibilityHandle);
                    data.occluded = occluded;
                    if (occluded)
                    {
                        data.occluder = new MeshOccluderInputs
                        {
                            furthestHandle = passHandle.ReadTexture(occluder.furthestHandle),
                            viewProjection = occluder.viewProjection,
                            extent = new Vector4(occluder.extent.x, occluder.extent.y, 0, 0),
                            mipCount = occluder.mipCount,
                            history = history
                        };
                        if (history)
                        {
                            MeshTransformVersion current = view.motion.current, previous = view.motion.previous;
                            current.owner.Record(builder, current, registry.scheduledVersions);
                            data.occluder.currentCount = current.count;
                            data.occluder.previousCount = previous.count;
                            data.occluder.currentTransformsHandle = passHandle.ReadBuffer(builder.ImportBuffer(current.matricesHandle));
                            data.occluder.currentGenerationsHandle = passHandle.ReadBuffer(builder.ImportBuffer(current.generationsHandle));
                            data.occluder.previousTransformsHandle = passHandle.ReadBuffer(builder.ImportBuffer(previous.matricesHandle));
                            data.occluder.previousGenerationsHandle = passHandle.ReadBuffer(builder.ImportBuffer(previous.generationsHandle));
                        }
                        else if (remainder)
                        {
                            data.drawnHandle = passHandle.ReadBuffer(drawnHandle);
                            data.remainder = true;
                        }
                    }
                    passHandle.SetExecuteFunc(s_ProduceView);
                }
                return visibilityHandle;
            }
        }

        // One compute producer per nonempty GPU list. The pass is declared, not appended: compile places it after its inputs
        // (its view's producer among them) and before its consumer's native group. Every list of a view reads the same
        // visibility; the pass only selects which level of each element draws and whether transitions dither.
        void RecordProducer(RGBuilder builder, int drawIndex, RGBufferHandle visibilityHandle)
        {
            using (MeshCpuMarkers.s_RecordProducer.Auto())
            {
                MeshDrawListRecord record = registry.GetListCopy(drawIndex);
                MeshGPUDrawPayload payload = record.payload;
                RGBufferHandle instancesHandle = builder.ImportBuffer(payload.instanceToEntityHandle);
                RGBufferHandle fadesHandle = builder.ImportBuffer(payload.instanceFadeHandle);
                RGBufferHandle argsHandle = builder.ImportBuffer(payload.indirectArgsHandle);
                {
                    RGComputePassHandle<MeshDrawProducerData> passHandle = builder.DeclareComputePass<MeshDrawProducerData>(MeshDrawProfiling.production);
                    passHandle.KeepImportedWritesLocal();
                    ref MeshDrawProducerData data = ref passHandle.GetPassData();
                    data = default;
                    data.registry = registry;
                    data.index = drawIndex;
                    data.visibilityHandle = passHandle.ReadBuffer(visibilityHandle);
                    passHandle.ReadBuffer(builder.ImportBuffer(record.cache.candidateBufferHandle));
                    passHandle.ReadBuffer(builder.ImportBuffer(record.cache.commandMetaBufferHandle));
                    passHandle.WriteBuffer(instancesHandle);
                    passHandle.WriteBuffer(fadesHandle);
                    passHandle.WriteBuffer(argsHandle);
                    passHandle.SetExecuteFunc(s_Produce);
                }
                RGBufferHandle[] outputsHandle = builder.GetRecordingArray<RGBufferHandle>(3);
                outputsHandle[0] = instancesHandle;
                outputsHandle[1] = fadesHandle;
                outputsHandle[2] = argsHandle;
                registry.SetProducerOutputs(drawIndex, outputsHandle);
            }
        }

        // One upload producer per nonempty CPU list; its build job is scheduled at compile when the list is live.
        void RecordUpload(RGBuilder builder, int drawIndex)
        {
            using (MeshCpuMarkers.s_RecordProducer.Auto())
            {
                MeshDrawListRecord record = registry.GetListCopy(drawIndex);
                RGBufferHandle instancesHandle = builder.ImportBuffer(record.cpuInstancesHandle);
                RGBufferHandle fadesHandle = builder.ImportBuffer(record.cpuFadesHandle);
                {
                    RGTransferPassHandle<MeshDrawUploadData> passHandle = builder.DeclareTransferPass<MeshDrawUploadData>(MeshDrawProfiling.upload);
                    passHandle.KeepImportedWritesLocal();
                    passHandle.WriteBuffer(instancesHandle);
                    passHandle.WriteBuffer(fadesHandle);
                    ref MeshDrawUploadData data = ref passHandle.GetPassData();
                    data.registry = registry;
                    data.index = drawIndex;
                    passHandle.SetExecuteFunc(s_Upload);
                }
                RGBufferHandle[] outputsHandle = builder.GetRecordingArray<RGBufferHandle>(2);
                outputsHandle[0] = instancesHandle;
                outputsHandle[1] = fadesHandle;
                registry.SetProducerOutputs(drawIndex, outputsHandle);
            }
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal void UseDrawList(IRGPass pass, in RGMeshDrawListHandle drawsHandle)
        {
            pass.extensionUses.Add(drawsHandle.index);
            MeshDrawListReads reads = registry.GetListCopy(drawsHandle.index).reads;
            pass.AddResourceRead(reads.matricesHandle.handle);
            pass.AddResourceRead(reads.generationsHandle.handle);
            pass.AddResourceRead(reads.layersHandle.handle);
            if (reads.hasPrevious)
            {
                pass.AddResourceRead(reads.previousMatricesHandle.handle);
                pass.AddResourceRead(reads.previousGenerationsHandle.handle);
            }
            // An empty list has no producer.
            if (reads.producerOutputsHandle != null)
            {
                for (int i = 0; i < reads.producerOutputsHandle.Length; ++i)
                    pass.AddResourceRead(reads.producerOutputsHandle[i].handle);
            }
        }
    }

    /// <summary>
    /// Pass-facing mesh recording API, after Unity's <c>Cull</c> → <c>CullingResults</c> → <c>CreateRendererList</c> chain (R11):
    /// <see cref="CullMeshView"/> records a view, <see cref="CreateMeshDrawList"/> records the list of one view and pass and
    /// declares its producer (and, for the view's first nonempty GPU list, the view's producer), which compile places. Submitting a list records its draw calls into the raster encoder. Native
    /// command-buffer submission keeps the words native submit and submission segment.
    /// </summary>
    public static class MeshGraphRecording
    {
        /// <summary>Binds the builder to its mesh world once; the extension and its view registry live as long as the builder.</summary>
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

        /// <summary>Records a view of this graph. A history occluder needs the view's accepted transform version.</summary>
        internal static RGMeshViewHandle CullMeshView(this RGBuilder builder, in MeshViewDesc desc)
            => MeshGraphExtension.Of(builder).registry.CullView(desc);

        /// <summary>
        /// Records the view that draws what the lists of <paramref name="drawnViewHandle"/>, a view culled against the previous
        /// frame's pyramid, left visible under the newer <paramref name="occluder"/>. Anything else throws.
        /// </summary>
        internal static RGMeshViewHandle CullMeshViewRemainder(this RGBuilder builder, in RGMeshViewHandle drawnViewHandle, in MeshOccluder occluder)
            => MeshGraphExtension.Of(builder).registry.CullRemainder(drawnViewHandle, occluder);

        /// <summary>
        /// Records the list of one view and pass and declares its producer; the producer is not inserted here. A second list
        /// of the same view and pass throws; the same view used for lists of two passes means the same thing in both.
        /// </summary>
        internal static RGMeshDrawListHandle CreateMeshDrawList(this RGBuilder builder, in RGMeshViewHandle viewHandle, MeshPassId pass)
            => MeshGraphExtension.Of(builder).CreateDrawList(builder, viewHandle, pass);

        /// <summary>Declares the pass as a consumer. A handle from another builder or an earlier graph fails here.</summary>
        public static RGMeshDrawListHandle UseMeshDrawList<TPassData>(this RGRasterPassHandle<TPassData> passHandle, in RGMeshDrawListHandle drawsHandle) where TPassData : struct
        {
            if (!(passHandle.extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
            if (!owner.registry.IsLive(drawsHandle))
            {
                throw new InvalidOperationException("The draw list belongs to another builder or an earlier graph.");
            }
            owner.UseDrawList(passHandle.pass, drawsHandle);
            return drawsHandle;
        }

        public static void SubmitMeshDrawList(this in RGRasterEncoder encoder, in RGMeshDrawListHandle drawsHandle)
        {
            if (!(encoder.m_Extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
            owner.registry.SubmitMeshDrawList(encoder.m_Core.commands, drawsHandle);
        }
    }
}
