using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct MeshDrawRequest
    {
        public MeshFilterProgram filter;
        public MeshSortPlan sort;
        public EMeshBackendPolicy backendPolicy;
        public int shaderPassIndex;
        public float3 viewPosition;
        public uint renderingLayerMask;
        public ulong viewKey;
    }

    public struct MeshDrawBuild : IDisposable
    {
        public JobHandle dependency;
        public bool isCreated;

        internal NativeList<VisibleMeshDraw> visibleDraws;
        internal NativeList<MeshDrawCommand> drawCommands;
        /// <summary>TransformId.Index per visible draw — CPU Submit / shading matrix lookup.</summary>
        internal NativeArray<int> instanceIndices;
        /// <summary>MeshInstanceId.Index per visible draw — GPU cull candidate stream.</summary>
        internal NativeArray<int> instanceSlotIndices;
        internal NativeArray<MeshPassDrawId> passDrawIds;

        public void Dispose()
        {
            if (!isCreated)
            {
                return;
            }

            dependency.Complete();

            if (visibleDraws.IsCreated) visibleDraws.Dispose();
            if (drawCommands.IsCreated) drawCommands.Dispose();
            if (instanceIndices.IsCreated) instanceIndices.Dispose();
            if (instanceSlotIndices.IsCreated) instanceSlotIndices.Dispose();
            if (passDrawIds.IsCreated) passDrawIds.Dispose();

            isCreated = false;
            dependency = default;
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

    public struct VisibleMeshDraw : IComparable<VisibleMeshDraw>
    {
        public MeshGroupingKey grouping;
        public MeshPassDrawId passDrawId;
        public MeshInstanceId instance;
        public ulong sortKey;
        public int drawIndex;
        public int transformIndex;

        public int CompareTo(VisibleMeshDraw other)
        {
            int c = sortKey.CompareTo(other.sortKey);
            if (c != 0)
            {
                return c;
            }

            c = passDrawId.Index.CompareTo(other.passDrawId.Index);
            if (c != 0)
            {
                return c;
            }

            c = passDrawId.Generation.CompareTo(other.passDrawId.Generation);
            if (c != 0)
            {
                return c;
            }

            return drawIndex.CompareTo(other.drawIndex);
        }
    }

    public struct MeshDrawCommand
    {
        public int meshUnityId;
        public int sectionIndex;
        public int materialUnityId;
        public int2 countOffset;

        public MeshDrawCommand(int meshUnityId, int sectionIndex, int materialUnityId, int2 countOffset)
        {
            this.meshUnityId = meshUnityId;
            this.sectionIndex = sectionIndex;
            this.materialUnityId = materialUnityId;
            this.countOffset = countOffset;
        }
    }
}
