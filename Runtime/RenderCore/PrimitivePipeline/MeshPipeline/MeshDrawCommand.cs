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

    public struct MeshDrawCommand
    {
        public int meshIndex;
        public int sectionIndex;
        public int materialIndex;
        public int2 countOffset;

        public MeshDrawCommand(in int meshIndex, in int sectionIndex, in int materialIndex, in int2 countOffset)
        {
            this.meshIndex = meshIndex;
            this.countOffset = countOffset;
            this.sectionIndex = sectionIndex;
            this.materialIndex = materialIndex;
        }
    }
}
