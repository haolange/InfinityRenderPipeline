using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Component.Editor;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderingLayerTests
    {
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(8u)]
        [TestCase(15u)]
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
                    update.SetEntityDesc(entity, MeshTestUtility.Desc(renderingLayerMask: mask ^ 0x0Fu));
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
                    reused = MeshTestUtility.CreateEntity(update, float3.zero, renderingLayerMask: 0x08);
                    update.Commit();
                }
                Assert.AreEqual(entity.Index, reused.Index);
                world.UpdateResidency();
                Assert.AreEqual(0x08u, ReadLayer(world, reused));
                using (MeshWorldUpdate update = world.BeginUpdate())
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => update.SetEntityDesc(reused, MeshTestUtility.Desc(renderingLayerMask: 0x10)));
                }
            }
        }

        static uint ReadLayer(MeshWorld world, MeshEntityId entity)
        {
            var actual = new uint[1];
            world.gpu.renderingLayersHandle.buffer.GetData(actual, 0, (int)entity.Index, 1);
            return actual[0];
        }

        // Four light channels fill the sideband's high nibble; any higher bit is an error, never a truncation.
        [TestCase(0x10u)]
        [TestCase(0x80u)]
        [TestCase(0xFFu)]
        [TestCase(uint.MaxValue)]
        public void HighBits_AreNotSilentlyTruncated(uint mask)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderingLayerUtility.Validate(mask));
        }

        [Test]
        public void Everything_IsTheFourChannelMask()
        {
            Assert.AreEqual(RenderingLayerUtility.ValidMask, (uint)ERenderingLayer.Everything);
            Assert.AreEqual(0x0Fu, RenderingLayerUtility.Validate(0x0Fu));
        }

        // The explicit clamp command keeps the four channels of every mask; a mask with none left is reported, not refilled.
        // Run in a preview scene independent of the test runner's untitled bootstrap scene.
        [Test]
        public void ClampCommand_KeepsFourChannelsAndReportsEmptiedMasks()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var rendererObject = new GameObject("ClampRenderer");
                var lightObject = new GameObject("ClampLight");
                SceneManager.MoveGameObjectToScene(rendererObject, scene);
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                MeshRenderer renderer = rendererObject.AddComponent<MeshRenderer>();
                renderer.renderingLayerMask = 0xFFu;
                Light light = lightObject.AddComponent<Light>();
                light.renderingLayerMask = 0x30;
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ClampLight used only channels above the four light channels"));
                Assert.AreEqual(2, LightChannelClampCommand.ClampScene(scene));
                Assert.AreEqual(0x0Fu, renderer.renderingLayerMask);
                Assert.AreEqual(0, light.renderingLayerMask);
                Assert.AreEqual(0, LightChannelClampCommand.ClampScene(scene), "A second run changes nothing.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void ShadowLayer_UsesNativeLightStorage()
        {
            var go = new GameObject("NativeShadowLayerTest");
            try
            {
                Light light = go.AddComponent<Light>();
                InfinityAdditionalLightData extension = go.AddComponent<InfinityAdditionalLightData>();
                extension.shadowLayer = ERenderingLayer.LightLayer3;
                Assert.AreEqual(0x08u, light.renderingLayerMask);
                light.renderingLayerMask = 2;
                Assert.AreEqual((ERenderingLayer)2, extension.shadowLayer);
                light.renderingLayerMask = 0x10;
                Assert.Throws<ArgumentOutOfRangeException>(() => { var invalid = extension.shadowLayer; });
                Assert.IsNull(typeof(InfinityAdditionalLightData).GetField("shadowLayer"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
