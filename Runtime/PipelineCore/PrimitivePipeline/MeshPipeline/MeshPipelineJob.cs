using System;
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
    public unsafe struct FMeshElementCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* viewFrustum;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FMeshElement* meshElements;

        [WriteOnly]
        public NativeArray<int> viewMeshElements;

        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref FMeshElement  meshElement = ref meshElements[index];

            for (int i = 0; i < 6; ++i)
            {
                ref FPlane plane = ref viewFrustum[i];
                distRadius.x = math.dot(plane.normalDist.xyz,  meshElement.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz),  meshElement.boundBox.extents);

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }

            viewMeshElements[index] = math.select(0, visible,  meshElement.visible == 1);
        }
    }

    [BurstCompile]
    public struct FMeshPassFilterJob : IJob
    {
        [ReadOnly]
        public FCullingData cullingData;

        [ReadOnly]
        public NativeArray<FMeshElement> meshElements;

        public FMeshPassDesctiption meshPassDesctiption;

        [WriteOnly]
        public NativeList<FPassMeshSection> passMeshSections;

        public void Execute()
        {
            FMeshElement meshElement;

            //Gather PassMeshBatch
            for (int i = 0; i < cullingData.viewMeshElements.Length; ++i)
            {
                if (cullingData.viewMeshElements[i] != 0)
                {
                    meshElement = meshElements[i];

                    if (meshElement.priority >= meshPassDesctiption.renderQueueMin && meshElement.priority <= meshPassDesctiption.renderQueueMax)
                    {
                        FPassMeshSection passMeshBatch = new FPassMeshSection(i, FMeshElement.MatchForDynamicInstance(ref meshElement));
                        passMeshSections.Add(passMeshBatch);
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct FMeshPassSortJob : IJob
    {
        public NativeList<FPassMeshSection> passMeshSections;

        public void Execute()
        {
            passMeshSections.Sort();
        }
    }

    [BurstCompile]
    public struct FMeshPassSortJobV2 : IJob
    {
        public int left;
        public int right;
        public NativeList<FPassMeshSection> passMeshSections;

        public void Execute()
        {
            Quicksort(left, right);
        }

        void Quicksort(in int leftValue, in int rightValue)
        {
            int i = leftValue;
            int j = rightValue;
            FPassMeshSection pivot = passMeshSections[(leftValue + rightValue) / 2];

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
                    FPassMeshSection temp = passMeshSections[i];
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
    public struct FMeshPassBuildJob : IJob
    {
        [WriteOnly]
        public NativeArray<int> meshBatchIndexs;

        [ReadOnly]
        public NativeArray<FMeshElement> meshElements;

        [ReadOnly]
        public NativeList<FPassMeshSection> passMeshSections;

        public NativeList<FMeshDrawCommand> meshDrawCommands;

        public void Execute()
        {
            FMeshElement meshElement;
            FMeshDrawCommand meshDrawCommand;
            FPassMeshSection passMeshSection;
            FMeshDrawCommand cacheMeshDrawCommand;
            FPassMeshSection lastPassMeshSection = new FPassMeshSection(-1, -1);

            for (int j = 0; j < passMeshSections.Length; ++j)
            {
                passMeshSection = passMeshSections[j];
                meshBatchIndexs[j] = passMeshSection.meshElementId;
                meshElement = meshElements[passMeshSection.meshElementId];

                if (!passMeshSection.Equals(lastPassMeshSection))
                {
                    lastPassMeshSection = passMeshSection;

                    meshDrawCommand.meshIndex = meshElement.staticMeshRef.Id;
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
    public struct FMeshPassGenerateJob : IJob
    {
        [ReadOnly]
        public FCullingData cullingData;

        [WriteOnly]
        public NativeArray<int> meshBatchIndexs;

        [ReadOnly]
        public NativeArray<FMeshElement> meshElements;

        [ReadOnly]
        public FMeshPassDesctiption meshPassDesctiption;

        public NativeList<FPassMeshSection> passMeshSections;

        public NativeList<FMeshDrawCommand> meshDrawCommands;

        public void Execute()
        {
            FMeshElement meshElement;

            //Gather PassMeshBatch
            for (int i = 0; i < cullingData.viewMeshElements.Length; ++i)
            {
                if (cullingData.viewMeshElements[i] != 0)
                {
                    meshElement = meshElements[i];

                    if(meshElement.priority >= meshPassDesctiption.renderQueueMin && meshElement.priority <= meshPassDesctiption.renderQueueMax)
                    {
                        FPassMeshSection passMeshBatch = new FPassMeshSection(i, FMeshElement.MatchForDynamicInstance(ref meshElement));
                        passMeshSections.Add(passMeshBatch);
                    }
                }
            }

            //Sort PassMeshBatch
            passMeshSections.Sort();
            //Quicksort(0, passMeshSections.Length - 1);

            //Build MeshDrawCommand
            FPassMeshSection passMeshSection;
            FPassMeshSection lastPassMeshSection = new FPassMeshSection(-1, -1);

            FMeshDrawCommand meshDrawCommand;
            FMeshDrawCommand cacheMeshDrawCommand;

            for (int j = 0; j < passMeshSections.Length; ++j)
            {
                passMeshSection = passMeshSections[j];
                meshBatchIndexs[j] = passMeshSection.meshElementId;
                meshElement = meshElements[passMeshSection.meshElementId];

                if (!passMeshSection.Equals(lastPassMeshSection))
                {
                    lastPassMeshSection = passMeshSection;

                    meshDrawCommand.meshIndex = meshElement.staticMeshRef.Id;
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
            FPassMeshSection pivot = passMeshSections[(leftValue + rightValue) / 2];

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
                    FPassMeshSection temp = passMeshSections[i];
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
