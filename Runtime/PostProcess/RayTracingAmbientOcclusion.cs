using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Ray Tracing/Ray Traced Ambient Occlusion")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class RayTracingAmbientOcclusion : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        public ClampedFloatParameter Radius = new ClampedFloatParameter(5, 5, 10);
        public ClampedIntParameter NumRays = new ClampedIntParameter(2, 1, 32);

        public bool IsActive() => active && enable.value;
    }
}
