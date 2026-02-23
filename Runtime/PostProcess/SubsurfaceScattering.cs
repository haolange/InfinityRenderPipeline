using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Lighting/Subsurface Scattering")]
    public class SubsurfaceScattering : VolumeComponent
    {
        [Header("Diffusion Profile")]
        public ClampedFloatParameter ScatteringDistance = new ClampedFloatParameter(1.0f, 0.1f, 5.0f);
        public ColorParameter SurfaceAlbedo = new ColorParameter(new Color(0.8f, 0.4f, 0.3f, 1.0f));

        [Header("Quality")]
        public ClampedIntParameter NumSamples = new ClampedIntParameter(11, 5, 25);
        public ClampedFloatParameter MaxRadius = new ClampedFloatParameter(5.0f, 1.0f, 20.0f);
    }
}
