using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    public enum ESortMode : byte
    {
        /// <summary>Shader state, then material, then mesh: the fewest state changes.</summary>
        State = 0,
        /// <summary>Coarse camera distance first, then state. For depth-tested passes.</summary>
        FrontToBack = 1,
        /// <summary>Priority, then exact camera distance descending. Blended passes; recorded by the CPU backend only.</summary>
        BackToFront = 2
    }

    /// <summary>
    /// A draw request in the style of DrawRenderers: which LightMode to draw, which queues, which entities, in what order.
    /// Equal descs share one interned <see cref="MeshPassCache"/> per world. Field-level equality is the identity.
    /// </summary>
    internal readonly struct MeshPassDesc : IEquatable<MeshPassDesc>
    {
        public readonly string lightMode;
        public readonly int queueMin;
        public readonly int queueMax;
        public readonly ESortMode sortMode;
        public readonly EMeshEntityFlags requireEntityFlags;
        /// <summary>The draw list binds the view's accepted previous transforms and fails at record without them.</summary>
        public readonly bool requirePreviousTransforms;

        public MeshPassDesc(string lightMode, int queueMin, int queueMax, ESortMode sortMode,
            EMeshEntityFlags requireEntityFlags = EMeshEntityFlags.None, bool requirePreviousTransforms = false)
        {
            this.lightMode = lightMode;
            this.queueMin = queueMin;
            this.queueMax = queueMax;
            this.sortMode = sortMode;
            this.requireEntityFlags = requireEntityFlags;
            this.requirePreviousTransforms = requirePreviousTransforms;
        }

        public bool Equals(MeshPassDesc other)
        {
            return string.Equals(lightMode, other.lightMode, StringComparison.Ordinal) && queueMin == other.queueMin && queueMax == other.queueMax
                && sortMode == other.sortMode && requireEntityFlags == other.requireEntityFlags && requirePreviousTransforms == other.requirePreviousTransforms;
        }

        public override bool Equals(object obj) => obj is MeshPassDesc other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = lightMode != null ? StringComparer.Ordinal.GetHashCode(lightMode) : 0;
                hash = hash * 31 + queueMin;
                hash = hash * 31 + queueMax;
                hash = hash * 31 + (int)sortMode;
                hash = hash * 31 + (int)requireEntityFlags;
                return hash * 31 + (requirePreviousTransforms ? 1 : 0);
            }
        }

        /// <summary>Opaque passes cross-fade LOD transitions with dithering; translucent passes draw the stable level only.</summary>
        public bool ditherLODTransitions => queueMax < MeshPassTable.TranslucentQueueMin;

        public override string ToString() => $"{lightMode} [{queueMin}, {queueMax}] {sortMode}";
    }
}
