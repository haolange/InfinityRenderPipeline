using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowCasterAllocationTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void CascadeFit_ContainsActualReceiverCornersAndUpstreamCaster(bool orthographic, bool customView)
        {
            var cameraObject = new GameObject("Shadow fit camera");
            var lightObject = new GameObject("Shadow fit light");
            var allocator = new ShadowAllocator();
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(2, 3, -8), Quaternion.Euler(12, 23, 0));
                camera.transform.localScale = new Vector3(2, 3, 4);
                camera.aspect = 1.7f;
                camera.orthographic = orthographic;
                camera.orthographicSize = 4;
                camera.fieldOfView = 55;
                if (customView) camera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1))
                    * Matrix4x4.TRS(new Vector3(-3, 1, -6), Quaternion.Euler(3, -12, 0), Vector3.one).inverse;
                Light light = lightObject.AddComponent<Light>();
                light.transform.rotation = Quaternion.Euler(35, 17, 0);
                var corners = new Vector3[4];
                var points = new Vector3[8];
                for (int d = 0; d < 2; d++)
                {
                    camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), d == 0 ? 2 : 20,
                        Camera.MonoOrStereoscopicEye.Mono, corners);
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = corners[i]; p.z = -p.z;
                        points[d * 4 + i] = camera.cameraToWorldMatrix.MultiplyPoint3x4(p);
                    }
                }
                Vector3 receiver = points[0];
                Bounds caster = new Bounds(receiver - light.transform.forward * 50, Vector3.one);
                allocator.FitCascade(camera, light, caster, 2, 20, 256, 4, out var view, out var projection, out var split);
                foreach (Vector3 p in points) AssertInside(projection * view, p);
                // An offscreen upstream caster projects onto a visible receiver and must survive shadow culling.
                AssertInside(projection * view, caster.center);
                Assert.AreEqual(6, split.cullingPlaneCount);
                Assert.AreEqual(0, split.shadowCascadeBlendCullingFactor);
                Assert.Greater(split.cullingSphere.w, 0);
            }
            finally { Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); }
        }

        static void AssertInside(Matrix4x4 matrix, Vector3 point)
        {
            Vector4 clip = matrix * new Vector4(point.x, point.y, point.z, 1);
            Assert.That(Mathf.Abs(clip.x / clip.w), Is.LessThanOrEqualTo(1.00001f));
            Assert.That(Mathf.Abs(clip.y / clip.w), Is.LessThanOrEqualTo(1.00001f));
            Assert.That(Mathf.Abs(clip.z / clip.w), Is.LessThanOrEqualTo(1.00001f));
        }

        [TestCase(FLightRecordFlags.SpotCone)]
        [TestCase(FLightRecordFlags.SpotPyramid)]
        [TestCase(FLightRecordFlags.SpotBox)]
        [TestCase(FLightRecordFlags.Planar)]
        public void LocalEligibility_RejectsBehindCasterBeforeBudget(int shapeFlags)
        {
            GameObject cameraObject = null, lightObject = null;
            try
            {
                using (var fixture = new MeshWorldFixture())
                {
                    MeshWorld world = fixture.world;
                    cameraObject = new GameObject("Caster filter camera");
                    lightObject = new GameObject("Caster filter light");
                    Mesh mesh = fixture.Mesh(1);
                    Material material = fixture.Material(0, 0);
                    Camera camera = cameraObject.AddComponent<Camera>();
                    Light light = lightObject.AddComponent<Light>(); light.range = 20; light.cullingMask = 1;
                    light.renderingLayerMask = 1;
                    MeshEntityId entity;
                    using (MeshWorldUpdate update = world.BeginUpdate())
                    {
                        entity = update.CreateEntity(float4x4.identity, new FBound(new float3(0, 0, -5), new float3(.25f)),
                            MeshTestUtility.Desc(EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow, layerMask: 1), MeshLodChain.Single);
                        update.AddElement(entity, MeshTestUtility.Id(mesh), 0, 0, MeshTestUtility.Id(material), MeshTestUtility.StateOf(material), 0);
                        update.Commit();
                    }
                    var bounds = new ShadowCasterBounds(); bounds.Collect(world);
                    var record = new FLightRecord { flags = shapeFlags | FLightRecordFlags.EnableShadow,
                        positionRange = new Vector4(0, 0, 0, 20), forward = new Vector4(0, 0, 1, .7f),
                        right = new Vector4(1, 0, 0, 0), area = new Vector4(2, 2, 0, 0) };
                    Assert.IsTrue(bounds.TryGetBounds(light, camera, out _), "Caster is eligible before local frustum filtering.");
                    Assert.IsFalse(bounds.HasLocalCaster(record, light, camera, 128, 3), "Behind emitter cannot occupy local shadow budget.");
                    world.WriteEntityPose(entity, float4x4.identity, new FBound(new float3(0, 0, 5), new float3(.25f)));
                    bounds.Collect(world);
                    Assert.IsTrue(bounds.HasLocalCaster(record, light, camera, 128, 3));
                    light.cullingMask = 2;
                    Assert.IsFalse(bounds.HasLocalCaster(record, light, camera, 128, 3));
                    light.cullingMask = 1; light.renderingLayerMask = 2;
                    Assert.IsFalse(bounds.HasLocalCaster(record, light, camera, 128, 3));
                    light.renderingLayerMask = 1;
                    using (MeshWorldUpdate update = world.BeginUpdate())
                    {
                        update.SetEntityDesc(entity, MeshTestUtility.Desc(EMeshEntityFlags.Visible | EMeshEntityFlags.CastShadow, layerMask: 1, sceneCullingMask: 0));
                        update.Commit();
                    }
                    bounds.Collect(world);
                    Assert.IsFalse(bounds.HasLocalCaster(record, light, camera, 128, 3));
                    using (MeshWorldUpdate update = world.BeginUpdate())
                    {
                        update.SetEntityDesc(entity, MeshTestUtility.Desc(EMeshEntityFlags.Visible, layerMask: 1));
                        update.Commit();
                    }
                    bounds.Collect(world);
                    Assert.IsFalse(bounds.TryGetBounds(light, camera, out _), "Only Shadow-pass members are casters.");
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            }
        }
    }
}
