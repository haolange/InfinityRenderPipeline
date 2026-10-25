using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class NativeLightingContractTests
    {
        [TestCase(LightmapBakeType.Realtime, MixedLightingMode.Shadowmask, 0, false)]
        [TestCase(LightmapBakeType.Baked, MixedLightingMode.Shadowmask, 0, false)]
        [TestCase(LightmapBakeType.Mixed, MixedLightingMode.IndirectOnly, 0, false)]
        [TestCase(LightmapBakeType.Mixed, MixedLightingMode.Subtractive, 0, false)]
        [TestCase(LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, -1, false)]
        [TestCase(LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 0, true)]
        [TestCase(LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 3, true)]
        [TestCase(LightmapBakeType.Mixed, MixedLightingMode.Shadowmask, 4, false)]
        public void ShadowmaskUsesNativeLightBakeOutput(LightmapBakeType type, MixedLightingMode mode, int channel, bool expected)
        {
            var go = new GameObject("OwnedNativeLight");
            try
            {
                var light = go.AddComponent<Light>();
                light.bakingOutput = new LightBakingOutput { lightmapBakeType = type, mixedLightingMode = mode, occlusionMaskChannel = channel };
                Assert.AreEqual(expected, LightContext.IsBakedShadowmaskLight(light));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AbsentLightHasNoShadowmask() => Assert.IsFalse(LightContext.IsBakedShadowmaskLight(null));

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
