namespace InfinityTech.Rendering.MeshPipeline
{
    public enum MeshPassId : byte
    {
        Depth = 0,
        GBuffer = 1,
        Forward = 2,
        Motion = 3,
        Shadow = 4,
        TranslucentDepth = 5,
        TranslucentT0 = 6,
        TranslucentT1 = 7,
        TranslucentT2 = 8
    }

    public enum EMeshViewKind : byte
    {
        Main = 0,
        SceneView = 1,
        Preview = 2,
        CascadeShadow = 3,
        LocalShadow = 4
    }

    public enum EMeshSortPolicy : byte
    {
        StructuralMaterialOrder = 0,
        DistanceBuckets = 1,
        None = 2
    }
}
