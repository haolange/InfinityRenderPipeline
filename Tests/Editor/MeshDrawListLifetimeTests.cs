using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.RenderGraph.Tests
{
    public class MeshDrawListLifetimeTests
    {
        struct EmptyRasterData { }
        struct DrawPassData { public RGMeshDrawListHandle meshDrawListHandle; }

        static void Compile(RGBuilder graph)
            => typeof(RGBuilder).GetMethod("CompilePass", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(graph, null);

        static int IndexOfPass(RGBuilder graph, string name)
        {
            for (int i = 0; i < graph.passCount; ++i)
            {
                if (graph.GetPass(i).name == name)
                {
                    return i;
                }
            }
            Assert.Fail("Missing pass " + name);
            return -1;
        }

        static int CountPasses(RGBuilder graph, string samplerName, bool liveOnly)
        {
            int count = 0;
            for (int i = 0; i < graph.passCount; ++i)
            {
                ProfilingSampler sampler = graph.GetPass(i).customSampler;
                if (sampler == null || sampler.name != samplerName) continue;
                if (!liveOnly || !graph.IsCulled(i)) count++;
            }
            return count;
        }

        // The compiled passes of one sampler as (reads, writes) columns of DescribeCompiledGraph.
        static System.Collections.Generic.List<(string reads, string writes)> CompiledPasses(RGBuilder graph, string samplerName)
        {
            var passes = new System.Collections.Generic.List<(string, string)>();
            string[] lines = graph.DescribeCompiledGraph().Split('\n');
            for (int i = 1; i < lines.Length; ++i)
            {
                string[] fields = lines[i].Split('|');
                if (fields.Length > 8 && fields[1] == samplerName) passes.Add((fields[7], fields[8]));
            }
            return passes;
        }

        // List producers and view producers.
        static System.Collections.Generic.List<(string reads, string writes)> CompiledProducers(RGBuilder graph) => CompiledPasses(graph, "ComputeMeshDrawList");
        static System.Collections.Generic.List<(string reads, string writes)> CompiledViewProducers(RGBuilder graph) => CompiledPasses(graph, "ComputeMeshView");

        // The compiled position of the first pass of one sampler whose (reads, writes) columns match, or -1.
        static int CompiledIndexOf(RGBuilder graph, string samplerName, System.Predicate<(string reads, string writes)> match)
        {
            string[] lines = graph.DescribeCompiledGraph().Split('\n');
            for (int i = 1; i < lines.Length; ++i)
            {
                string[] fields = lines[i].Split('|');
                if (fields.Length > 8 && fields[1] == samplerName && match((fields[7], fields[8]))) return i - 1;
            }
            return -1;
        }

        static RGBufferHandle Visibility(RGBuilder graph, in RGMeshViewHandle viewHandle) => Registry(graph).GetViewCopy(viewHandle.index).visibilityHandle;

        static string Column(RGTextureHandle textureHandle) => "Texture:" + textureHandle.handle.index + ",";
        static string Column(RGBufferHandle bufferHandle) => "Buffer:" + bufferHandle.handle.index + ",";

        static RGTextureHandle Target(RGBuilder graph)
            => graph.CreateTexture(new TextureDescriptor(1, 1) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, dimension = TextureDimension.Tex2D });

        // An occluder pyramid stand-in that a compute pass writes. A raster writer would join the consumer's native group,
        // and the producer would then read an attachment written inside that group.
        static RGTextureHandle WrittenTarget(RGBuilder graph)
        {
            RGTextureHandle textureHandle = Target(graph);
            {
                RGComputePassHandle<EmptyRasterData> passHandle = graph.AddComputePass<EmptyRasterData>(new ProfilingSampler("WriteOccluder"));
                passHandle.WriteTexture(textureHandle);
                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in EmptyRasterData data, in RGComputeEncoder encoder) => { });
            }
            return textureHandle;
        }

        static MeshOccluder Occluder(RGTextureHandle furthestHandle, bool history)
            => new MeshOccluder(furthestHandle, Matrix4x4.identity, new int2(2, 2), 1, history);

        static void AddConsumer(RGBuilder graph, RGTextureHandle targetHandle, CustomSamplerId sampler, RGMeshDrawListHandle drawsHandle, bool keep)
        {
            {
                RGRasterPassHandle<DrawPassData> passHandle = graph.AddRasterPass<DrawPassData>(ProfilingSampler.Get(sampler));
                passHandle.SetColorAttachment(targetHandle, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                if (keep) passHandle.EnablePassCulling(false);
                ref DrawPassData data = ref passHandle.GetPassData();
                data.meshDrawListHandle = passHandle.UseMeshDrawList(drawsHandle);
                passHandle.SetExecuteFunc((in DrawPassData passData, in RGRasterEncoder encoder) => encoder.SubmitMeshDrawList(passData.meshDrawListHandle));
            }
        }

        static MeshEntityId AddMember(MeshWorldFixture fixture)
        {
            Mesh mesh = fixture.Mesh(1);
            Material material = fixture.Material(0, 0);
            MeshEntityId entity;
            using (MeshWorldUpdate update = fixture.world.BeginUpdate())
            {
                entity = MeshTestUtility.CreateEntity(update, float3.zero, EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow);
                update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                update.Commit();
            }
            return entity;
        }

        static RGBuilder CreateGraph(MeshWorldFixture fixture, string name, EMeshDrawBackendPolicy backend)
        {
            if (backend == EMeshDrawBackendPolicy.GPUIndirect) MeshTestUtility.RequireGPUIndirect(fixture.world);
            fixture.world.requestedBackend = backend;
            fixture.world.UpdateResidency();
            var graph = new RGBuilder(name, fixture.pool);
            graph.SetMeshWorld(fixture.world);
            return graph;
        }

        static MeshViewRegistry Registry(RGBuilder graph) => MeshGraphExtension.Of(graph).registry;

        // A frustum view without motion.
        static RGMeshViewHandle View(RGBuilder graph, in MeshCullingContext context)
            => graph.CullMeshView(new MeshViewDesc(context, default, MeshOccluder.None));

        // A light-like context that filters rendering layers; index moves its far plane so contexts stay distinct.
        static MeshCullingContext LightContext(int index)
        {
            Plane[] planes = MeshTestUtility.BoxPlanes();
            planes[5] = new Plane(Vector3.back, 10f + index);
            return MeshCullingContext.Create(planes, ~0, ulong.MaxValue, (uint)ERenderingLayer.Everything, true, MeshTestUtility.Lod);
        }

        [Test]
        public void ViewsAndLists_BelongToTheirBuilder()
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder owner = CreateGraph(fixture, "DrawListOwner", EMeshDrawBackendPolicy.CPUDirect);
                var foreign = new RGBuilder("DrawListForeign", fixture.pool);
                try
                {
                    foreign.SetMeshWorld(fixture.world);
                    RGMeshViewHandle viewHandle = View(owner, MeshTestUtility.CameraContext());
                    Assert.Throws<System.InvalidOperationException>(() => foreign.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer),
                        "Two builders recording one world keep their own views.");
                    Assert.AreEqual(0, Registry(foreign).listCount);
                    RGMeshDrawListHandle drawsHandle = owner.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer);
                    {
                        RGRasterPassHandle<EmptyRasterData> passHandle = foreign.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Foreign"));
                        Assert.Throws<System.InvalidOperationException>(() => passHandle.UseMeshDrawList(drawsHandle), "A foreign handle fails at record.");
                        Assert.AreEqual(0, passHandle.pass.extensionUses.Count);
                        passHandle.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                    {
                        RGRasterPassHandle<EmptyRasterData> passHandle = owner.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Owner"));
                        Assert.IsTrue(passHandle.UseMeshDrawList(drawsHandle).IsValid);
                        CollectionAssert.AreEqual(new[] { drawsHandle.index }, passHandle.pass.extensionUses);
                        passHandle.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                }
                finally
                {
                    MeshTestUtility.Release(foreign);
                    MeshTestUtility.Release(owner);
                }
            }
        }

        [Test]
        public void CreateMeshDrawList_RejectsAPassOutsideTheTable_AndASecondListOfOneViewAndPass()
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder graph = CreateGraph(fixture, "OneListPerViewAndPass", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    RGMeshViewHandle viewHandle = View(graph, MeshTestUtility.CameraContext());
                    Assert.Throws<System.ArgumentOutOfRangeException>(() => graph.CreateMeshDrawList(viewHandle, new MeshPassId(InfinityMeshPasses.all.Length)));
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer),
                        "A view holds one list per pass; a second one throws instead of silently drawing nothing.");
                    Assert.AreEqual(1, Registry(graph).listCount);
                    Assert.IsTrue(Registry(graph).IsLive(drawsHandle));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void TranslucentStages_RecordBackToFrontOnCpu_AndRequireTheViewsMeshMotion()
        {
            using (var fixture = new MeshWorldFixture())
            {
                Mesh mesh = fixture.Mesh(1);
                Material material = fixture.Material(0, 1);
                using (MeshWorldUpdate update = fixture.world.BeginUpdate())
                {
                    MeshEntityId entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                    update.Commit();
                }
                Assert.AreEqual(1, fixture.world.GetPassCache(InfinityMeshPasses.TranslucentT0).memberCount, "A stage-1 material belongs to T0.");
                Assert.AreEqual(1, fixture.world.GetPassCache(InfinityMeshPasses.TranslucentDepth).memberCount, "Translucent surfaces write translucent depth.");
                Assert.AreEqual(0, fixture.world.GetPassCache(InfinityMeshPasses.GBuffer).memberCount, "Translucent surfaces never enter opaque passes.");

                RGBuilder graph = CreateGraph(fixture, "TranslucentDrawLists", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    // Even a GPU request records blended stages on the CPU backend, which sorts back to front.
                    fixture.world.requestedBackend = EMeshDrawBackendPolicy.GPUIndirect;
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    RGMeshViewHandle stillViewHandle = View(graph, context);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(stillViewHandle, InfinityMeshPasses.TranslucentT0),
                        "A stage that writes motion fails at record when its view has no mesh motion.");
                    RGMeshViewHandle viewHandle = graph.CullMeshView(new MeshViewDesc(context,
                        new MeshMotionBinding(fixture.world.transformVersions.current, null), MeshOccluder.None));
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.TranslucentT0);
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, Registry(graph).GetListCopy(drawsHandle.index).backend);

                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterTranslucentT0, drawsHandle, keep: true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, Registry(graph).GetListCopy(drawsHandle.index).state);
                    Assert.AreEqual(1, CountPasses(graph, "UploadMeshData", liveOnly: true));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", liveOnly: false));
                }
                finally
                {
                    MeshTestUtility.Release(graph);
                }
            }
        }

        [Test]
        public void ReleaseAll_RejectsTheOldHandles_AndReleasesVersionReferences()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListRelease", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    RGMeshViewHandle staleViewHandle = View(graph, context);
                    RGMeshDrawListHandle staleHandle = graph.CreateMeshDrawList(staleViewHandle, InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, staleHandle, true);
                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreEqual(2, fixture.world.transformVersions.current.references, "A recorded list retains its frozen transform version.");
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, registry.GetListCopy(staleHandle.index).state);
                    Assert.IsTrue(registry.GetListCopy(staleHandle.index).build.isCreated);
                    int generation = registry.graphGeneration;

                    graph.ClearRecordedGraph();
                    Assert.AreEqual(0, registry.listCount);
                    Assert.AreEqual(0, registry.viewCount);
                    Assert.AreNotEqual(generation, registry.graphGeneration);
                    Assert.IsFalse(registry.IsLive(staleHandle));
                    Assert.IsFalse(registry.IsLive(staleViewHandle));
                    Assert.AreEqual(1, fixture.world.transformVersions.current.references);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(staleViewHandle, InfinityMeshPasses.GBuffer),
                        "A view of an earlier graph fails at create.");

                    RGMeshViewHandle freshViewHandle = View(graph, context);
                    Assert.AreEqual(staleViewHandle.index, freshViewHandle.index);
                    Assert.IsFalse(registry.IsLive(staleViewHandle), "The graph generation rejects a view whose index was reused.");
                    RGMeshDrawListHandle freshHandle = graph.CreateMeshDrawList(freshViewHandle, InfinityMeshPasses.GBuffer);
                    Assert.AreEqual(staleHandle.index, freshHandle.index);
                    Assert.IsTrue(registry.IsLive(freshHandle));
                    Assert.IsFalse(registry.IsLive(staleHandle), "The graph generation rejects a handle whose index was reused.");
                    {
                        RGRasterPassHandle<EmptyRasterData> passHandle = graph.AddRasterPass<EmptyRasterData>(new ProfilingSampler("StaleConsumer"));
                        Assert.Throws<System.InvalidOperationException>(() => passHandle.UseMeshDrawList(staleHandle), "A stale handle fails at record.");
                        Assert.AreEqual(0, passHandle.pass.extensionUses.Count);
                        passHandle.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshDrawBackendPolicy.CPUDirect)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect)]
        public void UnusedDrawList_SchedulesNothing(EMeshDrawBackendPolicy backend)
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListUnused", backend);
                try
                {
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(View(graph, MeshTestUtility.CameraContext()), InfinityMeshPasses.Depth);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterDepth, drawsHandle, false);
                    Compile(graph);

                    MeshDrawListRecord record = Registry(graph).GetListCopy(drawsHandle.index);
                    Assert.AreEqual(EMeshDrawListState.Released, record.state);
                    Assert.IsFalse(record.build.isCreated, "A released CPU list never schedules its build.");
                    // The nonempty list declared its producers, so the culled counts below are not met by declaring none.
                    bool gpu = backend == EMeshDrawBackendPolicy.GPUIndirect;
                    Assert.AreEqual(gpu ? 1 : 0, CountPasses(graph, "ComputeMeshView", false), "A nonempty GPU list declares its view's one producer.");
                    Assert.AreEqual(gpu ? 1 : 0, CountPasses(graph, "ComputeMeshDrawList", false));
                    Assert.AreEqual(gpu ? 0 : 1, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", true), "The producer is culled with its consumer.");
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", true));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshView", true), "A view producer without a live list is culled with it.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshDrawBackendPolicy.CPUDirect)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect)]
        public void EmptyList_SkipsPreparation_AndRejectsCacheChanges(EMeshDrawBackendPolicy backend)
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder graph = CreateGraph(fixture, "EmptyList", backend);
                MeshEntityId entity;
                try
                {
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(View(graph, MeshTestUtility.CameraContext()), InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);
                    MeshViewRegistry registry = Registry(graph);
                    MeshDrawListRecord record = registry.GetListCopy(drawsHandle.index);
                    Assert.AreEqual(EMeshDrawListState.Empty, record.state);
                    Assert.IsFalse(record.build.isCreated);
                    Assert.IsNull(record.payload);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshView", false), "A view whose only list is empty declares no producer.");
                    Assert.IsNull(registry.GetViewCopy(registry.ViewOfList(drawsHandle.index)).payload);
                    using (var commands = new CommandBuffer())
                    {
                        Assert.DoesNotThrow(() => registry.SubmitMeshDrawList(commands, drawsHandle));
                        entity = AddMember(fixture);
                        Assert.Throws<System.InvalidOperationException>(() => registry.SubmitMeshDrawList(commands, drawsHandle));
                    }
                }
                finally { MeshTestUtility.Release(graph); }

                graph = CreateGraph(fixture, "NonemptyList", backend);
                try
                {
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(View(graph, MeshTestUtility.CameraContext()), InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, Registry(graph).GetListCopy(drawsHandle.index).state);
                    Assert.AreEqual(1, CountPasses(graph, backend == EMeshDrawBackendPolicy.GPUIndirect ? "ComputeMeshDrawList" : "UploadMeshData", true));
                    Assert.AreEqual(backend == EMeshDrawBackendPolicy.GPUIndirect ? 1 : 0, CountPasses(graph, "ComputeMeshView", true));
                }
                finally { MeshTestUtility.Release(graph); }

                using (MeshWorldUpdate update = fixture.world.BeginUpdate())
                {
                    update.RemoveEntity(entity);
                    update.Commit();
                }
                graph = CreateGraph(fixture, "EmptyAgain", backend);
                try
                {
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(View(graph, MeshTestUtility.CameraContext()), InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Empty, Registry(graph).GetListCopy(drawsHandle.index).state);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshView", false));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshDrawBackendPolicy.CPUDirect)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect)]
        public void EmptyCascades_KeepRasterConsumersWithoutMeshPreparation(EMeshDrawBackendPolicy backend)
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder graph = CreateGraph(fixture, "EmptyCascades", backend);
                try
                {
                    RGTextureHandle targetHandle = Target(graph);
                    for (int i = 0; i < 4; ++i)
                    {
                        RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(View(graph, LightContext(i)), InfinityMeshPasses.Shadow);
                        AddConsumer(graph, targetHandle, CustomSamplerId.RasterCascadeShadow, drawsHandle, true);
                    }
                    Compile(graph);
                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreEqual(4, registry.viewCount, "Each cascade is its own view.");
                    for (int i = 0; i < 4; ++i)
                        Assert.AreEqual(EMeshDrawListState.Empty, registry.GetListCopy(i).state);
                    Assert.AreEqual(4, CountPasses(graph, ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow).name, true));
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshView", false), "An empty cascade view dispatches nothing.");
                    for (int i = 0; i < 4; ++i)
                        Assert.IsNull(registry.GetViewCopy(i).payload, "An empty cascade view rents no culling context.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void LiveCpuLists_EachScheduleTheirOwnBuild()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListCpuLists", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    RGMeshViewHandle viewHandle = View(graph, MeshTestUtility.CameraContext());
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.Depth);
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    Compile(graph);

                    MeshViewRegistry registry = Registry(graph);
                    Assert.IsTrue(registry.GetListCopy(depthHandle.index).build.isCreated);
                    Assert.IsTrue(registry.GetListCopy(gbufferHandle.index).build.isCreated);
                    Assert.AreEqual(2, CountPasses(graph, "UploadMeshData", true), "One upload producer per live list.");
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshView", false), "A view drawn only on the CPU declares no GPU producer.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void RemainderView_DrawsOnlyWhatTheHistoryViewsListOfItsPassLeft()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "HistoryRemainder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshTransformVersion version = fixture.world.transformVersions.current;
                    var motion = new MeshMotionBinding(version, version);
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshViewHandle historyViewHandle = graph.CullMeshView(new MeshViewDesc(main, motion, Occluder(WrittenTarget(graph), true)));
                    RGMeshDrawListHandle firstHandle = graph.CreateMeshDrawList(historyViewHandle, InfinityMeshPasses.Depth);
                    RGMeshDrawListHandle cascadeHandle = graph.CreateMeshDrawList(View(graph, LightContext(0)), InfinityMeshPasses.Shadow);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CullMeshViewRemainder(historyViewHandle, Occluder(WrittenTarget(graph), true)),
                        "The remainder of a history view needs a newer occluder.");
                    RGTextureHandle earlyHandle = WrittenTarget(graph);
                    RGMeshViewHandle remainderViewHandle = graph.CullMeshViewRemainder(historyViewHandle, Occluder(earlyHandle, false));
                    Assert.Throws<System.InvalidOperationException>(() => graph.CullMeshViewRemainder(historyViewHandle, Occluder(earlyHandle, false)),
                        "A history view has one remainder view, so an instance draws at most once per view and pass.");
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(remainderViewHandle, InfinityMeshPasses.GBuffer),
                        "A remainder list draws what the list of the same pass on the history view left; GBuffer has no list there.");
                    RGMeshDrawListHandle remainderHandle = graph.CreateMeshDrawList(remainderViewHandle, InfinityMeshPasses.Depth);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterCascadeShadow, cascadeHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepthRecovery, remainderHandle, true);
                    Compile(graph);

                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, registry.GetListCopy(remainderHandle.index).state,
                        "What the history pyramid hid is left to the remainder view.");
                    RGBufferHandle historyHandle = Visibility(graph, historyViewHandle);
                    RGBufferHandle remainderVisibilityHandle = Visibility(graph, remainderViewHandle);
                    RGBufferHandle cascadeVisibilityHandle = registry.GetViewCopy(registry.ViewOfList(cascadeHandle.index)).visibilityHandle;
                    Assert.IsTrue(historyHandle.IsValid() && remainderVisibilityHandle.IsValid() && cascadeVisibilityHandle.IsValid());
                    var views = CompiledViewProducers(graph);
                    Assert.AreEqual(3, views.Count, "One producer per view with a GPU list.");
                    Assert.AreEqual(1, views.FindAll(p => p.writes.Contains(Column(historyHandle))).Count, "The history view's producer writes its visibility.");
                    Assert.AreEqual(1, views.FindAll(p => p.reads.Contains(Column(historyHandle))).Count, "Only the remainder view reads it as SRV_MeshViewDrawn.");
                    Assert.IsTrue(views.Find(p => p.reads.Contains(Column(historyHandle))).writes.Contains(Column(remainderVisibilityHandle)));
                    Assert.AreEqual(1, views.FindAll(p => p.reads.Contains(Column(earlyHandle))).Count, "Only the remainder view tests the newer occluder.");
                    var lists = CompiledProducers(graph);
                    Assert.AreEqual(3, lists.Count);
                    Assert.AreEqual(0, lists.FindAll(p => p.reads.Contains(Column(earlyHandle))).Count, "A list tests no occluder; its view did.");
                    Assert.AreEqual(1, lists.FindAll(p => p.reads.Contains(Column(historyHandle))).Count, "The history list reads its view's visibility.");
                    Assert.AreEqual(1, lists.FindAll(p => p.reads.Contains(Column(remainderVisibilityHandle))).Count, "The remainder list reads its own view's.");
                    Assert.AreEqual(1, lists.FindAll(p => p.reads.Contains(Column(cascadeVisibilityHandle))).Count);
                    int historyView = CompiledIndexOf(graph, "ComputeMeshView", p => p.writes.Contains(Column(historyHandle)));
                    int remainderView = CompiledIndexOf(graph, "ComputeMeshView", p => p.writes.Contains(Column(remainderVisibilityHandle)));
                    int remainderList = CompiledIndexOf(graph, "ComputeMeshDrawList", p => p.reads.Contains(Column(remainderVisibilityHandle)));
                    int recovery = IndexOfPass(graph, ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery).name);
                    Assert.GreaterOrEqual(historyView, 0);
                    Assert.Less(historyView, remainderView, "The remainder view's producer follows the history view's producer it reads.");
                    Assert.Less(remainderView, remainderList);
                    Assert.Less(remainderList, recovery);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void RemainderList_OnTheCpu_IsEmpty()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "CpuRemainder", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    MeshTransformVersion version = fixture.world.transformVersions.current;
                    RGMeshViewHandle historyViewHandle = graph.CullMeshView(new MeshViewDesc(MeshTestUtility.CameraContext(),
                        new MeshMotionBinding(version, version), Occluder(WrittenTarget(graph), true)));
                    RGMeshDrawListHandle firstHandle = graph.CreateMeshDrawList(historyViewHandle, InfinityMeshPasses.Depth);
                    RGMeshViewHandle remainderViewHandle = graph.CullMeshViewRemainder(historyViewHandle, Occluder(WrittenTarget(graph), false));
                    RGMeshDrawListHandle remainderHandle = graph.CreateMeshDrawList(remainderViewHandle, InfinityMeshPasses.Depth);
                    RGTextureHandle targetHandle = Target(graph);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepthRecovery, remainderHandle, true);
                    Compile(graph);
                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, registry.GetListCopy(firstHandle.index).state, "The CPU tests no occluder and draws every visible member.");
                    Assert.AreEqual(EMeshDrawListState.Empty, registry.GetListCopy(remainderHandle.index).state, "So nothing is left for the remainder.");
                    Assert.AreEqual(1, CountPasses(graph, "UploadMeshData", true));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void CurrentOccluder_AndFrustum_LeaveNoRemainder()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "CurrentOccluder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshViewHandle frustumViewHandle = View(graph, main);
                    RGMeshViewHandle occludedViewHandle = graph.CullMeshView(new MeshViewDesc(main, default, Occluder(WrittenTarget(graph), false)));
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(frustumViewHandle, InfinityMeshPasses.Depth);
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(occludedViewHandle, InfinityMeshPasses.GBuffer);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CullMeshViewRemainder(frustumViewHandle, Occluder(WrittenTarget(graph), false)),
                        "A frustum view draws every visible member; a newer occluder reveals nothing it left.");
                    Assert.Throws<System.InvalidOperationException>(() => graph.CullMeshViewRemainder(occludedViewHandle, Occluder(WrittenTarget(graph), false)),
                        "This frame's depth leaves nothing for a later view.");
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    Compile(graph);

                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, registry.GetListCopy(depthHandle.index).state);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, registry.GetListCopy(gbufferHandle.index).state);
                    Assert.AreEqual(2, CountPasses(graph, "ComputeMeshDrawList", true));
                    var views = CompiledViewProducers(graph);
                    Assert.AreEqual(2, views.Count);
                    RGBufferHandle frustumHandle = Visibility(graph, frustumViewHandle), occludedHandle = Visibility(graph, occludedViewHandle);
                    Assert.IsFalse(views.Exists(p => p.reads.Contains(Column(frustumHandle)) || p.reads.Contains(Column(occludedHandle))),
                        "Neither view is a remainder, so no view producer reads another view's visibility.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void HistoryOccluder_RequiresTheAcceptedTransformVersion()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "HistoryOccluder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    MeshOccluder occluder = Occluder(Target(graph), true);
                    var current = new MeshMotionBinding(fixture.world.transformVersions.current, null);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CullMeshView(new MeshViewDesc(context, current, occluder)));
                    Assert.Throws<System.InvalidOperationException>(() => graph.CullMeshView(new MeshViewDesc(context, default, occluder)));
                    Assert.AreEqual(0, Registry(graph).viewCount);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void EmptyAndNonemptyLists_OfAnOccludedView_RecordOnlyTheNeededProducer()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "OccludedEmptyList", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    RGMeshViewHandle viewHandle = graph.CullMeshView(new MeshViewDesc(MeshTestUtility.CameraContext(), default, Occluder(WrittenTarget(graph), false)));
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle liveHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer);
                    RGMeshDrawListHandle emptyHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.Forward);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, liveHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterForward, emptyHandle, true);
                    Compile(graph);
                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, registry.GetListCopy(liveHandle.index).state);
                    Assert.AreEqual(EMeshDrawListState.Empty, registry.GetListCopy(emptyHandle.index).state);
                    Assert.IsNull(registry.GetListCopy(emptyHandle.index).payload);
                    Assert.AreEqual(1, CountPasses(graph, "ComputeMeshDrawList", false));
                    Assert.AreEqual(1, CountPasses(graph, "ComputeMeshView", false), "The nonempty list declared the view's producer.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void GpuLists_EachRecordTheirOwnProducer_BeforeTheirConsumer()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListGpuProducers", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    RGMeshViewHandle mainViewHandle = View(graph, MeshTestUtility.CameraContext());
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(mainViewHandle, InfinityMeshPasses.Depth);
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(mainViewHandle, InfinityMeshPasses.GBuffer);
                    RGMeshDrawListHandle cascadeShadowHandle = graph.CreateMeshDrawList(View(graph, LightContext(0)), InfinityMeshPasses.Shadow);
                    RGMeshDrawListHandle localShadowHandle = graph.CreateMeshDrawList(View(graph, LightContext(1)), InfinityMeshPasses.Shadow);
                    RGTextureHandle shadowTargetHandle = graph.CreateTexture(new TextureDescriptor(2, 2) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, dimension = TextureDimension.Tex2D });
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    AddConsumer(graph, shadowTargetHandle, CustomSamplerId.RasterCascadeShadow, cascadeShadowHandle, true);
                    AddConsumer(graph, shadowTargetHandle, CustomSamplerId.RasterLocalShadow, localShadowHandle, true);
                    Compile(graph);
                    Assert.AreEqual(4, CountPasses(graph, "ComputeMeshDrawList", true), "One producer per GPU list.");
                    Assert.AreEqual(3, CountPasses(graph, "ComputeMeshView", true), "One producer per view: the main view's two lists share one.");
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    int depth = IndexOfPass(graph, ProfilingSampler.Get(CustomSamplerId.RasterDepth).name);
                    int gbuffer = IndexOfPass(graph, ProfilingSampler.Get(CustomSamplerId.RasterGBuffer).name);
                    int cascade = IndexOfPass(graph, ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow).name);
                    int local = IndexOfPass(graph, ProfilingSampler.Get(CustomSamplerId.RasterLocalShadow).name);
                    Assert.AreEqual(graph.CompiledPass(depth).mergeGroup, graph.CompiledPass(gbuffer).mergeGroup);
                    Assert.GreaterOrEqual(graph.CompiledPass(depth).mergeGroup, 0);
                    Assert.AreNotEqual(graph.CompiledPass(depth).mergeGroup, graph.CompiledPass(cascade).mergeGroup);
                    int geometryProducers = 0, shadowProducers = 0;
                    for (int i = 0; i < graph.passCount; ++i)
                    {
                        if (graph.GetPass(i).name != "ComputeMeshDrawList" || graph.IsCulled(i))
                        {
                            continue;
                        }
                        Assert.IsFalse(graph.CompiledPass(i).enableAsyncCompute, "A mesh producer stays on the graphics queue.");
                        if (i < depth)
                        {
                            geometryProducers++;
                        }
                        else if (i > gbuffer && i < cascade)
                        {
                            shadowProducers++;
                        }
                    }
                    Assert.AreEqual(2, geometryProducers, "Depth and GBuffer producers sit before their shared native group.");
                    Assert.AreEqual(2, shadowProducers, "Shadow producers sit before the shadow group and after geometry.");
                    Assert.Less(cascade, local);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        // View independence: an occluder belongs to the view culled with it; a view culled with MeshOccluder.None in the same
        // graph reads no other view's occluder. That light views are culled with MeshOccluder.None is guaranteed at their call
        // sites (CascadeShadowPass, LocalShadowPass) and checked by static_check meshpasses.
        [Test]
        public void Views_AreIndependent_AFrustumViewReadsNoOtherViewsOccluder()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "ViewIndependence", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    RGTextureHandle earlyHandle = WrittenTarget(graph);
                    RGMeshViewHandle mainViewHandle = graph.CullMeshView(new MeshViewDesc(MeshTestUtility.CameraContext(), default, Occluder(earlyHandle, false)));
                    RGMeshDrawListHandle mainHandle = graph.CreateMeshDrawList(mainViewHandle, InfinityMeshPasses.GBuffer);
                    RGMeshViewHandle lightViewHandle = View(graph, LightContext(0));
                    RGMeshDrawListHandle lightHandle = graph.CreateMeshDrawList(lightViewHandle, InfinityMeshPasses.Shadow);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, mainHandle, true);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterCascadeShadow, lightHandle, true);
                    Compile(graph);

                    Assert.AreEqual(EMeshDrawListState.Scheduled, Registry(graph).GetListCopy(lightHandle.index).state);
                    var views = CompiledViewProducers(graph);
                    Assert.AreEqual(2, views.Count);
                    Assert.AreEqual(1, views.FindAll(p => p.reads.Contains(Column(earlyHandle))).Count,
                        "Only the main view's producer reads its occluder; the frustum view has none.");
                    Assert.IsTrue(views.Find(p => p.reads.Contains(Column(earlyHandle))).writes.Contains(Column(Visibility(graph, mainViewHandle))));
                    var lists = CompiledProducers(graph);
                    Assert.AreEqual(2, lists.Count);
                    Assert.AreEqual(0, lists.FindAll(p => p.reads.Contains(Column(earlyHandle))).Count, "A list reads its view's visibility, never an occluder.");
                    Assert.AreEqual(1, lists.FindAll(p => p.reads.Contains(Column(Visibility(graph, lightViewHandle)))).Count);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        // R18: a list means what its view and pass say, whatever was created before it. The old protocol made a second list of
        // an equal culling context and pass draw only a remainder or nothing; views make identity a handle.
        [Test]
        public void OneView_MeansTheSameInEveryListOfIt_AndInAnyOrder()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "ViewMeaning", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshTransformVersion version = fixture.world.transformVersions.current;
                    var desc = new MeshViewDesc(MeshTestUtility.CameraContext(), new MeshMotionBinding(version, version), Occluder(WrittenTarget(graph), true));
                    RGTextureHandle occluderHandle = desc.occluder.furthestHandle;
                    RGMeshViewHandle firstViewHandle = graph.CullMeshView(desc);
                    RGMeshDrawListHandle firstDepthHandle = graph.CreateMeshDrawList(firstViewHandle, InfinityMeshPasses.Depth);
                    RGMeshDrawListHandle firstGBufferHandle = graph.CreateMeshDrawList(firstViewHandle, InfinityMeshPasses.GBuffer);
                    // An equal descriptor is a second view; its lists are created in the opposite pass order.
                    RGMeshViewHandle secondViewHandle = graph.CullMeshView(desc);
                    RGMeshDrawListHandle secondGBufferHandle = graph.CreateMeshDrawList(secondViewHandle, InfinityMeshPasses.GBuffer);
                    RGMeshDrawListHandle secondDepthHandle = graph.CreateMeshDrawList(secondViewHandle, InfinityMeshPasses.Depth);
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle[] listsHandle = { firstDepthHandle, firstGBufferHandle, secondGBufferHandle, secondDepthHandle };
                    foreach (RGMeshDrawListHandle drawsHandle in listsHandle)
                        AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);

                    MeshViewRegistry registry = Registry(graph);
                    Assert.AreNotEqual(firstViewHandle.index, secondViewHandle.index);
                    Assert.AreEqual(registry.ViewOfList(firstDepthHandle.index), registry.ViewOfList(firstGBufferHandle.index));
                    Assert.AreEqual(registry.ViewOfList(secondDepthHandle.index), registry.ViewOfList(secondGBufferHandle.index));
                    // Neither view became a remainder of the other: none reads another view's visibility (SRV_MeshViewDrawn).
                    Assert.AreEqual(-1, registry.GetViewCopy(firstViewHandle.index).drawnView);
                    Assert.AreEqual(-1, registry.GetViewCopy(secondViewHandle.index).drawnView);
                    RGBufferHandle firstHandle = Visibility(graph, firstViewHandle), secondHandle = Visibility(graph, secondViewHandle);
                    Assert.AreNotEqual(firstHandle.handle.index, secondHandle.handle.index, "Two views are two visibility products.");
                    var views = CompiledViewProducers(graph);
                    Assert.AreEqual(2, views.Count, "One producer per view, whatever order its lists were created in.");
                    foreach ((string reads, string writes) view in views)
                    {
                        StringAssert.Contains(Column(occluderHandle), view.reads, "Each view tests its own occluder.");
                        Assert.IsFalse(view.reads.Contains(Column(firstHandle)) || view.reads.Contains(Column(secondHandle)),
                            "No view producer reads another view's visibility; neither view is a remainder.");
                    }
                    var producers = CompiledProducers(graph);
                    Assert.AreEqual(4, producers.Count, "Every list of either view draws; none became a silent remainder.");
                    foreach (RGMeshDrawListHandle drawsHandle in listsHandle)
                    {
                        MeshDrawListRecord record = registry.GetListCopy(drawsHandle.index);
                        Assert.AreEqual(EMeshDrawListState.Scheduled, record.state);
                        bool first = registry.ViewOfList(drawsHandle.index) == firstViewHandle.index;
                        RGBufferHandle ownHandle = first ? firstHandle : secondHandle, otherHandle = first ? secondHandle : firstHandle;
                        var producer = producers.Find(p => p.writes.Contains(Column(record.reads.producerOutputsHandle[2])));
                        Assert.IsNotNull(producer.reads, "The list has its own producer.");
                        StringAssert.Contains(Column(ownHandle), producer.reads, "Every list of a view reads that view's visibility.");
                        Assert.IsFalse(producer.reads.Contains(Column(otherHandle)), "No list reads another view's visibility.");
                        Assert.IsFalse(producer.reads.Contains(Column(occluderHandle)), "A list tests no occluder; its view did once.");
                    }
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        // Plan M2: a view's visibility is produced once for every list of it.
        [Test]
        public void OneView_ProducesItsVisibilityOnce_ForEveryListOfIt()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "ViewProducer", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    RGMeshViewHandle viewHandle = View(graph, MeshTestUtility.CameraContext());
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.Depth);
                    RGBufferHandle visibilityHandle = Visibility(graph, viewHandle);
                    Assert.IsTrue(visibilityHandle.IsValid(), "The view's first nonempty GPU list declares its producer.");
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.GBuffer);
                    RGMeshDrawListHandle shadowHandle = graph.CreateMeshDrawList(viewHandle, InfinityMeshPasses.Shadow);
                    Assert.AreEqual(visibilityHandle.handle.index, Visibility(graph, viewHandle).handle.index, "Later lists reuse the view's visibility.");
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterCascadeShadow, shadowHandle, true);
                    Compile(graph);

                    MeshViewRecord view = Registry(graph).GetViewCopy(viewHandle.index);
                    Assert.IsNotNull(view.payload, "The view rents its culling context.");
                    Assert.AreEqual(math.max(1, fixture.world.entityHighWater), view.entityCount);
                    var views = CompiledViewProducers(graph);
                    Assert.AreEqual(1, views.Count, "One view producer for three lists.");
                    StringAssert.Contains(Column(visibilityHandle), views[0].writes);
                    var lists = CompiledProducers(graph);
                    Assert.AreEqual(3, lists.Count);
                    Assert.IsTrue(lists.TrueForAll(p => p.reads.Contains(Column(visibilityHandle))), "Every list reads the one visibility.");
                    int producer = CompiledIndexOf(graph, "ComputeMeshView", p => true);
                    Assert.Less(producer, CompiledIndexOf(graph, "ComputeMeshDrawList", p => true), "The view producer precedes every list producer.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshDrawBackendPolicy.Auto, true)]
        [TestCase(EMeshDrawBackendPolicy.CPUDirect, true)]
        [TestCase(EMeshDrawBackendPolicy.CPUDirect, false)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect, true)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect, false)]
        public void CompiledGraph_ProducesEachLiveListOnce_BeforeItsFirstConsumer(EMeshDrawBackendPolicy backend, bool live)
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListProduction", backend);
                try
                {
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(View(graph, MeshTestUtility.CameraContext()), InfinityMeshPasses.GBuffer);
                    RGTextureHandle targetHandle = Target(graph);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, drawsHandle, live);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, drawsHandle, live);
                    Compile(graph);

                    MeshDrawListRecord record = Registry(graph).GetListCopy(drawsHandle.index);
                    bool gpu = record.backend == EMeshDrawBackendPolicy.GPUIndirect;
                    string producer = gpu ? "ComputeMeshDrawList" : "UploadMeshData";
                    Assert.AreEqual(live ? 1 : 0, CountPasses(graph, producer, true));
                    Assert.AreEqual(0, CountPasses(graph, gpu ? "UploadMeshData" : "ComputeMeshDrawList", false));
                    Assert.AreEqual(live && gpu ? 1 : 0, CountPasses(graph, "ComputeMeshView", true), "A GPU list's view is produced once, with it.");
                    if (!live) return;

                    string compiled = graph.DescribeCompiledGraph();
                    Assert.Less(compiled.IndexOf(producer, System.StringComparison.Ordinal), compiled.IndexOf("RasterGBuffer", System.StringComparison.Ordinal));
                    if (gpu)
                        Assert.Less(compiled.IndexOf("|ComputeMeshView|", System.StringComparison.Ordinal), compiled.IndexOf("|ComputeMeshDrawList|", System.StringComparison.Ordinal));
                    if (gpu)
                    {
                        Assert.IsNotNull(record.payload);
                        Assert.GreaterOrEqual(record.payload.commandCapacity, record.commandCount);
                        Assert.GreaterOrEqual(record.payload.instanceCapacity, record.cache.memberCount);
                        Assert.IsFalse(record.build.isCreated);
                    }
                    else
                    {
                        Assert.IsTrue(record.build.isCreated);
                        Assert.IsNull(record.payload);
                    }
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void Payload_RetiredShellStaysOutOfThePoolUntilFlush()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshTestUtility.RequireGPUIndirect(fixture.world);
                MeshDrawGPUBackend backend = fixture.world.gpuBackend;
                var rented = new System.Collections.Generic.List<MeshGPUDrawPayload>();
                try
                {
                    MeshGPUDrawPayload payload = backend.RentPayload(1, 1);
                    backend.RetirePayload(payload);
                    Assert.IsNotNull(payload.indirectArgsHandle.buffer, "Retire is logical; the buffers stay with the payload until flush.");
                    MeshGPUDrawPayload other = backend.RentPayload(1, 1);
                    rented.Add(other);
                    Assert.AreNotSame(payload, other, "A retired payload is not handed out again before flush.");
                    backend.RetirePayload(other);
                    rented.Remove(other);
                    backend.FlushRetired(default);
                    Assert.IsNull(payload.indirectArgsHandle.buffer, "A pooled payload is an empty shell.");
                    Assert.AreEqual(0, payload.commandCapacity);

                    rented.Add(backend.RentPayload(1, 1));
                    rented.Add(backend.RentPayload(1, 1));
                    CollectionAssert.AreEquivalent(new[] { payload, other }, rented);
                    Assert.IsNotNull(rented[0].indirectArgsHandle.buffer);
                }
                finally
                {
                    foreach (MeshGPUDrawPayload payload in rented) backend.RetirePayload(payload);
                    backend.FlushRetired(default);
                }
            }
        }

        [Test]
        public void ViewPayload_RetiredShellStaysOutOfThePoolUntilFlush()
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshTestUtility.RequireGPUIndirect(fixture.world);
                MeshDrawGPUBackend backend = fixture.world.gpuBackend;
                MeshGPUViewPayload payload = backend.RentViewPayload(), other = null, again = null;
                // Each rented payload is retired exactly once, in the body or in the finally, so a failed assert neither
                // leaks a culling context nor pools a payload twice.
                bool payloadRetired = false, otherRetired = false;
                try
                {
                    backend.RetireViewPayload(payload);
                    payloadRetired = true;
                    Assert.IsNotNull(payload.cullingContextHandle.buffer, "Retire is logical; the culling context stays with the view until flush.");
                    other = backend.RentViewPayload();
                    Assert.AreNotSame(payload, other, "A retired view payload is not handed out again before flush.");
                    backend.RetireViewPayload(other);
                    otherRetired = true;
                    backend.FlushRetired(default);
                    Assert.IsNull(payload.cullingContextHandle.buffer, "A pooled view payload is an empty shell.");
                    Assert.IsNull(other.cullingContextHandle.buffer);
                    again = backend.RentViewPayload();
                    Assert.IsTrue(ReferenceEquals(again, payload) || ReferenceEquals(again, other));
                    Assert.IsNotNull(again.cullingContextHandle.buffer);
                }
                finally
                {
                    if (!payloadRetired) backend.RetireViewPayload(payload);
                    if (!otherRetired && !ReferenceEquals(other, payload)) backend.RetireViewPayload(other);
                    backend.RetireViewPayload(again);
                    backend.FlushRetired(default);
                }
            }
        }

        [Test]
        public void GpuLists_TwoViews_CancelThenRetryKeepPayloadsSeparate()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "CancelAndRetryGpuLists", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshCullingContext firstContext = MeshTestUtility.CameraContext();
                    MeshCullingContext secondContext = MeshTestUtility.CameraContext(new float3(1, 0, 0));
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle firstHandle = graph.CreateMeshDrawList(View(graph, firstContext), InfinityMeshPasses.GBuffer);
                    RGMeshDrawListHandle secondHandle = graph.CreateMeshDrawList(View(graph, secondContext), InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, secondHandle, true);
                    Compile(graph);
                    MeshViewRegistry registry = Registry(graph);
                    MeshGPUDrawPayload firstPayload = registry.GetListCopy(firstHandle.index).payload;
                    MeshGPUDrawPayload secondPayload = registry.GetListCopy(secondHandle.index).payload;
                    Assert.AreNotSame(firstPayload, secondPayload);
                    Assert.AreNotSame(firstPayload.indirectArgsHandle.buffer, secondPayload.indirectArgsHandle.buffer);
                    MeshGPUViewPayload firstView = registry.GetViewCopy(registry.ViewOfList(firstHandle.index)).payload;
                    MeshGPUViewPayload secondView = registry.GetViewCopy(registry.ViewOfList(secondHandle.index)).payload;
                    Assert.AreNotSame(firstView.cullingContextHandle.buffer, secondView.cullingContextHandle.buffer, "Each view owns its culling context.");

                    graph.ClearRecordedGraph();
                    fixture.world.gpuBackend.FlushRetired(default);
                    Assert.IsNull(firstPayload.indirectArgsHandle.buffer);
                    Assert.IsNull(secondPayload.indirectArgsHandle.buffer);
                    Assert.IsNull(firstView.cullingContextHandle.buffer, "Clearing the graph retires its views' culling contexts.");
                    Assert.IsNull(secondView.cullingContextHandle.buffer);

                    // The retry culls its views again: a view lives as long as the graph that recorded it.
                    targetHandle = Target(graph);
                    firstHandle = graph.CreateMeshDrawList(View(graph, firstContext), InfinityMeshPasses.GBuffer);
                    secondHandle = graph.CreateMeshDrawList(View(graph, secondContext), InfinityMeshPasses.GBuffer);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, secondHandle, true);
                    Compile(graph);
                    MeshGPUDrawPayload retriedFirst = registry.GetListCopy(firstHandle.index).payload;
                    MeshGPUDrawPayload retriedSecond = registry.GetListCopy(secondHandle.index).payload;
                    CollectionAssert.AreEquivalent(new[] { firstPayload, secondPayload }, new[] { retriedFirst, retriedSecond });
                    Assert.AreNotSame(retriedFirst.indirectArgsHandle.buffer, retriedSecond.indirectArgsHandle.buffer);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }
    }
}
