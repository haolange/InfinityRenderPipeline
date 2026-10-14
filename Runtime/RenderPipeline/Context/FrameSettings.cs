using System;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Per-camera feature switches (HDRP-style Frame Settings). A disabled setting keeps its features from being requested
    // for that camera even when their Volume is active. They do not turn on anything a Volume or the scene does not ask
    // for, and the required producers (depth, GBuffer, lighting, TAA, display) are not frame settings.
    public enum EFrameSetting
    {
        Decals,
        AmbientOcclusion,
        RayTracedAmbientOcclusion,
        ContactShadows,
        ScreenSpaceReflection,
        ScreenSpaceGlobalIllumination,
        VolumetricFog,
        VolumetricClouds,
        ReflectionProbes,
        LightCookies,
    }

    public enum EFrameSettingSource
    {
        AssetDefault,
        ReflectionDefault,
        CameraOverride,
    }

    // Stored as a disabled mask so the zero value (and data saved before a setting existed) means enabled.
    [Serializable]
    public struct FFrameSettings
    {
        [SerializeField]
        uint m_Disabled;

        public static FFrameSettings AllEnabled => default;

        // Reflection probe captures skip the screen-space and per-light effects whose cost buys little in a probe face.
        public static FFrameSettings ReflectionDefaults
        {
            get
            {
                FFrameSettings settings = default;
                settings.SetEnabled(EFrameSetting.AmbientOcclusion, false);
                settings.SetEnabled(EFrameSetting.RayTracedAmbientOcclusion, false);
                settings.SetEnabled(EFrameSetting.ContactShadows, false);
                settings.SetEnabled(EFrameSetting.ScreenSpaceReflection, false);
                settings.SetEnabled(EFrameSetting.ScreenSpaceGlobalIllumination, false);
                return settings;
            }
        }

        public bool IsEnabled(EFrameSetting setting) => (m_Disabled & Bit(setting)) == 0;

        public void SetEnabled(EFrameSetting setting, bool enabled)
        {
            if (enabled) m_Disabled &= ~Bit(setting);
            else m_Disabled |= Bit(setting);
        }

        internal static uint Bit(EFrameSetting setting) => 1u << (int)setting;
    }

    // Camera overrides: only the settings marked overridden replace the baseline.
    [Serializable]
    public struct FFrameSettingsOverride
    {
        [SerializeField]
        uint m_Overridden;

        [SerializeField]
        FFrameSettings m_Values;

        public bool IsOverridden(EFrameSetting setting) => (m_Overridden & FFrameSettings.Bit(setting)) != 0;

        public bool GetValue(EFrameSetting setting) => m_Values.IsEnabled(setting);

        public void SetOverride(EFrameSetting setting, bool overridden, bool enabled)
        {
            if (overridden) m_Overridden |= FFrameSettings.Bit(setting);
            else m_Overridden &= ~FFrameSettings.Bit(setting);
            m_Values.SetEnabled(setting, enabled);
        }

        public FFrameSettings Apply(FFrameSettings baseline)
        {
            foreach (EFrameSetting setting in FrameSettingsUtility.All)
            {
                if (IsOverridden(setting))
                    baseline.SetEnabled(setting, GetValue(setting));
            }
            return baseline;
        }
    }

    // Effective settings of one camera and where each value came from.
    public struct FResolvedFrameSettings
    {
        public FFrameSettings settings;
        public EFrameSettingSource baseline;
        public FFrameSettingsOverride cameraOverride;
        public bool hasCameraOverride;

        public bool IsEnabled(EFrameSetting setting) => settings.IsEnabled(setting);

        public EFrameSettingSource SourceOf(EFrameSetting setting) =>
            hasCameraOverride && cameraOverride.IsOverridden(setting) ? EFrameSettingSource.CameraOverride : baseline;
    }

    public static class FrameSettingsUtility
    {
        public static readonly EFrameSetting[] All = (EFrameSetting[])Enum.GetValues(typeof(EFrameSetting));

        // Baseline: the RP Asset's camera defaults, or its reflection-probe defaults for probe captures; then the
        // camera's overrides (InfinityAdditionalCameraData.frameSettings) when it has any.
        public static FResolvedFrameSettings Resolve(in FFrameSettings cameraDefaults, in FFrameSettings reflectionDefaults, bool reflectionCapture,
            bool hasCameraOverride, in FFrameSettingsOverride cameraOverride)
        {
            FFrameSettings baseline = reflectionCapture ? reflectionDefaults : cameraDefaults;
            return new FResolvedFrameSettings
            {
                settings = hasCameraOverride ? cameraOverride.Apply(baseline) : baseline,
                baseline = reflectionCapture ? EFrameSettingSource.ReflectionDefault : EFrameSettingSource.AssetDefault,
                cameraOverride = cameraOverride,
                hasCameraOverride = hasCameraOverride,
            };
        }
    }
}
