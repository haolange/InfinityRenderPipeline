using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Editor
{
    [VolumeComponentEditor(typeof(ColorGrading))]
    sealed class ColorGradingEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Temp;
        SerializedDataParameter m_Tint;
        SerializedDataParameter m_Sat, m_Contrast, m_Gamma, m_Gain, m_Offset;
        SerializedDataParameter m_SatS, m_ContrastS, m_GammaS, m_GainS, m_OffsetS, m_ShadowsMax;
        SerializedDataParameter m_SatM, m_ContrastM, m_GammaM, m_GainM, m_OffsetM;
        SerializedDataParameter m_SatH, m_ContrastH, m_GammaH, m_GainH, m_OffsetH, m_HighlightsMin, m_HighlightsMax;
        SerializedDataParameter m_Blue, m_Expand;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ColorGrading>(serializedObject);
            m_Temp = Unpack(o.Find(x => x.Temp));
            m_Tint = Unpack(o.Find(x => x.Tint));
            m_Sat = Unpack(o.Find(x => x.ColorSaturation));
            m_Contrast = Unpack(o.Find(x => x.ColorContrast));
            m_Gamma = Unpack(o.Find(x => x.ColorGamma));
            m_Gain = Unpack(o.Find(x => x.ColorGain));
            m_Offset = Unpack(o.Find(x => x.ColorOffset));
            m_SatS = Unpack(o.Find(x => x.ColorSaturationShadows));
            m_ContrastS = Unpack(o.Find(x => x.ColorContrastShadows));
            m_GammaS = Unpack(o.Find(x => x.ColorGammaShadows));
            m_GainS = Unpack(o.Find(x => x.ColorGainShadows));
            m_OffsetS = Unpack(o.Find(x => x.ColorOffsetShadows));
            m_ShadowsMax = Unpack(o.Find(x => x.ShadowsMax));
            m_SatM = Unpack(o.Find(x => x.ColorSaturationMidtones));
            m_ContrastM = Unpack(o.Find(x => x.ColorContrastMidtones));
            m_GammaM = Unpack(o.Find(x => x.ColorGammaMidtones));
            m_GainM = Unpack(o.Find(x => x.ColorGainMidtones));
            m_OffsetM = Unpack(o.Find(x => x.ColorOffsetMidtones));
            m_SatH = Unpack(o.Find(x => x.ColorSaturationHighlights));
            m_ContrastH = Unpack(o.Find(x => x.ColorContrastHighlights));
            m_GammaH = Unpack(o.Find(x => x.ColorGammaHighlights));
            m_GainH = Unpack(o.Find(x => x.ColorGainHighlights));
            m_OffsetH = Unpack(o.Find(x => x.ColorOffsetHighlights));
            m_HighlightsMin = Unpack(o.Find(x => x.HighlightsMin));
            m_HighlightsMax = Unpack(o.Find(x => x.HighlightsMax));
            m_Blue = Unpack(o.Find(x => x.BlueCorrection));
            m_Expand = Unpack(o.Find(x => x.ExpandGamut));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Temp, new GUIContent("Temperature"));
            PropertyField(m_Tint);
            EditorGUILayout.LabelField("Global", EditorStyles.boldLabel);
            PropertyField(m_Sat); PropertyField(m_Contrast); PropertyField(m_Gamma); PropertyField(m_Gain); PropertyField(m_Offset);
            EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);
            PropertyField(m_SatS); PropertyField(m_ContrastS); PropertyField(m_GammaS); PropertyField(m_GainS); PropertyField(m_OffsetS); PropertyField(m_ShadowsMax);
            EditorGUILayout.LabelField("Midtones", EditorStyles.boldLabel);
            PropertyField(m_SatM); PropertyField(m_ContrastM); PropertyField(m_GammaM); PropertyField(m_GainM); PropertyField(m_OffsetM);
            EditorGUILayout.LabelField("Highlights", EditorStyles.boldLabel);
            PropertyField(m_SatH); PropertyField(m_ContrastH); PropertyField(m_GammaH); PropertyField(m_GainH); PropertyField(m_OffsetH);
            PropertyField(m_HighlightsMin); PropertyField(m_HighlightsMax);
            PropertyField(m_Blue); PropertyField(m_Expand);
        }
    }

    [VolumeComponentEditor(typeof(FilmTonemap))]
    sealed class FilmTonemapEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Mode, m_Slope, m_Toe, m_Shoulder, m_Black, m_White;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<FilmTonemap>(serializedObject);
            m_Mode = Unpack(o.Find(x => x.mode));
            m_Slope = Unpack(o.Find(x => x.slope));
            m_Toe = Unpack(o.Find(x => x.toe));
            m_Shoulder = Unpack(o.Find(x => x.shoulder));
            m_Black = Unpack(o.Find(x => x.blackClip));
            m_White = Unpack(o.Find(x => x.whiteClip));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);
            if (((FilmTonemap)target).mode.value == EFilmTonemapMode.None)
            {
                EditorGUILayout.HelpBox("None disables the film curve. Closing a scene Volume inherits the default profile, which is Film.", MessageType.Info);
                return;
            }
            PropertyField(m_Slope); PropertyField(m_Toe); PropertyField(m_Shoulder); PropertyField(m_Black); PropertyField(m_White);
        }
    }

    [VolumeComponentEditor(typeof(Exposure))]
    sealed class ExposureEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Mode, m_Ev, m_Adapt, m_Low, m_High;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Exposure>(serializedObject);
            m_Mode = Unpack(o.Find(x => x.mode));
            m_Ev = Unpack(o.Find(x => x.evCompensation));
            m_Adapt = Unpack(o.Find(x => x.adaptSpeed));
            m_Low = Unpack(o.Find(x => x.lowPercentile));
            m_High = Unpack(o.Find(x => x.highPercentile));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);
            PropertyField(m_Ev);
            if (((Exposure)target).mode.value == EExposureMode.Auto)
            {
                PropertyField(m_Adapt); PropertyField(m_Low); PropertyField(m_High);
            }
        }
    }

    [VolumeComponentEditor(typeof(SubsurfaceScattering))]
    sealed class SubsurfaceScatteringEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_Samples;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<SubsurfaceScattering>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_Samples = Unpack(o.Find(x => x.numSamples));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            PropertyField(m_Samples, new GUIContent("Quality Samples"));
            EditorGUILayout.HelpBox("Distance, albedo, and max radius live on Diffusion Profiles assigned to the RP Asset.", MessageType.Info);
        }
    }

    [VolumeComponentEditor(typeof(RayTracingAmbientOcclusion))]
    sealed class RayTracingAmbientOcclusionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_Radius, m_Rays;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<RayTracingAmbientOcclusion>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_Radius = Unpack(o.Find(x => x.Radius));
            m_Rays = Unpack(o.Find(x => x.NumRays));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            if (!SystemInfo.supportsRayTracing)
                EditorGUILayout.HelpBox("This machine has no hardware RT. Infinity uses CoreRP UnifiedRayTracing compute on Metal.", MessageType.Info);
            PropertyField(m_Radius);
            PropertyField(m_Rays);
        }
    }

    [VolumeComponentEditor(typeof(Bloom))]
    sealed class BloomEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Threshold, m_Intensity, m_Scatter;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<Bloom>(serializedObject);
            m_Threshold = Unpack(o.Find(x => x.threshold));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_Scatter = Unpack(o.Find(x => x.scatter));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Threshold);
            PropertyField(m_Intensity);
            PropertyField(m_Scatter);
        }
    }

    [VolumeComponentEditor(typeof(Vignette))]
    sealed class VignetteEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Intensity, m_Smoothness;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<Vignette>(serializedObject);
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_Smoothness = Unpack(o.Find(x => x.smoothness));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Intensity);
            PropertyField(m_Smoothness);
        }
    }

    [VolumeComponentEditor(typeof(FilmGrain))]
    sealed class FilmGrainEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Intensity, m_Response;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<FilmGrain>(serializedObject);
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_Response = Unpack(o.Find(x => x.response));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Intensity);
            PropertyField(m_Response);
        }
    }

    [VolumeComponentEditor(typeof(ScreenSpaceReflection))]
    sealed class ScreenSpaceReflectionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_MaxDistance, m_Thickness, m_NumRays, m_NumSteps, m_Bias, m_Fade, m_Roughness;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceReflection>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_MaxDistance = Unpack(o.Find(x => x.MaxDistance));
            m_Thickness = Unpack(o.Find(x => x.Thickness));
            m_NumRays = Unpack(o.Find(x => x.NumRays));
            m_NumSteps = Unpack(o.Find(x => x.NumSteps));
            m_Bias = Unpack(o.Find(x => x.BrdfBias));
            m_Fade = Unpack(o.Find(x => x.fade));
            m_Roughness = Unpack(o.Find(x => x.MaxRoughness));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            EditorGUILayout.LabelField("Trace", EditorStyles.boldLabel);
            PropertyField(m_MaxDistance); PropertyField(m_Thickness); PropertyField(m_NumRays); PropertyField(m_NumSteps); PropertyField(m_Bias); PropertyField(m_Fade); PropertyField(m_Roughness);
        }
    }

    [VolumeComponentEditor(typeof(ScreenSpaceIndirectDiffuse))]
    sealed class ScreenSpaceIndirectDiffuseEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_MaxDistance, m_Thickness, m_NumRays, m_Intensity;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceIndirectDiffuse>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_MaxDistance = Unpack(o.Find(x => x.MaxDistance));
            m_Thickness = Unpack(o.Find(x => x.Thickness));
            m_NumRays = Unpack(o.Find(x => x.NumRays));
            m_Intensity = Unpack(o.Find(x => x.IntensityScale));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            EditorGUILayout.LabelField("Trace", EditorStyles.boldLabel);
            PropertyField(m_MaxDistance); PropertyField(m_Thickness); PropertyField(m_NumRays); PropertyField(m_Intensity);
        }
    }

    [VolumeComponentEditor(typeof(ScreenSpaceAmbientOcclusion))]
    sealed class ScreenSpaceAmbientOcclusionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_Rays, m_Steps, m_Power, m_Radius, m_Intensity, m_Sharpness;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceAmbientOcclusion>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_Rays = Unpack(o.Find(x => x.NumRays));
            m_Steps = Unpack(o.Find(x => x.NumSteps));
            m_Power = Unpack(o.Find(x => x.Power));
            m_Radius = Unpack(o.Find(x => x.Radius));
            m_Intensity = Unpack(o.Find(x => x.Intensity));
            m_Sharpness = Unpack(o.Find(x => x.sharpness));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            PropertyField(m_Rays); PropertyField(m_Steps); PropertyField(m_Power); PropertyField(m_Radius); PropertyField(m_Intensity); PropertyField(m_Sharpness);
        }
    }

    [VolumeComponentEditor(typeof(VolumetricFog))]
    sealed class VolumetricFogEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_Density, m_Height, m_Falloff, m_Albedo, m_Aniso, m_Ambient, m_Slices, m_Distance, m_Temporal;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<VolumetricFog>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_Density = Unpack(o.Find(x => x.Density));
            m_Height = Unpack(o.Find(x => x.Height));
            m_Falloff = Unpack(o.Find(x => x.HeightFalloff));
            m_Albedo = Unpack(o.Find(x => x.Albedo));
            m_Aniso = Unpack(o.Find(x => x.Anisotropy));
            m_Ambient = Unpack(o.Find(x => x.AmbientIntensity));
            m_Slices = Unpack(o.Find(x => x.DepthSlices));
            m_Distance = Unpack(o.Find(x => x.MaxDistance));
            m_Temporal = Unpack(o.Find(x => x.TemporalWeight));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            PropertyField(m_Density); PropertyField(m_Height); PropertyField(m_Falloff); PropertyField(m_Albedo);
            PropertyField(m_Aniso); PropertyField(m_Ambient); PropertyField(m_Slices); PropertyField(m_Distance); PropertyField(m_Temporal);
        }
    }

    [VolumeComponentEditor(typeof(VolumetricCloud))]
    sealed class VolumetricCloudEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_Bottom, m_Thickness, m_Density, m_Shape, m_Erosion, m_Aniso, m_Silver, m_Spread, m_Ambient, m_Primary, m_Light, m_Temporal;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<VolumetricCloud>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_Bottom = Unpack(o.Find(x => x.CloudLayerBottom));
            m_Thickness = Unpack(o.Find(x => x.CloudLayerThickness));
            m_Density = Unpack(o.Find(x => x.DensityMultiplier));
            m_Shape = Unpack(o.Find(x => x.ShapeFactor));
            m_Erosion = Unpack(o.Find(x => x.ErosionFactor));
            m_Aniso = Unpack(o.Find(x => x.Anisotropy));
            m_Silver = Unpack(o.Find(x => x.SilverIntensity));
            m_Spread = Unpack(o.Find(x => x.SilverSpread));
            m_Ambient = Unpack(o.Find(x => x.AmbientIntensity));
            m_Primary = Unpack(o.Find(x => x.NumPrimarySteps));
            m_Light = Unpack(o.Find(x => x.NumLightSteps));
            m_Temporal = Unpack(o.Find(x => x.TemporalWeight));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            PropertyField(m_Bottom); PropertyField(m_Thickness); PropertyField(m_Density); PropertyField(m_Shape); PropertyField(m_Erosion);
            PropertyField(m_Aniso); PropertyField(m_Silver); PropertyField(m_Spread); PropertyField(m_Ambient); PropertyField(m_Primary); PropertyField(m_Light); PropertyField(m_Temporal);
        }
    }

    [VolumeComponentEditor(typeof(ContactShadow))]
    sealed class ContactShadowEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable, m_Steps, m_Distance, m_Thickness, m_Intensity, m_Fade;
        public override void OnEnable()
        {
            var o = new PropertyFetcher<ContactShadow>(serializedObject);
            m_Enable = Unpack(o.Find(x => x.enable));
            m_Steps = Unpack(o.Find(x => x.NumSteps));
            m_Distance = Unpack(o.Find(x => x.MaxDistance));
            m_Thickness = Unpack(o.Find(x => x.Thickness));
            m_Intensity = Unpack(o.Find(x => x.Intensity));
            m_Fade = Unpack(o.Find(x => x.FadeDistance));
        }
        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);
            PropertyField(m_Steps); PropertyField(m_Distance); PropertyField(m_Thickness); PropertyField(m_Intensity); PropertyField(m_Fade);
        }
    }
}
