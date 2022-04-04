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
        internal NativeList<int2> countOffsets;
        private ProfilingSampler m_DrawProfiler;
        internal NativeList<FTerrainDrawCommand> terrainDrawCommands;

        public FTerrainPassProcessor()
        {
            m_DrawProfiler = new ProfilingSampler("RenderLoop.DrawTerrainBatcher");
        }

        internal void DispatchSetup()
        {

        }

        internal void WaitSetupFinish()
        {

        }

        internal void DispatchDraw(ref FRDGContext graphContext, in int passIndex)
        {
            using (new ProfilingScope(graphContext.cmdBuffer, m_DrawProfiler))
            {
                for (int i = 0; i < terrainDrawCommands.Length; ++i)
                {
                    int2 countOffset = countOffsets[i];
                    FTerrainDrawCommand terrainDrawCommand = terrainDrawCommands[i];

                    for (int j = 0; j < countOffset.x; ++j)
                    {
                        graphContext.cmdBuffer.DrawMeshInstancedProcedural(Meshes[terrainDrawCommand.LOD], 0, material, passIndex, countOffset.x);
                    }
                }
            }

            countOffsets.Dispose();
            terrainDrawCommands.Dispose();
        }
    }
}
