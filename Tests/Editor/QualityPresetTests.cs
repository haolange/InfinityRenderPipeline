using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class QualityPresetTests
    {
        static T Create<T>() where T : ScriptableObject => ScriptableObject.CreateInstance<T>();

        [Test]
        [Category("Unverified-0.5")]
        public void EveryTable_HasOnePresetPerLevel()
        {
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.GTAO.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.RTAO.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.VolumetricFog.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.VolumetricCloud.Length);
            Assert.Throws<System.ArgumentException>(() => QualityPresets.Select(QualityPresets.GTAO, EQualityLevel.Custom));
        }

        [Test]
        [Category("Unverified-0.5")]
        public void Presets_StayInsideVolumeParameterRanges()
        {
            var ao = Create<ScreenSpaceAmbientOcclusion>();
            var fog = Create<VolumetricFog>();
            var cloud = Create<VolumetricCloud>();
            var rtao = Create<RayTracingAmbientOcclusion>();
            try
            {
                foreach (FGTAOSettings p in QualityPresets.GTAO)
                {
                    Assert.That(p.numRays, Is.InRange(ao.NumRays.min, ao.NumRays.max));
                    Assert.That(p.numSteps, Is.InRange(ao.NumSteps.min, ao.NumSteps.max));
                    Assert.That(p.temporalWeight, Is.InRange(ao.TemporalWeight.min, ao.TemporalWeight.max));
                }
                foreach (FVolumetricFogSettings p in QualityPresets.VolumetricFog)
                {
                    Assert.That(p.depthSlices, Is.InRange(fog.DepthSlices.min, fog.DepthSlices.max));
                    Assert.That(p.temporalWeight, Is.InRange(fog.TemporalWeight.min, fog.TemporalWeight.max));
                }
                foreach (FVolumetricCloudSettings p in QualityPresets.VolumetricCloud)
                {
                    Assert.That(p.numPrimarySteps, Is.InRange(cloud.NumPrimarySteps.min, cloud.NumPrimarySteps.max));
                    Assert.That(p.numLightSteps, Is.InRange(cloud.NumLightSteps.min, cloud.NumLightSteps.max));
                }
                foreach (FRTAOSettings p in QualityPresets.RTAO)
                {
                    Assert.That(p.numRays, Is.InRange(rtao.NumRays.min, rtao.NumRays.max));
                }
            }
            finally
            {
                Object.DestroyImmediate(ao); Object.DestroyImmediate(fog); Object.DestroyImmediate(cloud); Object.DestroyImmediate(rtao);
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void Custom_ReadsVolume_PresetReadsTable_ArtisticAlwaysFromVolume()
        {
            var ao = Create<ScreenSpaceAmbientOcclusion>();
            try
            {
                ao.NumRays.value = 3;
                ao.Radius.value = 4.0f;
                ao.quality.value = EQualityLevel.Custom;
                FGTAOSettings custom = ao.Resolve();
                Assert.AreEqual(3, custom.numRays);
                Assert.AreEqual(4.0f, custom.radius);

                ao.quality.value = EQualityLevel.Ultra;
                FGTAOSettings ultra = ao.Resolve();
                Assert.AreEqual(QualityPresets.GTAO[(int)EQualityLevel.Ultra].numRays, ultra.numRays);
                Assert.AreEqual(4.0f, ultra.radius, "Artistic parameters never come from a preset.");
            }
            finally
            {
                Object.DestroyImmediate(ao);
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void DefaultLevel_IsCustom_SoExistingProfilesKeepTheirValues()
        {
            var fog = Create<VolumetricFog>();
            try
            {
                Assert.AreEqual(EQualityLevel.Custom, fog.quality.value);
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }
    }
}
