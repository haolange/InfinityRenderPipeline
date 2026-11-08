using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;
using InfinityTech.Rendering.RenderGraph;
using Unity.Mathematics;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RayTracingInputContractTests
    {
        [Test]
        public void NativeAndCustomInstancesSharingMeshRemainDistinct()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var a = new GameObject("NativeA"); var b = new GameObject("NativeB");
            var env = new InfinityRayTracingEnvironment();
            try
            {
                using (var fixture = new MeshWorldFixture())
                {
                    MeshWorld world = fixture.world;
                    a.AddComponent<MeshFilter>().sharedMesh = mesh; var rendererA = a.AddComponent<MeshRenderer>();
                    b.AddComponent<MeshFilter>().sharedMesh = mesh; b.AddComponent<MeshRenderer>();
                    using (MeshWorldUpdate update = world.BeginUpdate())
                    {
                        for (int i = 0; i < 2; ++i)
                        {
                            MeshEntityId entity = MeshTestUtility.CreateEntity(update, new float3(i, 0, 0));
                            update.AddElement(entity, UnityEntityId.ToUInt64(mesh), 0, 0, 0, default, 0);
                        }
                        update.Commit();
                    }
                    env.CollectGeometry(world);
                    Assert.AreEqual(2, env.Geometry.Count(x => x.mesh == mesh && x.native));
                    Assert.AreEqual(2, env.Geometry.Count(x => x.mesh == mesh && !x.native));
                    rendererA.enabled = false;
                    env.CollectGeometry(world);
                    Assert.AreEqual(1, env.Geometry.Count(x => x.mesh == mesh && x.native));
                    rendererA.enabled = true; rendererA.forceRenderingOff = true;
                    env.CollectGeometry(world);
                    Assert.AreEqual(1, env.Geometry.Count(x => x.mesh == mesh && x.native));
                    b.SetActive(false);
                    env.CollectGeometry(world);
                    Assert.AreEqual(0, env.Geometry.Count(x => x.mesh == mesh && x.native));
                    Assert.AreEqual(2, env.Geometry.Count(x => x.mesh == mesh && !x.native), "Custom entities do not follow native renderer state.");
                }
            }
            finally { env.Dispose(); Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void NativeTransformChangesDoNotNeedCustomSceneRevision()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var go = new GameObject("MovingNative"); var env = new InfinityRayTracingEnvironment();
            try
            {
                using (var fixture = new MeshWorldFixture())
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>();
                    env.CollectGeometry(fixture.world);
                    var previous = env.Geometry.Single(x => x.mesh == mesh).matrix;
                    go.transform.position = new Vector3(3, 2, 1);
                    env.CollectGeometry(fixture.world);
                    Assert.AreNotEqual(previous, env.Geometry.Single(x => x.mesh == mesh).matrix);
                }
            }
            finally { env.Dispose(); Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void FailedQueueKeepsAccelerationBuildPending()
        {
            var env = new InfinityRayTracingEnvironment();
            try
            {
                ((IRGPassQueueObserver)env).OnQueueFailed();
                Assert.IsTrue(env.NeedsBuild);
                Assert.IsTrue(env.NeedsBuild, "Reading build state must not accept production.");
                ((IRGPassQueueObserver)env).OnQueued();
                Assert.IsFalse(env.NeedsBuild, "A second camera shares the queued build.");
                env.CompleteFrame(false);
                Assert.IsTrue(env.NeedsBuild, "Failed submission cannot accept the queued build.");
                ((IRGPassQueueObserver)env).OnQueued();
                env.CompleteFrame(true);
                Assert.IsFalse(env.NeedsBuild);
            }
            finally { env.Dispose(); }
        }
    }
}
