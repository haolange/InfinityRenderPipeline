using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Lighting/Screen Space Ambient Occlusion")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class ScreenSpaceAmbientOcclusion : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        [Tooltip("Low/Medium/High/Ultra use hardcoded presets for the quality parameters. Custom uses the values below.")]
        public QualityLevelParameter quality = new QualityLevelParameter(EQualityLevel.Custom);
        public ClampedIntParameter NumRays = new ClampedIntParameter(2, 1, 4);
        public ClampedIntParameter NumSteps = new ClampedIntParameter(2, 1, 8);
        public ClampedFloatParameter Power = new ClampedFloatParameter(2.5f, 1.0f, 5.0f);
        public ClampedFloatParameter Radius = new ClampedFloatParameter(3.0f, 1.0f, 5.0f);
        public ClampedFloatParameter Intensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        public ClampedFloatParameter sharpness = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0.0f, 8.0f);
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.93f, 0.0f, 0.97f);

        public bool IsActive() => active && enable.value;

        public FGTAOSettings Resolve()
        {
            FGTAOSettings settings = quality.value == EQualityLevel.Custom
                ? new FGTAOSettings
                {
                    numRays = NumRays.value,
                    numSteps = NumSteps.value,
                    sharpness = sharpness.value,
                    temporalScale = TemporalScale.value,
                    temporalWeight = TemporalWeight.value,
                }
                : QualityPresets.Select(QualityPresets.GTAO, quality.value);
            settings.power = Power.value;
            settings.radius = Radius.value;
            settings.intensity = Intensity.value;
            return settings;
        }
    }
}
