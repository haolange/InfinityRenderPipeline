using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace InfinityTech.Rendering.MeshPipeline
{
    [BurstCompile]
    public unsafe struct MeshElementCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* viewFrustum;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public MeshElement* meshElements;

        [WriteOnly]
        public NativeArray<int> viewMeshElements;

        public void Execute(int index)
        {
            int visible = 1;
            ref MeshElement  meshElement = ref meshElements[index];

            for (int i = 0; i < 6; ++i)
            {
                ref FPlane plane = ref viewFrustum[i];

                float2 distRadius;
                distRadius.x = math.dot(math.abs(plane.normalDist.xyz), meshElement.boundBox.extents);
                distRadius.y = math.dot(plane.normalDist.xyz,  meshElement.boundBox.center) + plane.normalDist.w;

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }

            viewMeshElements[index] = math.select(0, visible,  meshElement.visible == 1);
        }
    }

    [BurstCompile]
    public struct MeshPassFilterJob : IJob
    {
        [ReadOnly]
        public CullingDatas cullingDatas;

        [ReadOnly]
        public NativeArray<MeshElement> meshElements;

        public MeshPassDescriptor meshPassDescriptor;

        [WriteOnly]
        public NativeList<PassMeshSection> passMeshSections;

        public void Execute()
        {
            MeshElement meshElement;

            //Gather PassMeshBatch
            for (int i = 0; i < cullingDatas.viewMeshElements.Length; ++i)
            {
                if (cullingDatas.viewMeshElements[i] != 0)
                {
                    meshElement = meshElements[i];

                    if (meshElement.priority >= meshPassDescriptor.renderQueueMin && meshElement.priority <= meshPassDescriptor.renderQueueMax)
                    {
                        PassMeshSection passMeshBatch = new PassMeshSection(i, MeshElement.MatchForDynamicInstance(ref meshElement));
                        passMeshSections.Add(passMeshBatch);
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct MeshPassSortJob : IJob
    {
        public NativeList<PassMeshSection> passMeshSections;

        public void Execute()
        {
            passMeshSections.Sort();
        }
    }

    [BurstCompile]
    public struct MeshPassSortJobV2 : IJob
    {
        public int left;
        public int right;
        public NativeList<PassMeshSection> passMeshSections;

        public void Execute()
        {
            Quicksort(left, right);
        }

        void Quicksort(in int leftValue, in int rightValue)
        {
            int i = leftValue;
            int j = rightValue;
            PassMeshSection pivot = passMeshSections[(leftValue + rightValue) / 2];

            while (i <= j)
            {
                // Lesser
                while (passMeshSections[i].CompareTo(pivot) < 0)
                {
                    ++i;
                }

                // Greater
                while (passMeshSections[j].CompareTo(pivot) > 0)
                {
                    --j;
                }

                if (i <= j)
                {
                    // Swap
                    PassMeshSection temp = passMeshSections[i];
                    passMeshSections[i] = passMeshSections[j];
                    passMeshSections[j] = temp;

                    ++i;
                    --j;
                }
            }

            // Recurse
            if (leftValue < j)
            {
                Quicksort(leftValue, j);
            }

            if (i < rightValue)
            {
                Quicksort(i, rightValue);
            }
        }
    }

    [BurstCompile]
    public struct MeshPassBuildJob : IJob
    {
        [WriteOnly]
        public NativeArray<int> meshBatchIndexs;

        [ReadOnly]
        public NativeArray<MeshElement> meshElements;

        [ReadOnly]
        public NativeList<PassMeshSection> passMeshSections;

        public NativeList<MeshDrawCommand> meshDrawCommands;

        public void Execute()
        {
            MeshElement meshElement;
            MeshDrawCommand meshDrawCommand;
            PassMeshSection passMeshSection;
            MeshDrawCommand cacheMeshDrawCommand;
            PassMeshSection lastPassMeshSection = new PassMeshSection(-1, -1);

            for (int j = 0; j < passMeshSections.Length; ++j)
            {
                passMeshSection = passMeshSections[j];
                meshBatchIndexs[j] = passMeshSection.meshElementId;
                meshElement = meshElements[passMeshSection.meshElementId];

                if (!passMeshSection.Equals(lastPassMeshSection))
                {
                    lastPassMeshSection = passMeshSection;

                    meshDrawCommand.meshIndex = meshElement.meshRef.Id;
                    meshDrawCommand.sectionIndex = meshElement.sectionIndex;
                    meshDrawCommand.materialIndex = meshElement.materialRef.Id;
                    meshDrawCommand.countOffset.x = 0;
                    meshDrawCommand.countOffset.y = j;
                    meshDrawCommands.Add(meshDrawCommand);
                }

                cacheMeshDrawCommand = meshDrawCommands[meshDrawCommands.Length - 1];
                cacheMeshDrawCommand.countOffset.x += 1;
                meshDrawCommands[meshDrawCommands.Length - 1] = cacheMeshDrawCommand;
            }
        }
    }

    [BurstCompile]
    public struct MeshPassGenerateJob : IJob
    {
        [ReadOnly]
        public CullingDatas cullingDatas;

        [WriteOnly]
        public NativeArray<int> meshBatchIndexs;

        [ReadOnly]
        public NativeArray<MeshElement> meshElements;

        [ReadOnly]
        public MeshPassDescriptor meshPassDescriptor;

        public NativeList<PassMeshSection> passMeshSections;

        public NativeList<MeshDrawCommand> meshDrawCommands;

        public void Execute()
        {
            MeshElement meshElement;

            //Gather PassMeshBatch
            for (int i = 0; i < cullingDatas.viewMeshElements.Length; ++i)
            {
                if (cullingDatas.viewMeshElements[i] != 0)
                {
                    meshElement = meshElements[i];

                    if(meshElement.priority >= meshPassDescriptor.renderQueueMin && meshElement.priority <= meshPassDescriptor.renderQueueMax)
                    {
                        PassMeshSection passMeshBatch = new PassMeshSection(i, MeshElement.MatchForDynamicInstance(ref meshElement));
                        passMeshSections.Add(passMeshBatch);
                    }
                }
            }

            //Sort PassMeshBatch
            passMeshSections.Sort();
            //Quicksort(0, passMeshSections.Length - 1);

            //Build MeshDrawCommand
            PassMeshSection passMeshSection;
            PassMeshSection lastPassMeshSection = new PassMeshSection(-1, -1);

            MeshDrawCommand meshDrawCommand;
            MeshDrawCommand cacheMeshDrawCommand;

            for (int j = 0; j < passMeshSections.Length; ++j)
            {
                passMeshSection = passMeshSections[j];
                meshBatchIndexs[j] = passMeshSection.meshElementId;
                meshElement = meshElements[passMeshSection.meshElementId];

                if (!passMeshSection.Equals(lastPassMeshSection))
                {
                    lastPassMeshSection = passMeshSection;

                    meshDrawCommand.meshIndex = meshElement.meshRef.Id;
                    meshDrawCommand.sectionIndex = meshElement.sectionIndex;
                    meshDrawCommand.materialIndex = meshElement.materialRef.Id;
                    meshDrawCommand.countOffset.x = 0;
                    meshDrawCommand.countOffset.y = j;
                    meshDrawCommands.Add(meshDrawCommand);
                }

                cacheMeshDrawCommand = meshDrawCommands[meshDrawCommands.Length - 1];
                cacheMeshDrawCommand.countOffset.x += 1;
                meshDrawCommands[meshDrawCommands.Length - 1] = cacheMeshDrawCommand;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Quicksort(in int leftValue, in int rightValue)
        {
            int i = leftValue;
            int j = rightValue;
            PassMeshSection pivot = passMeshSections[(leftValue + rightValue) / 2];

            while (i <= j)
            {
                // Lesser
                while (passMeshSections[i].CompareTo(pivot) < 0)
                {
                    ++i;
                }
  
                // Greater
                while (passMeshSections[j].CompareTo(pivot) > 0)
                {
                    --j;
                }

                if (i <= j)
                {
                    // Swap
                    PassMeshSection temp = passMeshSections[i];
                    passMeshSections[i] = passMeshSections[j];
                    passMeshSections[j] = temp;

                    ++i;
                    --j;
                }
            }

            // Recurse
            if (leftValue < j)
            {
                Quicksort(leftValue, j);
            }

            if (i < rightValue)
            {
                Quicksort(i, rightValue);
            }
        }
    }
}
