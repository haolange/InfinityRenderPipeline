using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    public enum EGatherMethod
    {
        DotsV1,
        DotsV2,
        DefaultV1,
        DefaultV2
    }

    public struct FMeshDrawCommand
    {
        public int meshIndex;
        public int submeshIndex;
        public int materialindex;
        public int2 countOffset;

        public FMeshDrawCommand(in int meshIndex, in int submeshIndex, in int materialindex, in int2 countOffset)
        {
            this.meshIndex = meshIndex;
            this.submeshIndex = submeshIndex;
            this.materialindex = materialindex;
            this.countOffset = countOffset;
        }
    }
}
