using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class MeshViewTests
    {
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

        static MeshView Kind(EMeshViewKind kind)
        {
            return new MeshView { kind = kind };
        }
    }
}
