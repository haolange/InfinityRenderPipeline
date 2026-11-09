using System;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Quality tier of a Volume feature. Every tier except Custom selects a hardcoded preset in code
    // (QualityPresets.<Feature>.cs); Custom uses the low-level parameters authored on the Volume.
    public enum EQualityLevel
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
        Custom = 4,
    }

    [Serializable]
    public sealed class QualityLevelParameter : VolumeParameter<EQualityLevel>
    {
        public QualityLevelParameter(EQualityLevel value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    // Each feature declares its low-level settings struct, one preset per non-Custom level, and a Resolve() on its
    // Volume component. Passes only ever see the resolved struct; they never read EQualityLevel.
    public static partial class QualityPresets
    {
        public const int PresetCount = 4;

        public static T Select<T>(T[] presets, EQualityLevel level) where T : struct
        {
            if (presets == null || presets.Length != PresetCount)
                throw new InvalidOperationException($"InfinityRP: {typeof(T).Name} must have exactly {PresetCount} presets (Low, Medium, High, Ultra).");
            if (level == EQualityLevel.Custom)
                throw new ArgumentException("Custom has no preset; read the Volume parameters instead.", nameof(level));
            return presets[(int)level];
        }
    }
}
