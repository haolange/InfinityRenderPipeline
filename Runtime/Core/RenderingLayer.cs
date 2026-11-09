using System;

namespace InfinityTech.Rendering
{
    // Canonical 8-bit rendering layer mask for lights and meshes.
    // Surface lighting and caster filtering use these bits; camera visibility is separate.
    [Flags]
    public enum ERenderingLayer : byte
    {
        Nothing = 0,
        LightLayerDefault = 1 << 0,
        LightLayer1 = 1 << 1,
        LightLayer2 = 1 << 2,
        LightLayer3 = 1 << 3,
        LightLayer4 = 1 << 4,
        LightLayer5 = 1 << 5,
        LightLayer6 = 1 << 6,
        LightLayer7 = 1 << 7,
        Everything = 0xFF,
    }

    public static class RenderingLayerUtility
    {
        public static uint Validate(uint mask)
        {
            if ((mask & ~0xFFu) != 0)
                throw new ArgumentOutOfRangeException(nameof(mask), mask, "Infinity rendering layers are eight bits. Migrate Unity Everything explicitly; unknown high bits are invalid.");
            return mask;
        }
    }
}
