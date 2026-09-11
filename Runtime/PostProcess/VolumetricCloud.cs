using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Sky and Fog/Volumetric Cloud")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class VolumetricCloud : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        public ClampedFloatParameter CloudLayerBottom = new ClampedFloatParameter(1500.0f, 500.0f, 5000.0f);
        public ClampedFloatParameter CloudLayerThickness = new ClampedFloatParameter(3500.0f, 500.0f, 10000.0f);
        public ClampedFloatParameter DensityMultiplier = new ClampedFloatParameter(0.4f, 0.0f, 2.0f);
        public ClampedFloatParameter ShapeFactor = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);
        public ClampedFloatParameter ErosionFactor = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);
        public ClampedFloatParameter Anisotropy = new ClampedFloatParameter(0.6f, -1.0f, 1.0f);
        public ClampedFloatParameter SilverIntensity = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);
        public ClampedFloatParameter SilverSpread = new ClampedFloatParameter(0.1f, 0.0f, 0.5f);
        public ClampedFloatParameter AmbientIntensity = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);
        public ClampedIntParameter NumPrimarySteps = new ClampedIntParameter(64, 16, 128);
        public ClampedIntParameter NumLightSteps = new ClampedIntParameter(6, 1, 12);
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.9f, 0.0f, 0.99f);

        public bool IsActive() => active && enable.value;
    }
}
