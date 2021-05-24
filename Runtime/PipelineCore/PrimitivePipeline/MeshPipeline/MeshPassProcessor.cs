using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct FMeshPassDesctiption
    {
        public int renderQueueMin;
        public int renderQueueMax;
        public int renderLayerMask;
        public bool excludeMotionVectorObjects;

        public FMeshPassDesctiption(in RendererList rendererList)
        {
            renderLayerMask = (int)rendererList.filteringSettings.renderingLayerMask;
            renderQueueMin = rendererList.filteringSettings.renderQueueRange.lowerBound;
            renderQueueMax = rendererList.filteringSettings.renderQueueRange.upperBound;
            excludeMotionVectorObjects = rendererList.filteringSettings.excludeMotionVectorObjects;
        }

        public FMeshPassDesctiption(in int minQueue, in int maxQueue)
        {
            renderLayerMask = 0;
            renderQueueMin = minQueue;
            renderQueueMax = maxQueue;
            excludeMotionVectorObjects = false;
        }
    }

    public class FMeshPassProcessor
    {
        private FGPUScene m_GPUScene;
        private NativeArray<int> m_MeshBatchIndexs;
        private MaterialPropertyBlock m_PropertyBlock;
        private NativeList<JobHandle> m_MeshPassTaskRefs;
        private NativeList<FPassMeshBatch> m_PassMeshBatchs;
        private NativeList<FMeshDrawCommand> m_MeshDrawCommands;

        public FMeshPassProcessor(FGPUScene gpuScene, ref NativeList<JobHandle> meshPassTaskRefs)
        {
            m_GPUScene = gpuScene;
            m_PropertyBlock = new MaterialPropertyBlock();
            m_MeshPassTaskRefs = meshPassTaskRefs;
        }

        internal void DispatchSetup(ref FCullingData cullingData, in FMeshPassDesctiption meshPassDesctiption)
        {
            if (m_GPUScene.meshBatchs.IsCreated == false || cullingData.viewMeshBatchs.IsCreated == false || cullingData.isRendererView != true) { return; }
            if (cullingData.viewMeshBatchs.Length == 0) { return; }

            m_MeshBatchIndexs = new NativeArray<int>(cullingData.viewMeshBatchs.Length, Allocator.TempJob);
            m_PassMeshBatchs = new NativeList<FPassMeshBatch>(cullingData.viewMeshBatchs.Length, Allocator.TempJob);
            m_MeshDrawCommands = new NativeList<FMeshDrawCommand>(cullingData.viewMeshBatchs.Length, Allocator.TempJob);

            FMeshDrawCommandBuildJob meshDrawCommandBuildJob = new FMeshDrawCommandBuildJob();
            {
                meshDrawCommandBuildJob.cullingData = cullingData;
                meshDrawCommandBuildJob.meshBatchs = m_GPUScene.meshBatchs;
                meshDrawCommandBuildJob.passMeshBatchs = m_PassMeshBatchs;
                meshDrawCommandBuildJob.meshBatchIndexs = m_MeshBatchIndexs;
                meshDrawCommandBuildJob.meshDrawCommands = m_MeshDrawCommands;
                meshDrawCommandBuildJob.meshPassDesctiption = meshPassDesctiption;
            }
            m_MeshPassTaskRefs.Add(meshDrawCommandBuildJob.Schedule());
        }

        internal void DispatchDraw(ref RDGGraphContext graphContext, in int passIndex)
        {
            if (!m_MeshBatchIndexs.IsCreated && !m_PassMeshBatchs.IsCreated && !m_MeshDrawCommands.IsCreated) { return; }

            using (new ProfilingScope(graphContext.cmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshBatch)))
            {
                BufferRef bufferRef = graphContext.resourceFactory.AllocateBuffer(new BufferDescription(10000, Marshal.SizeOf(typeof(int))));
                graphContext.cmdBuffer.SetBufferData(bufferRef.buffer, m_MeshBatchIndexs);

                for (int i = 0; i < m_MeshDrawCommands.Length; ++i)
                {
                    FMeshDrawCommand meshDrawCommand = m_MeshDrawCommands[i];
                    Mesh mesh = graphContext.world.meshAssets.Get(meshDrawCommand.meshIndex);
                    Material material = graphContext.world.materialAssets.Get(meshDrawCommand.materialIndex);

                    m_PropertyBlock.SetInt(InfinityShaderIDs.MeshBatchOffset, meshDrawCommand.countOffset.y);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchIndexs, bufferRef.buffer);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchBuffer, m_GPUScene.bufferRef.buffer);
                    graphContext.cmdBuffer.DrawMeshInstancedProcedural(mesh, meshDrawCommand.sectionIndex, material, passIndex, meshDrawCommand.countOffset.x, m_PropertyBlock);
                }

                graphContext.resourceFactory.ReleaseBuffer(bufferRef);
            }

            m_PassMeshBatchs.Dispose();
            m_MeshBatchIndexs.Dispose();
            m_MeshDrawCommands.Dispose();
        }
    }
}
