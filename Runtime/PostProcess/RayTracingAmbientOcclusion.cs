using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Ray Tracing/Ray Traced Ambient Occlusion")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class RayTracingAmbientOcclusion : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        [Tooltip("Low/Medium/High/Ultra use hardcoded presets for the quality parameters. Custom uses the values below.")]
        public QualityLevelParameter quality = new QualityLevelParameter(EQualityLevel.Custom);
        public ClampedFloatParameter Radius = new ClampedFloatParameter(5, 5, 10);
        public ClampedIntParameter NumRays = new ClampedIntParameter(2, 1, 32);

        public bool IsActive() => active && enable.value;

        public FRTAOSettings Resolve()
        {
            FRTAOSettings settings = quality.value == EQualityLevel.Custom
                ? new FRTAOSettings { numRays = NumRays.value }
                : QualityPresets.Select(QualityPresets.RTAO, quality.value);
            settings.radius = Radius.value;
            return settings;
        }
    }
}
