using System;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>One interned pass cache: LightMode, queues, entity flags, sort key and CPU recording.</summary>
    internal readonly struct MeshPassDesc : IEquatable<MeshPassDesc>
    {
        public readonly string lightMode;
        // The interned id of lightMode: per-frame lookups compare ids, never names (ShaderTagId.name allocates a string).
        public readonly ShaderTagId passTag;
        public readonly int queueMin;
        public readonly int queueMax;
        public readonly EMeshEntityFlags requireEntityFlags;
        /// <summary>The draw list binds the view's accepted previous transforms and fails at record without them.</summary>
        public readonly bool requirePreviousTransforms;
        /// <summary>Which compiled gather writes the instance key.</summary>
        public readonly EMeshSortKey sortKey;
        /// <summary>This pass records on the CPU backend.</summary>
        public readonly bool cpuBackend;

        public MeshPassDesc(string lightMode, int queueMin, int queueMax,
            EMeshEntityFlags requireEntityFlags = EMeshEntityFlags.None, bool requirePreviousTransforms = false,
            EMeshSortKey sortKey = EMeshSortKey.State, bool cpuBackend = false)
        {
            this.lightMode = lightMode;
            passTag = new ShaderTagId(lightMode);
            this.queueMin = queueMin;
            this.queueMax = queueMax;
            this.requireEntityFlags = requireEntityFlags;
            this.requirePreviousTransforms = requirePreviousTransforms;
            this.sortKey = sortKey;
            this.cpuBackend = cpuBackend;
        }

        public bool Equals(MeshPassDesc other)
        {
            return string.Equals(lightMode, other.lightMode, StringComparison.Ordinal) && queueMin == other.queueMin && queueMax == other.queueMax
                && requireEntityFlags == other.requireEntityFlags && requirePreviousTransforms == other.requirePreviousTransforms
                && sortKey == other.sortKey && cpuBackend == other.cpuBackend;
        }

        public override bool Equals(object obj) => obj is MeshPassDesc other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = lightMode != null ? StringComparer.Ordinal.GetHashCode(lightMode) : 0;
                hash = hash * 31 + queueMin;
                hash = hash * 31 + queueMax;
                hash = hash * 31 + (int)requireEntityFlags;
                hash = hash * 31 + (requirePreviousTransforms ? 1 : 0);
                hash = hash * 31 + (int)sortKey;
                return hash * 31 + (cpuBackend ? 1 : 0);
            }
        }

        /// <summary>Opaque passes cross-fade LOD transitions with dithering; translucent passes draw the stable level only.</summary>
        public bool ditherLODTransitions => queueMax < MeshPassTable.TranslucentQueueMin;

        public override string ToString() => $"{lightMode} [{queueMin}, {queueMax}]";
    }
}
