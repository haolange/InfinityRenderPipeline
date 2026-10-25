using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;
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
            using var scene = new MeshScene(16);
            try
            {
                a.AddComponent<MeshFilter>().sharedMesh = mesh; var rendererA = a.AddComponent<MeshRenderer>();
                b.AddComponent<MeshFilter>().sharedMesh = mesh; b.AddComponent<MeshRenderer>();
                using (var update = scene.BeginUpdate())
                {
                    for (int i = 0; i < 2; ++i)
                    {
                        var transform = update.CreateTransform(float4x4.Translate(new float3(i, 0, 0)));
                        var instance = update.CreateInstance(transform, new FBound(float3.zero, new float3(1)), ~0, 1,
                            EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off);
                        update.CreateDraw(instance, UnityEntityId.ToUInt64(mesh), 0, 0, EPassEligibility.GBuffer, 2000, 0);
                    }
                    update.Commit();
                }
                env.CollectGeometry(scene);
                Assert.AreEqual(2, env.Geometry.Count(x => x.mesh == mesh && x.native));
                Assert.AreEqual(2, env.Geometry.Count(x => x.mesh == mesh && !x.native));
                rendererA.enabled = false;
                env.CollectGeometry(scene);
                Assert.AreEqual(1, env.Geometry.Count(x => x.mesh == mesh && x.native));
                rendererA.enabled = true; rendererA.forceRenderingOff = true;
                env.CollectGeometry(scene);
                Assert.AreEqual(1, env.Geometry.Count(x => x.mesh == mesh && x.native));
                b.SetActive(false);
                env.CollectGeometry(scene);
                Assert.AreEqual(0, env.Geometry.Count(x => x.mesh == mesh && x.native));
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
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>();
                env.CollectGeometry(null);
                var previous = env.Geometry.Single(x => x.mesh == mesh).matrix;
                go.transform.position = new Vector3(3, 2, 1);
                env.CollectGeometry(null);
                Assert.AreNotEqual(previous, env.Geometry.Single(x => x.mesh == mesh).matrix);
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
