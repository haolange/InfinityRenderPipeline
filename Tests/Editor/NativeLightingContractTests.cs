using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class NativeLightingContractTests
    {
        // Baked visibility belongs to Mixed + Shadowmask directional lights; the native bake output names the shadowmask channel.
        [TestCase(LightType.Directional, LightmapBakeType.Realtime, MixedLightingMode.Shadowmask, 0, -1)]
        [TestCase(LightType.Directional, LightmapBakeType.Baked, MixedLightingMode.Shadowmask, 0, -1)]
        [TestCase(LightType.Directional, LightmapBakeType.Mixed, MixedLightingMode.IndirectOnly, 0, -1)]
        [TestCase(LightType.Directional, LightmapBakeType.Mixed, MixedLightingMode.Subtractive, 0, -1)]
        [TestCase(LightType.Directional, LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, -1, -1)]
        [TestCase(LightType.Directional, LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 0, 0)]
        [TestCase(LightType.Directional, LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 3, 3)]
        [TestCase(LightType.Directional, LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 4, -1)]
        [TestCase(LightType.Point, LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 1, -1)]
        [TestCase(LightType.Spot, LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 2, -1)]
        public void MixedLightChannelUsesNativeLightBakeOutput(LightType lightType, LightmapBakeType type, MixedLightingMode mode, int channel, int expected)
        {
            var go = new GameObject("OwnedNativeLight");
            try
            {
                var light = go.AddComponent<Light>();
                light.type = lightType;
                light.bakingOutput = new LightBakingOutput { lightmapBakeType = type, mixedLightingMode = mode, occlusionMaskChannel = channel };
                Assert.AreEqual(expected, LightContext.MixedLightChannel(light));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AbsentLightHasNoMixedLightChannel() => Assert.AreEqual(-1, LightContext.MixedLightChannel(null));

        [TestCase(false)]
        [TestCase(true)]
        public void CustomComponentDoesNotChangeNativeRenderer(bool original)
        {
            var go = new GameObject("OwnedIndependentMesh");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.forceRenderingOff = original;
                var mesh = go.AddComponent<MeshComponent>();
                Assert.AreEqual(original, renderer.forceRenderingOff);
                mesh.enabled = false;
                Assert.AreEqual(original, renderer.forceRenderingOff);
                mesh.enabled = true;
                Assert.AreEqual(original, renderer.forceRenderingOff);
                Object.DestroyImmediate(mesh);
                Assert.AreEqual(original, renderer.forceRenderingOff);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
