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
    public readonly struct MeshPassDesc : IEquatable<MeshPassDesc>
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
        public bool ditherLodTransitions => queueMax < MeshPassPresets.TranslucentQueueMin;

        public override string ToString() => $"{lightMode} [{queueMin}, {queueMax}] {sortMode}";
    }

    /// <summary>The pipeline's mesh passes. Every preset is interned when the world is created.</summary>
    public static class MeshPassPresets
    {
        public const int OpaqueQueueMax = 2999;
        public const int TranslucentQueueMin = 3000;
        public const int TranslucentQueueMax = 5000;

        public static readonly MeshPassDesc Depth = new MeshPassDesc("RasterDepthPass", 0, OpaqueQueueMax, ESortMode.FrontToBack);
        public static readonly MeshPassDesc GBuffer = new MeshPassDesc("RasterGBufferPass", 0, OpaqueQueueMax, ESortMode.State);
        public static readonly MeshPassDesc Forward = new MeshPassDesc("RasterForwardPass", 0, OpaqueQueueMax, ESortMode.State);
        public static readonly MeshPassDesc Motion = new MeshPassDesc("RasterMotionPass", 0, OpaqueQueueMax, ESortMode.FrontToBack,
            EMeshEntityFlags.ObjectMotion, requirePreviousTransforms: true);
        public static readonly MeshPassDesc Shadow = new MeshPassDesc("RasterShadowPass", 0, OpaqueQueueMax, ESortMode.State, EMeshEntityFlags.CastShadow);
        public static readonly MeshPassDesc TranslucentDepth = new MeshPassDesc("RasterTranslucentDepthPass", TranslucentQueueMin, TranslucentQueueMax, ESortMode.FrontToBack);
        public static readonly MeshPassDesc TranslucentT0 = new MeshPassDesc("RasterTranslucentT0Pass", TranslucentQueueMin, TranslucentQueueMax, ESortMode.BackToFront,
            requirePreviousTransforms: true);
        public static readonly MeshPassDesc TranslucentT1 = new MeshPassDesc("RasterTranslucentT1Pass", TranslucentQueueMin, TranslucentQueueMax, ESortMode.BackToFront,
            requirePreviousTransforms: true);
        public static readonly MeshPassDesc TranslucentT2 = new MeshPassDesc("RasterTranslucentT2Pass", TranslucentQueueMin, TranslucentQueueMax, ESortMode.BackToFront,
            requirePreviousTransforms: true);

        internal static readonly MeshPassDesc[] all =
        {
            Depth, GBuffer, Forward, Motion, Shadow, TranslucentDepth, TranslucentT0, TranslucentT1, TranslucentT2
        };

        /// <summary>The LightModes that Infinity mesh shaders and the material route define.</summary>
        public static bool IsMeshLightMode(string lightMode)
        {
            foreach (MeshPassDesc preset in all)
                if (string.Equals(preset.lightMode, lightMode, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
