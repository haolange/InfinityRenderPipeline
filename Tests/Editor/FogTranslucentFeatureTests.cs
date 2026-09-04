using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.PostProcess;

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
        public void VolumetricFog_VolumeHasOverrides_RequiresActiveAndOverrideState()
        {
            VolumetricFog fog = ScriptableObject.CreateInstance<VolumetricFog>();
            try
            {
                fog.active = true;
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(fog));

                fog.Density.overrideState = true;
                Assert.IsTrue(GraphicsUtility.VolumeHasOverrides(fog));

                fog.active = false;
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(fog));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }
    }
}
