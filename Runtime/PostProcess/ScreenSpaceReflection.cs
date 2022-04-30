using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("ScreenSpaceRayTracing/Screen Space Reflection")]
    public class ScreenSpaceReflection : VolumeComponent
    {
        [Header("Tracing")]
        public ClampedIntParameter numRays = new ClampedIntParameter(1, 1, 12);
        public ClampedIntParameter numSteps = new ClampedIntParameter(64, 58, 128);
        public ClampedFloatParameter brdfBias = new ClampedFloatParameter(0.7f, 0, 1);
        public ClampedFloatParameter fadeness = new ClampedFloatParameter(0.05f, 0.05f, 0.25f);
        public ClampedFloatParameter maxRoughness = new ClampedFloatParameter(0.5f, 0, 1);


        [Header("Spatial")]
        public ClampedIntParameter radius = new ClampedIntParameter(2, 0, 2);
        public ClampedIntParameter numSample = new ClampedIntParameter(1, 1, 4);


        [Header("Temporal")]
        public ClampedFloatParameter temporalScale = new ClampedFloatParameter(1.25f, 0, 8);
        public ClampedFloatParameter temporalWeight = new ClampedFloatParameter(0.99f, 0, 0.99f);


        [Header("Bilateral")]
        public ClampedIntParameter numBilateral = new ClampedIntParameter(2, 0, 2);
        public ClampedFloatParameter colorWeight = new ClampedFloatParameter(0.1f, 0, 1);
        public ClampedFloatParameter depthWeight = new ClampedFloatParameter(0.1f, 0, 1);
        public ClampedFloatParameter normalWeight = new ClampedFloatParameter(0.1f, 0, 1);
    }
}
