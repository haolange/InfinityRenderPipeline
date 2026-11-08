using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>How an entity's pose reaches the world: Static poses are written on explicit notification, Dynamic poses are read from the Transform every frame.</summary>
    [Serializable]
    public enum EMeshMobility
    {
        Static = 0,
        Dynamic = 1
    }

    [Serializable]
    public enum EMotionType
    {
        Camera = 0,
        Object = 1
    }

    [Serializable]
    public enum ECastShadowMethod
    {
        Off = 0,
        Static = 1,
        Dynamic = 2
    }

    [Serializable]
    public enum EMeshBackendPolicy
    {
        Auto = 0,
        CpuDirect = 1,
        GpuIndirect = 2
    }
}
