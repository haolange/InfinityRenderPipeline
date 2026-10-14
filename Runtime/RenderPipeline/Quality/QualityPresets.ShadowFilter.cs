using System;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline
{
    // Low-level shadow filtering settings shared by every shadowed light. Selected on the RP Asset (shadow filtering
    // is a project-wide cost decision, not a per-Volume one); each light chooses its filter (Hard, PCF, PCSS, VSM, EVSM).
    [Serializable]
    public struct FShadowFilterSettings
    {
        [Tooltip("PCF tent radius in shadow-map texels (1 = 3x3, 2 = 5x5, 3 = 7x7).")]
        [Range(1, 3)] public int pcfRadius;
        [Tooltip("PCSS blocker-search samples.")]
        [Range(4, 64)] public int pcssBlockerSamples;
        [Tooltip("PCSS filter samples.")]
        [Range(4, 64)] public int pcssFilterSamples;
        [Tooltip("Largest PCSS penumbra and blocker-search radius in shadow-map texels.")]
        [Range(2, 64)] public float pcssMaxPenumbra;
        [Tooltip("EVSM warp exponent (16-bit moments stay finite up to about 5.54).")]
        [Range(1, 5.54f)] public float evsmExponent;
        [Tooltip("Cuts the VSM / EVSM light-bleeding tail (0 = none).")]
        [Range(0, 0.95f)] public float lightBleedingReduction;
        [Tooltip("Gaussian blur radius of the VSM / EVSM moments in texels (0 = no blur).")]
        [Range(0, 4)] public int momentBlurRadius;
    }

    public static partial class QualityPresets
    {
        public static readonly FShadowFilterSettings[] ShadowFilter =
        {
            new FShadowFilterSettings { pcfRadius = 1, pcssBlockerSamples = 8, pcssFilterSamples = 12, pcssMaxPenumbra = 12, evsmExponent = 5.0f, lightBleedingReduction = 0.3f, momentBlurRadius = 1 },  // Low
            new FShadowFilterSettings { pcfRadius = 2, pcssBlockerSamples = 16, pcssFilterSamples = 24, pcssMaxPenumbra = 24, evsmExponent = 5.4f, lightBleedingReduction = 0.25f, momentBlurRadius = 2 }, // Medium
            new FShadowFilterSettings { pcfRadius = 2, pcssBlockerSamples = 24, pcssFilterSamples = 32, pcssMaxPenumbra = 32, evsmExponent = 5.54f, lightBleedingReduction = 0.2f, momentBlurRadius = 2 }, // High
            new FShadowFilterSettings { pcfRadius = 3, pcssBlockerSamples = 32, pcssFilterSamples = 48, pcssMaxPenumbra = 48, evsmExponent = 5.54f, lightBleedingReduction = 0.15f, momentBlurRadius = 3 }, // Ultra
        };
    }
}
