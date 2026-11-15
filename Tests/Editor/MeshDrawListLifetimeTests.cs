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
                    RGMeshDrawListHandle drawsHandle = owner.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
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
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterTranslucentT0Pass));
                    Assert.AreEqual(EMeshDrawBackendPolicy.CPUDirect, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(drawsHandle.index).backend);

                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterTranslucentT0, drawsHandle, keep: true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(drawsHandle.index).state);
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
                    RGMeshDrawListHandle staleHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, staleHandle, true);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(2, fixture.world.transformVersions.current.references, "A recorded list retains its frozen transform version.");
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(staleHandle.index).state);
                    Assert.IsTrue(lists.GetRecordCopy(staleHandle.index).build.isCreated);
                    int generation = lists.GraphGeneration;

                    graph.ClearRecordedGraph();
                    Assert.AreEqual(0, lists.Count);
                    Assert.AreNotEqual(generation, lists.GraphGeneration);
                    Assert.IsFalse(lists.IsLive(staleHandle));
                    Assert.AreEqual(1, fixture.world.transformVersions.current.references);

                    RGMeshDrawListHandle freshHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    Assert.AreEqual(staleHandle.index, freshHandle.index);
                    Assert.IsTrue(lists.IsLive(freshHandle));
                    Assert.IsFalse(lists.IsLive(staleHandle), "The graph generation rejects a handle whose index was reused.");
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
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterDepthPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterDepth, drawsHandle, false);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshDrawListRecord record = lists.GetRecordCopy(drawsHandle.index);
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
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshDrawListRecord record = lists.GetRecordCopy(drawsHandle.index);
                    Assert.AreEqual(EMeshDrawListState.Empty, record.state);
                    Assert.IsFalse(record.build.isCreated);
                    Assert.IsNull(record.payload);
                    Assert.AreEqual(0, CountPasses(graph, "UploadMeshData", false));
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                    using (var commands = new CommandBuffer())
                    {
                        Assert.DoesNotThrow(() => lists.SubmitMeshDrawList(commands, drawsHandle));
                        entity = AddMember(fixture);
                        Assert.Throws<System.InvalidOperationException>(() => lists.SubmitMeshDrawList(commands, drawsHandle));
                    }
                }
                finally { MeshTestUtility.Release(graph); }

                graph = CreateGraph(fixture, "NonemptyList", backend);
                try
                {
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Scheduled, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(drawsHandle.index).state);
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
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterGBuffer, drawsHandle, true);
                    Compile(graph);
                    Assert.AreEqual(EMeshDrawListState.Empty, MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(drawsHandle.index).state);
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
                    RGTextureHandle targetHandle = Target(graph);
                    for (int i = 0; i < 4; ++i)
                    {
                        RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(LightContext(i), InfinityPassIDs.RasterShadowPass));
                        AddConsumer(graph, targetHandle, CustomSamplerId.RasterCascadeShadow, drawsHandle, true);
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
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.IsTrue(lists.GetRecordCopy(depthHandle.index).build.isCreated);
                    Assert.IsTrue(lists.GetRecordCopy(gbufferHandle.index).build.isCreated);
                    Assert.AreEqual(2, CountPasses(graph, "UploadMeshData", true), "One upload producer per live list.");
                    Assert.AreEqual(0, CountPasses(graph, "ComputeMeshDrawList", false));
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }

        [Test]
        public void HistoryOccluder_LeavesTheRemainderToTheNextListOfItsContextAndPass()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "HistoryRemainder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshTransformVersion version = fixture.world.transformVersions.current;
                    graph.SetMeshMotion(new MeshMotionBinding(version, version));
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    MeshCullingContext light = LightContext(0);
                    RGTextureHandle targetHandle = Target(graph);
                    graph.RegisterMeshOccluder(main, new MeshOccluder(WrittenTarget(graph), Matrix4x4.identity, new int2(2, 2), 1, true));
                    RGMeshDrawListHandle firstHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle cascadeHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(light, InfinityPassIDs.RasterShadowPass));
                    Assert.Throws<System.InvalidOperationException>(() => graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass)),
                        "The remainder of a history list needs a newer occluder.");
                    graph.RegisterMeshOccluder(main, new MeshOccluder(WrittenTarget(graph), Matrix4x4.identity, new int2(2, 2), 1, false));
                    RGMeshDrawListHandle remainderHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle afterHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle cascadeAgainHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(light, InfinityPassIDs.RasterShadowPass));
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterCascadeShadow, cascadeHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepthRecovery, remainderHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepthRecovery, afterHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterCascadeShadow, cascadeAgainHandle, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(remainderHandle.index).state,
                        "What the history pyramid hid is left to the next list of the context and pass.");
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(afterHandle.index).state, "The remainder exhausts it.");
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(cascadeAgainHandle.index).state,
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
        public void CurrentOccluder_LeavesNothing_AndAnOccluderAfterTheFirstListLeavesNothing()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "CurrentOccluder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    graph.RegisterMeshOccluder(main, new MeshOccluder(WrittenTarget(graph), Matrix4x4.identity, new int2(2, 2), 1, false));
                    RGMeshDrawListHandle depthAgainHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle gbufferAgainHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepthRecovery, depthAgainHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferAgainHandle, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(depthAgainHandle.index).state, "An occluder after the first list reveals nothing it hid.");
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(gbufferHandle.index).state);
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(gbufferAgainHandle.index).state, "This frame's depth leaves nothing for a later list.");
                    Assert.AreEqual(2, CountPasses(graph, "ComputeMeshDrawList", true));
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
                    graph.RegisterMeshOccluder(context, new MeshOccluder(WrittenTarget(graph), Matrix4x4.identity, new int2(2, 2), 1, false));
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle liveHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle emptyHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(context, InfinityPassIDs.RasterForwardPass));
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, liveHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterForward, emptyHandle, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(liveHandle.index).state);
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(emptyHandle.index).state);
                    Assert.IsNull(lists.GetRecordCopy(emptyHandle.index).payload);
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
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle depthHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterDepthPass));
                    RGMeshDrawListHandle gbufferHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(main, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle cascadeShadowHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(LightContext(0), InfinityPassIDs.RasterShadowPass));
                    RGMeshDrawListHandle localShadowHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(LightContext(1), InfinityPassIDs.RasterShadowPass));
                    RGTextureHandle shadowTargetHandle = graph.CreateTexture(new TextureDescriptor(2, 2) { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, dimension = TextureDimension.Tex2D });
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, depthHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, gbufferHandle, true);
                    AddConsumer(graph, shadowTargetHandle, CustomSamplerId.RasterCascadeShadow, cascadeShadowHandle, true);
                    AddConsumer(graph, shadowTargetHandle, CustomSamplerId.RasterLocalShadow, localShadowHandle, true);
                    Compile(graph);
                    Assert.AreEqual(4, CountPasses(graph, "ComputeMeshDrawList", true), "Visibility is evaluated inside each list's producer.");
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

        [Test]
        public void LightContext_IgnoresAnOccluderRegisteredForTheMainContext()
        {
            using (var fixture = new MeshWorldFixture())
            {
                AddMember(fixture);
                RGBuilder graph = CreateGraph(fixture, "LightOccluder", EMeshDrawBackendPolicy.GPUIndirect);
                try
                {
                    MeshTransformVersion version = fixture.world.transformVersions.current;
                    graph.SetMeshMotion(new MeshMotionBinding(version, version));
                    MeshCullingContext main = MeshTestUtility.CameraContext();
                    MeshCullingContext light = LightContext(0);
                    RGTextureHandle earlyHandle = WrittenTarget(graph);
                    graph.RegisterMeshOccluder(main, new MeshOccluder(earlyHandle, Matrix4x4.identity, new int2(2, 2), 1, false));
                    RGMeshDrawListHandle lightHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(light, InfinityPassIDs.RasterShadowPass));
                    RGMeshDrawListHandle lightAgainHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(light, InfinityPassIDs.RasterShadowPass));
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterCascadeShadow, lightHandle, true);
                    AddConsumer(graph, Target(graph), CustomSamplerId.RasterCascadeShadow, lightAgainHandle, true);
                    Compile(graph);

                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    Assert.AreEqual(EMeshDrawListState.Scheduled, lists.GetRecordCopy(lightHandle.index).state);
                    Assert.AreEqual(EMeshDrawListState.Empty, lists.GetRecordCopy(lightAgainHandle.index).state,
                        "The light context has no occluder, so the first list leaves nothing.");
                    int producer = IndexOfPass(graph, "ComputeMeshDrawList");
                    string reads = graph.DescribeCompiledGraph().Split('\n')[producer + 1];
                    StringAssert.DoesNotContain("Texture:" + earlyHandle.handle.index + ",", reads);
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
                    RGMeshDrawListHandle drawsHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(MeshTestUtility.CameraContext(), InfinityPassIDs.RasterGBufferPass));
                    RGTextureHandle targetHandle = Target(graph);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, drawsHandle, live);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterDepth, drawsHandle, live);
                    Compile(graph);

                    MeshDrawListRecord record = MeshGraphExtension.Of(graph).drawLists.GetRecordCopy(drawsHandle.index);
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
                    RGTextureHandle targetHandle = Target(graph);
                    RGMeshDrawListHandle firstHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(firstView, InfinityPassIDs.RasterGBufferPass));
                    RGMeshDrawListHandle secondHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(secondView, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, secondHandle, true);
                    Compile(graph);
                    MeshDrawListRegistry lists = MeshGraphExtension.Of(graph).drawLists;
                    MeshGPUDrawPayload firstPayload = lists.GetRecordCopy(firstHandle.index).payload;
                    MeshGPUDrawPayload secondPayload = lists.GetRecordCopy(secondHandle.index).payload;
                    Assert.AreNotSame(firstPayload, secondPayload);
                    Assert.AreNotSame(firstPayload.indirectArgsHandle.buffer, secondPayload.indirectArgsHandle.buffer);

                    graph.ClearRecordedGraph();
                    fixture.world.gpuBackend.FlushRetired(default);
                    Assert.IsNull(firstPayload.indirectArgsHandle.buffer);
                    Assert.IsNull(secondPayload.indirectArgsHandle.buffer);

                    targetHandle = Target(graph);
                    firstHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(firstView, InfinityPassIDs.RasterGBufferPass));
                    secondHandle = graph.CreateMeshDrawList(new MeshDrawListDesc(secondView, InfinityPassIDs.RasterGBufferPass));
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, firstHandle, true);
                    AddConsumer(graph, targetHandle, CustomSamplerId.RasterGBuffer, secondHandle, true);
                    Compile(graph);
                    MeshGPUDrawPayload retriedFirst = lists.GetRecordCopy(firstHandle.index).payload;
                    MeshGPUDrawPayload retriedSecond = lists.GetRecordCopy(secondHandle.index).payload;
                    CollectionAssert.AreEquivalent(new[] { firstPayload, secondPayload }, new[] { retriedFirst, retriedSecond });
                    Assert.AreNotSame(retriedFirst.indirectArgsHandle.buffer, retriedSecond.indirectArgsHandle.buffer);
                }
                finally { MeshTestUtility.Release(graph); }
            }
        }
    }
}
