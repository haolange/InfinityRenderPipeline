using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class CombineLutPassUtilityData
    {
        internal static string CombineLookupTextureName = "CombineLookupTexture";
        internal static int CombineLookupTextureID = Shader.PropertyToID("CombineLookupTexture");

        internal static int WhiteBalanceTempID = Shader.PropertyToID("WhiteTemp");
        internal static int WhiteBalanceTintID = Shader.PropertyToID("WhiteTint");

        internal static int FilmSlopeID = Shader.PropertyToID("FilmSlope");
        internal static int FilmToeID = Shader.PropertyToID("FilmToe");
        internal static int FilmShoulderID = Shader.PropertyToID("FilmShoulder");
        internal static int FilmBlackClipID = Shader.PropertyToID("FilmBlackClip");
        internal static int FilmWhiteClipID = Shader.PropertyToID("FilmWhiteClip");

        internal static int ColorSaturationID = Shader.PropertyToID("ColorSaturation");
        internal static int ColorContrastID = Shader.PropertyToID("ColorContrast");
        internal static int ColorGammaID = Shader.PropertyToID("ColorGamma");
        internal static int ColorGainID = Shader.PropertyToID("ColorGain");
        internal static int ColorOffsetID = Shader.PropertyToID("ColorOffset");

        internal static int ColorSaturationShadowsID = Shader.PropertyToID("ColorSaturationShadows");
        internal static int ColorContrastShadowsID = Shader.PropertyToID("ColorContrastShadows");
        internal static int ColorGammaShadowsID = Shader.PropertyToID("ColorGammaShadows");
        internal static int ColorGainShadowsID = Shader.PropertyToID("ColorGainShadows");
        internal static int ColorOffsetShadowsID = Shader.PropertyToID("ColorOffsetShadows");
        internal static int ColorCorrectionShadowsMaxID = Shader.PropertyToID("ColorCorrectionShadowsMax");

        internal static int ColorSaturationMidtonesID = Shader.PropertyToID("ColorSaturationMidtones");
        internal static int ColorContrastMidtonesID = Shader.PropertyToID("ColorContrastMidtones");
        internal static int ColorGammaMidtonesID = Shader.PropertyToID("ColorGammaMidtones");
        internal static int ColorGainMidtonesID = Shader.PropertyToID("ColorGainMidtones");
        internal static int ColorOffsetMidtonesID = Shader.PropertyToID("ColorOffsetMidtones");

        internal static int ColorSaturationHighlightsID = Shader.PropertyToID("ColorSaturationHighlights");
        internal static int ColorContrastHighlightsID = Shader.PropertyToID("ColorContrastHighlights");
        internal static int ColorGammaHighlightsID = Shader.PropertyToID("ColorGammaHighlights");
        internal static int ColorGainHighlightsID = Shader.PropertyToID("ColorGainHighlights");
        internal static int ColorOffsetHighlightsID = Shader.PropertyToID("ColorOffsetHighlights");
        internal static int ColorCorrectionHighlightsMinID = Shader.PropertyToID("ColorCorrectionHighlightsMin");
        internal static int ColorCorrectionHighlightsMaxID = Shader.PropertyToID("ColorCorrectionHighlightsMax");

        internal static int BlueCorrectionID = Shader.PropertyToID("BlueCorrection");
        internal static int ExpandGamutID = Shader.PropertyToID("ExpandGamut");

        internal static int ColorScaleID = Shader.PropertyToID("ColorScale");
        internal static int OverlayColorID = Shader.PropertyToID("OverlayColor");
        internal static int MappingPolynomialID = Shader.PropertyToID("MappingPolynomial");

        internal static int OutputGamutID = Shader.PropertyToID("OutputGamut");
        internal static int OutputDeviceID = Shader.PropertyToID("OutputDevice");
        internal static int InverseGammaID = Shader.PropertyToID("InverseGamma");
        internal static int ColorShadowTint2ID = Shader.PropertyToID("ColorShadow_Tint2");
    }

    internal struct CombineLutParameterDescriptor : IEquatable<CombineLutParameterDescriptor>
    {
        public float WhiteTemp;
        public float WhiteTint;

        public float FilmSlope;
        public float FilmToe;
        public float FilmShoulder;
        public float FilmBlackClip;
        public float FilmWhiteClip;

        public float4 ColorSaturation;
        public float4 ColorContrast;
        public float4 ColorGamma;
        public float4 ColorGain;
        public float4 ColorOffset;

        public float4 ColorSaturationShadows;
        public float4 ColorContrastShadows;
        public float4 ColorGammaShadows;
        public float4 ColorGainShadows;
        public float4 ColorOffsetShadows;
        public float ColorCorrectionShadowsMax;

        public float4 ColorSaturationMidtones;
        public float4 ColorContrastMidtones;
        public float4 ColorGammaMidtones;
        public float4 ColorGainMidtones;
        public float4 ColorOffsetMidtones;

        public float4 ColorSaturationHighlights;
        public float4 ColorContrastHighlights;
        public float4 ColorGammaHighlights;
        public float4 ColorGainHighlights;
        public float4 ColorOffsetHighlights;
        public float ColorCorrectionHighlightsMin;
        public float ColorCorrectionHighlightsMax;

        public float BlueCorrection;
        public float ExpandGamut;

        public float4 ColorScale;
        public float4 OverlayColor;
        public float4 MappingPolynomial;

        public int OutputGamut;
        public int OutputDevice;
        public int OutputMode;
        public int HDREncoding;
        public float4 InverseGamma;
        public float4 ColorShadowTint2;

        public bool Equals(CombineLutParameterDescriptor other)
        {
            return WhiteTemp.Equals(other.WhiteTemp)
                && WhiteTint.Equals(other.WhiteTint)
                && FilmSlope.Equals(other.FilmSlope)
                && FilmToe.Equals(other.FilmToe)
                && FilmShoulder.Equals(other.FilmShoulder)
                && FilmBlackClip.Equals(other.FilmBlackClip)
                && FilmWhiteClip.Equals(other.FilmWhiteClip)
                && ColorSaturation.Equals(other.ColorSaturation)
                && ColorContrast.Equals(other.ColorContrast)
                && ColorGamma.Equals(other.ColorGamma)
                && ColorGain.Equals(other.ColorGain)
                && ColorOffset.Equals(other.ColorOffset)
                && ColorSaturationShadows.Equals(other.ColorSaturationShadows)
                && ColorContrastShadows.Equals(other.ColorContrastShadows)
                && ColorGammaShadows.Equals(other.ColorGammaShadows)
                && ColorGainShadows.Equals(other.ColorGainShadows)
                && ColorOffsetShadows.Equals(other.ColorOffsetShadows)
                && ColorCorrectionShadowsMax.Equals(other.ColorCorrectionShadowsMax)
                && ColorSaturationMidtones.Equals(other.ColorSaturationMidtones)
                && ColorContrastMidtones.Equals(other.ColorContrastMidtones)
                && ColorGammaMidtones.Equals(other.ColorGammaMidtones)
                && ColorGainMidtones.Equals(other.ColorGainMidtones)
                && ColorOffsetMidtones.Equals(other.ColorOffsetMidtones)
                && ColorSaturationHighlights.Equals(other.ColorSaturationHighlights)
                && ColorContrastHighlights.Equals(other.ColorContrastHighlights)
                && ColorGammaHighlights.Equals(other.ColorGammaHighlights)
                && ColorGainHighlights.Equals(other.ColorGainHighlights)
                && ColorOffsetHighlights.Equals(other.ColorOffsetHighlights)
                && ColorCorrectionHighlightsMin.Equals(other.ColorCorrectionHighlightsMin)
                && ColorCorrectionHighlightsMax.Equals(other.ColorCorrectionHighlightsMax)
                && BlueCorrection.Equals(other.BlueCorrection)
                && ExpandGamut.Equals(other.ExpandGamut)
                && ColorScale.Equals(other.ColorScale)
                && OverlayColor.Equals(other.OverlayColor)
                && MappingPolynomial.Equals(other.MappingPolynomial)
                && OutputGamut == other.OutputGamut
                && OutputDevice == other.OutputDevice
                && OutputMode == other.OutputMode
                && HDREncoding == other.HDREncoding
                && InverseGamma.Equals(other.InverseGamma)
                && ColorShadowTint2.Equals(other.ColorShadowTint2);
        }
    }

    internal static class CombineLutParameterUtility
    {
        public static CombineLutParameterDescriptor FromVolumeStack(FilmTonemap film, ColorGrading grading, Exposure exposure)
        {
            CombineLutParameterDescriptor descriptor = default;
            ApplyFilmTonemap(ref descriptor, film);
            ApplyColorGrading(ref descriptor, grading);
            // Exposure is applied as a pre-LUT multiply. Read it with the same stack snapshot so
            // inactive components fall back to class defaults instead of a second IdentityLut path.
            _ = exposure.active;

            descriptor.ColorScale = new float4(1.0f, 1.0f, 1.0f, 0.0f);
            descriptor.OverlayColor = new float4(0.0f, 0.0f, 0.0f, 0.0f);
            return descriptor;
        }

        static void ApplyFilmTonemap(ref CombineLutParameterDescriptor descriptor, FilmTonemap film)
        {
            if (film.active)
            {
                descriptor.FilmSlope = film.Slop.value;
                descriptor.FilmToe = film.Toe.value;
                descriptor.FilmShoulder = film.Shoulder.value;
                descriptor.FilmBlackClip = film.BlackClip.value;
                descriptor.FilmWhiteClip = film.WhiteClip.value;
                return;
            }

            descriptor.FilmSlope = 0.88f;
            descriptor.FilmToe = 0.55f;
            descriptor.FilmShoulder = 0.26f;
            descriptor.FilmBlackClip = 0.0f;
            descriptor.FilmWhiteClip = 0.04f;
        }

        static void ApplyColorGrading(ref CombineLutParameterDescriptor descriptor, ColorGrading grading)
        {
            if (grading.active)
            {
                descriptor.WhiteTemp = grading.Temp.value;
                descriptor.WhiteTint = grading.Tint.value;
                descriptor.ColorSaturation = grading.ColorSaturation.value;
                descriptor.ColorContrast = grading.ColorContrast.value;
                descriptor.ColorGamma = grading.ColorGamma.value;
                descriptor.ColorGain = grading.ColorGain.value;
                descriptor.ColorOffset = grading.ColorOffset.value;
                descriptor.ColorSaturationShadows = grading.ColorSaturationShadows.value;
                descriptor.ColorContrastShadows = grading.ColorContrastShadows.value;
                descriptor.ColorGammaShadows = grading.ColorGammaShadows.value;
                descriptor.ColorGainShadows = grading.ColorGainShadows.value;
                descriptor.ColorOffsetShadows = grading.ColorOffsetShadows.value;
                descriptor.ColorCorrectionShadowsMax = grading.ShadowsMax.value;
                descriptor.ColorSaturationMidtones = grading.ColorSaturationMidtones.value;
                descriptor.ColorContrastMidtones = grading.ColorContrastMidtones.value;
                descriptor.ColorGammaMidtones = grading.ColorGammaMidtones.value;
                descriptor.ColorGainMidtones = grading.ColorGainMidtones.value;
                descriptor.ColorOffsetMidtones = grading.ColorOffsetMidtones.value;
                descriptor.ColorSaturationHighlights = grading.ColorSaturationHighlights.value;
                descriptor.ColorContrastHighlights = grading.ColorContrastHighlights.value;
                descriptor.ColorGammaHighlights = grading.ColorGammaHighlights.value;
                descriptor.ColorGainHighlights = grading.ColorGainHighlights.value;
                descriptor.ColorOffsetHighlights = grading.ColorOffsetHighlights.value;
                descriptor.ColorCorrectionHighlightsMin = grading.HighlightsMin.value;
                descriptor.ColorCorrectionHighlightsMax = grading.HighlightsMax.value;
                descriptor.BlueCorrection = grading.BlueCorrection.value;
                descriptor.ExpandGamut = grading.ExpandGamut.value;
                return;
            }

            float4 one = new float4(1.0f, 1.0f, 1.0f, 1.0f);
            float4 zero = new float4(0.0f, 0.0f, 0.0f, 0.0f);
            descriptor.WhiteTemp = 6500.0f;
            descriptor.WhiteTint = 0.0f;
            descriptor.ColorSaturation = one;
            descriptor.ColorContrast = one;
            descriptor.ColorGamma = one;
            descriptor.ColorGain = one;
            descriptor.ColorOffset = zero;
            descriptor.ColorSaturationShadows = one;
            descriptor.ColorContrastShadows = one;
            descriptor.ColorGammaShadows = one;
            descriptor.ColorGainShadows = one;
            descriptor.ColorOffsetShadows = zero;
            descriptor.ColorCorrectionShadowsMax = 0.09f;
            descriptor.ColorSaturationMidtones = one;
            descriptor.ColorContrastMidtones = one;
            descriptor.ColorGammaMidtones = one;
            descriptor.ColorGainMidtones = one;
            descriptor.ColorOffsetMidtones = zero;
            descriptor.ColorSaturationHighlights = one;
            descriptor.ColorContrastHighlights = one;
            descriptor.ColorGammaHighlights = one;
            descriptor.ColorGainHighlights = one;
            descriptor.ColorOffsetHighlights = zero;
            descriptor.ColorCorrectionHighlightsMin = 0.5f;
            descriptor.ColorCorrectionHighlightsMax = 1.0f;
            descriptor.BlueCorrection = DefaultVolumeProfileFactory.PackagedBlueCorrection;
            descriptor.ExpandGamut = DefaultVolumeProfileFactory.PackagedExpandGamut;
        }
    }

    public partial class InfinityRenderPipeline
    {
        struct CombineLutPassData
        {
            public ComputeShader combineLUTShader;
            public RGTextureRef combineLookupTexture;
            public CombineLutParameterDescriptor combineLutParameterDescriptor;
        }

        void ComputeCombineLuts(CameraFrameState frameState, in CombineLutParameterDescriptor combineLutParameterDescriptor)
        {
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.combineLUTShader, "MainCS"))
            {
                throw new InvalidOperationException("InfinityRP: CombineLUT is required but combineLUTShader kernel MainCS is missing.");
            }

            TextureDescriptor combineLookupTextureDescriptor = new TextureDescriptor(32, 32, 32);
            combineLookupTextureDescriptor.dimension = TextureDimension.Tex3D;
            combineLookupTextureDescriptor.name = CombineLutPassUtilityData.CombineLookupTextureName;
            combineLookupTextureDescriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            combineLookupTextureDescriptor.enableRandomWrite = true;
            combineLookupTextureDescriptor.depthBufferBits = EDepthBits.None;
            combineLookupTextureDescriptor.filterMode = FilterMode.Trilinear;
            combineLookupTextureDescriptor.wrapMode = TextureWrapMode.Clamp;

            frameState.combineLutCache.Resolve(combineLutParameterDescriptor, combineLookupTextureDescriptor, out FTextureRef lutHandle, out bool hit);
            RGTextureRef combineLookupTexture = m_RGBuilder.ImportTexture(lutHandle);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.CombineLookupTexture, combineLookupTexture);

            if (hit)
            {
                return;
            }

            //Add ColorGradePass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<CombineLutPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeCombineLuts)))
            {
                //Setup Phase
                ref CombineLutPassData passData = ref passRef.GetPassData<CombineLutPassData>();
                passData.combineLUTShader = pipelineAsset.combineLUTShader;
                passData.combineLookupTexture = passRef.WriteTexture(combineLookupTexture);
                passData.combineLutParameterDescriptor = combineLutParameterDescriptor;

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in CombineLutPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {

                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.WhiteBalanceTempID, passData.combineLutParameterDescriptor.WhiteTemp);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.WhiteBalanceTintID, passData.combineLutParameterDescriptor.WhiteTint);

                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.FilmSlopeID, passData.combineLutParameterDescriptor.FilmSlope);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.FilmToeID, passData.combineLutParameterDescriptor.FilmToe);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.FilmShoulderID, passData.combineLutParameterDescriptor.FilmShoulder);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.FilmBlackClipID, passData.combineLutParameterDescriptor.FilmBlackClip);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.FilmWhiteClipID, passData.combineLutParameterDescriptor.FilmWhiteClip);

                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorSaturationID, passData.combineLutParameterDescriptor.ColorSaturation);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorContrastID, passData.combineLutParameterDescriptor.ColorContrast);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGammaID, passData.combineLutParameterDescriptor.ColorGamma);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGainID, passData.combineLutParameterDescriptor.ColorGain);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorOffsetID, passData.combineLutParameterDescriptor.ColorOffset);

                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorSaturationShadowsID, passData.combineLutParameterDescriptor.ColorSaturationShadows);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorContrastShadowsID, passData.combineLutParameterDescriptor.ColorContrastShadows);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGammaShadowsID, passData.combineLutParameterDescriptor.ColorGammaShadows);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGainShadowsID, passData.combineLutParameterDescriptor.ColorGainShadows);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorOffsetShadowsID, passData.combineLutParameterDescriptor.ColorOffsetShadows);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorCorrectionShadowsMaxID, passData.combineLutParameterDescriptor.ColorCorrectionShadowsMax);

                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorSaturationMidtonesID, passData.combineLutParameterDescriptor.ColorSaturationMidtones);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorContrastMidtonesID, passData.combineLutParameterDescriptor.ColorContrastMidtones);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGammaMidtonesID, passData.combineLutParameterDescriptor.ColorGammaMidtones);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGainMidtonesID, passData.combineLutParameterDescriptor.ColorGainMidtones);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorOffsetMidtonesID, passData.combineLutParameterDescriptor.ColorOffsetMidtones);

                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorSaturationHighlightsID, passData.combineLutParameterDescriptor.ColorSaturationHighlights);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorContrastHighlightsID, passData.combineLutParameterDescriptor.ColorContrastHighlights);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGammaHighlightsID, passData.combineLutParameterDescriptor.ColorGammaHighlights);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorGainHighlightsID, passData.combineLutParameterDescriptor.ColorGainHighlights);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorOffsetHighlightsID, passData.combineLutParameterDescriptor.ColorOffsetHighlights);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorCorrectionHighlightsMinID, passData.combineLutParameterDescriptor.ColorCorrectionHighlightsMin);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorCorrectionHighlightsMaxID, passData.combineLutParameterDescriptor.ColorCorrectionHighlightsMax);

                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.BlueCorrectionID, passData.combineLutParameterDescriptor.BlueCorrection);
                    cmdEncoder.SetComputeFloatParam(passData.combineLUTShader, CombineLutPassUtilityData.ExpandGamutID, passData.combineLutParameterDescriptor.ExpandGamut);

                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorScaleID, passData.combineLutParameterDescriptor.ColorScale);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.OverlayColorID, passData.combineLutParameterDescriptor.OverlayColor);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.MappingPolynomialID, passData.combineLutParameterDescriptor.MappingPolynomial);

                    cmdEncoder.SetComputeIntParam(passData.combineLUTShader, CombineLutPassUtilityData.OutputGamutID, passData.combineLutParameterDescriptor.OutputGamut);
                    cmdEncoder.SetComputeIntParam(passData.combineLUTShader, CombineLutPassUtilityData.OutputDeviceID, passData.combineLutParameterDescriptor.OutputDevice);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.InverseGammaID, passData.combineLutParameterDescriptor.InverseGamma);
                    cmdEncoder.SetComputeVectorParam(passData.combineLUTShader, CombineLutPassUtilityData.ColorShadowTint2ID, passData.combineLutParameterDescriptor.ColorShadowTint2);

                    cmdEncoder.SetComputeTextureParam(passData.combineLUTShader, 0, CombineLutPassUtilityData.CombineLookupTextureID, passData.combineLookupTexture);

                    cmdEncoder.DispatchCompute(passData.combineLUTShader, 0, 4, 4, 4);
                });
            }

            frameState.combineLutCache.MarkProduced();
        }
    }
}