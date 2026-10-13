using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Lighting/Screen Space Reflection")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class ScreenSpaceReflection : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);

        // Artistic
        [Tooltip("Maximum reflected ray length in meters. Hits fade out over the last 10%.")]
        public MinFloatParameter MaxDistance = new MinFloatParameter(50f, 0.01f);
        [Tooltip("Assumed thickness of depth-buffer surfaces in meters.")]
        public MinFloatParameter Thickness = new MinFloatParameter(0.1f, 0.001f);
        [Tooltip("Surfaces rougher than this keep probe and sky reflections only.")]
        public ClampedFloatParameter MaxRoughness = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        [Tooltip("Width of the screen-edge fade in UV units.")]
        public ClampedFloatParameter fade = new ClampedFloatParameter(0.05f, 0.01f, 0.25f);

        // Quality
        [Tooltip("Low/Medium/High/Ultra use hardcoded presets for the quality parameters. Custom uses the values below.")]
        public QualityLevelParameter quality = new QualityLevelParameter(EQualityLevel.Custom);
        [Tooltip("Trace one ray per 2x2 pixel quad, rotating through the quad every frame.")]
        public BoolParameter HalfResolution = new BoolParameter(true);
        [Tooltip("Below this roughness a pixel uses only its own mirror ray.")]
        public ClampedFloatParameter MirrorRoughness = new ClampedFloatParameter(0.05f, 0.0f, 0.3f);
        [Tooltip("Tiles whose smoothest pixel is at least this rough trace with the linear march instead of Hi-Z.")]
        public ClampedFloatParameter LinearRoughness = new ClampedFloatParameter(0.3f, 0.0f, 1.0f);
        [Tooltip("Hi-Z walk budget; the walk takes at most four times this many cell steps.")]
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 4, 64);
        [Tooltip("Steps of the linear march used by rough tiles.")]
        public ClampedIntParameter LinearSteps = new ClampedIntParameter(16, 4, 64);
        [Tooltip("Rays reused per pixel by the resolve, including the pixel's own ray.")]
        public ClampedIntParameter ResolveSamples = new ClampedIntParameter(4, 1, 16);
        [Tooltip("Ray reuse radius in trace pixels.")]
        public ClampedFloatParameter ResolveRadius = new ClampedFloatParameter(2.0f, 0.0f, 4.0f);
        [Tooltip("Truncates the GGX lobe tail: 0 samples the full lobe, 1 only the mirror direction.")]
        public ClampedFloatParameter BrdfBias = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);
        [Tooltip("Maximum history weight of the temporal filter.")]
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.93f, 0.0f, 0.97f);
        [Tooltip("Width of the temporal variance clip box in standard deviations.")]
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0.0f, 8.0f);
        [Tooltip("A-trous spatial passes after the temporal filter.")]
        public ClampedIntParameter SpatialPasses = new ClampedIntParameter(1, 0, 1);

        public bool IsActive() => active && enable.value;

        public FSSRSettings Resolve()
        {
            FSSRSettings settings = quality.value == EQualityLevel.Custom
                ? new FSSRSettings
                {
                    halfResolution = HalfResolution.value,
                    mirrorRoughness = MirrorRoughness.value,
                    linearRoughness = LinearRoughness.value,
                    hiZSteps = NumSteps.value,
                    linearSteps = LinearSteps.value,
                    resolveSamples = ResolveSamples.value,
                    resolveRadius = ResolveRadius.value,
                    brdfBias = BrdfBias.value,
                    temporalWeight = TemporalWeight.value,
                    temporalClampScale = TemporalScale.value,
                    spatialPasses = SpatialPasses.value,
                }
                : QualityPresets.Select(QualityPresets.SSR, quality.value);
            settings.maxDistance = MaxDistance.value;
            settings.thickness = Thickness.value;
            settings.maxRoughness = MaxRoughness.value;
            settings.edgeFade = fade.value;
            return settings;
        }
    }
}
