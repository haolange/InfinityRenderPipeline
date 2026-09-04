using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Post Process/Vignette")]
    public class Vignette : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.4f, 0.01f, 1.0f);
    }
}
