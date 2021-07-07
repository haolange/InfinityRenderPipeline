using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
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
    public struct FMeshDrawCommandBuildJob : IJob
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
    }
}
