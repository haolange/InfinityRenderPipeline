using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.PostProcess;
using static InfinityTech.Rendering.Pipeline.Tests.VolumeTestUtility;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class FogTranslucentFeatureTests
    {
        [Test]
        public void FoggedSceneColor_IsDistinctFromOpaque()
        {
            Assert.IsTrue(TranslucentFeatureUtility.FoggedSceneColorIsDistinctFromOpaque());
            Assert.AreNotEqual(InfinityShaderIDs.OpaqueSceneColorBuffer, InfinityShaderIDs.FoggedSceneColorBuffer);
            Assert.AreNotEqual(InfinityShaderIDs.OpaqueSceneColorBuffer, InfinityShaderIDs.ReactiveMaskBuffer);
        }

        [Test]
        public void ResolveTemporalSceneColorId_IsFoggedSceneColor()
        {
            Assert.AreEqual(InfinityShaderIDs.FoggedSceneColorBuffer, TranslucentFeatureUtility.ResolveTemporalSceneColorId());
        }

        [Test]
        public void ShouldRecordFogComposite_RequiresFogOrCloud()
        {
            Assert.IsFalse(TranslucentFeatureUtility.ShouldRecordFogComposite(false, false));
            Assert.IsTrue(TranslucentFeatureUtility.ShouldRecordFogComposite(true, false));
            Assert.IsTrue(TranslucentFeatureUtility.ShouldRecordFogComposite(false, true));
            Assert.IsTrue(TranslucentFeatureUtility.ShouldRecordFogComposite(true, true));
        }

        [Test]
        public void ShouldProduceReactiveMask_WhenTaaPathActive()
        {
            Assert.IsTrue(TranslucentFeatureUtility.ShouldProduceReactiveMask(true));
            Assert.IsFalse(TranslucentFeatureUtility.ShouldProduceReactiveMask(false));
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
