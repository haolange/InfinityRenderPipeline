using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Color Grade/FilmTonemap")]
    public class FilmTonemap : VolumeComponent
    {
        public ClampedFloatParameter Slop = new ClampedFloatParameter(0.88f, 0.0f, 1.0f);
        public ClampedFloatParameter Toe = new ClampedFloatParameter(0.55f, 0.0f, 1.0f);
        public ClampedFloatParameter Shoulder = new ClampedFloatParameter(0.26f, 0.0f, 1.0f);
        public ClampedFloatParameter BlackClip = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter WhiteClip = new ClampedFloatParameter(0.04f, 0.0f, 1.0f);
    }
}
