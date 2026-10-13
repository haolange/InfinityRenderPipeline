using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    // Screen-space traced shadows from the depth buffer for the sun and for local lights with Contact Shadows enabled
    // (up to 24 local lights per camera).
    [Serializable, VolumeComponentMenu("Shadowing/Contact Shadow")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class ContactShadow : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        [Tooltip("Low/Medium/High/Ultra use hardcoded presets for the quality parameters. Custom uses the values below.")]
        public QualityLevelParameter quality = new QualityLevelParameter(EQualityLevel.Custom);
        [Tooltip("Ray-march steps toward each local light.")]
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 4, 32);
        [Tooltip("Ray-march steps toward the sun.")]
        public ClampedIntParameter SunNumSteps = new ClampedIntParameter(16, 4, 64);
        [Tooltip("Ray length in meters.")]
        public ClampedFloatParameter MaxDistance = new ClampedFloatParameter(0.1f, 0.01f, 1.0f);
        [Tooltip("Assumed thickness of the depth buffer surface in meters.")]
        public ClampedFloatParameter Thickness = new ClampedFloatParameter(0.01f, 0.001f, 0.1f);
        public ClampedFloatParameter Intensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        [Tooltip("Camera distance at which contact shadows have faded out (meters); the fade covers its last 20%.")]
        public ClampedFloatParameter FadeDistance = new ClampedFloatParameter(50.0f, 1.0f, 200.0f);

        public bool IsActive() => active && enable.value && Intensity.value > 0.0f;

        public FContactShadowSettings Resolve()
        {
            FContactShadowSettings settings = quality.value == EQualityLevel.Custom
                ? new FContactShadowSettings { numSteps = NumSteps.value, sunNumSteps = SunNumSteps.value }
                : QualityPresets.Select(QualityPresets.ContactShadow, quality.value);
            settings.maxDistance = MaxDistance.value;
            settings.thickness = Thickness.value;
            settings.intensity = Intensity.value;
            settings.fadeDistance = FadeDistance.value;
            return settings;
        }
    }
}
