using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Rendering Feature/Screen Space Ray Tracing/Screen Space Reflection")]
    public class ScreenSpaceReflection : VolumeComponent
    {
        [Header("Tracing")]
        public ClampedIntParameter NumRays = new ClampedIntParameter(2, 1, 12);
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 8, 64);
        public ClampedFloatParameter BrdfBias = new ClampedFloatParameter(0.7f, 0.0f, 1.0f);
        public ClampedFloatParameter Fadeness = new ClampedFloatParameter(0.05f, 0.05f, 0.25f);
        public ClampedFloatParameter MaxRoughness = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Header("Spatial")]
        public ClampedIntParameter SpatialRadius = new ClampedIntParameter(2, 0, 2);
        public ClampedIntParameter SpatialSample = new ClampedIntParameter(1, 1, 4);

        [Header("Temporal")]
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0.0f, 8.0f);
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.93f, 0.0f, 0.97f);

        [Header("Bilateral")]
        public ClampedIntParameter BilateralSample = new ClampedIntParameter(2, 0, 2);
        public ClampedFloatParameter BilateralColorWeight = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);
        public ClampedFloatParameter BilateralDepthWeight = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);
        public ClampedFloatParameter BilateralNormalWeight = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);
    }
}
