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
    public class RGDrawListLifetimeTests
    {
        struct EmptyRasterData { }
        struct DrawPassData { public RGDrawListHandle draws; }

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

        static void AddConsumer(RGBuilder graph, RGTextureHandle target, CustomSamplerId sampler, RGDrawListHandle draws, bool keep)
        {
            using (RGRasterPassHandle pass = graph.AddRasterPass<DrawPassData>(ProfilingSampler.Get(sampler)))
            {
                pass.SetColorAttachment(target, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                if (keep) pass.EnablePassCulling(false);
                ref DrawPassData data = ref pass.GetPassData<DrawPassData>();
                data.draws = pass.UseDrawList(draws);
                pass.SetExecuteFunc((in DrawPassData passData, in RGRasterEncoder encoder) => encoder.Draw(passData.draws));
            }
        }

        static void AddMember(MeshWorldFixture fixture)
        {
            Mesh mesh = fixture.Mesh(1);
            Material material = fixture.Material(0, 0);
            using (MeshWorldUpdate update = fixture.world.BeginUpdate())
            {
                MeshEntityId entity = MeshTestUtility.CreateEntity(update, float3.zero);
                update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                update.Commit();
            }
        }

        static RGBuilder CreateGraph(MeshWorldFixture fixture, string name, EMeshBackendPolicy backend)
        {
            if (backend == EMeshBackendPolicy.GpuIndirect) MeshTestUtility.RequireGpuIndirect(fixture.world);
            fixture.world.SetBackendOverride(backend);
            fixture.world.UpdateResidency();
            var graph = new RGBuilder(name, fixture.pool);
            graph.SetMeshWorld(fixture.world);
            return graph;
        }

        [Test]
        public void UseDrawList_RejectsAnotherBuildersHandle()
        {
            using (var fixture = new MeshWorldFixture())
            {
                RGBuilder owner = CreateGraph(fixture, "DrawListOwner", EMeshBackendPolicy.CpuDirect);
                var foreign = new RGBuilder("DrawListForeign", fixture.pool);
                try
                {
                    foreign.SetMeshWorld(fixture.world);
                    RGDrawListHandle draws = owner.CreateDrawList(MeshTestUtility.CameraView(1), MeshPassPresets.GBuffer);
                    using (RGRasterPassHandle pass = foreign.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Foreign")))
                    {
                        Assert.Throws<System.InvalidOperationException>(() => pass.UseDrawList(draws), "A foreign handle fails at record.");
                        Assert.AreEqual(0, pass.pass.extensionUses.Count);
                        pass.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                    using (RGRasterPassHandle pass = owner.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Owner")))
                    {
                        Assert.IsTrue(pass.UseDrawList(draws).IsValid);
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
        public void TranslucentStages_RecordBackToFrontOnCpu_AndRequireTheViewsPreviousTransforms()
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
                Assert.AreEqual(1, fixture.world.GetPassCache(MeshPassPresets.TranslucentT0).memberCount, "A stage-1 material belongs to T0.");
                Assert.AreEqual(1, fixture.world.GetPassCache(MeshPassPresets.TranslucentDepth).memberCount, "Translucent surfaces write translucent depth.");
                Assert.AreEqual(0, fixture.world.GetPassCache(MeshPassPresets.GBuffer).memberCount, "Translucent surfaces never enter opaque passes.");

                RGBuilder graph = CreateGraph(fixture, "TranslucentDrawLists", EMeshBackendPolicy.CpuDirect);
                try
                {
                    // Even a GPU request records blended stages on the CPU backend, which sorts back to front.
                    fixture.world.SetBackendOverride(EMeshBackendPolicy.GpuIndirect);
                    MeshView view = MeshTestUtility.CameraView(7);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateDrawList(view, MeshPassPresets.TranslucentT0),
                        "A stage that writes motion fails at record without the view's previous-transform producer.");
                    fixture.world.BindPreviousTransforms(view.viewKey, new MeshMotionBinding(fixture.world.transformVersions.current, null));
                    RGDrawListHandle draws = graph.CreateDrawList(view, MeshPassPresets.TranslucentT0);
                    Assert.AreEqual(EMeshBackendPolicy.CpuDirect, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).backend);

                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterTranslucentT0, draws, keep: true);
                    Compile(graph);
                    Assert.AreEqual(ERGDrawListCompileState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
                    Assert.AreEqual(1, CountPasses(graph, "UploadMeshData", liveOnly: true));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCompaction", liveOnly: true));
                }
                finally
                {
                    MeshTestUtility.Release(graph);
                }
            }
        }

        [Test]
        public void ReleaseAll_RejectsTheOldHandle_AndReleasesVisibilityAndVersionReferences()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListRelease", EMeshBackendPolicy.CpuDirect);
                try
                {
                    MeshView view = MeshTestUtility.CameraView(2);
                    RGDrawListHandle stale = graph.CreateDrawList(view, MeshPassPresets.GBuffer);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, stale, true);
                    RGDrawListContext lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(2, fixture.world.transformVersions.current.references, "A recorded list retains its frozen transform version.");
                    Compile(graph);
                    Assert.AreEqual(ERGDrawListCompileState.Scheduled, lists.GetRecordCopy(stale.index).state);
                    Assert.AreEqual(1, fixture.world.visibilityShare.liveCount);
                    int generation = lists.GraphGeneration;

                    graph.ClearRecordedGraph();
                    Assert.AreEqual(0, lists.Count);
                    Assert.AreNotEqual(generation, lists.GraphGeneration);
                    Assert.IsFalse(lists.IsLive(stale));
                    Assert.AreEqual(0, fixture.world.visibilityShare.liveCount);
                    Assert.AreEqual(1, fixture.world.transformVersions.current.references);

                    RGDrawListHandle fresh = graph.CreateDrawList(view, MeshPassPresets.GBuffer);
                    Assert.AreEqual(stale.index, fresh.index);
                    Assert.IsTrue(lists.IsLive(fresh));
                    Assert.IsFalse(lists.IsLive(stale), "The graph generation rejects a handle whose index was reused.");
                    using (RGRasterPassHandle pass = graph.AddRasterPass<EmptyRasterData>(new ProfilingSampler("StaleConsumer")))
                    {
                        Assert.Throws<System.InvalidOperationException>(() => pass.UseDrawList(stale), "A stale handle fails at record.");
                        Assert.AreEqual(0, pass.pass.extensionUses.Count);
                        pass.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshBackendPolicy.CpuDirect)]
        [TestCase(EMeshBackendPolicy.GpuIndirect)]
        public void UnusedDrawList_SchedulesNothing(EMeshBackendPolicy backend)
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListUnused", backend);
                try
                {
                    MeshView view = MeshTestUtility.CameraView(3);
                    RGDrawListHandle draws = graph.CreateDrawList(view, MeshPassPresets.Depth);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterDepth, draws, false);
                    Assert.AreEqual(backend == EMeshBackendPolicy.GpuIndirect ? 1 : 0, CountPasses(graph, "ComputeMeshCulling", false));
                    Compile(graph);

                    RGDrawListContext lists = MeshGraphExtension.Of(graph).drawLists;
                    RGDrawListRecord record = lists.GetRecordCopy(draws.index);
                    Assert.AreEqual(ERGDrawListCompileState.Released, record.state);
                    Assert.IsNull(record.payload);
                    Assert.IsFalse(record.visibility.IsValid);
                    Assert.AreEqual(0, fixture.world.stats.visibilityProductions);
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCulling", true), "Culling without a consumer is culled with it.");
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCompaction", false));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void LiveCpuLists_ShareOneVisibilityResultPerView()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListCpuShare", EMeshBackendPolicy.CpuDirect);
                try
                {
                    MeshView view = MeshTestUtility.CameraView(4);
                    RGTextureHandle target = Target(graph);
                    RGDrawListHandle depth = graph.CreateDrawList(view, MeshPassPresets.Depth);
                    RGDrawListHandle gbuffer = graph.CreateDrawList(view, MeshPassPresets.GBuffer);
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, depth, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, gbuffer, true);
                    Compile(graph);

                    RGDrawListContext lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshVisibilityHandle first = lists.GetRecordCopy(depth.index).visibility;
                    MeshVisibilityHandle second = lists.GetRecordCopy(gbuffer.index).visibility;
                    Assert.IsTrue(first.IsValid);
                    Assert.AreEqual(first.slot, second.slot);
                    Assert.AreEqual(first.generation, second.generation);
                    Assert.AreEqual(1, fixture.world.stats.visibilityProductions);
                    Assert.AreEqual(2, CountPasses(graph, "UploadMeshData", true), "One upload producer per live list.");
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCulling", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCompaction", false));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void GpuCulling_IsRecordedOncePerVisibilityKey()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListGpuCull", EMeshBackendPolicy.GpuIndirect);
                try
                {
                    MeshView main = MeshTestUtility.CameraView(5);
                    MeshView cascade = MeshView.FromShadow(EMeshViewKind.CascadeShadow, 0xCAFEul, main.CopyPlanes(), main.viewPosition, ~0,
                        (uint)ERenderingLayer.Everything, 0, ulong.MaxValue, MeshTestUtility.Lod);
                    MeshView local = MeshView.FromShadow(EMeshViewKind.LocalShadow, 0xCAFEul, main.CopyPlanes(), main.viewPosition, ~0,
                        (uint)ERenderingLayer.Everything, 0, ulong.MaxValue, MeshTestUtility.Lod);
                    graph.EnsureMeshGpuCull(main);
                    graph.EnsureMeshGpuCull(cascade);
                    graph.EnsureMeshGpuCull(local);
                    RGDrawListHandle depth = graph.CreateDrawList(main, MeshPassPresets.Depth);
                    RGDrawListHandle gbuffer = graph.CreateDrawList(main, MeshPassPresets.GBuffer);
                    RGDrawListHandle cascadeShadow = graph.CreateDrawList(cascade, MeshPassPresets.Shadow);
                    RGDrawListHandle localShadow = graph.CreateDrawList(local, MeshPassPresets.Shadow);
                    Assert.AreEqual(3, CountPasses(graph, "ComputeMeshCulling", false));

                    RGDrawListContext lists = MeshGraphExtension.Of(graph).drawLists;
                    RGBufferHandle mainVisibility = lists.GetRecordCopy(depth.index).gpuVisibility;
                    Assert.IsTrue(mainVisibility.IsValid());
                    Assert.AreEqual(mainVisibility.handle, lists.GetRecordCopy(gbuffer.index).gpuVisibility.handle, "Lists of one view bind one culling producer.");
                    Assert.AreNotEqual(mainVisibility.handle, lists.GetRecordCopy(cascadeShadow.index).gpuVisibility.handle);
                    Assert.AreNotEqual(lists.GetRecordCopy(cascadeShadow.index).gpuVisibility.handle, lists.GetRecordCopy(localShadow.index).gpuVisibility.handle);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshBackendPolicy.Auto, true)]
        [TestCase(EMeshBackendPolicy.CpuDirect, true)]
        [TestCase(EMeshBackendPolicy.CpuDirect, false)]
        [TestCase(EMeshBackendPolicy.GpuIndirect, true)]
        [TestCase(EMeshBackendPolicy.GpuIndirect, false)]
        public void CompiledGraph_PreparesEachLiveListOnce_BeforeItsFirstConsumer(EMeshBackendPolicy backend, bool live)
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListPreparation", backend);
                try
                {
                    RGDrawListHandle draws = graph.CreateDrawList(MeshTestUtility.CameraView(6), MeshPassPresets.GBuffer);
                    RGTextureHandle target = Target(graph);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, draws, live);
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, draws, live);
                    Compile(graph);

                    RGDrawListRecord record = MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index);
                    bool gpu = record.backend == EMeshBackendPolicy.GpuIndirect;
                    Assert.AreEqual(live ? 1 : 0, CountPasses(graph, "UploadMeshData", true));
                    Assert.AreEqual(live && gpu ? 1 : 0, CountPasses(graph, "ComputeMeshCompaction", true));
                    if (!live) return;

                    string compiled = graph.DescribeCompiledGraph();
                    Assert.Less(compiled.IndexOf("UploadMeshData"), compiled.IndexOf("RasterGBuffer"));
                    if (gpu)
                    {
                        Assert.Less(compiled.IndexOf("ComputeMeshCompaction"), compiled.IndexOf("RasterGBuffer"));
                        Assert.IsFalse(record.visibility.IsValid, "GPU lists never acquire CPU visibility.");
                        Assert.AreEqual(0, fixture.world.stats.visibilityProductions);
                        Assert.IsNotNull(record.payload);
                        Assert.GreaterOrEqual(record.payload.commandCapacity, record.commandCount);
                        Assert.GreaterOrEqual(record.payload.instanceCapacity, record.cache.memberCount);
                    }
                    else
                    {
                        Assert.IsTrue(record.visibility.IsValid);
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
                MeshTestUtility.RequireGpuIndirect(fixture.world);
                MeshGpuDrawBackend backend = fixture.world.gpuBackend;
                var rented = new System.Collections.Generic.List<MeshGpuDrawPayload>();
                try
                {
                    MeshGpuDrawPayload payload = backend.RentPayload(1, 1);
                    backend.RetirePayload(payload);
                    Assert.IsNotNull(payload.indirectArgs.buffer, "Retire is logical; the buffers stay with the payload until flush.");
                    MeshGpuDrawPayload other = backend.RentPayload(1, 1);
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
                    foreach (MeshGpuDrawPayload payload in rented) backend.RetirePayload(payload);
                    backend.FlushRetired(default);
                }
            }
        }
    }
}
