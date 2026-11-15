using System;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>The nine mesh passes. Each one is a pass cache for the life of the world.</summary>
    internal static class MeshPassTable
    {
        public const int OpaqueQueueMax = 2999;
        public const int TranslucentQueueMin = 3000;
        public const int TranslucentQueueMax = 5000;

        public static readonly MeshPassDesc Depth = new MeshPassDesc("RasterDepthPass", "Depth", 0, OpaqueQueueMax);
        public static readonly MeshPassDesc GBuffer = new MeshPassDesc("RasterGBufferPass", "GBuffer", 0, OpaqueQueueMax);
        public static readonly MeshPassDesc Forward = new MeshPassDesc("RasterForwardPass", "Forward", 0, OpaqueQueueMax);
        public static readonly MeshPassDesc Motion = new MeshPassDesc("RasterMotionPass", "Motion", 0, OpaqueQueueMax,
            EMeshEntityFlags.ObjectMotion, requirePreviousTransforms: true);
        public static readonly MeshPassDesc Shadow = new MeshPassDesc("RasterShadowPass", "Shadow", 0, OpaqueQueueMax, EMeshEntityFlags.CastShadow);
        public static readonly MeshPassDesc TranslucentDepth = new MeshPassDesc("RasterTranslucentDepthPass", "TranslucentDepth", TranslucentQueueMin, TranslucentQueueMax);
        public static readonly MeshPassDesc TranslucentT0 = new MeshPassDesc("RasterTranslucentT0Pass", "TranslucentT0", TranslucentQueueMin, TranslucentQueueMax,
            requirePreviousTransforms: true, sortKey: EMeshSortKey.TranslucentColor, cpuBackend: true);
        public static readonly MeshPassDesc TranslucentT1 = new MeshPassDesc("RasterTranslucentT1Pass", "TranslucentT1", TranslucentQueueMin, TranslucentQueueMax,
            requirePreviousTransforms: true, sortKey: EMeshSortKey.TranslucentColor, cpuBackend: true);
        public static readonly MeshPassDesc TranslucentT2 = new MeshPassDesc("RasterTranslucentT2Pass", "TranslucentT2", TranslucentQueueMin, TranslucentQueueMax,
            requirePreviousTransforms: true, sortKey: EMeshSortKey.TranslucentColor, cpuBackend: true);

        internal static readonly MeshPassDesc[] all =
        {
            Depth, GBuffer, Forward, Motion, Shadow, TranslucentDepth, TranslucentT0, TranslucentT1, TranslucentT2
        };

        internal static bool TryGet(ShaderTagId passTag, out MeshPassDesc desc)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].passTag == passTag)
                {
                    desc = all[i];
                    return true;
                }
            }
            desc = default;
            return false;
        }

        /// <summary>The LightModes that Infinity mesh shaders and the material route define.</summary>
        public static bool IsMeshLightMode(string lightMode)
        {
            foreach (MeshPassDesc preset in all)
                if (string.Equals(preset.lightMode, lightMode, StringComparison.Ordinal))
                {
                    return true;
                }
            return false;
        }
    }
}
