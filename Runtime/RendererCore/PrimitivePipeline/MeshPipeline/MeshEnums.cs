using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    [Serializable]
    public enum EStateType
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
    public enum EGeometrySourceKind
    {
        IndexedMesh = 0,
        SkinnedDeformed = 1,
        Procedural = 2,
        MeshletCluster = 3
    }

    [Flags]
    [Serializable]
    public enum EMeshInstanceFlags
    {
        None = 0,
        CastShadow = 1 << 0,
        ReceiveShadow = 1 << 1,
        Visible = 1 << 2,
        AffectIndirect = 1 << 3
    }

    [Flags]
    [Serializable]
    public enum EPassEligibility
    {
        None = 0,
        Depth = 1 << 0,
        GBuffer = 1 << 1,
        Forward = 1 << 2,
        Motion = 1 << 3,
        Shadow = 1 << 4,
        Transparent = 1 << 5
    }

    [Serializable]
    public enum EMeshBackendPolicy
    {
        Auto = 0,
        CpuDirect = 1,
        GpuIndirect = 2
    }

    [Serializable]
    public enum EMeshSortSemantic
    {
        PassPriority = 0,
        RenderQueue = 1,
        PipelineState = 2,
        Shader = 3,
        Material = 4,
        GeometryLayout = 5,
        Mesh = 6,
        Section = 7,
        InstanceGroup = 8,
        Distance = 9,
        StableDrawId = 10
    }

    [Serializable]
    public enum ESortDirection
    {
        Ascending = 0,
        Descending = 1
    }
}
