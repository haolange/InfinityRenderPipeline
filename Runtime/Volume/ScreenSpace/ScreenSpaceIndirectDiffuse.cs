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

        // Artistic
        [Tooltip("Maximum ray length in meters. Hits fade out over the last 10%.")]
        public MinFloatParameter MaxDistance = new MinFloatParameter(50f, 0.01f);
        [Tooltip("Assumed thickness of depth-buffer surfaces in meters.")]
        public MinFloatParameter Thickness = new MinFloatParameter(0.1f, 0.001f);
        [Tooltip("Scales the radiance gathered from the screen.")]
        public ClampedFloatParameter IntensityScale = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        // Quality
        [Tooltip("Low/Medium/High/Ultra use hardcoded presets for the quality parameters. Custom uses the values below.")]
        public QualityLevelParameter quality = new QualityLevelParameter(EQualityLevel.Custom);
        [Tooltip("Trace one ray per 2x2 pixel quad, rotating through the quad every frame.")]
        public BoolParameter HalfResolution = new BoolParameter(true);
        [Tooltip("Hi-Z walk budget; the walk takes at most four times this many cell steps.")]
        public ClampedIntParameter NumSteps = new ClampedIntParameter(12, 4, 64);
        [Tooltip("Rays reused per pixel by the resolve, including the pixel's own ray.")]
        public ClampedIntParameter ResolveSamples = new ClampedIntParameter(6, 1, 16);
        [Tooltip("Ray reuse radius in trace pixels.")]
        public ClampedFloatParameter ResolveRadius = new ClampedFloatParameter(3.0f, 0.0f, 4.0f);
        [Tooltip("Maximum history weight of the temporal filter.")]
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.93f, 0.0f, 0.97f);
        [Tooltip("Width of the temporal variance clip box in standard deviations.")]
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0.0f, 8.0f);
        [Tooltip("A-trous spatial passes after the temporal filter.")]
        public ClampedIntParameter SpatialPasses = new ClampedIntParameter(1, 0, 1);

        public bool IsActive() => active && enable.value;

        public FSSGISettings Resolve()
        {
            FSSGISettings settings = quality.value == EQualityLevel.Custom
                ? new FSSGISettings
                {
                    halfResolution = HalfResolution.value,
                    hiZSteps = NumSteps.value,
                    resolveSamples = ResolveSamples.value,
                    resolveRadius = ResolveRadius.value,
                    temporalWeight = TemporalWeight.value,
                    temporalClampScale = TemporalScale.value,
                    spatialPasses = SpatialPasses.value,
                }
                : QualityPresets.Select(QualityPresets.SSGI, quality.value);
            settings.maxDistance = MaxDistance.value;
            settings.thickness = Thickness.value;
            settings.intensity = IntensityScale.value;
            return settings;
        }
    }
}
