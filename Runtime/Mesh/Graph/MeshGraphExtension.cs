using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// A Hi-Z occluder for one culling context: the furthest-depth pyramid (mip 0 is half the full extent), the
    /// view-projection it was built with and the full-resolution extent. A history occluder is the previous frame's pyramid
    /// and proves occlusion only for entities whose transform did not change since.
    /// </summary>
    internal readonly struct MeshOccluder
    {
        internal readonly RGTextureHandle furthestHandle;
        internal readonly Matrix4x4 viewProjection;
        internal readonly int2 extent;
        internal readonly int mipCount;
        internal readonly bool history;

        internal MeshOccluder(RGTextureHandle furthestHandle, Matrix4x4 viewProjection, int2 extent, int mipCount, bool history)
        {
            this.furthestHandle = furthestHandle;
            this.viewProjection = viewProjection;
            this.extent = extent;
            this.mipCount = mipCount;
            this.history = history;
        }
    }

    /// <summary>
    /// Per-builder mesh state. The render graph knows this object only as an <see cref="IRGGraphExtension"/>:
    /// it calls Contribute once per compile and Clear whenever the recorded graph is dropped.
    /// </summary>
    internal sealed class MeshGraphExtension : IRGGraphExtension
    {
        struct MeshDrawProducerData
        {
            public MeshDrawListRegistry lists;
            public int index;
            public MeshCullInputs inputs;
            public bool occluded;
            public MeshOccluderInputs occluder;
            public RGBufferHandle drawnHandle;
            public bool remainder;
        }

        struct MeshDrawUploadData
        {
            public MeshDrawListRegistry lists;
            public int index;
        }

        struct OccluderEntry
        {
            public MeshCullingContext context;
            public MeshOccluder occluder;
        }

        // The draws of one culling context and pass so far. Only a list tested against the previous frame's pyramid can leave
        // something visible undrawn: it records which candidates it drew (tracked), and the next list of the context and pass,
        // tested against a newer occluder, draws the rest. A list tested against this frame's depth, or against nothing, leaves
        // nothing that a newer occluder could reveal, so every later list of the context and pass is empty.
        struct RepeatEntry
        {
            public MeshCullingContext context;
            public ShaderTagId passTag;
            public RGBufferHandle drawnHandle;
            public bool tracked;
        }

        static readonly RGComputePassExecuteAction<MeshDrawProducerData> s_Produce = static (in MeshDrawProducerData data, in RGComputeEncoder encoder)
            => data.lists.Produce(data.index, encoder, data.inputs, data.occluded, data.occluder, data.drawnHandle, data.remainder);
        static readonly RGTransferPassExecuteAction<MeshDrawUploadData> s_Upload = static (in MeshDrawUploadData data, in RGTransferEncoder encoder)
            => data.lists.Upload(data.index, encoder.m_Core.commands);

        internal readonly MeshWorld world;
        internal readonly MeshDrawListRegistry drawLists;
        readonly List<OccluderEntry> m_Occluders = new List<OccluderEntry>(2);
        // One entry per culling context and pass of the camera; sized like the registry so recording does not grow it.
        readonly List<RepeatEntry> m_Repeats = new List<RepeatEntry>(64);
        readonly HashSet<MeshTransformVersion> m_ScheduledVersions = new HashSet<MeshTransformVersion>();
        MeshMotionBinding m_Motion;

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
            m_Occluders.Clear();
            m_Repeats.Clear();
            m_Motion = default;
            drawLists.ReleaseAll();
        }

        public void Contribute(RGBuilder builder)
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
            drawLists.PrepareLive();
        }

        /// <summary>The camera's current and accepted transform versions, set once per graph before its first draw list.</summary>
        internal void SetMotion(in MeshMotionBinding motion)
        {
            if (!motion.isValid)
            {
                throw new ArgumentException("Mesh motion needs the current transform version.", nameof(motion));
            }
            m_Motion = motion;
        }

        /// <summary>Later draw lists of this context test against the occluder; the latest registration wins.</summary>
        internal void RegisterOccluder(in MeshCullingContext context, in MeshOccluder occluder)
        {
            if (occluder.history && m_Motion.previous == null)
            {
                throw new InvalidOperationException("A history occluder needs the camera's accepted transform version.");
            }
            m_Occluders.Add(new OccluderEntry { context = context, occluder = occluder });
        }

        bool TryOccluder(in MeshCullingContext context, out MeshOccluder occluder)
        {
            for (int i = m_Occluders.Count - 1; i >= 0; --i)
            {
                if (m_Occluders[i].context.Equals(context))
                {
                    occluder = m_Occluders[i].occluder;
                    return true;
                }
            }
            occluder = default;
            return false;
        }

        int FindRepeat(in MeshCullingContext context, ShaderTagId passTag)
        {
            for (int i = 0; i < m_Repeats.Count; ++i)
            {
                if (m_Repeats[i].passTag == passTag && m_Repeats[i].context.Equals(context))
                {
                    return i;
                }
            }
            return -1;
        }

        internal RGMeshDrawListHandle Create(RGBuilder builder, in MeshCullingContext context, in MeshPassDesc desc)
        {
            MeshPassCache cache = world.GetPassCache(desc);
            cache.EnsureShaderBindings();
            if (desc.requirePreviousTransforms && !m_Motion.isValid)
            {
                throw new InvalidOperationException($"Mesh pass '{desc.lightMode}' requires the camera's mesh motion (SetMeshMotion).");
            }
            EMeshDrawBackendPolicy backend = world.SelectBackend(desc);
            bool gpu = backend == EMeshDrawBackendPolicy.GPUIndirect;
            int repeat = FindRepeat(context, desc.passTag);
            MeshOccluder occluder = default;
            bool occluded = gpu && TryOccluder(context, out occluder);
            bool remainder = repeat >= 0 && m_Repeats[repeat].tracked;
            if (remainder && (!occluded || occluder.history))
            {
                throw new InvalidOperationException($"The remainder of '{desc.lightMode}' needs a newer occluder than the previous frame's pyramid.");
            }
            bool empty = cache.memberCount == 0 || (repeat >= 0 && !remainder);
            RGMeshDrawListHandle drawsHandle = drawLists.Create(context, cache, backend, m_Motion, empty);
            MeshDrawListRecord record = drawLists.GetRecordCopy(drawsHandle.index);
            // Recording references keep the frozen tables alive for this graph.
            var reads = new MeshDrawListReads
            {
                matricesHandle = builder.ImportBuffer(record.tables.version.matricesHandle),
                generationsHandle = builder.ImportBuffer(record.tables.version.generationsHandle),
                layersHandle = builder.ImportBuffer(record.tables.renderingLayersHandle)
            };
            MeshTransformVersion previous = record.motion.previous;
            if (previous != null)
            {
                reads.previousMatricesHandle = builder.ImportBuffer(previous.matricesHandle);
                reads.previousGenerationsHandle = builder.ImportBuffer(previous.generationsHandle);
                reads.hasPrevious = true;
            }
            drawLists.SetReads(drawsHandle.index, reads);

            RGBufferHandle drawnHandle = default;
            if (!empty)
            {
                record.tables.version.owner.Record(builder, record.tables.version, m_ScheduledVersions);
                if (gpu)
                {
                    drawnHandle = RecordProducer(builder, drawsHandle.index, occluded, occluder, remainder ? m_Repeats[repeat].drawnHandle : default, remainder);
                }
                else
                {
                    RecordUpload(builder, drawsHandle.index);
                }
            }
            if (repeat < 0)
            {
                m_Repeats.Add(new RepeatEntry { context = context, passTag = desc.passTag, drawnHandle = drawnHandle, tracked = !empty && occluded && occluder.history });
            }
            else if (remainder)
            {
                RepeatEntry entry = m_Repeats[repeat];
                entry.tracked = false;
                m_Repeats[repeat] = entry;
            }
            return drawsHandle;
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

        // One compute producer per nonempty GPU list, recorded where the list is created. A history-occluded list returns the
        // drawn table it writes; a remainder list reads the one it is given.
        RGBufferHandle RecordProducer(RGBuilder builder, int drawIndex, bool occluded, in MeshOccluder occluder, RGBufferHandle remainderDrawnHandle, bool remainder)
        {
            MeshDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
            MeshGPUDrawPayload payload = record.payload;
            RGBufferHandle instancesHandle = builder.ImportBuffer(payload.instanceToEntityHandle);
            RGBufferHandle fadesHandle = builder.ImportBuffer(payload.instanceFadeHandle);
            RGBufferHandle argsHandle = builder.ImportBuffer(payload.indirectArgsHandle);
            RGBufferHandle writtenHandle = occluded && occluder.history
                ? builder.CreateBuffer(new BufferDescriptor(math.max(1, record.candidateCount), sizeof(uint)) { name = "MeshDrawnCandidates" })
                : default;
            {
                RGComputePassHandle<MeshDrawProducerData> passHandle = builder.AddComputePass<MeshDrawProducerData>(MeshDrawProfiling.production);
                passHandle.KeepImportedWritesLocal();
                ref MeshDrawProducerData data = ref passHandle.GetPassData();
                data = default;
                data.lists = drawLists;
                data.index = drawIndex;
                data.inputs = CullInputs(builder, passHandle);
                passHandle.ReadBuffer(builder.ImportBuffer(record.cache.candidateBufferHandle));
                passHandle.ReadBuffer(builder.ImportBuffer(record.cache.commandMetaBufferHandle));
                passHandle.WriteBuffer(builder.ImportBuffer(payload.cullingContextHandle));
                passHandle.WriteBuffer(instancesHandle);
                passHandle.WriteBuffer(fadesHandle);
                passHandle.WriteBuffer(argsHandle);
                data.occluded = occluded;
                if (occluded)
                {
                    data.occluder = new MeshOccluderInputs
                    {
                        furthestHandle = passHandle.ReadTexture(occluder.furthestHandle),
                        viewProjection = occluder.viewProjection,
                        extent = new Vector4(occluder.extent.x, occluder.extent.y, 0, 0),
                        mipCount = occluder.mipCount,
                        history = occluder.history
                    };
                    if (occluder.history)
                    {
                        MeshTransformVersion current = m_Motion.current, previous = m_Motion.previous;
                        current.owner.Record(builder, current, m_ScheduledVersions);
                        data.occluder.currentCount = current.count;
                        data.occluder.previousCount = previous.count;
                        data.occluder.currentTransformsHandle = passHandle.ReadBuffer(builder.ImportBuffer(current.matricesHandle));
                        data.occluder.currentGenerationsHandle = passHandle.ReadBuffer(builder.ImportBuffer(current.generationsHandle));
                        data.occluder.previousTransformsHandle = passHandle.ReadBuffer(builder.ImportBuffer(previous.matricesHandle));
                        data.occluder.previousGenerationsHandle = passHandle.ReadBuffer(builder.ImportBuffer(previous.generationsHandle));
                    }
                    if (occluder.history)
                    {
                        data.drawnHandle = passHandle.WriteBuffer(writtenHandle);
                    }
                    else if (remainder)
                    {
                        data.drawnHandle = passHandle.ReadBuffer(remainderDrawnHandle);
                        data.remainder = true;
                    }
                }
                passHandle.SetExecuteFunc(s_Produce);
            }
            RGBufferHandle[] outputsHandle = builder.GetRecordingArray<RGBufferHandle>(3);
            outputsHandle[0] = instancesHandle;
            outputsHandle[1] = fadesHandle;
            outputsHandle[2] = argsHandle;
            drawLists.SetProducerOutputs(drawIndex, outputsHandle);
            return writtenHandle;
        }

        // One upload producer per nonempty CPU list; its build job is scheduled at compile when the list is live.
        void RecordUpload(RGBuilder builder, int drawIndex)
        {
            MeshDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
            RGBufferHandle instancesHandle = builder.ImportBuffer(record.cpuInstancesHandle);
            RGBufferHandle fadesHandle = builder.ImportBuffer(record.cpuFadesHandle);
            {
                RGTransferPassHandle<MeshDrawUploadData> passHandle = builder.AddTransferPass<MeshDrawUploadData>(MeshDrawProfiling.upload);
                passHandle.KeepImportedWritesLocal();
                passHandle.WriteBuffer(instancesHandle);
                passHandle.WriteBuffer(fadesHandle);
                ref MeshDrawUploadData data = ref passHandle.GetPassData();
                data.lists = drawLists;
                data.index = drawIndex;
                passHandle.SetExecuteFunc(s_Upload);
            }
            RGBufferHandle[] outputsHandle = builder.GetRecordingArray<RGBufferHandle>(2);
            outputsHandle[0] = instancesHandle;
            outputsHandle[1] = fadesHandle;
            drawLists.SetProducerOutputs(drawIndex, outputsHandle);
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal void UseDrawList(IRGPass pass, in RGMeshDrawListHandle drawsHandle)
        {
            pass.extensionUses.Add(drawsHandle.index);
            MeshDrawListReads reads = drawLists.GetRecordCopy(drawsHandle.index).reads;
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
    /// One culling context and one mesh pass, the mesh counterpart of <c>RendererListDesc</c>. The constructor only stores
    /// the two fields; <see cref="MeshGraphRecording.CreateMeshDrawList"/> checks that the tag is one of the nine mesh passes.
    /// </summary>
    public readonly struct MeshDrawListDesc
    {
        public readonly MeshCullingContext cullingContext;
        public readonly ShaderTagId passTag;

        public MeshDrawListDesc(in MeshCullingContext cullingContext, ShaderTagId passTag)
        {
            this.cullingContext = cullingContext;
            this.passTag = passTag;
        }
    }

    /// <summary>
    /// Pass-facing mesh recording API. Creating a draw list records its producer at that point of the graph; submitting it
    /// records its draw calls into the raster encoder. Native command-buffer submission keeps the words native submit and
    /// submission segment.
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

        /// <summary>
        /// Records the list's producer here, before its raster consumer. A later list of the same context and pass draws only
        /// what earlier ones left: after a list tested against the previous frame's pyramid, what a newer occluder no longer
        /// hides; otherwise nothing.
        /// </summary>
        public static RGMeshDrawListHandle CreateMeshDrawList(this RGBuilder builder, in MeshDrawListDesc desc)
        {
            if (!MeshPassTable.TryGet(desc.passTag, out MeshPassDesc pass))
                throw new ArgumentException($"'{desc.passTag.name}' is not a mesh pass.", nameof(desc));
            return MeshGraphExtension.Of(builder).Create(builder, desc.cullingContext, pass);
        }

        internal static void SetMeshMotion(this RGBuilder builder, in MeshMotionBinding motion)
            => MeshGraphExtension.Of(builder).SetMotion(motion);

        internal static void RegisterMeshOccluder(this RGBuilder builder, in MeshCullingContext context, in MeshOccluder occluder)
            => MeshGraphExtension.Of(builder).RegisterOccluder(context, occluder);

        /// <summary>Declares the pass as a consumer. A handle from another builder or an earlier graph fails here.</summary>
        public static RGMeshDrawListHandle UseMeshDrawList<TPassData>(this RGRasterPassHandle<TPassData> passHandle, in RGMeshDrawListHandle drawsHandle) where TPassData : struct
        {
            if (!(passHandle.extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
            if (!owner.drawLists.IsLive(drawsHandle))
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
            owner.drawLists.SubmitMeshDrawList(encoder.m_Core.commands, drawsHandle);
        }
    }
}
