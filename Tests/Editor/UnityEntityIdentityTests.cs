using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

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
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    MeshEntityId entity = MeshTestUtility.CreateEntity(update, float3.zero);
                    update.AddElement(entity, first, 0, first, MeshTestUtility.Unrouted, 0);
                    update.AddElement(entity, second, 0, second, MeshTestUtility.Unrouted, 0);
                    update.Commit();
                }
                Assert.AreEqual(2, world.sectionCount, "Meshes that differ only in identity generation bits are distinct sections.");
                Assert.AreEqual(2, world.materialCount);
            }

            Plane[] planes = MeshTestUtility.BoxPlanes();
            var slice0 = new MeshVisibilityKey(MeshView.FromShadow(EMeshViewKind.CascadeShadow, first, planes, float3.zero, 7, 1, 0, ulong.MaxValue), false);
            var slice1 = new MeshVisibilityKey(MeshView.FromShadow(EMeshViewKind.CascadeShadow, first, planes, float3.zero, 7, 1, 1, ulong.MaxValue), false);
            var replaced = new MeshVisibilityKey(MeshView.FromShadow(EMeshViewKind.CascadeShadow, second, planes, float3.zero, 7, 1, 0, ulong.MaxValue), false);
            Assert.AreNotEqual(slice0, slice1);
            Assert.AreNotEqual(slice0, replaced);
        }
    }
}
