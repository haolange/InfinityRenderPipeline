using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.RenderGraph.Tests
{
    public class RGDrawListLifetimeTests
    {
        [Test]
        public void Payload_RetireStaysOutOfPoolUntilFlush()
        {
            MeshDrawGPUBackend.FlushRetiredPayloads();
            MeshDrawGpuPayload payload = MeshDrawGPUBackend.RentPayload();
            MeshDrawGPUBackend.RetirePayload(payload);

            MeshDrawGpuPayload other = MeshDrawGPUBackend.RentPayload();
            Assert.AreNotSame(payload, other);

            MeshDrawGPUBackend.ReturnPayload(other);
            MeshDrawGPUBackend.FlushRetiredPayloads();

            MeshDrawGpuPayload again = MeshDrawGPUBackend.RentPayload();
            Assert.AreSame(payload, again);
            MeshDrawGPUBackend.ReturnPayload(again);
        }

        [Test]
        public void GpuIndirect_PrepareLive_DoesNotAcquireVisibility()
        {
            if (!TryBindMeshDrawShader())
            {
                Assert.Ignore("MeshDraw compute shader is not available.");
            }

            using (var scene = new MeshScene(16))
            {
                var pool = new ResourcePool();
                var world = new MeshWorld(scene, pool);
                try
                {
                    MeshPipelineDiagnostics.Reset();
                    world.SetBackendOverride(EMeshBackendPolicy.GpuIndirect);
                    var context = new RGDrawListContext();
                    MeshView view = TestView();
                    MeshPassContext pass = world.BindPass(view, MeshPassId.GBuffer);
                    RGDrawListRef draws = context.Declare(
                        world.Processor,
                        pass,
                        MeshVisibilityHandle.Invalid,
                        world.VisibilityShare,
                        view,
                        MeshPassId.GBuffer,
                        world);
                    context.MarkLiveConsumer(draws.index, 0);
                    context.PrepareLive(world);

                    RGDrawListRecord record = context.GetRecordCopy(draws.index);
                    Assert.AreEqual(EMeshBackendPolicy.GpuIndirect, record.selectedBackend);
                    Assert.IsFalse(record.visibilityHandle.IsValid);
                    Assert.IsFalse(record.extract.isCreated);
                    Assert.AreEqual(0, MeshPipelineDiagnostics.VisibilityProductionsPerFrame);
                    context.ReleaseAll();
                }
                finally
                {
                    world.Dispose();
                    pool.Dispose();
                    MeshDrawGPUBackend.SetShader(null);
                }
            }
        }

        [Test]
        public void UnusedDrawList_DoesNotAcquireOrDispatchCull()
        {
            using (var scene = new MeshScene(16))
            {
                var pool = new ResourcePool();
                var world = new MeshWorld(scene, pool);
                try
                {
                    MeshPipelineDiagnostics.Reset();
                    world.SetBackendOverride(EMeshBackendPolicy.CpuDirect);
                    var context = new RGDrawListContext();
                    MeshView view = TestView();
                    MeshPassContext pass = world.BindPass(view, MeshPassId.GBuffer);
                    context.Declare(
                        world.Processor,
                        pass,
                        MeshVisibilityHandle.Invalid,
                        world.VisibilityShare,
                        view,
                        MeshPassId.GBuffer,
                        world);
                    context.PrepareLive(world);

                    Assert.AreEqual(0, MeshPipelineDiagnostics.VisibilityProductionsPerFrame);
                    Assert.IsFalse(context.HasLiveView(view));
                    context.ReleaseAll();
                }
                finally
                {
                    world.Dispose();
                    pool.Dispose();
                }
            }
        }

        [Test]
        public void CpuDirect_PrepareLive_InternsVisibility()
        {
            using (var scene = new MeshScene(16))
            {
                var pool = new ResourcePool();
                var world = new MeshWorld(scene, pool);
                try
                {
                    MeshPipelineDiagnostics.Reset();
                    world.SetBackendOverride(EMeshBackendPolicy.CpuDirect);
                    var context = new RGDrawListContext();
                    MeshView view = TestView();
                    MeshPassContext pass = world.BindPass(view, MeshPassId.GBuffer);
                    RGDrawListRef draws = context.Declare(
                        world.Processor,
                        pass,
                        MeshVisibilityHandle.Invalid,
                        world.VisibilityShare,
                        view,
                        MeshPassId.GBuffer,
                        world);
                    context.MarkLiveConsumer(draws.index, 0);
                    context.PrepareLive(world);

                    RGDrawListRecord record = context.GetRecordCopy(draws.index);
                    Assert.AreEqual(EMeshBackendPolicy.CpuDirect, record.selectedBackend);
                    Assert.IsTrue(record.visibilityHandle.IsValid);
                    Assert.AreEqual(1, MeshPipelineDiagnostics.VisibilityProductionsPerFrame);
                    context.ReleaseAll();
                }
                finally
                {
                    world.Dispose();
                    pool.Dispose();
                }
            }
        }

        [Test]
        public void CreateDrawList_RecordsOneMeshGpuCullPerView()
        {
            if (!TryBindMeshDrawShader())
            {
                Assert.Ignore("MeshDraw compute shader is not available.");
            }

            using (var scene = new MeshScene(16))
            {
                var pool = new ResourcePool();
                var world = new MeshWorld(scene, pool);
                var graph = new RGBuilder("MeshGpuCullRecord");
                try
                {
                    world.SetBackendOverride(EMeshBackendPolicy.GpuIndirect);
                    world.UpdateResidency();
                    graph.SetMeshWorld(world);
                    MeshView view = TestView();
                    graph.CreateDrawList(view, MeshPassId.Depth);
                    graph.CreateDrawList(view, MeshPassId.GBuffer);
                    Assert.AreEqual(1, CountSamplerPasses(graph, "ComputeMeshDrawCull"));
                    Assert.AreEqual(0, CountSamplerPasses(graph, "ComputeMeshDrawCullCascade"));
                }
                finally
                {
                    graph.Dispose();
                    world.Dispose();
                    pool.Dispose();
                    MeshDrawGPUBackend.SetShader(null);
                }
            }
        }

        [Test]
        public void UnusedDrawList_DoesNotRunRecordedMeshGpuCull()
        {
            if (!TryBindMeshDrawShader())
            {
                Assert.Ignore("MeshDraw compute shader is not available.");
            }

            using (var scene = new MeshScene(16))
            {
                var pool = new ResourcePool();
                var world = new MeshWorld(scene, pool);
                var graph = new RGBuilder("MeshGpuCullUnused");
                try
                {
                    MeshPipelineDiagnostics.Reset();
                    world.SetBackendOverride(EMeshBackendPolicy.GpuIndirect);
                    world.UpdateResidency();
                    graph.SetMeshWorld(world);
                    MeshView view = TestView();
                    graph.CreateDrawList(view, MeshPassId.Depth);
                    Assert.AreEqual(1, CountSamplerPasses(graph, "ComputeMeshDrawCull"));

                    MethodInfo compile = typeof(RGBuilder).GetMethod("CompilePass", BindingFlags.Instance | BindingFlags.NonPublic);
                    compile.Invoke(graph, null);
                    FieldInfo recordsField = typeof(RGBuilder).GetField("m_DrawListRecords", BindingFlags.Instance | BindingFlags.NonPublic);
                    var records = (RGDrawListContext)recordsField.GetValue(graph);
                    Assert.IsFalse(records.HasLiveView(view));
                    Assert.AreEqual(0, MeshPipelineDiagnostics.GpuCullDispatchesPerFrame);
                    Assert.AreEqual(0, MeshPipelineDiagnostics.VisibilityProductionsPerFrame);
                }
                finally
                {
                    graph.Dispose();
                    world.Dispose();
                    pool.Dispose();
                    MeshDrawGPUBackend.SetShader(null);
                }
            }
        }

        [Test]
        public void EnsureMeshGpuCull_UsesShadowSamplerAndDedupsCreate()
        {
            if (!TryBindMeshDrawShader())
            {
                Assert.Ignore("MeshDraw compute shader is not available.");
            }

            using (var scene = new MeshScene(16))
            {
                var pool = new ResourcePool();
                var world = new MeshWorld(scene, pool);
                var graph = new RGBuilder("MeshGpuCullEnsure");
                try
                {
                    world.SetBackendOverride(EMeshBackendPolicy.GpuIndirect);
                    world.UpdateResidency();
                    graph.SetMeshWorld(world);
                    MeshView main = TestView();
                    MeshView cascade = MeshView.FromCascadeShadow(
                        0xCAFEul,
                        main.CopyPlanes(),
                        main.viewPosition,
                        ~0,
                        (uint)ERenderingLayer.Everything,
                        0);
                    graph.EnsureMeshGpuCull(main);
                    graph.EnsureMeshGpuCull(cascade);
                    graph.CreateDrawList(main, MeshPassId.Depth);
                    graph.CreateDrawList(cascade, MeshPassId.Shadow);
                    Assert.AreEqual(1, CountSamplerPasses(graph, "ComputeMeshDrawCull"));
                    Assert.AreEqual(1, CountSamplerPasses(graph, "ComputeMeshDrawCullCascade"));
                    Assert.AreEqual(0, CountSamplerPasses(graph, "ComputeMeshDrawCullLocal"));
                }
                finally
                {
                    graph.Dispose();
                    world.Dispose();
                    pool.Dispose();
                    MeshDrawGPUBackend.SetShader(null);
                }
            }
        }

        struct DrawPreparationData { public RGDrawListRef draws; }
        [TestCase(EMeshBackendPolicy.CpuDirect,true)]
        [TestCase(EMeshBackendPolicy.GpuIndirect,true)]
        [TestCase(EMeshBackendPolicy.GpuIndirect,false)]
        public void CompiledGraph_PreparesSharedLiveListsOnce_AndOmitsDeadPreparation(EMeshBackendPolicy backend,bool live)
        {
            if(!TryBindMeshDrawShader())Assert.Ignore("Mesh compute unavailable.");
            using(var scene=new MeshScene(16))
            {
                var update=scene.BeginUpdate();var transform=update.CreateTransform(float4x4.identity);
                var instance=update.CreateInstance(transform,new FBound(float3.zero,new float3(1)),~0,1,EMeshInstanceFlags.Visible,EMotionType.Object,ECastShadowMethod.Off);
                update.CreateDraw(instance,100,0,1,EPassEligibility.GBuffer,2000,0);update.Commit();
                var pool=new ResourcePool();var world=new MeshWorld(scene,pool);var graph=new RGBuilder("PreparationContract",pool);
                try
                {
                    world.SetBackendOverride(backend);world.UpdateResidency();graph.SetMeshWorld(world);
                    var draws=graph.CreateDrawList(TestView(),MeshPassId.GBuffer);
                    var target=graph.CreateTexture(new TextureDescriptor(1,1){colorFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,dimension=TextureDimension.Tex2D});
                    for(int i=0;i<2;i++)
                    {
                        using(var pass=graph.AddRasterPass<DrawPreparationData>(ProfilingSampler.Get(i==0?CustomSamplerId.RasterGBuffer:CustomSamplerId.RasterDepth)))
                        {
                            pass.SetColorAttachment(target,0,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);
                            pass.EnablePassCulling(!live);ref var data=ref pass.GetPassData<DrawPreparationData>();data.draws=pass.UseDrawList(draws);
                            pass.SetExecuteFunc((in DrawPreparationData data,in RGRasterEncoder commands,RGObjectPool objects)=>commands.Draw(data.draws));
                        }
                    }
                    typeof(RGBuilder).GetMethod("CompilePass",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(graph,null);
                    Assert.AreEqual(live?1:0,CountSamplerPasses(graph,"UploadMeshDrawData"));
                    Assert.AreEqual(live&&backend==EMeshBackendPolicy.GpuIndirect?1:0,CountSamplerPasses(graph,"ComputeMeshDrawCompact"));
                    if(live)
                    {
                        string compiled=graph.DescribeCompiledGraph();
                        Assert.Less(compiled.IndexOf("UploadMeshDrawData"),compiled.IndexOf("RasterGBuffer"));
                        if(backend==EMeshBackendPolicy.GpuIndirect)Assert.Less(compiled.IndexOf("ComputeMeshDrawCompact"),compiled.IndexOf("RasterGBuffer"));
                    }
                }
                finally{graph.Dispose();world.Dispose();pool.Dispose();MeshDrawGPUBackend.SetShader(null);}
            }
        }

        static bool TryBindMeshDrawShader()
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            MeshDrawGPUBackend.SetShader(shader);
            return MeshDrawGPUBackend.SupportsIndirect;
        }

        static MeshView TestView(ulong viewKey = 1)
        {
            var view = new MeshView
            {
                viewKey = viewKey,
                kind = EMeshViewKind.Main,
                layerMask = ~0,
                sceneCullingMask = ulong.MaxValue,
                renderingLayerMask = (uint)ERenderingLayer.Everything,
                enableVisibility = true,
                planeCount = 6
            };
            Plane[] planes =
            {
                new Plane(Vector3.right, 10f),
                new Plane(Vector3.left, 10f),
                new Plane(Vector3.up, 10f),
                new Plane(Vector3.down, 10f),
                new Plane(Vector3.forward, 10f),
                new Plane(Vector3.back, 10f)
            };
            for (int i = 0; i < planes.Length; ++i)
            {
                view.SetPlane(i, planes[i]);
            }

            return view;
        }

        static int CountSamplerPasses(RGBuilder graph, string samplerName)
        {
            FieldInfo field = typeof(RGBuilder).GetField("m_PassList", BindingFlags.Instance | BindingFlags.NonPublic);
            var passes = (List<IRGPass>)field.GetValue(graph);
            int count = 0;
            for (int i = 0; i < passes.Count; ++i)
            {
                if (passes[i].customSampler != null && passes[i].customSampler.name == samplerName)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
