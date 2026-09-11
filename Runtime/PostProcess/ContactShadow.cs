using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Shadowing/Contact Shadow")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class ContactShadow : VolumeComponent, IInfinityVolumeActivity
    {
        public BoolParameter enable = new BoolParameter(false);
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 4, 32);
        public ClampedFloatParameter MaxDistance = new ClampedFloatParameter(0.1f, 0.01f, 1.0f);
        public ClampedFloatParameter Thickness = new ClampedFloatParameter(0.01f, 0.001f, 0.1f);
        public ClampedFloatParameter Intensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        public ClampedFloatParameter FadeDistance = new ClampedFloatParameter(50.0f, 1.0f, 200.0f);

        public bool IsActive() => active && enable.value && Intensity.value > 0.0f;
    }
}
