using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Native;
using InfinityTech.Rendering.RDG;
using System.Runtime.InteropServices;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct MeshPassDescriptor
    {
        public int renderQueueMin;
        public int renderQueueMax;
        public int renderLayerMask;
        public bool excludeMotionVectorObjects;

        public MeshPassDescriptor(in RendererListDesc rendererListDesc)
        {
            renderLayerMask = (int)rendererListDesc.layerMask;
            renderQueueMin = rendererListDesc.renderQueueRange.lowerBound;
            renderQueueMax = rendererListDesc.renderQueueRange.upperBound;
            excludeMotionVectorObjects = rendererListDesc.excludeObjectMotionVectors;
        }

        public MeshPassDescriptor(in int minQueue, in int maxQueue)
        {
            renderLayerMask = 0;
            renderQueueMin = minQueue;
            renderQueueMax = maxQueue;
            excludeMotionVectorObjects = false;
        }
    }

    public class MeshPassProcessor
    {
        private GPUScene m_GPUScene;
        private ProfilingSampler m_DrawProfiler;
        private NativeArray<int> m_MeshBatchIndexs;
        private MaterialPropertyBlock m_PropertyBlock;
        private NativeList<JobHandle> m_MeshPassTaskRefs;
        private NativeList<PassMeshSection> m_PassMeshSections;
        private NativeList<MeshDrawCommand> m_MeshDrawCommands;

        public MeshPassProcessor(GPUScene gpuScene, ref NativeList<JobHandle> meshPassTaskRefs)
        {
            m_GPUScene = gpuScene;
            m_DrawProfiler = new ProfilingSampler("RenderLoop.DrawMeshBatcher");
            m_PropertyBlock = new MaterialPropertyBlock();
            m_MeshPassTaskRefs = meshPassTaskRefs;
        }

        internal void DispatchSetup(in CullingDatas cullingDatas, in MeshPassDescriptor meshPassDescriptor)
        {
            if (m_GPUScene.meshElements.IsCreated == false || cullingDatas.viewMeshElements.IsCreated == false || cullingDatas.isSceneView != true) { return; }
            if (cullingDatas.viewMeshElements.Length == 0) { return; }

            m_MeshBatchIndexs = new NativeArray<int>(cullingDatas.viewMeshElements.Length, Allocator.TempJob);
            m_PassMeshSections = new NativeList<PassMeshSection>(cullingDatas.viewMeshElements.Length, Allocator.TempJob);
            m_MeshDrawCommands = new NativeList<MeshDrawCommand>(cullingDatas.viewMeshElements.Length, Allocator.TempJob);

            MeshPassFilterJob meshPassFilterJob;
            meshPassFilterJob.cullingDatas = cullingDatas;
            meshPassFilterJob.meshElements = m_GPUScene.meshElements;
            meshPassFilterJob.passMeshSections = m_PassMeshSections;
            meshPassFilterJob.meshPassDescriptor = meshPassDescriptor;
            JobHandle filterHandle = meshPassFilterJob.Schedule();

            MeshPassSortJob meshPassSortJob;
            meshPassSortJob.passMeshSections = m_PassMeshSections;
            JobHandle sortHandle = meshPassSortJob.Schedule(filterHandle);

            MeshPassBuildJob meshPassBuildJob;
            meshPassBuildJob.meshElements = m_GPUScene.meshElements;
            meshPassBuildJob.meshBatchIndexs = m_MeshBatchIndexs;
            meshPassBuildJob.meshDrawCommands = m_MeshDrawCommands;
            meshPassBuildJob.passMeshSections = m_PassMeshSections;
            m_MeshPassTaskRefs.Add(meshPassBuildJob.Schedule(sortHandle));
        }

        internal void DispatchDraw(in RDGContext graphContext, in int passIndex)
        {
            if (!m_MeshBatchIndexs.IsCreated && !m_PassMeshSections.IsCreated && !m_MeshDrawCommands.IsCreated) { return; }

            using (new ProfilingScope(graphContext.cmdBuffer, m_DrawProfiler))
            {
                FBufferRef bufferRef = graphContext.resourcePool.GetBuffer(new BufferDescriptor(10000, Marshal.SizeOf(typeof(int))));
                graphContext.cmdBuffer.SetBufferData(bufferRef.buffer, m_MeshBatchIndexs, 0, 0, m_MeshBatchIndexs.Length);

                for (int i = 0; i < m_MeshDrawCommands.Length; ++i)
                {
                    MeshDrawCommand meshDrawCommand = m_MeshDrawCommands[i];
                    Mesh mesh = (Mesh)Resources.InstanceIDToObject(meshDrawCommand.meshIndex);
                    Material material = (Material)Resources.InstanceIDToObject(meshDrawCommand.materialIndex);

                    m_PropertyBlock.Clear();
                    m_PropertyBlock.SetInt(InfinityShaderIDs.MeshBatchOffset, meshDrawCommand.countOffset.y);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchIndexs, bufferRef.buffer);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchBuffer, m_GPUScene.bufferRef.buffer);
                    graphContext.cmdBuffer.DrawMeshInstancedProcedural(mesh, meshDrawCommand.sectionIndex, material, passIndex, meshDrawCommand.countOffset.x, m_PropertyBlock);
                }

                graphContext.resourcePool.ReleaseBuffer(bufferRef);
            }

            m_MeshBatchIndexs.Dispose();
            m_PassMeshSections.Dispose();
            m_MeshDrawCommands.Dispose();
        }
    }
}
