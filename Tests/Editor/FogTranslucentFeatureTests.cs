using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Volume;
using static InfinityTech.Rendering.Pipeline.Tests.VolumeTestUtility;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Scene-color stages are derived from the features that record; producers write fixed keys and nothing moves a key.
    public class FogTranslucentFeatureTests
    {
        static FrameFeatureSet Recording(params EFrameFeature[] recorded)
        {
            var features = new FrameFeatureSet();
            foreach (EFrameFeature feature in recorded)
            {
                features.Request(feature);
                features.MarkSupported(feature);
            }
            return features;
        }

        [Test]
        public void SceneColorKeys_WithoutOptionalStages_AreTheDeferredLighting()
        {
            var keys = new SceneColorKeys(Recording(), ETemporalResolve.None, false);
            Assert.AreEqual(InfinityShaderIDs.LightingBuffer, keys.opaque);
            Assert.AreEqual(InfinityShaderIDs.LightingBuffer, keys.fogged);
            Assert.AreEqual(InfinityShaderIDs.LightingBuffer, keys.resolved);
            Assert.AreEqual(InfinityShaderIDs.PostProcessBuffer, keys.display);
        }

        [Test]
        public void SceneColorKeys_FollowTheRecordedProducers()
        {
            var keys = new SceneColorKeys(Recording(EFrameFeature.SSGI, EFrameFeature.VolumetricCloud), ETemporalResolve.TAA, true);
            Assert.AreEqual(InfinityShaderIDs.ScreenSpaceCompositeBuffer, keys.opaque);
            Assert.AreEqual(InfinityShaderIDs.FoggedSceneColorBuffer, keys.fogged);
            Assert.AreEqual(InfinityShaderIDs.AntiAliasingBuffer, keys.resolved);
            Assert.AreEqual(InfinityShaderIDs.DebugViewBuffer, keys.display);

            keys = new SceneColorKeys(Recording(EFrameFeature.SSR, EFrameFeature.VolumetricFog), ETemporalResolve.SuperResolution, false);
            Assert.AreEqual(InfinityShaderIDs.ScreenSpaceCompositeBuffer, keys.opaque);
            Assert.AreEqual(InfinityShaderIDs.FoggedSceneColorBuffer, keys.fogged);
            Assert.AreEqual(InfinityShaderIDs.SuperResolutionBuffer, keys.resolved);
        }

        [Test]
        public void SceneColorKeys_IgnoreARequestThatCannotRecord()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.VolumetricFog);
            features.Request(EFrameFeature.SSR);
            var keys = new SceneColorKeys(features, ETemporalResolve.None, false);
            Assert.AreEqual(InfinityShaderIDs.LightingBuffer, keys.opaque, "An unsupported request records no composite.");
            Assert.AreEqual(keys.opaque, keys.fogged, "An unsupported request records no fog composite.");
        }

        [Test]
        public void VolumetricFog_IsActive_RequiresEnable()
        {
            VolumetricFog fog = ScriptableObject.CreateInstance<VolumetricFog>();
            try
            {
                fog.active = true;
                Assert.IsFalse(VolumeComponentActive(fog));

                fog.enable.value = true;
                Assert.IsTrue(VolumeComponentActive(fog));

                fog.active = false;
                Assert.IsFalse(VolumeComponentActive(fog));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }
    }
}
