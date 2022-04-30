using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("RayTracing/RTAO")]
    public class RayTraceAmbientOcclusion : VolumeComponent
    {
        [Header("Tracing Property")]
        public ClampedFloatParameter radius = new ClampedFloatParameter(5, 5, 10);
        public ClampedIntParameter numRays = new ClampedIntParameter(2, 1, 32);
    }
}
