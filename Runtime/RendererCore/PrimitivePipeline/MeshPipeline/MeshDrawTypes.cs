using System;
using Unity.Collections;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct MeshDrawExtract : IDisposable
    {
        public bool isCreated;
        internal NativeList<MeshDrawCommand> drawCommands;
        internal NativeList<int> instanceIndices;
        internal NativeList<int> instanceSlotIndices;

        public void Dispose()
        {
            if (!isCreated)
            {
                return;
            }

            if (drawCommands.IsCreated) drawCommands.Dispose();
            if (instanceIndices.IsCreated) instanceIndices.Dispose();
            if (instanceSlotIndices.IsCreated) instanceSlotIndices.Dispose();
            isCreated = false;
        }
    }

    public struct MeshDrawList
    {
        public NativeArray<MeshDrawCommand> commands;
        /// <summary>TransformId.Index per visible draw — CPU Submit / shading matrix lookup.</summary>
        public NativeArray<int> instanceIndices;
        /// <summary>MeshInstanceId.Index per visible draw — GPU cull candidate stream.</summary>
        public NativeArray<int> instanceSlotIndices;
        public int commandCount;
        public int instanceCount;
        public bool isValid;

        public static MeshDrawList Invalid => default;
    }

    public struct MeshDrawCommand
    {
        public int bakedTextureSet;
        public ulong meshUnityId;
        public int sectionIndex;
        public ulong materialUnityId;
        public int2 countOffset;

        public MeshDrawCommand(ulong meshUnityId, int sectionIndex, ulong materialUnityId, int2 countOffset, int bakedTextureSet = 0)
        {
            this.meshUnityId = meshUnityId;
            this.sectionIndex = sectionIndex;
            this.materialUnityId = materialUnityId;
            this.countOffset = countOffset;
            this.bakedTextureSet = bakedTextureSet;
        }
    }
}
