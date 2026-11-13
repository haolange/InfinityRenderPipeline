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
        struct DrawPassData { public RGMeshDrawListHandle meshDrawList; }

        static void Compile(RGBuilder graph)
            => typeof(RGBuilder).GetMethod("CompilePass", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(graph, null);

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

        static RGTextureHandle Target(RGBuilder graph)
            => graph.CreateTexture(new TextureDescriptor(1, 1) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, dimension = TextureDimension.Tex2D });

        static void AddConsumer(RGBuilder graph, RGTextureHandle target, CustomSamplerId sampler, RGMeshDrawListHandle draws, bool keep)
        {
            using (var pass = graph.AddRasterPass<DrawPassData>(ProfilingSampler.Get(sampler)))
            {
                pass.SetColorAttachment(target, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                if (keep) pass.EnablePassCulling(false);
                ref DrawPassData data = ref pass.GetPassData();
                data.meshDrawList = pass.UseMeshDrawList(draws);
                pass.SetExecuteFunc((in DrawPassData passData, in RGRasterEncoder encoder) => encoder.SubmitMeshDrawList(passData.meshDrawList));
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

        // A light-like context that filters rendering layers; index moves its far plane so contexts stay distinct.
        static MeshCullingContext LightContext(int index)
        {
            Plane[] planes = MeshTestUtility.BoxPlanes();
            planes[5] = new Plane(Vector3.back, 10f + index);
            return MeshCullingContext.Create(planes, ~0, ulong.MaxValue, (uint)ERenderingLayer.Everything, true, MeshTestUtility.Lod);
        }

        [Test]
        public void UseMeshDrawList_RejectsAnotherBuildersHandle()
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder owner = CreateGraph(fixture, "DrawListOwner", EMeshDrawBackendPolicy.CPUDirect);
                var foreign = new RGBuilder("DrawListForeign", fixture.pool);
                try
                {
                    foreign.SetMeshWorld(fixture.world);
                    RGMeshDrawListHandle draws = owner.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    using (var pass = foreign.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Foreign")))
                    {
                        Assert.Throws<System.InvalidOperationException>(() => pass.UseMeshDrawList(draws), "A foreign handle fails at record.");
                        Assert.AreEqual(0, pass.pass.extensionUses.Count);
                        pass.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                    using (var pass = owner.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Owner")))
                    {
                        Assert.IsTrue(pass.UseMeshDrawList(draws).IsValid);
                        CollectionAssert.AreEqual(new[] { draws.index }, pass.pass.extensionUses);
                        pass.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
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
        public void CreateMeshDrawList_RejectsATagThatIsNotAMeshPass()
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder graph = CreateGraph(fixture, "UnknownMeshPass", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    var desc = new MeshDrawListDesc(MeshTestUtility.CameraContext(), new ShaderTagId("NotAMeshPass"));
                    System.ArgumentException error = Assert.Throws<System.ArgumentException>(() => graph.CreateMeshDrawList(desc));
                    StringAssert.Contains("NotAMeshPass", error.Message);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void TranslucentStages_RecordBackToFrontOnCpu_AndRequireTheCamerasMeshMotion()
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
                Assert.AreEqual(1, fixture.world.GetPassCache(MeshPassTable.TranslucentT0).memberCount, "A stage-1 material belongs to T0.");
                Assert.AreEqual(1, fixture.world.GetPassCache(MeshPassTable.TranslucentDepth).memberCount, "Translucent surfaces write translucent depth.");
                Assert.AreEqual(0, fixture.world.GetPassCache(MeshPassTable.GBuffer).memberCount, "Translucent surfaces never enter opaque passes.");

                RGBuilder graph = CreateGraph(fixture, "TranslucentDrawLists", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    // Even a GPU request records blended stages on the CPU backend, which sorts back to front.
                    fixture.world.requestedBackend = EMeshDrawBackendPolicy.GPUIndirect;
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterTranslucentT0Pass)),
                        "A stage that writes motion fails at record without the camera's mesh motion.");
                    graph.SetMeshMotion(new MeshMotionBinding(fixture.world.transformVersions.current, null));
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterTranslucentT0Pass));
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).backend);

                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterTranslucentT0, draws, keep: true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
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
        public void ReleaseAll_RejectsTheOldHandle_AndReleasesVersionReferences()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListRelease", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    RGMeshDrawListHandle stale = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, stale, true);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(2, fixture.world.transformVersions.current.references, "A recorded list retains its frozen transform version.");
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(stale.index).state);
                    Assert.IsTrue(lists.GetRecordCopy(stale.index).build.isCreated);
                    int generation = lists.GraphGeneration;

                    graph.ClearRecordedGraph();
                    Assert.AreEqual(0, lists.Count);
                    Assert.AreNotEqual(generation, lists.GraphGeneration);
                    Assert.IsFalse(lists.IsLive(stale));
                    Assert.AreEqual(1, fixture.world.transformVersions.current.references);

                    RGMeshDrawListHandle fresh = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    Assert.AreEqual(stale.index, fresh.index);
                    Assert.IsTrue(lists.IsLive(fresh));
                    Assert.IsFalse(lists.IsLive(stale), "The graph generation rejects a handle whose index was reused.");
                    using (var pass = graph.AddRasterPass<EmptyRasterData>(new ProfilingSampler("StaleConsumer")))
                    {
                        Assert.Throws<System.InvalidOperationException>(() => pass.UseMeshDrawList(stale), "A stale handle fails at record.");
                        Assert.AreEqual(0, pass.pass.extensionUses.Count);
                        pass.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterDepthPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterDepth, draws, false);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshDrawListRecord record = lists.GetRecordCopy(draws.index);
                    Assert.AreEqual(EMeshDrawListState.Released, record.state);
                    Assert.IsFalse(record.build.isCreated, "A released CPU list never schedules its build.");
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", true), "The producer is culled with its consumer.");
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", true));
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, draws, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshDrawListRecord record = lists.GetRecordCopy(draws.index);
                    Assert.AreEqual(EMeshDrawListState.Empty, record.state);
                    Assert.IsFalse(record.build.isCreated);
                    Assert.IsNull(record.payload);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                    using (var commands = new CommandBuffer())
                    {
                        Assert.DoesNotThrow(() => lists.SubmitMeshDrawList(commands, draws));
                        entity = AddMember(fixture);
                        Assert.Throws<System.InvalidOperationException>(() => lists.SubmitMeshDrawList(commands, draws));
                    }
                }
                finally { MeshTestUtility.Release(graph); }

                graph = CreateGraph(fixture, "NonemptyList", backend);
                try
                {
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, draws, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
                    Assert.AreEqual(1, CountPasses(graph, backend == EMeshDrawBackendPolicy.GPUIndirect ? "ComputeMeshDrawList" : "UploadMeshData", true));
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, draws, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Empty, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
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
                    RGTextureHandle target = Target(graph);
                    for (int i = 0; i < 4; ++i)
                    {
                        RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(LightContext(i), InfinityPassIDs.RasterShadowPass));
                        AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, draws, true);
                    }
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    for (int i = 0; i < 4; ++i)
                        Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(i).state);
                    Assert.AreEqual(4, CountPasses(graph, ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow).name, true));
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
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
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle depth = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbuffer = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, depth, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, gbuffer, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.IsTrue(lists.GetRecordCopy(depth.index).build.isCreated);
                    Assert.IsTrue(lists.GetRecordCopy(gbuffer.index).build.isCreated);
                    Assert.AreEqual(2, CountPasses(graph, "UploadMeshData", true), "One upload producer per live list.");
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void Occluder_LeavesARemainderOnlyForItsContext()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "OccluderRemainder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    MeshCullingContext light = LightContext(0);
                    RGTextureHandle target = Target(graph);
                    graph.RegisterMeshOccluder(main, new MeshOccluder(Target(graph), Matrix4x4.identity, new int2(2, 2), 1, false));
                    RGMeshDrawListHandle first = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle cascade = graph.CreateMeshDrawList(new MeshDrawListDesc(light, InfinityPassIDs.RasterShadowPass));
                    RGMeshDrawListHandle remainder = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle cascadeAgain = graph.CreateMeshDrawList(new MeshDrawListDesc(light, InfinityPassIDs.RasterShadowPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, first, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, cascade, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterDepthRecovery, remainder, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, cascadeAgain, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(remainder.index).state,
                        "An occluded context leaves what its occluder hid to a later list of the same pass.");
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(cascadeAgain.index).state,
                        "Without an occluder a list draws every visible member, so nothing is left.");
                    Assert.AreEqual(3, CountPasses(graph, "ComputeMeshDrawList", true));
                    string compiled = graph.DescribeCompiledGraph();
                    int producer = compiled.IndexOf("|ComputeMeshDrawList|", System.StringComparison.Ordinal);
                    Assert.GreaterOrEqual(producer, 0);
                    Assert.Less(producer, compiled.IndexOf(ProfilingSampler.Get(CustomSamplerId.RasterDepthRecovery).name, System.StringComparison.Ordinal));
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
                    graph.SetMeshMotion(new MeshMotionBinding(fixture.world.transformVersions.current, null));
                    var occluder = new MeshOccluder(Target(graph), Matrix4x4.identity, new int2(2, 2), 1, true);
                    Assert.Throws<System.InvalidOperationException>(() => graph.RegisterMeshOccluder(MeshTestUtility.CameraContext(), occluder));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void EmptyAndNonemptyLists_OfAnOccludedContext_RecordOnlyTheNeededProducer()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "OccludedEmptyList", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshCullingContext context = MeshTestUtility.CameraContext();
                    graph.RegisterMeshOccluder(context, new MeshOccluder(Target(graph), Matrix4x4.identity, new int2(2, 2), 1, false));
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle live = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle empty = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterForwardPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, live, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterForward, empty, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(live.index).state);
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(empty.index).state);
                    Assert.IsNull(lists.GetRecordCopy(empty.index).payload);
                    Assert.AreEqual(1, CountPasses(graph, "ComputeMeshDrawList", false));
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
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle depth = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbuffer = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle cascadeShadow = graph.CreateMeshDrawList(new MeshDrawListDesc(LightContext(0), InfinityPassIDs.RasterShadowPass));
                    RGMeshDrawListHandle localShadow = graph.CreateMeshDrawList(new MeshDrawListDesc(LightContext(1), InfinityPassIDs.RasterShadowPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, depth, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, gbuffer, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, cascadeShadow, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterLocalShadow, localShadow, true);
                    Compile(graph);
                    Assert.AreEqual(4, CountPasses(graph, "ComputeMeshDrawList", true), "Visibility is evaluated inside each list's producer.");
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    string compiled = graph.DescribeCompiledGraph();
                    Assert.Less(compiled.IndexOf("ComputeMeshDrawList", System.StringComparison.Ordinal),
                        compiled.IndexOf(ProfilingSampler.Get(CustomSamplerId.RasterDepth).name, System.StringComparison.Ordinal));
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    RGTextureHandle target = Target(graph);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, draws, live);
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, draws, live);
                    Compile(graph);

                    MeshDrawListRecord record = MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index);
                    bool gpu = record.backend == EMeshDrawBackendPolicy.GPUIndirect;
                    string producer = gpu ? "ComputeMeshDrawList" : "UploadMeshData";
                    Assert.AreEqual(live ? 1 : 0, CountPasses(graph, producer, true));
                    Assert.AreEqual(0, CountPasses(graph, gpu ? "UploadMeshData" : "ComputeMeshDrawList", false));
                    if (!live) return;

                    string compiled = graph.DescribeCompiledGraph();
                    Assert.Less(compiled.IndexOf(producer, System.StringComparison.Ordinal), compiled.IndexOf("RasterGBuffer", System.StringComparison.Ordinal));
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
                    Assert.IsNotNull(payload.indirectArgs.buffer, "Retire is logical; the buffers stay with the payload until flush.");
                    MeshGPUDrawPayload other = backend.RentPayload(1, 1);
                    rented.Add(other);
                    Assert.AreNotSame(payload, other, "A retired payload is not handed out again before flush.");
                    backend.RetirePayload(other);
                    rented.Remove(other);
                    backend.FlushRetired(default);
                    Assert.IsNull(payload.indirectArgs.buffer, "A pooled payload is an empty shell.");
                    Assert.AreEqual(0, payload.commandCapacity);

                    rented.Add(backend.RentPayload(1, 1));
                    rented.Add(backend.RentPayload(1, 1));
                    CollectionAssert.AreEquivalent(new[] { payload, other }, rented);
                    Assert.IsNotNull(rented[0].indirectArgs.buffer);
                }
                finally
                {
                    foreach (MeshGPUDrawPayload payload in rented) backend.RetirePayload(payload);
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
                    MeshCullingContext firstView = MeshTestUtility.CameraContext();
                    MeshCullingContext secondView = MeshTestUtility.CameraContext(new float3(1, 0, 0));
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle first = graph.CreateMeshDrawList(new MeshDrawListDesc(firstView, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle second = graph.CreateMeshDrawList(new MeshDrawListDesc(secondView, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, first, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, second, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshGPUDrawPayload firstPayload = lists.GetRecordCopy(first.index).payload;
                    MeshGPUDrawPayload secondPayload = lists.GetRecordCopy(second.index).payload;
                    Assert.AreNotSame(firstPayload, secondPayload);
                    Assert.AreNotSame(firstPayload.indirectArgs.buffer, secondPayload.indirectArgs.buffer);

                    graph.ClearRecordedGraph();
                    fixture.world.gpuBackend.FlushRetired(default);
                    Assert.IsNull(firstPayload.indirectArgs.buffer);
                    Assert.IsNull(secondPayload.indirectArgs.buffer);

                    target = Target(graph);
                    first = graph.CreateMeshDrawList(new MeshDrawListDesc(firstView, InfinityPassIDs.RasterGBufferPass));
                    second = graph.CreateMeshDrawList(new MeshDrawListDesc(secondView, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, first, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, second, true);
                    Compile(graph);
                    MeshGPUDrawPayload retriedFirst = lists.GetRecordCopy(first.index).payload;
                    MeshGPUDrawPayload retriedSecond = lists.GetRecordCopy(second.index).payload;
                    CollectionAssert.AreEquivalent(new[] { firstPayload, secondPayload }, new[] { retriedFirst, retriedSecond });
                    Assert.AreNotSame(retriedFirst.indirectArgs.buffer, retriedSecond.indirectArgs.buffer);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }
    }
}
