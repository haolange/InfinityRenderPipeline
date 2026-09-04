using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Post Process/FilmGrain")]
    public class FilmGrain : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter response = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);
    }
}
