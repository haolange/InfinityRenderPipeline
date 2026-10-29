using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Lighting/Screen Space Indirect Diffuse")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class ScreenSpaceIndirectDiffuse : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);

        // Artistic. Rays are clipped to the near and far planes and to the screen; there is no world-space length.
        [Tooltip("After a ray passes behind a surface, keep tracing for the next one.")]
        public BoolParameter TraceBehind = new BoolParameter(true);
        [Tooltip("Trace rays that travel toward the camera.")]
        public BoolParameter TowardRay = new BoolParameter(true);
        [Tooltip("Scales the radiance gathered from the screen.")]
        public ClampedFloatParameter IntensityScale = new ClampedFloatParameter(1.0f, 0.0f, 10.0f);

        // Quality
        [Tooltip("Low/Medium/High/Ultra use hardcoded presets for the quality parameters. Custom uses the values below.")]
        public QualityLevelParameter quality = new QualityLevelParameter(EQualityLevel.Custom);
        [Tooltip("Hi-Z walk budget; the walk takes at most four times this many cell steps.")]
        public ClampedIntParameter NumSteps = new ClampedIntParameter(12, 4, 64);
        [Tooltip("Cosine rays traced per half-resolution work pixel.")]
        public ClampedIntParameter Samples = new ClampedIntParameter(1, 1, 4);
        [Tooltip("Maximum history weight of the temporal filter.")]
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.93f, 0.0f, 0.97f);
        [Tooltip("Width of the temporal variance clip box in standard deviations.")]
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0.0f, 8.0f);

        public bool IsActive() => active && enable.value;

        public FSSGISettings Resolve()
        {
            FSSGISettings settings = quality.value == EQualityLevel.Custom
                ? new FSSGISettings
                {
                    hiZSteps = NumSteps.value,
                    samples = Samples.value,
                    temporalWeight = TemporalWeight.value,
                    temporalClampScale = TemporalScale.value,
                }
                : QualityPresets.Select(QualityPresets.SSGI, quality.value);
            settings.traceBehind = TraceBehind.value;
            settings.traceToward = TowardRay.value;
            settings.intensity = IntensityScale.value;
            return settings;
        }
    }
}
