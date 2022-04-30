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
    public class TerrainPassProcessor
    {
        internal Mesh[] meshes;
        internal Material material;
        internal NativeList<int2> countOffsets;
        private ProfilingSampler m_DrawProfiler;
        internal NativeList<TerrainDrawCommand> terrainDrawCommands;

        public TerrainPassProcessor()
        {
            m_DrawProfiler = new ProfilingSampler("RenderLoop.DrawTerrainBatcher");
        }

        internal void DispatchSetup()
        {

        }

        internal void WaitSetupFinish()
        {

        }

        internal void DispatchDraw(ref RDGContext graphContext, in int passIndex)
        {
            using (new ProfilingScope(graphContext.cmdBuffer, m_DrawProfiler))
            {
                for (int i = 0; i < terrainDrawCommands.Length; ++i)
                {
                    int2 countOffset = countOffsets[i];
                    TerrainDrawCommand terrainDrawCommand = terrainDrawCommands[i];

                    for (int j = 0; j < countOffset.x; ++j)
                    {
                        graphContext.cmdBuffer.DrawMeshInstancedProcedural(meshes[terrainDrawCommand.lod], 0, material, passIndex, countOffset.x);
                    }
                }
            }

            countOffsets.Dispose();
            terrainDrawCommands.Dispose();
        }
    }
}
