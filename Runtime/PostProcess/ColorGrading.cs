using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.PostProcess
{
    [Serializable, VolumeComponentMenu("Color Grade/ColorGrading")]
    public class ColorGrading : VolumeComponent
    {
        [Header("Temperature")]
        public ClampedFloatParameter Temp = new ClampedFloatParameter(6500.0f, 1500.0f, 15000.0f);
        public ClampedFloatParameter Tint = new ClampedFloatParameter(0.0f, -1.0f, 1.0f);

        [Header("Global")]
        public Vector4Parameter ColorSaturation = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorContrast = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGamma = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGain = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorOffset = new Vector4Parameter(new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

        [Header("Shadows")]
        public Vector4Parameter ColorSaturationShadows = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorContrastShadows = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGammaShadows = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGainShadows = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorOffsetShadows = new Vector4Parameter(new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        public ClampedFloatParameter ShadowsMax = new ClampedFloatParameter(0.09f, -1.0f, 1.0f);

        [Header("Midtones")]
        public Vector4Parameter ColorSaturationMidtones = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorContrastMidtones = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGammaMidtones = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGainMidtones = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorOffsetMidtones = new Vector4Parameter(new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

        [Header("Highlights")]
        public Vector4Parameter ColorSaturationHighlights = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorContrastHighlights = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGammaHighlights = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorGainHighlights = new Vector4Parameter(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        public Vector4Parameter ColorOffsetHighlights = new Vector4Parameter(new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
        public ClampedFloatParameter HighlightsMin = new ClampedFloatParameter(0.5f, -1.0f, 1.0f);
        public ClampedFloatParameter HighlightsMax = new ClampedFloatParameter(1.0f, 1.0f, 10.0f);

        [Header("Misc")]
        public ClampedFloatParameter BlueCorrection = new ClampedFloatParameter(0.6f, 0.0f, 1.0f);
        public ClampedFloatParameter ExpandGamut = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
    }
}
