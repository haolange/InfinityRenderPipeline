using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Runtime.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("ScreenSpaceRayTracing/Screen Space Reflection")]
    public class StochasticScreenSpaceReflection : VolumeComponent
    {
        [Header("Tracing Property")]

        public ClampedIntParameter NumRays = new ClampedIntParameter(1, 1, 12);
        public ClampedIntParameter NumSteps = new ClampedIntParameter(64, 58, 128);
        public ClampedFloatParameter BRDFBias = new ClampedFloatParameter(0.7f, 0, 1);
        public ClampedFloatParameter Thickness = new ClampedFloatParameter(0.1f, 0.05f, 5.0f);
        public ClampedFloatParameter Fadeness = new ClampedFloatParameter(0.05f, 0.05f, 0.25f);


        [Header("Spatial Property")]

        public ClampedIntParameter SpatialRadius = new ClampedIntParameter(2, 0, 2);


        [Header("Temporal Property")]

        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0, 8);
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.99f, 0, 0.99f);


        [Header("Bilateral Property")]

        public ClampedIntParameter BilateralRadius = new ClampedIntParameter(2, 0, 2);
        public ClampedFloatParameter ColorWeight = new ClampedFloatParameter(1, 0, 8);
        public ClampedFloatParameter NormalWeight = new ClampedFloatParameter(1, 0, 8);
        public ClampedFloatParameter PositionWeight = new ClampedFloatParameter(1, 0, 8);
        public ClampedFloatParameter VarianceWeight = new ClampedFloatParameter(1, 0, 8);
    }
}
