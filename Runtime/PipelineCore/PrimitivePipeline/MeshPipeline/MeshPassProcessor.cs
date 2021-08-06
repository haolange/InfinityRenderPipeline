using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RDG;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct FMeshPassDesctiption
    {
        public int renderQueueMin;
        public int renderQueueMax;
        public int renderLayerMask;
        public bool excludeMotionVectorObjects;

        public FMeshPassDesctiption(in RendererListDesc rendererListDesc)
        {
            renderLayerMask = (int)rendererListDesc.layerMask;
            renderQueueMin = rendererListDesc.renderQueueRange.lowerBound;
            renderQueueMax = rendererListDesc.renderQueueRange.upperBound;
            excludeMotionVectorObjects = rendererListDesc.excludeObjectMotionVectors;
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
        private NativeList<FPassMeshSection> m_PassMeshSections;
        private NativeList<FMeshDrawCommand> m_MeshDrawCommands;

        public FMeshPassProcessor(FGPUScene gpuScene, ref NativeList<JobHandle> meshPassTaskRefs)
        {
            m_GPUScene = gpuScene;
            m_PropertyBlock = new MaterialPropertyBlock();
            m_MeshPassTaskRefs = meshPassTaskRefs;
        }

        internal void DispatchSetup(in FCullingData cullingData, in FMeshPassDesctiption meshPassDesctiption)
        {
            if (m_GPUScene.meshElements.IsCreated == false || cullingData.viewMeshElements.IsCreated == false || cullingData.isSceneView != true) { return; }
            if (cullingData.viewMeshElements.Length == 0) { return; }

            m_MeshBatchIndexs = new NativeArray<int>(cullingData.viewMeshElements.Length, Allocator.TempJob);
            m_PassMeshSections = new NativeList<FPassMeshSection>(cullingData.viewMeshElements.Length, Allocator.TempJob);
            m_MeshDrawCommands = new NativeList<FMeshDrawCommand>(cullingData.viewMeshElements.Length, Allocator.TempJob);

            FMeshDrawCommandBuildJob meshDrawCommandBuildJob;
            meshDrawCommandBuildJob.cullingData = cullingData;
            meshDrawCommandBuildJob.meshElements = m_GPUScene.meshElements;
            meshDrawCommandBuildJob.meshBatchIndexs = m_MeshBatchIndexs;
            meshDrawCommandBuildJob.meshDrawCommands = m_MeshDrawCommands;
            meshDrawCommandBuildJob.passMeshSections = m_PassMeshSections;
            meshDrawCommandBuildJob.meshPassDesctiption = meshPassDesctiption;
            m_MeshPassTaskRefs.Add(meshDrawCommandBuildJob.Schedule());
        }

        internal void DispatchDraw(in RDGGraphContext graphContext, in int passIndex)
        {
            if (!m_MeshBatchIndexs.IsCreated && !m_PassMeshSections.IsCreated && !m_MeshDrawCommands.IsCreated) { return; }

            using (new ProfilingScope(graphContext.cmdBuffer, ProfilingSampler.Get(CustomSamplerId.DrawMeshBatcher)))
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

            m_MeshBatchIndexs.Dispose();
            m_PassMeshSections.Dispose();
            m_MeshDrawCommands.Dispose();
        }
    }
}
