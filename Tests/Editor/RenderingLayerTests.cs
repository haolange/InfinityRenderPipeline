using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderingLayerTests
    {
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(128u)]
        [TestCase(255u)]
        public void TransformIndexedLayer_RoundTripsToGpuAndRollsBack(uint mask)
        {
            var scene = new MeshScene(16);
            var pool = new ResourcePool();
            var residency = new MeshSceneResidency(pool, scene);
            try
            {
                TransformId transform;
                MeshInstanceId instance;
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    transform = update.CreateTransform(float4x4.identity);
                    instance = update.CreateInstance(transform, default(FBound), ~0, mask,
                        EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Dynamic);
                    update.Commit();
                }
                Assert.AreEqual(mask, scene.GetTransformRenderingLayer((int)transform.Index));
                residency.Update();
                var actual = new uint[1];
                residency.RenderingLayerBuffer.buffer.GetData(actual, 0, (int)transform.Index, 1);
                Assert.AreEqual(mask, actual[0]);
                using (MeshSceneUpdate update = scene.BeginUpdate())
                    update.SetInstanceRendering(instance, mask ^ 0xFFu, EMotionType.Object, ECastShadowMethod.Dynamic);
                Assert.AreEqual(mask, scene.GetTransformRenderingLayer((int)transform.Index));
                residency.Update();
                residency.RenderingLayerBuffer.buffer.GetData(actual, 0, (int)transform.Index, 1);
                Assert.AreEqual(mask, actual[0]);
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    update.RemoveInstance(instance);
                    update.Commit();
                }
                residency.Update();
                residency.RenderingLayerBuffer.buffer.GetData(actual, 0, (int)transform.Index, 1);
                Assert.AreEqual(0, actual[0]);
                using (MeshSceneUpdate update = scene.BeginUpdate())
                {
                    TransformId reused = update.CreateTransform(float4x4.identity);
                    Assert.AreEqual(transform.Index, reused.Index);
                    update.CreateInstance(reused, default(FBound), ~0, 0x80,
                        EMeshInstanceFlags.Visible, EMotionType.Object, ECastShadowMethod.Dynamic);
                    update.Commit();
                }
                residency.Update();
                residency.RenderingLayerBuffer.buffer.GetData(actual, 0, (int)transform.Index, 1);
                Assert.AreEqual(0x80, actual[0]);
            }
            finally
            {
                residency.Dispose();
                pool.Dispose();
                scene.Dispose();
            }
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
