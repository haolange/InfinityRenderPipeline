using System.Collections.Generic;
using UnityEngine;

namespace InfinityTech.Rendering.LightPipeline
{
    /// <summary>
    /// Tracks per-light desired local shadow resolution tiers with hysteresis deadband and cooldown
    /// frames so camera jitter near tier boundaries does not re-render or thrash the atlas every frame.
    /// Zero steady-state managed heap allocation.
    /// </summary>
    internal sealed class ShadowResolutionHistory
    {
        public const int DefaultCooldownFrames = 8;
        public const int DefaultPruneFrames = 120;
        public const float UpgradeHysteresis = 1.25f;
        public const float DowngradeHysteresis = 0.75f;

        struct Entry
        {
            public int resolution;
            public int lastChangeFrame;
            public int lastSeenFrame;
        }

        readonly Dictionary<(ulong, ulong), Entry> m_Entries = new Dictionary<(ulong, ulong), Entry>(64);
        readonly List<(ulong, ulong)> m_PruneScratch = new List<(ulong, ulong)>(64);
        int m_LastPrunedFrame = int.MinValue;

        public int EntryCount => m_Entries.Count;

        public void Clear()
        {
            m_Entries.Clear();
            m_PruneScratch.Clear();
            m_LastPrunedFrame = int.MinValue;
        }

        public static int CapResolutionForFaceCount(int maxTile, int minTile, int atlasResolution, int faceCount)
        {
            if (faceCount <= 1)
                return Mathf.Max(minTile, maxTile);
            int cubeCap = ShadowAtlasLayout.PreviousPowerOfTwo(Mathf.Max(minTile, atlasResolution / 4));
            return Mathf.Clamp(Mathf.Min(maxTile, cubeCap), minTile, Mathf.Max(minTile, maxTile));
        }

        public static int TargetTierFromProjectedPixels(float projectedPixels, int minTile, int maxTile)
        {
            minTile = ShadowAtlasLayout.PreviousPowerOfTwo(Mathf.Max(1, minTile));
            maxTile = ShadowAtlasLayout.PreviousPowerOfTwo(Mathf.Max(minTile, maxTile));
            if (!(projectedPixels > 0f) || float.IsNaN(projectedPixels))
                return minTile;

            int tier = minTile;
            while (tier < maxTile && projectedPixels >= tier * 1.5f)
                tier <<= 1;
            return Mathf.Clamp(tier, minTile, maxTile);
        }

        public int Resolve(
            ulong lightId,
            ulong sceneCullingMask,
            float projectedPixels,
            int faceCount,
            int minTile,
            int maxTile,
            int atlasResolution,
            int frame,
            int cooldownFrames = DefaultCooldownFrames)
        {
            minTile = ShadowAtlasLayout.PreviousPowerOfTwo(Mathf.Max(1, minTile));
            int effectiveMax = CapResolutionForFaceCount(maxTile, minTile, atlasResolution, faceCount);
            int target = TargetTierFromProjectedPixels(projectedPixels, minTile, effectiveMax);

            var key = (lightId, sceneCullingMask);
            if (!m_Entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry
                {
                    resolution = target,
                    lastChangeFrame = frame,
                    lastSeenFrame = frame
                };
                m_Entries[key] = entry;
                PruneIfDue(frame);
                return target;
            }

            // Clamp stored tier into the current bounds if atlas settings changed.
            int current = Mathf.Clamp(ShadowAtlasLayout.PreviousPowerOfTwo(Mathf.Max(minTile, entry.resolution)), minTile, effectiveMax);
            entry.lastSeenFrame = frame;

            if (frame < entry.lastChangeFrame)
                entry.lastChangeFrame = frame;

            bool cooldownElapsed = (frame - entry.lastChangeFrame) >= cooldownFrames;
            if (cooldownElapsed && target != current)
            {
                if (target > current)
                {
                    float upgradeThreshold = current * 1.5f * UpgradeHysteresis;
                    if (projectedPixels >= upgradeThreshold)
                    {
                        current = target;
                        entry.lastChangeFrame = frame;
                    }
                }
                else
                {
                    float downgradeThreshold = current * 0.75f * DowngradeHysteresis;
                    if (projectedPixels <= downgradeThreshold)
                    {
                        current = target;
                        entry.lastChangeFrame = frame;
                    }
                }
            }

            entry.resolution = current;
            m_Entries[key] = entry;
            PruneIfDue(frame);
            return current;
        }

        void PruneIfDue(int frame)
        {
            if (frame == m_LastPrunedFrame || (m_LastPrunedFrame != int.MinValue && frame - m_LastPrunedFrame < DefaultPruneFrames))
                return;

            m_LastPrunedFrame = frame;
            if (m_Entries.Count == 0)
                return;

            m_PruneScratch.Clear();
            foreach (var pair in m_Entries)
            {
                if (frame - pair.Value.lastSeenFrame >= DefaultPruneFrames || frame < pair.Value.lastSeenFrame)
                    m_PruneScratch.Add(pair.Key);
            }
            for (int i = 0; i < m_PruneScratch.Count; ++i)
                m_Entries.Remove(m_PruneScratch[i]);
            m_PruneScratch.Clear();
        }
    }
}
