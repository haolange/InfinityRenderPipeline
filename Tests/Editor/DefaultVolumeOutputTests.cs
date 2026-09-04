using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.PostProcess;
using Unity.Mathematics;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DefaultVolumeOutputTests
    {
        [Test]
        public void DefaultProfileFactory_RequiredComponentsHaveOverrides()
        {
            VolumeProfile profile = DefaultVolumeProfileFactory.CreateInMemory();
            try
            {
                Assert.IsTrue(DefaultVolumeProfileFactory.HasRequiredDefaultComponents(profile));
                Assert.IsTrue(profile.TryGet(out Exposure exposure));
                Assert.IsTrue(profile.TryGet(out FilmTonemap film));
                Assert.IsTrue(profile.TryGet(out ColorGrading grading));
                Assert.IsTrue(DefaultVolumeProfileFactory.AllParametersOverridden(exposure));
                Assert.IsTrue(DefaultVolumeProfileFactory.AllParametersOverridden(film));
                Assert.IsTrue(DefaultVolumeProfileFactory.AllParametersOverridden(grading));
                Assert.AreEqual(EExposureMode.Manual, exposure.mode.value);
                Assert.AreEqual(0.0f, exposure.evCompensation.value);
                AssertPackagedFilmAndGrade(film, grading);
                Assert.IsFalse(profile.Has<Bloom>());
                Assert.IsFalse(profile.Has<Vignette>());
                Assert.IsFalse(profile.Has<FilmGrain>());
                Assert.IsFalse(profile.Has<ScreenSpaceReflection>());
                Assert.IsFalse(profile.Has<ScreenSpaceIndirectDiffuse>());
                Assert.IsFalse(profile.Has<ScreenSpaceAmbientOcclusion>());
                Assert.IsFalse(profile.Has<VolumetricFog>());
                Assert.IsFalse(profile.Has<VolumetricCloud>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DefaultProfileFactory_PackagedFilmAndGradeAreNeutralAtMidGray()
        {
            VolumeProfile profile = DefaultVolumeProfileFactory.CreateInMemory();
            try
            {
                Assert.IsTrue(profile.TryGet(out FilmTonemap film));
                Assert.IsTrue(profile.TryGet(out ColorGrading grading));
                AssertPackagedFilmAndGrade(film, grading);
                Assert.AreEqual(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), grading.ColorSaturation.value);
                Assert.AreEqual(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), grading.ColorContrast.value);
                Assert.AreEqual(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), grading.ColorGamma.value);
                Assert.AreEqual(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), grading.ColorGain.value);
                Assert.AreEqual(new Vector4(0.0f, 0.0f, 0.0f, 0.0f), grading.ColorOffset.value);
                Assert.IsTrue(film.Slop.overrideState);
                Assert.IsTrue(grading.ExpandGamut.overrideState);
                Assert.IsTrue(grading.BlueCorrection.overrideState);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ColorGrading_ClassDefaultsAreNeutralExpandAndBlue()
        {
            ColorGrading grading = ScriptableObject.CreateInstance<ColorGrading>();
            try
            {
                Assert.AreEqual(DefaultVolumeProfileFactory.PackagedExpandGamut, grading.ExpandGamut.value);
                Assert.AreEqual(DefaultVolumeProfileFactory.PackagedBlueCorrection, grading.BlueCorrection.value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(grading);
            }
        }

        [Test]
        public void CombineLutDescriptor_HasNoIdentityLutField()
        {
            Assert.IsNull(typeof(CombineLutParameterDescriptor).GetField("IdentityLut", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.IsNull(typeof(CombineLutParameterDescriptor).GetField("IdentityLUT", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        [Test]
        public void CombineLutDescriptor_AnyFieldChange_BreaksEquals()
        {
            CombineLutParameterDescriptor a = CreateFilledDescriptor();
            Assert.IsTrue(a.Equals(CreateFilledDescriptor()));

            AssertFieldChangesEquals(a, d => { d.WhiteTemp = 5000.0f; return d; });
            AssertFieldChangesEquals(a, d => { d.WhiteTint = 0.2f; return d; });
            AssertFieldChangesEquals(a, d => { d.FilmSlope = 0.5f; return d; });
            AssertFieldChangesEquals(a, d => { d.FilmToe = 0.1f; return d; });
            AssertFieldChangesEquals(a, d => { d.FilmShoulder = 0.9f; return d; });
            AssertFieldChangesEquals(a, d => { d.FilmBlackClip = 0.1f; return d; });
            AssertFieldChangesEquals(a, d => { d.FilmWhiteClip = 0.2f; return d; });
            AssertFieldChangesEquals(a, d => { d.ColorSaturation = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorContrast = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGamma = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGain = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorOffset = new float4(0.1f, 0, 0, 0); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorSaturationShadows = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorContrastShadows = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGammaShadows = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGainShadows = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorOffsetShadows = new float4(0.1f, 0, 0, 0); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorCorrectionShadowsMax = 0.2f; return d; });
            AssertFieldChangesEquals(a, d => { d.ColorSaturationMidtones = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorContrastMidtones = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGammaMidtones = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGainMidtones = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorOffsetMidtones = new float4(0.1f, 0, 0, 0); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorSaturationHighlights = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorContrastHighlights = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGammaHighlights = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorGainHighlights = new float4(2, 1, 1, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorOffsetHighlights = new float4(0.1f, 0, 0, 0); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorCorrectionHighlightsMin = 0.2f; return d; });
            AssertFieldChangesEquals(a, d => { d.ColorCorrectionHighlightsMax = 2.0f; return d; });
            AssertFieldChangesEquals(a, d => { d.BlueCorrection = 0.1f; return d; });
            AssertFieldChangesEquals(a, d => { d.ExpandGamut = 0.2f; return d; });
            AssertFieldChangesEquals(a, d => { d.ColorScale = new float4(2, 1, 1, 0); return d; });
            AssertFieldChangesEquals(a, d => { d.OverlayColor = new float4(1, 0, 0, 1); return d; });
            AssertFieldChangesEquals(a, d => { d.MappingPolynomial = new float4(1, 2, 3, 4); return d; });
            AssertFieldChangesEquals(a, d => { d.OutputGamut = 2; return d; });
            AssertFieldChangesEquals(a, d => { d.OutputDevice = 3; return d; });
            AssertFieldChangesEquals(a, d => { d.OutputMode = (int)EOutputMode.HDR; return d; });
            AssertFieldChangesEquals(a, d => { d.HDREncoding = (int)EHDREncoding.HLG_Rec2020; return d; });
            AssertFieldChangesEquals(a, d => { d.InverseGamma = new float4(1, 2, 3, 4); return d; });
            AssertFieldChangesEquals(a, d => { d.ColorShadowTint2 = new float4(1, 0, 0, 1); return d; });
        }

        [Test]
        public void CombineLutFromInactiveVolumes_UsesClassDefaults()
        {
            FilmTonemap film = ScriptableObject.CreateInstance<FilmTonemap>();
            ColorGrading grading = ScriptableObject.CreateInstance<ColorGrading>();
            Exposure exposure = ScriptableObject.CreateInstance<Exposure>();
            try
            {
                film.active = false;
                grading.active = false;
                exposure.active = false;
                film.Slop.value = 0.1f;
                grading.Temp.value = 2000.0f;

                CombineLutParameterDescriptor descriptor = CombineLutParameterUtility.FromVolumeStack(film, grading, exposure);
                Assert.AreEqual(0.88f, descriptor.FilmSlope);
                Assert.AreEqual(0.55f, descriptor.FilmToe);
                Assert.AreEqual(0.26f, descriptor.FilmShoulder);
                Assert.AreEqual(0.0f, descriptor.FilmBlackClip);
                Assert.AreEqual(0.04f, descriptor.FilmWhiteClip);
                Assert.AreEqual(6500.0f, descriptor.WhiteTemp);
                Assert.AreEqual(DefaultVolumeProfileFactory.PackagedExpandGamut, descriptor.ExpandGamut);
                Assert.AreEqual(DefaultVolumeProfileFactory.PackagedBlueCorrection, descriptor.BlueCorrection);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(film);
                UnityEngine.Object.DestroyImmediate(grading);
                UnityEngine.Object.DestroyImmediate(exposure);
            }
        }

        [Test]
        public void OutputAuthority_TargetActiveImportAndMissing()
        {
            Assert.AreEqual(
                GraphicsFormat.R8G8B8A8_UNorm,
                OutputTransformUtility.ResolveBackbufferFormat(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.B8G8R8A8_SRGB, true, GraphicsFormat.R16G16B16A16_SFloat));
            Assert.AreEqual(
                GraphicsFormat.B8G8R8A8_SRGB,
                OutputTransformUtility.ResolveBackbufferFormat(null, GraphicsFormat.B8G8R8A8_SRGB, true, GraphicsFormat.R16G16B16A16_SFloat));
            Assert.AreEqual(
                GraphicsFormat.R16G16B16A16_SFloat,
                OutputTransformUtility.ResolveBackbufferFormat(null, null, true, GraphicsFormat.R16G16B16A16_SFloat));
            Assert.Throws<InvalidOperationException>(() =>
                OutputTransformUtility.ResolveBackbufferFormat(null, null, false, GraphicsFormat.None));
            Assert.Throws<InvalidOperationException>(() =>
                OutputTransformUtility.ResolveBackbufferFormat(null, null, true, GraphicsFormat.None));
            Assert.Throws<InvalidOperationException>(() =>
                OutputTransformUtility.ResolveBackbufferFormat(
                    camera: null,
                    EOutputMode.HDR,
                    hdrAvailable: true,
                    hasLastKnownFormat: false,
                    lastKnownFormat: GraphicsFormat.None));
            Assert.AreEqual(
                GraphicsFormat.B8G8R8A8_SRGB,
                OutputTransformUtility.ResolveBackbufferFormat(
                    camera: null,
                    EOutputMode.SDR,
                    hdrAvailable: false,
                    hasLastKnownFormat: true,
                    lastKnownFormat: GraphicsFormat.B8G8R8A8_SRGB));
        }

        static void AssertPackagedFilmAndGrade(FilmTonemap film, ColorGrading grading)
        {
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedFilmSlope, film.Slop.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedFilmToe, film.Toe.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedFilmShoulder, film.Shoulder.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedFilmBlackClip, film.BlackClip.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedFilmWhiteClip, film.WhiteClip.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedWhiteTemp, grading.Temp.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedWhiteTint, grading.Tint.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedExpandGamut, grading.ExpandGamut.value);
            Assert.AreEqual(DefaultVolumeProfileFactory.PackagedBlueCorrection, grading.BlueCorrection.value);
        }

        static CombineLutParameterDescriptor CreateFilledDescriptor()
        {
            CombineLutParameterDescriptor descriptor = default;
            descriptor.WhiteTemp = 6500.0f;
            descriptor.WhiteTint = 0.0f;
            descriptor.FilmSlope = 0.88f;
            descriptor.FilmToe = 0.55f;
            descriptor.FilmShoulder = 0.26f;
            descriptor.FilmBlackClip = 0.0f;
            descriptor.FilmWhiteClip = 0.04f;
            descriptor.ColorSaturation = new float4(1, 1, 1, 1);
            descriptor.ColorContrast = new float4(1, 1, 1, 1);
            descriptor.ColorGamma = new float4(1, 1, 1, 1);
            descriptor.ColorGain = new float4(1, 1, 1, 1);
            descriptor.ColorOffset = new float4(0, 0, 0, 0);
            descriptor.ColorSaturationShadows = new float4(1, 1, 1, 1);
            descriptor.ColorContrastShadows = new float4(1, 1, 1, 1);
            descriptor.ColorGammaShadows = new float4(1, 1, 1, 1);
            descriptor.ColorGainShadows = new float4(1, 1, 1, 1);
            descriptor.ColorOffsetShadows = new float4(0, 0, 0, 0);
            descriptor.ColorCorrectionShadowsMax = 0.09f;
            descriptor.ColorSaturationMidtones = new float4(1, 1, 1, 1);
            descriptor.ColorContrastMidtones = new float4(1, 1, 1, 1);
            descriptor.ColorGammaMidtones = new float4(1, 1, 1, 1);
            descriptor.ColorGainMidtones = new float4(1, 1, 1, 1);
            descriptor.ColorOffsetMidtones = new float4(0, 0, 0, 0);
            descriptor.ColorSaturationHighlights = new float4(1, 1, 1, 1);
            descriptor.ColorContrastHighlights = new float4(1, 1, 1, 1);
            descriptor.ColorGammaHighlights = new float4(1, 1, 1, 1);
            descriptor.ColorGainHighlights = new float4(1, 1, 1, 1);
            descriptor.ColorOffsetHighlights = new float4(0, 0, 0, 0);
            descriptor.ColorCorrectionHighlightsMin = 0.5f;
            descriptor.ColorCorrectionHighlightsMax = 1.0f;
            descriptor.BlueCorrection = 0.6f;
            descriptor.ExpandGamut = 1.0f;
            descriptor.ColorScale = new float4(1, 1, 1, 0);
            descriptor.OverlayColor = new float4(0, 0, 0, 0);
            descriptor.MappingPolynomial = new float4(0, 1, 0, 1);
            descriptor.OutputGamut = OutputTransformUtility.OutputGamutSRGB;
            descriptor.OutputDevice = OutputTransformUtility.OutputDeviceLinear;
            descriptor.OutputMode = (int)EOutputMode.SDR;
            descriptor.HDREncoding = (int)EHDREncoding.PQ_Rec2020;
            descriptor.InverseGamma = new float4(1.0f / 2.2f, 1, 1, 0);
            descriptor.ColorShadowTint2 = new float4(0, 0, 0, 1);
            return descriptor;
        }

        static void AssertFieldChangesEquals(CombineLutParameterDescriptor original, Func<CombineLutParameterDescriptor, CombineLutParameterDescriptor> mutate)
        {
            CombineLutParameterDescriptor mutated = mutate(original);
            Assert.IsFalse(original.Equals(mutated));
        }
    }
}
