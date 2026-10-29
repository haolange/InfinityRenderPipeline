using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class QualityPresetTests
    {
        static T Create<T>() where T : ScriptableObject => ScriptableObject.CreateInstance<T>();

        [Test]
        public void EveryTable_HasOnePresetPerLevel()
        {
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.GTAO.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.RTAO.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.VolumetricFog.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.VolumetricCloud.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.SSR.Length);
            Assert.AreEqual(QualityPresets.PresetCount, QualityPresets.SSGI.Length);
            Assert.Throws<System.ArgumentException>(() => QualityPresets.Select(QualityPresets.GTAO, EQualityLevel.Custom));
        }

        [Test]
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

        [Test]
        public void SSRPresets_StayInsideVolumeParameterRanges_AndResolveMatchesTable()
        {
            var ssr = Create<ScreenSpaceReflection>();
            try
            {
                foreach (FSSRSettings p in QualityPresets.SSR)
                {
                    Assert.That(p.mirrorRoughness, Is.InRange(ssr.MirrorRoughness.min, ssr.MirrorRoughness.max));
                    Assert.That(p.linearRoughness, Is.InRange(ssr.LinearRoughness.min, ssr.LinearRoughness.max));
                    Assert.That(p.hiZSteps, Is.InRange(ssr.NumSteps.min, ssr.NumSteps.max));
                    Assert.That(p.linearSteps, Is.InRange(ssr.LinearSteps.min, ssr.LinearSteps.max));
                    Assert.That(p.binarySteps, Is.InRange(ssr.BinarySearchSteps.min, ssr.BinarySearchSteps.max));
                    Assert.That(p.resolveSamples, Is.InRange(ssr.ResolveSamples.min, ssr.ResolveSamples.max));
                    Assert.That(p.samples, Is.InRange(ssr.Samples.min, ssr.Samples.max));
                    Assert.That(p.resolveRadius, Is.InRange(ssr.ResolveRadius.min, ssr.ResolveRadius.max));
                    Assert.That(p.brdfBias, Is.InRange(ssr.BrdfBias.min, ssr.BrdfBias.max));
                    Assert.That(p.temporalWeight, Is.InRange(ssr.TemporalWeight.min, ssr.TemporalWeight.max));
                    Assert.That(p.temporalClampScale, Is.InRange(ssr.TemporalScale.min, ssr.TemporalScale.max));
                    Assert.That(p.spatialPasses, Is.InRange(ssr.SpatialPasses.min, ssr.SpatialPasses.max));
                    Assert.Less(p.mirrorRoughness, p.linearRoughness, "Mirror pixels must classify as Hi-Z tiles.");
                }

                FSSRSettings medium = QualityPresets.SSR[(int)EQualityLevel.Medium];
                Assert.AreEqual(medium.halfResolution, ssr.HalfResolution.value, "Medium equals the Volume defaults.");
                Assert.AreEqual(medium.linearRoughness, ssr.LinearRoughness.value);
                Assert.AreEqual(medium.hiZSteps, ssr.NumSteps.value);
                Assert.AreEqual(medium.linearSteps, ssr.LinearSteps.value);
                Assert.AreEqual(medium.binarySteps, ssr.BinarySearchSteps.value);
                Assert.AreEqual(medium.resolveSamples, ssr.ResolveSamples.value);
                Assert.AreEqual(medium.samples, ssr.Samples.value);
                Assert.AreEqual(1, QualityPresets.SSR[(int)EQualityLevel.Low].samples);
                Assert.AreEqual(2, QualityPresets.SSR[(int)EQualityLevel.Ultra].samples);

                ssr.TraceBehind.value = false;
                ssr.quality.value = EQualityLevel.Low;
                FSSRSettings low = ssr.Resolve();
                Assert.IsTrue(low.halfResolution);
                Assert.AreEqual(QualityPresets.SSR[(int)EQualityLevel.Low].resolveSamples, low.resolveSamples);
                Assert.AreEqual(QualityPresets.SSR[(int)EQualityLevel.Low].binarySteps, low.binarySteps);
                Assert.IsFalse(low.traceBehind, "Artistic parameters never come from a preset.");
            }
            finally
            {
                Object.DestroyImmediate(ssr);
            }
        }

        [Test]
        public void SSGIPresets_StayInsideVolumeParameterRanges_AndResolveMatchesTable()
        {
            var ssgi = Create<ScreenSpaceIndirectDiffuse>();
            try
            {
                foreach (FSSGISettings p in QualityPresets.SSGI)
                {
                    Assert.That(p.hiZSteps, Is.InRange(ssgi.NumSteps.min, ssgi.NumSteps.max));
                    Assert.That(p.temporalWeight, Is.InRange(ssgi.TemporalWeight.min, ssgi.TemporalWeight.max));
                    Assert.That(p.temporalClampScale, Is.InRange(ssgi.TemporalScale.min, ssgi.TemporalScale.max));
                    Assert.That(p.samples, Is.InRange(ssgi.Samples.min, ssgi.Samples.max));
                }

                FSSGISettings medium = QualityPresets.SSGI[(int)EQualityLevel.Medium];
                Assert.AreEqual(medium.hiZSteps, ssgi.NumSteps.value);
                Assert.AreEqual(medium.samples, ssgi.Samples.value);
                Assert.AreEqual(medium.temporalWeight, ssgi.TemporalWeight.value);
                Assert.AreEqual(medium.temporalClampScale, ssgi.TemporalScale.value);

                ssgi.IntensityScale.value = 0.5f;
                ssgi.quality.value = EQualityLevel.Ultra;
                FSSGISettings ultra = ssgi.Resolve();
                Assert.AreEqual(2, ultra.samples);
                Assert.AreEqual(QualityPresets.SSGI[(int)EQualityLevel.Ultra].hiZSteps, ultra.hiZSteps);
                Assert.AreEqual(0.5f, ultra.intensity, "Artistic parameters never come from a preset.");
            }
            finally
            {
                Object.DestroyImmediate(ssgi);
            }
        }

        [TestCase(1920, 1080, false, 1920, 1080, 240, 135)]
        [TestCase(1920, 1080, true, 960, 540, 120, 68)]
        [TestCase(17, 19, true, 9, 10, 2, 2)]
        [TestCase(1, 1, true, 1, 1, 1, 1)]
        public void ScreenSpaceTraceAndTileSizes(int width, int height, bool half, int traceX, int traceY, int tilesX, int tilesY)
        {
            Unity.Mathematics.int2 trace = ScreenSpaceTraceUtility.TraceSize(new Unity.Mathematics.int2(width, height), half);
            Unity.Mathematics.int2 tiles = ScreenSpaceTraceUtility.TileCount(trace);
            Assert.AreEqual(traceX, trace.x);
            Assert.AreEqual(traceY, trace.y);
            Assert.AreEqual(tilesX, tiles.x);
            Assert.AreEqual(tilesY, tiles.y);
        }
    }
}
