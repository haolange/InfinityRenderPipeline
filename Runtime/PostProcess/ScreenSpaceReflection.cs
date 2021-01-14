using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("ScreenSpaceRayTracing/Screen Space Reflection")]
    public class ScreenSpaceReflection : VolumeComponent
    {
        [Header("Tracing Property")]
        public ClampedIntParameter NumRays = new ClampedIntParameter(1, 1, 12);
        public ClampedIntParameter NumSteps = new ClampedIntParameter(64, 58, 128);
        public ClampedFloatParameter BRDFBias = new ClampedFloatParameter(0.7f, 0, 1);
        public ClampedFloatParameter Fadeness = new ClampedFloatParameter(0.05f, 0.05f, 0.25f);
        public ClampedFloatParameter RoughnessDiscard = new ClampedFloatParameter(0.5f, 0, 1);


        [Header("Spatial Property")]
        public ClampedIntParameter NumSpatial = new ClampedIntParameter(1, 1, 4);
        public ClampedIntParameter SpatialRadius = new ClampedIntParameter(2, 0, 2);


        [Header("Temporal Property")]
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0, 8);
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.99f, 0, 0.99f);


        [Header("Bilateral Property")]
        public ClampedIntParameter NumBilateral = new ClampedIntParameter(2, 0, 2);
        public ClampedFloatParameter ColorWeight = new ClampedFloatParameter(0.1f, 0, 1);
        public ClampedFloatParameter DepthWeight = new ClampedFloatParameter(0.1f, 0, 1);
        public ClampedFloatParameter NormalWeight = new ClampedFloatParameter(0.1f, 0, 1);
    }
}
