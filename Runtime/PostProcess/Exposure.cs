using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.PostProcess
{
    public enum EExposureMode
    {
        Manual = 0,
        Auto = 1
    }

    [Serializable]
    public sealed class ExposureModeParameter : VolumeParameter<EExposureMode>
    {
        public ExposureModeParameter(EExposureMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Exposure")]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class Exposure : VolumeComponent, IInfinityVolumeActivity
    {
        public ExposureModeParameter mode = new ExposureModeParameter(EExposureMode.Manual);
        public FloatParameter evCompensation = new FloatParameter(0.0f);
        public ClampedFloatParameter adaptSpeed = new ClampedFloatParameter(1.5f, 0.01f, 10.0f);
        public ClampedFloatParameter lowPercentile = new ClampedFloatParameter(10.0f, 0.0f, 49.0f);
        public ClampedFloatParameter highPercentile = new ClampedFloatParameter(90.0f, 51.0f, 100.0f);

        public bool IsActive() => active;
    }
}
