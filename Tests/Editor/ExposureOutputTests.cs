using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ExposureOutputTests
    {
        [Test]
        public void EvToMultiplier_ZeroIsOne()
        {
            Assert.AreEqual(1.0f, ExposureUtility.EvToMultiplier(0.0f), 1e-6f);
        }

        [Test]
        public void EvToMultiplier_PlusOneIsTwo()
        {
            Assert.AreEqual(2.0f, ExposureUtility.EvToMultiplier(1.0f), 1e-6f);
        }

        [Test]
        public void EvToMultiplier_MinusOneIsHalf()
        {
            Assert.AreEqual(0.5f, ExposureUtility.EvToMultiplier(-1.0f), 1e-6f);
        }

        [Test]
        public void Exposure_VolumeHasOverrides_RequiresActiveAndOverrideState()
        {
            Exposure exposure = ScriptableObject.CreateInstance<Exposure>();
            try
            {
                exposure.active = true;
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(exposure));
                Assert.AreEqual(0.0f, ExposureUtility.ResolveCpuEvCompensation(exposure));
                Assert.AreEqual(1.0f, ExposureUtility.ResolveManualMultiplier(exposure));
                Assert.IsFalse(ExposureUtility.ShouldRecordAuto(exposure));

                exposure.evCompensation.overrideState = true;
                Assert.IsTrue(GraphicsUtility.VolumeHasOverrides(exposure));
                exposure.evCompensation.value = 1.0f;
                Assert.AreEqual(1.0f, ExposureUtility.ResolveCpuEvCompensation(exposure));
                Assert.AreEqual(2.0f, ExposureUtility.ResolveManualMultiplier(exposure), 1e-6f);

                exposure.mode.overrideState = true;
                exposure.mode.value = EExposureMode.Auto;
                Assert.IsTrue(ExposureUtility.ShouldRecordAuto(exposure));
                Assert.AreEqual(1.0f, ExposureUtility.ResolveManualMultiplier(exposure));

                exposure.active = false;
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(exposure));
                Assert.IsFalse(ExposureUtility.ShouldRecordAuto(exposure));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(exposure);
            }
        }

        [Test]
        public void BloomVignetteGrain_VolumeHasOverrides_RequiresActiveAndOverrideState()
        {
            Bloom bloom = ScriptableObject.CreateInstance<Bloom>();
            Vignette vignette = ScriptableObject.CreateInstance<Vignette>();
            FilmGrain grain = ScriptableObject.CreateInstance<FilmGrain>();
            try
            {
                bloom.active = true;
                vignette.active = true;
                grain.active = true;
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(bloom));
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(vignette));
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(grain));

                bloom.intensity.overrideState = true;
                vignette.intensity.overrideState = true;
                grain.intensity.overrideState = true;
                Assert.IsTrue(GraphicsUtility.VolumeHasOverrides(bloom));
                Assert.IsTrue(GraphicsUtility.VolumeHasOverrides(vignette));
                Assert.IsTrue(GraphicsUtility.VolumeHasOverrides(grain));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bloom);
                UnityEngine.Object.DestroyImmediate(vignette);
                UnityEngine.Object.DestroyImmediate(grain);
            }
        }

        [Test]
        public void OutputCapability_HDRRequestedButUnavailable_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                OutputTransformUtility.ValidateCapability(
                    EOutputMode.HDR,
                    EHDREncoding.PQ_Rec2020,
                    hdrAvailable: false,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    ColorGamut.HDR10));
        }

        [Test]
        public void OutputCapability_HDRPQ_OnSrgbBackbuffer_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                OutputTransformUtility.ValidateCapability(
                    EOutputMode.HDR,
                    EHDREncoding.PQ_Rec2020,
                    hdrAvailable: true,
                    GraphicsFormat.R8G8B8A8_SRGB,
                    ColorGamut.sRGB));
        }

        [Test]
        public void OutputCapability_ScRGB_OnEightBit_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                OutputTransformUtility.ValidateCapability(
                    EOutputMode.HDR,
                    EHDREncoding.scRGB_Linear,
                    hdrAvailable: true,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    ColorGamut.sRGB));
        }

        [Test]
        public void OutputCapability_SDR_OnLinearSrgb_UsesHardwareEncode()
        {
            OutputTransformDecision decision = OutputTransformUtility.Resolve(
                EOutputMode.SDR,
                EHDREncoding.PQ_Rec2020,
                hdrAvailable: false,
                GraphicsFormat.B8G8R8A8_SRGB,
                ColorSpace.Linear,
                ColorGamut.sRGB);

            Assert.AreEqual(EOutputEncodePolicy.HardwareSRGB, decision.policy);
            Assert.AreEqual(GraphicsFormat.R16G16B16A16_SFloat, decision.displayFormat);
            Assert.AreEqual(OutputTransformUtility.OutputDeviceLinear, decision.outputDevice);
        }

        [Test]
        public void OutputCapability_SDR_OnUnorm_UsesShaderEncode()
        {
            OutputTransformDecision decision = OutputTransformUtility.Resolve(
                EOutputMode.SDR,
                EHDREncoding.PQ_Rec2020,
                hdrAvailable: false,
                GraphicsFormat.R8G8B8A8_UNorm,
                ColorSpace.Linear,
                ColorGamut.sRGB);

            Assert.AreEqual(EOutputEncodePolicy.ShaderLinearToSRGB, decision.policy);
            Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, decision.displayFormat);
        }

        [Test]
        public void OutputCapability_HDRPQ_OnHdr10_Passes()
        {
            OutputTransformDecision decision = OutputTransformUtility.Resolve(
                EOutputMode.HDR,
                EHDREncoding.PQ_Rec2020,
                hdrAvailable: true,
                GraphicsFormat.R16G16B16A16_SFloat,
                ColorSpace.Linear,
                ColorGamut.HDR10);

            Assert.AreEqual(EOutputEncodePolicy.ShaderPQRec2020, decision.policy);
            Assert.AreEqual(OutputTransformUtility.OutputGamutRec2020, decision.outputGamut);
        }

        [Test]
        public void CombineLutKey_FieldEquals_IncludesOutputMode()
        {
            CombineLutParameterDescriptor a = default;
            a.WhiteTemp = 6500.0f;
            a.OutputMode = (int)EOutputMode.SDR;
            a.HDREncoding = (int)EHDREncoding.PQ_Rec2020;
            a.IdentityLut = 1;

            CombineLutParameterDescriptor b = a;
            Assert.IsTrue(a.Equals(b));

            b.OutputMode = (int)EOutputMode.HDR;
            Assert.IsFalse(a.Equals(b));
        }
    }
}
