using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderingLayerTests
    {
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(128u)]
        [TestCase(255u)]
        public void EntityIndexedLayer_RoundTripsToGpuAndRollsBack(uint mask)
        {
            using (var fixture = new MeshWorldFixture())
            {
                MeshWorld world = fixture.world;
                MeshEntityId entity;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    entity = MeshTestUtility.CreateEntity(update, float3.zero, renderingLayerMask: mask);
                    update.Commit();
                }
                Assert.IsTrue(world.TryGetEntity(entity, out MeshEntity record));
                Assert.AreEqual(mask, record.desc.renderingLayerMask);
                world.UpdateResidency();
                Assert.AreEqual(mask, ReadLayer(world, entity));

                using (MeshWorldUpdate update = world.BeginUpdate())
                    update.SetEntityDesc(entity, MeshTestUtility.Desc(renderingLayerMask: mask ^ 0xFFu));
                Assert.IsTrue(world.TryGetEntity(entity, out record));
                Assert.AreEqual(mask, record.desc.renderingLayerMask);
                world.UpdateResidency();
                Assert.AreEqual(mask, ReadLayer(world, entity));

                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    update.RemoveEntity(entity);
                    update.Commit();
                }
                world.UpdateResidency();
                Assert.AreEqual(0u, ReadLayer(world, entity), "A dead entity slot uploads no layer.");

                MeshEntityId reused;
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    reused = MeshTestUtility.CreateEntity(update, float3.zero, renderingLayerMask: 0x80);
                    update.Commit();
                }
                Assert.AreEqual(entity.Index, reused.Index);
                world.UpdateResidency();
                Assert.AreEqual(0x80u, ReadLayer(world, reused));
                using (MeshWorldUpdate update = world.BeginUpdate())
                    Assert.Throws<ArgumentOutOfRangeException>(() => update.SetEntityDesc(reused, MeshTestUtility.Desc(renderingLayerMask: 0x100)));
            }
        }

        static uint ReadLayer(MeshWorld world, MeshEntityId entity)
        {
            var actual = new uint[1];
            world.gpu.renderingLayers.buffer.GetData(actual, 0, (int)entity.Index, 1);
            return actual[0];
        }

        [TestCase(256u)]
        [TestCase(uint.MaxValue)]
        public void UnknownHighBits_AreNotSilentlyTruncated(uint mask)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderingLayerUtility.Validate(mask));
        }

        [Test]
        public void ShadowLayer_UsesNativeLightStorage()
        {
            var go = new GameObject("NativeShadowLayerTest");
            try
            {
                Light light = go.AddComponent<Light>();
                InfinityAdditionalLightData extension = go.AddComponent<InfinityAdditionalLightData>();
                extension.shadowLayer = ERenderingLayer.LightLayer7;
                Assert.AreEqual(0x80u, light.renderingLayerMask);
                light.renderingLayerMask = 2;
                Assert.AreEqual((ERenderingLayer)2, extension.shadowLayer);
                light.renderingLayerMask = 0x100;
                Assert.Throws<ArgumentOutOfRangeException>(() => { var invalid = extension.shadowLayer; });
                Assert.IsNull(typeof(InfinityAdditionalLightData).GetField("shadowLayer"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
