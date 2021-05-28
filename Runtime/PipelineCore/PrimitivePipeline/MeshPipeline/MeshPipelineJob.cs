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
        public NativeArray<int> viewMeshBatchs;


        public void Execute(int index)
        {
            int VisibleState = 1;
            float2 distRadius = new float2(0, 0);
            ref FMeshElement MeshBatch = ref meshElements[index];

            for (int i = 0; i < 6; ++i)
            {
                ref FPlane plane = ref viewFrustum[i];
                distRadius.x = math.dot(plane.normalDist.xyz, MeshBatch.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), MeshBatch.boundBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }

            viewMeshBatchs[index] = math.select(0, VisibleState, MeshBatch.visible == 1);
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

        public NativeList<FPassMeshSection> m_PassMeshSections;

        public NativeList<FMeshDrawCommand> meshDrawCommands;


        public void Execute()
        {
            FMeshElement meshElement;

            //Gather PassMeshBatch
            for (int i = 0; i < cullingData.viewMeshBatchs.Length; ++i)
            {
                if (cullingData.viewMeshBatchs[i] != 0)
                {
                    meshElement = meshElements[i];

                    if(meshElement.priority >= meshPassDesctiption.renderQueueMin && meshElement.priority <= meshPassDesctiption.renderQueueMax)
                    {
                        FPassMeshSection PassMeshBatch = new FPassMeshSection(i, FMeshElement.MatchForDynamicInstance(ref meshElement));
                        m_PassMeshSections.Add(PassMeshBatch);
                    }
                }
            }

            //Sort PassMeshBatch
            m_PassMeshSections.Sort();

            //Build MeshDrawCommand
            FPassMeshSection passMeshSection;
            FPassMeshSection lastPassMeshSection = new FPassMeshSection(-1, -1);

            FMeshDrawCommand meshDrawCommand;
            FMeshDrawCommand cacheMeshDrawCommand;

            for (int j = 0; j < m_PassMeshSections.Length; ++j)
            {
                passMeshSection = m_PassMeshSections[j];
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
    public struct FHashmapGatherValueJob<TKey, TValue> : IJob where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TValue> dscArray;

        [ReadOnly]
        public NativeHashMap<TKey, TValue> srcMap;

        public void Execute()
        {
            srcMap.GetValueArray(dscArray);
        }
    }

    [BurstCompile]
    public unsafe struct FHashmapParallelGatherValueJob<TKey, TValue> : IJobParallelFor where TKey : struct, IEquatable<TKey> where TValue : struct
    {
        [WriteOnly]
        public NativeArray<TValue> dscArray;

        [ReadOnly]
        public NativeHashMap<TKey, TValue> srcMap;

        public void Execute(int index)
        {
            dscArray[index] = UnsafeUtility.ReadArrayElement<TValue>(srcMap.m_HashMapData.m_Buffer->values, index);
        }
    }
}
