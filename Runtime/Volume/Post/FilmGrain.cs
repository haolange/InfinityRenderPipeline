using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Post-processing/Film Grain")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class FilmGrain : VolumeComponent, IInfinityVolumeActivity
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        public ClampedFloatParameter response = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);

        public bool IsActive() => active && intensity.value > 0.0f;
    }
}
