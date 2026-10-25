using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshViewTests
    {
        [Test]
        public void CpuCameraCullingPreservesExplicitCullingMatrix()
        {
            var go = new GameObject("MeshViewTests.CustomCulling");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.cullingMatrix = Matrix4x4.Ortho(-3, 7, -2, 5, .2f, 90)
                    * Matrix4x4.Translate(new Vector3(11, -4, 8));
                var expected = GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix);
                using var scene = new MeshScene(16);
                var result = MeshVisibilityUtility.CullInstances(scene, camera, true);
                try
                {
                    for (int i = 0; i < 6; ++i) Assert.AreEqual((FPlane)expected[i], result.frustum[i]);
                }
                finally { result.Release(); }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SameCameraSameFrame_ViewKeysMatch()
        {
            var go = new GameObject("MeshViewTests.Camera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                Assert.IsTrue(camera.TryGetCullingParameters(out ScriptableCullingParameters culling));
                MeshView first = MeshView.FromCamera(camera, ref culling);
                MeshView second = MeshView.FromCamera(camera, ref culling);
                Assert.AreEqual(first.viewKey, second.viewKey);
                Assert.AreEqual(UnityEntityId.ToUInt64(camera), first.viewKey);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CascadeAndLocal_ViewKeysDifferFromMainCamera()
        {
            var go = new GameObject("MeshViewTests.Camera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                Assert.IsTrue(camera.TryGetCullingParameters(out ScriptableCullingParameters culling));
                MeshView main = MeshView.FromCamera(camera, ref culling);
                Plane[] planes = main.CopyPlanes();
                ulong lightKey = main.viewKey ^ 0x1111ul;
                MeshView cascade = MeshView.FromCascadeShadow(lightKey, planes, main.viewPosition, ~0, (uint)ERenderingLayer.Everything, 0);
                MeshView local = MeshView.FromLocalShadow(lightKey, planes, main.viewPosition, ~0, (uint)ERenderingLayer.Everything, 0);
                Assert.AreNotEqual(main.viewKey, cascade.viewKey);
                Assert.AreNotEqual(main.viewKey, local.viewKey);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void KindMapsToFixedPolicyId()
        {
            Assert.AreEqual(MeshVisibilityShare.PolicyMainFrustum, Kind(EMeshViewKind.Main).PolicyId);
            Assert.AreEqual(MeshVisibilityShare.PolicyMainFrustum, Kind(EMeshViewKind.SceneView).PolicyId);
            Assert.AreEqual(MeshVisibilityShare.PolicyMainFrustum, Kind(EMeshViewKind.Preview).PolicyId);
            Assert.AreEqual(MeshVisibilityShare.PolicyCascadeShadow, Kind(EMeshViewKind.CascadeShadow).PolicyId);
            Assert.AreEqual(MeshVisibilityShare.PolicyLocalShadow, Kind(EMeshViewKind.LocalShadow).PolicyId);
            Assert.IsFalse(Kind(EMeshViewKind.Main).FilterRenderingLayers);
            Assert.IsTrue(Kind(EMeshViewKind.CascadeShadow).FilterRenderingLayers);
            Assert.IsTrue(Kind(EMeshViewKind.LocalShadow).FilterRenderingLayers);
        }

        [Test]
        public void ManualView_DefaultSceneScopeMatchesExplicitAllAndPreservesExplicitEmpty()
        {
            var view = new MeshView { viewKey = 91, kind = EMeshViewKind.Main, layerMask = ~0,
                renderingLayerMask = 1, enableVisibility = true, planeCount = 6 };
            Assert.AreEqual(ulong.MaxValue, view.sceneCullingMask);
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Ortho(-10, 10, -10, 10, -10, 10));
            for (int i = 0; i < 6; i++) view.SetPlane(i, planes[i]);
            var explicitAll = view; explicitAll.sceneCullingMask = ulong.MaxValue;
            var empty = view; empty.sceneCullingMask = 0;
            Assert.AreEqual(new MeshGpuVisibilityKey(view), new MeshGpuVisibilityKey(explicitAll));
            Assert.AreNotEqual(new MeshGpuVisibilityKey(view), new MeshGpuVisibilityKey(empty));
            foreach (ulong mask in new[] { 0ul, 1ul, 1ul << 63, 0x8000000000000001ul, ulong.MaxValue })
            {
                var roundTrip = view; roundTrip.sceneCullingMask = mask;
                Assert.AreEqual(mask, roundTrip.sceneCullingMask);
            }
            using (var scene = new MeshScene(16))
            using (var share = new MeshVisibilityShare())
            {
                using (var update = scene.BeginUpdate())
                {
                    update.CreateInstance(update.CreateTransform(float4x4.identity), new FBound(float3.zero, new float3(1)),
                        ~0, 1, EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off, sceneCullingMask: 1ul << 63);
                    update.Commit();
                }
                share.BeginFrame(scene.VisibilityRevision);
                var original = share.Acquire(scene, view);
                var all = share.Acquire(scene, explicitAll);
                var none = share.Acquire(scene, empty);
                Assert.AreEqual(original, all, "Default and explicit-all CPU cache identity must agree.");
                Assert.AreEqual(1, share.GetResult(original).instanceVisibility[0]);
                Assert.AreEqual(0, share.GetResult(none).instanceVisibility[0]);
                share.Release(original); share.Release(all); share.Release(none);
            }
        }

        [Test]
        public void SceneMasks_IsolateOverlappingInstancesOnCpuAndGpuAndSurviveRollback()
        {
            Assert.IsTrue(SystemInfo.supportsComputeShaders, "The scene-mask GPU contract requires compute.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            Assert.IsNotNull(shader);
            int kernel = shader.FindKernel("CullFrustum");
            using (var scene = new MeshScene(16))
            using (var share = new MeshVisibilityShare())
            {
                var pool = new ResourcePool();
                var gpu = new GpuScene(pool, scene);
                var output = new ComputeBuffer(2, sizeof(uint));
                try
                {
                    MeshInstanceId low, high;
                    using (var update = scene.BeginUpdate())
                    {
                        var bounds = new FBound(float3.zero, new float3(1));
                        low = update.CreateInstance(update.CreateTransform(float4x4.identity), bounds, ~0, 1,
                            EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off, sceneCullingMask: 1);
                        high = update.CreateInstance(update.CreateTransform(float4x4.identity), bounds, ~0, 1,
                            EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Off, sceneCullingMask: 1ul << 63);
                        update.Commit();
                    }
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Ortho(-10, 10, -10, 10, -10, 10));
                    gpu.Update();
                    foreach (ulong mask in new[] { 1ul, 1ul << 63, 0ul, 1ul | (1ul << 63) })
                        AssertSceneVisibility(scene, gpu, shader, kernel, output, planes, mask,
                            (mask & 1) != 0 ? 1u : 0u, (mask & (1ul << 63)) != 0 ? 1u : 0u);

                    int revision = scene.VisibilityRevision;
                    scene.ClearBoundsDirtyRange();
                    scene.ClearTransformDirtyRange();
                    using (var update = scene.BeginUpdate())
                    {
                        update.SetInstanceSceneCullingMask(low, 1ul << 63);
                        Assert.Greater(scene.VisibilityRevision, revision);
                        Assert.IsTrue(scene.HasBoundsDirtyRange);
                    }
                    Assert.AreEqual(revision, scene.VisibilityRevision);
                    Assert.IsFalse(scene.HasBoundsDirtyRange);
                    Assert.IsTrue(scene.TryGetInstance(low, out var restored));
                    Assert.AreEqual(1ul, restored.sceneCullingMask);
                    AssertSceneVisibility(scene, gpu, shader, kernel, output, planes, 1, 1, 0);

                    var view = new MeshView { viewKey = 31, layerMask = ~0, sceneCullingMask = 1,
                        renderingLayerMask = 1, enableVisibility = true, planeCount = 6 };
                    for (int i = 0; i < 6; i++) view.SetPlane(i, planes[i]);
                    share.BeginFrame(scene.VisibilityRevision);
                    var first = share.Acquire(scene, view);
                    var other = view; other.sceneCullingMask = 1ul << 63;
                    var second = share.Acquire(scene, other);
                    Assert.AreNotEqual(first, second);
                    Assert.AreNotEqual(new MeshGpuVisibilityKey(view), new MeshGpuVisibilityKey(other));
                    share.Release(first); share.Release(second);
                    using (var update = scene.BeginUpdate())
                    {
                        update.SetInstanceSceneCullingMask(low, 1ul << 63);
                        update.Commit();
                    }
                    gpu.Update();
                    AssertSceneVisibility(scene, gpu, shader, kernel, output, planes, 1, 0, 0);
                    AssertSceneVisibility(scene, gpu, shader, kernel, output, planes, 1ul << 63, 1, 1);
                }
                finally { output.Dispose(); gpu.Dispose(); pool.Dispose(); }
            }
        }

        static void AssertSceneVisibility(MeshScene scene, GpuScene gpu, ComputeShader shader, int kernel,
            ComputeBuffer output, Plane[] planes, ulong mask, uint first, uint second)
        {
            var cpu = MeshVisibilityUtility.CullInstances(scene, planes, true, sceneCullingMask: mask);
            try
            {
                Assert.AreEqual(first, cpu.instanceVisibility[0]);
                Assert.AreEqual(second, cpu.instanceVisibility[1]);
            }
            finally { cpu.Release(); }
            var vectors = new Vector4[6];
            for (int i = 0; i < 6; i++) vectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            shader.SetVectorArray("_FrustumPlanes", vectors);
            shader.SetInt("_InstanceCount", 2);
            shader.SetInt("_ViewLayerMask", ~0);
            shader.SetInt("_ViewRenderingLayerMask", 1);
            shader.SetInt("_FilterRenderingLayers", 0);
            shader.SetInt("_ViewExcludeFlags", 0);
            shader.SetInt("_ViewSceneCullingMaskLow", unchecked((int)mask));
            shader.SetInt("_ViewSceneCullingMaskHigh", unchecked((int)(mask >> 32)));
            shader.SetBuffer(kernel, "_InstanceBoundsCenter", gpu.BoundsCenterBuffer.buffer);
            shader.SetBuffer(kernel, "_InstanceBoundsExtent", gpu.BoundsExtentBuffer.buffer);
            shader.SetBuffer(kernel, "_InstanceFlags", gpu.InstanceFlagsBuffer.buffer);
            shader.SetBuffer(kernel, "_InstanceLayerMask", gpu.InstanceLayerMaskBuffer.buffer);
            shader.SetBuffer(kernel, "_InstanceSceneCullingMask", gpu.InstanceSceneCullingMaskBuffer.buffer);
            shader.SetBuffer(kernel, "_InstanceRenderingLayer", gpu.InstanceRenderingLayerBuffer.buffer);
            shader.SetBuffer(kernel, "_Visibility", output);
            shader.Dispatch(kernel, 1, 1, 1);
            var actual = new uint[2];
            output.GetData(actual);
            CollectionAssert.AreEqual(new[] { first, second }, actual, "GPU scene mask " + mask.ToString("X16"));
        }

        static MeshView Kind(EMeshViewKind kind)
        {
            return new MeshView { kind = kind };
        }
    }
}
