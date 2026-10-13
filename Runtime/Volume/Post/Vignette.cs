using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Post-processing/Vignette")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class Vignette : VolumeComponent, IInfinityVolumeActivity
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.4f, 0.01f, 1.0f);

        public bool IsActive() => active && intensity.value > 0.0f;
    }
}
