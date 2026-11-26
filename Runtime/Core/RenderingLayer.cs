using System;

namespace InfinityTech.Rendering
{
    // The four light channels of meshes, lights, decals and analytic occluders. A light reaches a surface when the two
    // masks share a channel; the sideband stores a pixel's channels in four bits.
    [Flags]
    public enum ERenderingLayer : byte
    {
        Nothing = 0,
        LightLayerDefault = 1 << 0,
        LightLayer1 = 1 << 1,
        LightLayer2 = 1 << 2,
        LightLayer3 = 1 << 3,
        Everything = 0x0F,
    }

    public static class RenderingLayerUtility
    {
        // Every bit a mask may set.
        public const uint ValidMask = 0x0Fu;

        public static uint Validate(uint mask)
        {
            if ((mask & ~ValidMask) != 0)
                throw new ArgumentOutOfRangeException(nameof(mask), mask, "Infinity light channels are four bits. Run Window/Infinity/Lighting/Clamp Light Channels In Open Scenes, then correct the remaining masks.");
            return mask;
        }
    }
}
