using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using InfinityTech.Rendering.Pipeline;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public class FTerrainPassProcessor
    {
        internal Mesh[] Meshes;
        internal Material material;
        internal NativeList<int2> CountOffsets;
        internal NativeList<FTerrainDrawCommand> TerrainDrawCommands;


        public FTerrainPassProcessor()
        {

        }

        internal void DispatchSetup()
        {

        }

        internal void WaitSetupFinish()
        {

        }

        internal void DispatchDraw(ref RDGContext graphContext, in int passIndex)
        {
            //Draw Call
            using (new ProfilingScope(graphContext.cmdBuffer, ProfilingSampler.Get(CustomSamplerId.DrawTerrainBatcher)))
            {
                for (int i = 0; i < TerrainDrawCommands.Length; ++i)
                {
                    int2 CountOffset = CountOffsets[i];
                    FTerrainDrawCommand TerrainDrawCommand = TerrainDrawCommands[i];

                    for (int j = 0; j < CountOffset.x; ++j)
                    {
                        graphContext.cmdBuffer.DrawMeshInstancedProcedural(Meshes[TerrainDrawCommand.LOD], 0, material, passIndex, CountOffset.x);
                    }
                }
            }

            //Release TerrainPassData
            CountOffsets.Dispose();
            TerrainDrawCommands.Dispose();
        }
    }
}
