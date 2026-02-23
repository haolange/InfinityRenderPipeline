using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Atmosphere/Volumetric Cloud")]
    public class VolumetricCloud : VolumeComponent
    {
        [Header("Shape")]
        public ClampedFloatParameter CloudLayerBottom = new ClampedFloatParameter(1500.0f, 500.0f, 5000.0f);
        public ClampedFloatParameter CloudLayerThickness = new ClampedFloatParameter(3500.0f, 500.0f, 10000.0f);
        public ClampedFloatParameter DensityMultiplier = new ClampedFloatParameter(0.4f, 0.0f, 2.0f);
        public ClampedFloatParameter ShapeFactor = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);
        public ClampedFloatParameter ErosionFactor = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);

        [Header("Lighting")]
        public ClampedFloatParameter Anisotropy = new ClampedFloatParameter(0.6f, -1.0f, 1.0f);
        public ClampedFloatParameter SilverIntensity = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);
        public ClampedFloatParameter SilverSpread = new ClampedFloatParameter(0.1f, 0.0f, 0.5f);
        public ClampedFloatParameter AmbientIntensity = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Header("Quality")]
        public ClampedIntParameter NumPrimarySteps = new ClampedIntParameter(64, 16, 128);
        public ClampedIntParameter NumLightSteps = new ClampedIntParameter(6, 1, 12);

        [Header("Temporal")]
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.9f, 0.0f, 0.99f);
    }
}
