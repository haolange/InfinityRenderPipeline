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
        public MinFloatParameter MaxDistance = new MinFloatParameter(50f, 0.01f);
        public MinFloatParameter Thickness = new MinFloatParameter(0.1f, 0.001f);
        public ClampedIntParameter NumRays = new ClampedIntParameter(1, 1, 12);
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 8, 32);
        public ClampedFloatParameter IntensityScale = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        public ClampedIntParameter SpatialRadius = new ClampedIntParameter(2, 0, 2);
        public ClampedIntParameter SpatialSample = new ClampedIntParameter(1, 1, 4);
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0.0f, 8.0f);
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.93f, 0.0f, 0.97f);
        public ClampedIntParameter BilateralSample = new ClampedIntParameter(2, 0, 2);
        public ClampedFloatParameter BilateralColorWeight = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);
        public ClampedFloatParameter BilateralDepthWeight = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);
        public ClampedFloatParameter BilateralNormalWeight = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);

        public bool IsActive() => active && enable.value;
    }
}
