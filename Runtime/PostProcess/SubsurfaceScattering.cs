using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Lighting/Subsurface Scattering")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class SubsurfaceScattering : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        public ClampedIntParameter numSamples = new ClampedIntParameter(11, 5, 25);

        public bool IsActive() => active && enable.value;
    }
}
