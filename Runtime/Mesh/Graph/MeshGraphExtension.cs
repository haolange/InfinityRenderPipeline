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
        internal readonly RGTextureHandle furthest;
        internal readonly Matrix4x4 viewProjection;
        internal readonly int2 extent;
        internal readonly int mipCount;
        internal readonly bool history;

        internal MeshOccluder(RGTextureHandle furthest, Matrix4x4 viewProjection, int2 extent, int mipCount, bool history)
        {
            this.furthest = furthest;
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
            public RGBufferHandle drawn;
            public bool readDrawn;
        }

        struct MeshDrawUploadData
        {
            public MeshDrawListRegistry lists;
            public int index;
        }

        struct BufferPair
        {
            public RGBufferHandle matrices, generations, layers, previousMatrices, previousGenerations;
            public bool hasPrevious;
        }

        struct OccluderEntry
        {
            public MeshCullingContext context;
            public MeshOccluder occluder;
        }

        // The draws of one culling context and pass so far. A list drawn with an occluder records which candidates it drew
        // (tracked); a later list of the same context and pass draws only the rest. Without an occluder a list draws every
        // visible candidate, so nothing is left for a later one.
        struct RepeatEntry
        {
            public MeshCullingContext context;
            public ShaderTagId passTag;
            public RGBufferHandle drawn;
            public bool tracked;
        }

        static readonly RGComputePassExecuteAction<MeshDrawProducerData> s_Produce = static (in MeshDrawProducerData data, in RGComputeEncoder encoder)
            => data.lists.Produce(data.index, encoder, data.inputs, data.occluded, data.occluder, data.drawn, data.readDrawn);
        static readonly RGTransferPassExecuteAction<MeshDrawUploadData> s_Upload = static (in MeshDrawUploadData data, in RGTransferEncoder encoder)
            => data.lists.Upload(data.index, encoder.m_Core.commands);

        internal readonly MeshWorld world;
        internal readonly MeshDrawListRegistry drawLists;
        readonly Dictionary<int, RGBufferHandle[]> m_ProducerOutputs = new Dictionary<int, RGBufferHandle[]>();
        readonly Dictionary<int, BufferPair> m_TableReads = new Dictionary<int, BufferPair>();
        readonly List<OccluderEntry> m_Occluders = new List<OccluderEntry>(2);
        readonly List<RepeatEntry> m_Repeats = new List<RepeatEntry>(16);
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
            m_ProducerOutputs.Clear();
            m_TableReads.Clear();
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
            bool empty = cache.memberCount == 0 || (repeat >= 0 && !m_Repeats[repeat].tracked);
            RGMeshDrawListHandle draws = drawLists.Create(context, cache, backend, m_Motion, empty);
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

            RGBufferHandle drawn = default;
            if (!empty)
            {
                record.tables.version.owner.Record(builder, record.tables.version, m_ScheduledVersions);
                if (gpu)
                {
                    drawn = RecordProducer(builder, draws.index, occluded, occluder, repeat);
                }
                else
                {
                    RecordUpload(builder, draws.index);
                }
            }
            if (repeat < 0)
            {
                m_Repeats.Add(new RepeatEntry { context = context, passTag = desc.passTag, drawn = drawn, tracked = !empty && occluded });
            }
            return draws;
        }

        MeshCullInputs CullInputs<TPassData>(RGBuilder builder, RGComputePassHandle<TPassData> pass) where TPassData : struct
        {
            MeshWorldGPU gpu = world.gpu;
            return new MeshCullInputs
            {
                boundsCenter = pass.ReadBuffer(builder.ImportBuffer(gpu.boundsCenter)),
                boundsExtent = pass.ReadBuffer(builder.ImportBuffer(gpu.boundsExtent)),
                lodChains = pass.ReadBuffer(builder.ImportBuffer(gpu.lodChains)),
                flags = pass.ReadBuffer(builder.ImportBuffer(gpu.flags)),
                layerMask = pass.ReadBuffer(builder.ImportBuffer(gpu.layerMask)),
                sceneCullingMask = pass.ReadBuffer(builder.ImportBuffer(gpu.sceneCullingMask)),
                renderingLayers = pass.ReadBuffer(builder.ImportBuffer(gpu.renderingLayers))
            };
        }

        // One compute producer per nonempty GPU list, recorded where the list is created. Returns the drawn table it writes
        // when occluded.
        RGBufferHandle RecordProducer(RGBuilder builder, int drawIndex, bool occluded, in MeshOccluder occluder, int repeat)
        {
            MeshDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
            MeshGPUDrawPayload payload = record.payload;
            RGBufferHandle instances = builder.ImportBuffer(payload.instanceToEntity);
            RGBufferHandle fades = builder.ImportBuffer(payload.instanceFade);
            RGBufferHandle args = builder.ImportBuffer(payload.indirectArgs);
            RGBufferHandle drawn = default;
            bool readDrawn = false;
            if (occluded)
            {
                readDrawn = repeat >= 0;
                drawn = readDrawn ? m_Repeats[repeat].drawn
                    : builder.CreateBuffer(new BufferDescriptor(math.max(1, record.candidateCount), sizeof(uint)) { name = "MeshDrawnCandidates" });
            }
            using (var pass = builder.AddComputePass<MeshDrawProducerData>(MeshDrawProfiling.production))
            {
                pass.KeepImportedWritesLocal();
                ref MeshDrawProducerData data = ref pass.GetPassData();
                data = default;
                data.lists = drawLists;
                data.index = drawIndex;
                data.inputs = CullInputs(builder, pass);
                pass.ReadBuffer(builder.ImportBuffer(record.cache.candidateBuffer));
                pass.WriteBuffer(builder.ImportBuffer(payload.commandMeta));
                pass.WriteBuffer(builder.ImportBuffer(payload.cullingContext));
                pass.WriteBuffer(instances);
                pass.WriteBuffer(fades);
                pass.WriteBuffer(args);
                data.occluded = occluded;
                if (occluded)
                {
                    data.occluder = new MeshOccluderInputs
                    {
                        furthest = pass.ReadTexture(occluder.furthest),
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
                        data.occluder.currentTransforms = pass.ReadBuffer(builder.ImportBuffer(current.matrices));
                        data.occluder.currentGenerations = pass.ReadBuffer(builder.ImportBuffer(current.generations));
                        data.occluder.previousTransforms = pass.ReadBuffer(builder.ImportBuffer(previous.matrices));
                        data.occluder.previousGenerations = pass.ReadBuffer(builder.ImportBuffer(previous.generations));
                    }
                    if (readDrawn)
                    {
                        pass.ReadBuffer(drawn);
                    }
                    data.drawn = pass.WriteBuffer(drawn);
                    data.readDrawn = readDrawn;
                }
                pass.SetExecuteFunc(s_Produce);
            }
            RGBufferHandle[] outputs = builder.GetRecordingArray<RGBufferHandle>(3);
            outputs[0] = instances;
            outputs[1] = fades;
            outputs[2] = args;
            m_ProducerOutputs[drawIndex] = outputs;
            return drawn;
        }

        // One upload producer per nonempty CPU list; its build job is scheduled at compile when the list is live.
        void RecordUpload(RGBuilder builder, int drawIndex)
        {
            MeshDrawListRecord record = drawLists.GetRecordCopy(drawIndex);
            RGBufferHandle instances = builder.ImportBuffer(record.cpuInstances);
            RGBufferHandle fades = builder.ImportBuffer(record.cpuFades);
            using (var upload = builder.AddTransferPass<MeshDrawUploadData>(MeshDrawProfiling.upload))
            {
                upload.KeepImportedWritesLocal();
                upload.WriteBuffer(instances);
                upload.WriteBuffer(fades);
                ref MeshDrawUploadData data = ref upload.GetPassData();
                data.lists = drawLists;
                data.index = drawIndex;
                upload.SetExecuteFunc(s_Upload);
            }
            RGBufferHandle[] outputs = builder.GetRecordingArray<RGBufferHandle>(2);
            outputs[0] = instances;
            outputs[1] = fades;
            m_ProducerOutputs[drawIndex] = outputs;
        }

        // The pass records the draw-list index; compile reads it back to find live consumers.
        internal void UseDrawList(IRGPass pass, in RGMeshDrawListHandle draws)
        {
            pass.extensionUses.Add(draws.index);
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
            if (m_ProducerOutputs.TryGetValue(draws.index, out RGBufferHandle[] outputs))
            {
                for (int i = 0; i < outputs.Length; ++i)
                    pass.AddResourceRead(outputs[i].handle);
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
        /// Records the list's producer here, before its raster consumer. A later list of the same context and pass draws
        /// only what earlier ones left: with an occluder that is what the occluder hid then, without one nothing.
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
        public static RGMeshDrawListHandle UseMeshDrawList<TPassData>(this RGRasterPassHandle<TPassData> pass, in RGMeshDrawListHandle draws) where TPassData : struct
        {
            if (!(pass.extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
            if (!owner.drawLists.IsLive(draws))
            {
                throw new InvalidOperationException("The draw list belongs to another builder or an earlier graph.");
            }
            owner.UseDrawList(pass.pass, draws);
            return draws;
        }

        public static void SubmitMeshDrawList(this in RGRasterEncoder encoder, in RGMeshDrawListHandle draws)
        {
            if (!(encoder.m_Extension is MeshGraphExtension owner))
            {
                throw new InvalidOperationException("This pass's builder has no MeshWorld.");
            }
            owner.drawLists.SubmitMeshDrawList(encoder.m_Core.commands, draws);
        }
    }
}
