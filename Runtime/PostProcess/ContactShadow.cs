using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Shadow/Contact Shadow")]
    public class ContactShadow : VolumeComponent
    {
        [Header("Tracing")]
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 4, 32);
        public ClampedFloatParameter MaxDistance = new ClampedFloatParameter(0.1f, 0.01f, 1.0f);
        public ClampedFloatParameter Thickness = new ClampedFloatParameter(0.01f, 0.001f, 0.1f);
        public ClampedFloatParameter Intensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        public ClampedFloatParameter FadeDistance = new ClampedFloatParameter(50.0f, 1.0f, 200.0f);
    }
}
