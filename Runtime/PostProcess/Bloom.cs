using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Post-processing/Bloom")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class Bloom : VolumeComponent, IInfinityVolumeActivity
    {
        public ClampedFloatParameter threshold = new ClampedFloatParameter(1.0f, 0.0f, 16.0f);
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0.0f, 0.0f, 8.0f);
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);

        public bool IsActive() => active && intensity.value > 0.0f;
    }
}
