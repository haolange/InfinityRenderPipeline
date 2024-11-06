using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Ray Tracing/Ray Tracing Ambient Occlusion")]
    public class RayTracingAmbientOcclusion : VolumeComponent
    {
        [Header("Tracing Property")]
        public ClampedFloatParameter Radius = new ClampedFloatParameter(5, 5, 10);
        public ClampedIntParameter NumRays = new ClampedIntParameter(2, 1, 32);
    }
}
