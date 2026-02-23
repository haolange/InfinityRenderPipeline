using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Lighting/Deferred Shading")]
    public class DeferredShadingSettings : VolumeComponent
    {
        [Header("Indirect Lighting")]
        public ClampedFloatParameter IndirectDiffuseIntensity = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);
        public ClampedFloatParameter IndirectSpecularIntensity = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Header("Quality")]
        public ClampedIntParameter TileSize = new ClampedIntParameter(16, 8, 32);
    }
}
