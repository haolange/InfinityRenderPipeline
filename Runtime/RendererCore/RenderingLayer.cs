using System;

namespace InfinityTech.Rendering
{
    // Canonical 8-bit rendering layer mask for lights and meshes.
    // Filter: (instance.mask & filter.mask) == 0 rejects. Everything=0xFF; use ~0u only for full 32-bit open.
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
}
