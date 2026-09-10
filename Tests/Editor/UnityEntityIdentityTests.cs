using NUnit.Framework;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class UnityEntityIdentityTests
    {
        [Test]
        public void TransientMeshAndMaterial_RoundTripCompleteNativeIdentity()
        {
            var mesh = new Mesh { name = "NativeIdentityProbe", hideFlags = HideFlags.DontSave };
            var material = new Material(Shader.Find("InfinityPipeline/InfinityLit-Instanced")) { hideFlags = HideFlags.DontSave };
            try
            {
                Assert.AreSame(mesh, UnityEntityId.ToObject<Mesh>(UnityEntityId.ToUInt64(mesh)));
                Assert.AreSame(material, UnityEntityId.ToObject<Material>(UnityEntityId.ToUInt64(material)));
                Assert.AreEqual(mesh.GetEntityId(), UnityEntityId.FromUInt64(UnityEntityId.ToUInt64(mesh)));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
        [Test]
        public void UpperIdentityBitsAndShadowSubview_ArePartOfEquality()
        {
            ulong first = (768ul << 32) | 42, second = (769ul << 32) | 42;
            Assert.AreNotEqual(new MeshGroupingKey(first, 0, first, 0), new MeshGroupingKey(second, 0, first, 0));
            Assert.AreNotEqual(new MeshPassDrawCacheKey(first, 0, 0, first, 0, first, 0, 0, 0, 0), new MeshPassDrawCacheKey(second, 0, 0, first, 0, first, 0, 0, 0, 0));
            var slice0 = new MeshVisibilitySignature(1, first, 7, 1, MeshVisibilityShare.PolicyCascadeShadow, 0);
            var slice1 = new MeshVisibilitySignature(1, first, 7, 1, MeshVisibilityShare.PolicyCascadeShadow, 1);
            var replaced = new MeshVisibilitySignature(1, second, 7, 1, MeshVisibilityShare.PolicyCascadeShadow, 0);
            Assert.AreNotEqual(slice0, slice1); Assert.AreNotEqual(slice0, replaced);
        }
    }
}
