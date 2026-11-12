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
            if (keep)
                MeshGraphExtension.Of(graph).PrepareMeshDrawList(graph, draws);
            using (RGRasterPassHandle pass = graph.AddRasterPass<DrawPassData>(ProfilingSampler.Get(sampler)))
            {
                pass.SetColorAttachment(target, 0, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                if (keep) pass.EnablePassCulling(false);
                ref DrawPassData data = ref pass.GetPassData<DrawPassData>();
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
                    RGMeshDrawListHandle draws = owner.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraView(1), InfinityPassIDs.RasterGBufferPass));
                    using (RGRasterPassHandle pass = foreign.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Foreign")))
                    {
                        Assert.Throws<System.InvalidOperationException>(() => pass.UseMeshDrawList(draws), "A foreign handle fails at record.");
                        Assert.AreEqual(0, pass.pass.extensionUses.Count);
                        pass.SetExecuteFunc((in EmptyRasterData data, in RGRasterEncoder encoder) => { });
                    }
                    using (RGRasterPassHandle pass = owner.AddRasterPass<EmptyRasterData>(new ProfilingSampler("Owner")))
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
                    var desc = new MeshDrawListDesc(MeshTestUtility.CameraView(3), new ShaderTagId("NotAMeshPass"));
                    System.ArgumentException error = Assert.Throws<System.ArgumentException>(() => graph.CreateMeshDrawList(desc));
                    StringAssert.Contains("NotAMeshPass", error.Message);
                }
                finally { MeshTestUtility.Release(graph); }
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
                Assert.AreEqual(1, fixture.world.GetPassCache(MeshPassTable.TranslucentT0).memberCount, "A stage-1 material belongs to T0.");
                Assert.AreEqual(1, fixture.world.GetPassCache(MeshPassTable.TranslucentDepth).memberCount, "Translucent surfaces write translucent depth.");
                Assert.AreEqual(0, fixture.world.GetPassCache(MeshPassTable.GBuffer).memberCount, "Translucent surfaces never enter opaque passes.");

                RGBuilder graph = CreateGraph(fixture, "TranslucentDrawLists", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    // Even a GPU request records blended stages on the CPU backend, which sorts back to front.
                    fixture.world.requestedBackend = EMeshDrawBackendPolicy.GPUIndirect;
                    MeshView view = MeshTestUtility.CameraView(7);
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterTranslucentT0Pass)),
                        "A stage that writes motion fails at record without the view's previous-transform producer.");
                    fixture.world.BindPreviousTransforms(view.viewKey, new MeshMotionBinding(fixture.world.transformVersions.current, null));
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterTranslucentT0Pass));
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).backend);

                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterTranslucentT0, draws, keep: true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
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
                RGBuilder graph = CreateGraph(fixture, "DrawListRelease", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    MeshView view = MeshTestUtility.CameraView(2);
                    RGMeshDrawListHandle stale = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, stale, true);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(2, fixture.world.transformVersions.current.references, "A recorded list retains its frozen transform version.");
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(stale.index).state);
                    Assert.AreEqual(1, fixture.world.visibilityShare.liveCount);
                    int generation = lists.GraphGeneration;

                    graph.ClearRecordedGraph();
                    Assert.AreEqual(0, lists.Count);
                    Assert.AreNotEqual(generation, lists.GraphGeneration);
                    Assert.IsFalse(lists.IsLive(stale));
                    Assert.AreEqual(0, fixture.world.visibilityShare.liveCount);
                    Assert.AreEqual(1, fixture.world.transformVersions.current.references);

                    RGMeshDrawListHandle fresh = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterGBufferPass));
                    Assert.AreEqual(stale.index, fresh.index);
                    Assert.IsTrue(lists.IsLive(fresh));
                    Assert.IsFalse(lists.IsLive(stale), "The graph generation rejects a handle whose index was reused.");
                    using (RGRasterPassHandle pass = graph.AddRasterPass<EmptyRasterData>(new ProfilingSampler("StaleConsumer")))
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
                    MeshView view = MeshTestUtility.CameraView(3);
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterDepthPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterDepth, draws, false);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshDrawListRecord record = lists.GetRecordCopy(draws.index);
                    Assert.AreEqual(EMeshDrawListState.Released, record.state);
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraView(30), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, draws, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshDrawListRecord record = lists.GetRecordCopy(draws.index);
                    Assert.AreEqual(EMeshDrawListState.Empty, record.state);
                    Assert.IsFalse(record.visibility.IsValid);
                    Assert.IsFalse(record.gpuVisibility.IsValid());
                    Assert.IsFalse(record.build.isCreated);
                    Assert.IsNull(record.payload);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCompaction", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCulling", true));
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraView(30), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, draws, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
                    Assert.AreEqual(1, CountPasses(graph, "UploadMeshData", true));
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
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraView(30), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, draws, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Empty, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index).state);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
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
                    MeshView main = MeshTestUtility.CameraView(31);
                    RGTextureHandle target = Target(graph);
                    for (int i = 0; i < 4; ++i)
                    {
                        MeshView cascade = MeshView.FromShadow(EMeshViewKind.CascadeShadow, (ulong)(100 + i), main.CopyPlanes(), main.viewPosition, ~0,
                            (uint)ERenderingLayer.Everything, i, ulong.MaxValue, MeshTestUtility.Lod);
                        RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(cascade, InfinityPassIDs.RasterShadowPass));
                        AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, draws, true);
                    }
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    for (int i = 0; i < 4; ++i)
                        Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(i).state);
                    Assert.AreEqual(4, CountPasses(graph, ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow).name, true));
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCompaction", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshCulling", true));
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
                RGBuilder graph = CreateGraph(fixture, "DrawListCpuShare", EMeshDrawBackendPolicy.CPUDirect);
                try
                {
                    MeshView view = MeshTestUtility.CameraView(4);
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle depth = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbuffer = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, depth, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, gbuffer, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
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
        public void OcclusionFinal_IsReadByAPlainMainView_WithoutAFrustumProducer()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "OcclusionFinalBind", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshView plain = MeshTestUtility.CameraView(11);
                    var motion = new MeshMotionBinding(fixture.world.transformVersions.current, null);
                    MeshGraphExtension extension = MeshGraphExtension.Of(graph);
                    MeshOcclusionLists occlusion = extension.RecordMeshOcclusionPrediction(graph, plain, motion, default, Matrix4x4.identity, new int2(8, 8), 9);
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle main = graph.CreateMeshDrawList(new MeshDrawListDesc(plain, InfinityPassIDs.RasterGBufferPass));
                    MeshView shadow = MeshView.FromShadow(EMeshViewKind.CascadeShadow, 0xBEEFul, plain.CopyPlanes(), plain.viewPosition, ~0,
                        (uint)ERenderingLayer.Everything, 0, ulong.MaxValue, MeshTestUtility.Lod);
                    RGMeshDrawListHandle cascade = graph.CreateMeshDrawList(new MeshDrawListDesc(shadow, InfinityPassIDs.RasterShadowPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, main, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, cascade, true);
                    Compile(graph);

                    Assert.IsTrue(extension.TryOcclusionFinal(plain, out RGBufferHandle final, out _));
                    MeshDrawListRegistry lists = extension.drawLists;
                    RGBufferHandle bound = lists.GetRecordCopy(main.index).gpuVisibility;
                    Assert.AreEqual(final.handle.index, bound.handle.index);
                    Assert.AreEqual(final.handle.generation, bound.handle.generation);
                    Assert.AreNotEqual(bound.handle.index, lists.GetRecordCopy(cascade.index).gpuVisibility.handle.index);
                    Assert.AreEqual(2, CountPasses(graph, "ComputeMeshCulling", true), "Occlusion prediction plus the shadow frustum. The plain main view adds no frustum producer.");
                    Assert.AreEqual(occlusion.final.viewKey, plain.viewKey);
                    string compiled = graph.DescribeCompiledGraph();
                    int culling = compiled.IndexOf("|ComputeMeshCulling|", System.StringComparison.Ordinal);
                    int compaction = compiled.IndexOf("|ComputeMeshCompaction|", System.StringComparison.Ordinal);
                    Assert.GreaterOrEqual(culling, 0);
                    Assert.Less(culling, compaction, "Compaction reads this frame's culling output.");
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void EmptyAndNonemptyLists_SharingAView_KeepOnlyTheNeededOcclusionProducer()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "OcclusionEmptyList", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshView view = MeshTestUtility.CameraView(32);
                    var motion = new MeshMotionBinding(fixture.world.transformVersions.current, null);
                    MeshGraphExtension extension = MeshGraphExtension.Of(graph);
                    extension.RecordMeshOcclusionPrediction(graph, view, motion, default, Matrix4x4.identity, new int2(8, 8), 9);
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle live = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle empty = graph.CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.RasterForwardPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, live, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterForward, empty, true);
                    Compile(graph);
                    Assert.IsTrue(extension.TryOcclusionFinal(view, out RGBufferHandle final, out _));
                    MeshDrawListRecord liveRecord = extension.drawLists.GetRecordCopy(live.index);
                    MeshDrawListRecord emptyRecord = extension.drawLists.GetRecordCopy(empty.index);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, liveRecord.state);
                    Assert.AreEqual(final.handle.index, liveRecord.gpuVisibility.handle.index);
                    Assert.AreEqual(EMeshDrawListState.Empty, emptyRecord.state);
                    Assert.IsFalse(emptyRecord.gpuVisibility.IsValid());
                    Assert.AreEqual(1, CountPasses(graph, "ComputeMeshCulling", true));
                    Assert.AreEqual(1, CountPasses(graph, "ComputeMeshCompaction", true));
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
                RGBuilder graph = CreateGraph(fixture, "DrawListGpuCull", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshView main = MeshTestUtility.CameraView(5);
                    MeshView cascade = MeshView.FromShadow(EMeshViewKind.CascadeShadow, 0xCAFEul, main.CopyPlanes(), main.viewPosition, ~0,
                        (uint)ERenderingLayer.Everything, 0, ulong.MaxValue, MeshTestUtility.Lod);
                    MeshView local = MeshView.FromShadow(EMeshViewKind.LocalShadow, 0xCAFEul, main.CopyPlanes(), main.viewPosition, ~0,
                        (uint)ERenderingLayer.Everything, 0, ulong.MaxValue, MeshTestUtility.Lod);
                    graph.RequestMeshGPUCulling(main);
                    graph.RequestMeshGPUCulling(cascade);
                    graph.RequestMeshGPUCulling(local);
                    RGTextureHandle target = Target(graph);
                    RGMeshDrawListHandle depth = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbuffer = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle cascadeShadow = graph.CreateMeshDrawList(new MeshDrawListDesc(cascade, InfinityPassIDs.RasterShadowPass));
                    RGMeshDrawListHandle localShadow = graph.CreateMeshDrawList(new MeshDrawListDesc(local, InfinityPassIDs.RasterShadowPass));
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, depth, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, gbuffer, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterCascadeShadow, cascadeShadow, true);
                    AddConsumer(graph, target, CustomSamplerId.RasterLocalShadow, localShadow, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(1, CountPasses(graph, "ComputeMeshCulling", true), "One frustum batch covers every view.");
                    int stride = fixture.world.gpuVisibility.frustumStride;
                    Assert.AreEqual(0, lists.GetRecordCopy(depth.index).gpuVisibilityBase);
                    Assert.AreEqual(0, lists.GetRecordCopy(gbuffer.index).gpuVisibilityBase);
                    Assert.AreEqual(stride, lists.GetRecordCopy(cascadeShadow.index).gpuVisibilityBase);
                    Assert.AreEqual(stride * 2, lists.GetRecordCopy(localShadow.index).gpuVisibilityBase);
                    RGBufferHandle mainVisibility = lists.GetRecordCopy(depth.index).gpuVisibility;
                    Assert.IsTrue(mainVisibility.IsValid());
                    Assert.AreEqual(mainVisibility.handle.index, lists.GetRecordCopy(gbuffer.index).gpuVisibility.handle.index, "Lists of one view share a slice.");
                    Assert.AreEqual(mainVisibility.handle.index, lists.GetRecordCopy(cascadeShadow.index).gpuVisibility.handle.index, "Every frustum view reads the batch.");
                    Assert.AreEqual(mainVisibility.handle.index, lists.GetRecordCopy(localShadow.index).gpuVisibility.handle.index);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [TestCase(EMeshDrawBackendPolicy.Auto, true)]
        [TestCase(EMeshDrawBackendPolicy.CPUDirect, true)]
        [TestCase(EMeshDrawBackendPolicy.CPUDirect, false)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect, true)]
        [TestCase(EMeshDrawBackendPolicy.GPUIndirect, false)]
        public void CompiledGraph_PreparesEachLiveListOnce_BeforeItsFirstConsumer(EMeshDrawBackendPolicy backend, bool live)
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "DrawListPreparation", backend);
                try
                {
                    RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraView(6), InfinityPassIDs.RasterGBufferPass));
                    RGTextureHandle target = Target(graph);
                    AddConsumer(graph, target, CustomSamplerId.RasterGBuffer, draws, live);
                    AddConsumer(graph, target, CustomSamplerId.RasterDepth, draws, live);
                    Compile(graph);

                    MeshDrawListRecord record = MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(draws.index);
                    bool gpu = record.backend == EMeshDrawBackendPolicy.GPUIndirect;
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
                    MeshView firstView = MeshTestUtility.CameraView(101);
                    MeshView secondView = MeshTestUtility.CameraView(102, new float3(1, 0, 0));
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
