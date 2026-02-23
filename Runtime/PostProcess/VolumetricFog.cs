using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Atmosphere/Volumetric Fog")]
    public class VolumetricFog : VolumeComponent
    {
        [Header("Fog")]
        public ClampedFloatParameter Density = new ClampedFloatParameter(0.01f, 0.0f, 1.0f);
        public ClampedFloatParameter Height = new ClampedFloatParameter(50.0f, 0.0f, 500.0f);
        public ClampedFloatParameter HeightFalloff = new ClampedFloatParameter(0.2f, 0.001f, 2.0f);
        public ColorParameter Albedo = new ColorParameter(new Color(0.9f, 0.9f, 0.9f, 1.0f));

        [Header("Scattering")]
        public ClampedFloatParameter Anisotropy = new ClampedFloatParameter(0.5f, -1.0f, 1.0f);
        public ClampedFloatParameter AmbientIntensity = new ClampedFloatParameter(1.0f, 0.0f, 10.0f);

        [Header("Quality")]
        public ClampedIntParameter DepthSlices = new ClampedIntParameter(64, 16, 128);
        public ClampedFloatParameter MaxDistance = new ClampedFloatParameter(128.0f, 32.0f, 512.0f);

        [Header("Temporal")]
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.9f, 0.0f, 0.99f);
    }
}
