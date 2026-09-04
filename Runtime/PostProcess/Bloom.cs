using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Post Process/Bloom")]
    public class Bloom : VolumeComponent
    {
        [Header("Bloom")]
        public ClampedFloatParameter threshold = new ClampedFloatParameter(1.0f, 0.0f, 16.0f);
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0.0f, 0.0f, 8.0f);
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);
    }
}
