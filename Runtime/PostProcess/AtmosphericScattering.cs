using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Atmosphere/Atmospheric Scattering")]
    public class AtmosphericScattering : VolumeComponent
    {
        [Header("Planet")]
        public FloatParameter PlanetRadius = new FloatParameter(6360000.0f);
        public FloatParameter AtmosphereHeight = new FloatParameter(60000.0f);

        [Header("Rayleigh")]
        public ColorParameter RayleighScattering = new ColorParameter(new Color(0.00580f, 0.01356f, 0.03310f, 1.0f));
        public FloatParameter RayleighHeight = new FloatParameter(8000.0f);

        [Header("Mie")]
        public FloatParameter MieScattering = new FloatParameter(0.003996f);
        public FloatParameter MieAbsorption = new FloatParameter(0.000444f);
        public FloatParameter MieHeight = new FloatParameter(1200.0f);
        public ClampedFloatParameter MieAnisotropy = new ClampedFloatParameter(0.8f, -1.0f, 1.0f);

        [Header("Ozone")]
        public ColorParameter OzoneAbsorption = new ColorParameter(new Color(0.000650f, 0.001881f, 0.000085f, 1.0f));
        public FloatParameter OzoneLayerCenter = new FloatParameter(25000.0f);
        public FloatParameter OzoneLayerWidth = new FloatParameter(15000.0f);

        [Header("Ground")]
        public ColorParameter GroundAlbedo = new ColorParameter(new Color(0.3f, 0.3f, 0.3f, 1.0f));

        [Header("Quality")]
        public ClampedIntParameter TransmittanceLUTWidth = new ClampedIntParameter(256, 64, 512);
        public ClampedIntParameter TransmittanceLUTHeight = new ClampedIntParameter(64, 16, 128);
        public ClampedIntParameter MultiScatteringLUTSize = new ClampedIntParameter(32, 16, 64);
    }
}
