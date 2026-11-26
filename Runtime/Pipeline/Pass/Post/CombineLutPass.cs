using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class CombineLutPassUtilityData
    {
        internal static readonly string CombineLookupTextureName = "CombineLookupTexture";
        internal static readonly int CBV_CombineLutParametersID = Shader.PropertyToID("CBV_CombineLutParameters");
    }

    // The record CombineLutPass writes, bound as one constant buffer; it is also the LUT cache key.
    // GPU-RECORD: CBV_CombineLutParameters Shaders/Pass/Post/Compute_CombineLUTs.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct CombineLutParameterDescriptor : IEquatable<CombineLutParameterDescriptor>
    {
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

        public float WhiteTemp;
        public float WhiteTint;
        public float FilmSlope;
        public float FilmToe;

        public float FilmShoulder;
        public float FilmBlackClip;
        public float FilmWhiteClip;
        public float FilmEnabled;

        public float ColorCorrectionShadowsMax;
        public float ColorCorrectionHighlightsMin;
        public float ColorCorrectionHighlightsMax;
        public float BlueCorrection;

        public float ExpandGamut;
        // Fills the last 16-byte register; excluded from the cache key.
        public int padding0;
        public int padding1;
        public int padding2;

        public bool Equals(CombineLutParameterDescriptor other)
        {
            return WhiteTemp.Equals(other.WhiteTemp)
                && WhiteTint.Equals(other.WhiteTint)
                && FilmSlope.Equals(other.FilmSlope)
                && FilmToe.Equals(other.FilmToe)
                && FilmShoulder.Equals(other.FilmShoulder)
                && FilmBlackClip.Equals(other.FilmBlackClip)
                && FilmWhiteClip.Equals(other.FilmWhiteClip)
                && FilmEnabled.Equals(other.FilmEnabled)
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
                && ExpandGamut.Equals(other.ExpandGamut);
        }
    }

    internal static class CombineLutParameterUtility
    {
        public static CombineLutParameterDescriptor FromVolumeStack(FilmTonemap film, ColorGrading grading)
        {
            CombineLutParameterDescriptor descriptor = default;
            ApplyFilmTonemap(ref descriptor, film);
            ApplyColorGrading(ref descriptor, grading);
            return descriptor;
        }

        static void ApplyFilmTonemap(ref CombineLutParameterDescriptor descriptor, FilmTonemap film)
        {
            descriptor.FilmEnabled = film.IsActive() ? 1.0f : 0.0f;
            descriptor.FilmSlope = film.slope.value;
            descriptor.FilmToe = film.toe.value;
            descriptor.FilmShoulder = film.shoulder.value;
            descriptor.FilmBlackClip = film.blackClip.value;
            descriptor.FilmWhiteClip = film.whiteClip.value;
        }

        static void ApplyColorGrading(ref CombineLutParameterDescriptor descriptor, ColorGrading grading)
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
        }
    }

    internal sealed class CombineLutPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal CombineLutPass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        struct CombineLutPassData
        {
            public ComputeShader combineLUTShader;
            public ComputeKernel combineLutKernel;
            public RGTextureHandle combineLookupTextureHandle;
            public RGConstantsHandle parametersHandle;
        }

        internal void ComputeCombineLuts(in ViewContext context, in CombineLutParameterDescriptor combineLutParameterDescriptor)
        {
            TextureDescriptor combineLookupTextureDescriptor = new TextureDescriptor(32, 32, 32);
            combineLookupTextureDescriptor.dimension = TextureDimension.Tex3D;
            combineLookupTextureDescriptor.name = CombineLutPassUtilityData.CombineLookupTextureName;
            combineLookupTextureDescriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            combineLookupTextureDescriptor.enableRandomWrite = true;
            combineLookupTextureDescriptor.filterMode = FilterMode.Trilinear;
            combineLookupTextureDescriptor.wrapMode = TextureWrapMode.Clamp;

            AtmosphereCameraState atmosphereState = context.state.GetAtmosphereState();
            atmosphereState.combineLutCache.Resolve(combineLutParameterDescriptor, combineLookupTextureDescriptor, out TextureHandle lutHandle, out bool hit);
            RGTextureHandle combineLookupTextureHandle = m_RGBuilder.ImportTexture(lutHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.CombineLookupTexture, combineLookupTextureHandle);

            if (hit)
            {
                return;
            }

            //Add ColorGradePass
            {
                RGComputePassHandle<CombineLutPassData> passHandle = m_RGBuilder.AddComputePass<CombineLutPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeCombineLuts));
                //Setup Phase
                ref CombineLutPassData passData = ref passHandle.GetPassData();
                passData.combineLUTShader = m_Resources.shaders.combineLUTShader;
                passData.combineLutKernel = m_Resources.kernels.combineLut;
                passData.combineLookupTextureHandle = passHandle.WriteTexture(combineLookupTextureHandle);
                passData.parametersHandle = passHandle.WriteConstants(combineLutParameterDescriptor);

                //Execute Phase
                passHandle.EnablePassCulling(false);
                passHandle.EnableAsyncCompute(true);
                passHandle.SetExecuteFunc((in CombineLutPassData passData, in RGComputeEncoder cmdEncoder) =>
                {

                    cmdEncoder.SetComputeConstantBufferParam(passData.combineLUTShader, CombineLutPassUtilityData.CBV_CombineLutParametersID, passData.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(passData.combineLUTShader, passData.combineLutKernel.index, InfinityShaderIDs.CombineLookupTexture, passData.combineLookupTextureHandle);

                    cmdEncoder.DispatchCompute(passData.combineLUTShader, passData.combineLutKernel.index, passData.combineLutKernel.GroupsX(32), passData.combineLutKernel.GroupsY(32), passData.combineLutKernel.GroupsZ(32));
                });
            }

            atmosphereState.combineLutCache.MarkProduced();
        }
    }
}