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
        private bool m_GatherState;
        private bool m_ScheduleState;
        private JobHandle m_Handle;
        private FGPUScene m_GPUScene;
        private MaterialPropertyBlock m_PropertyBlock;
        private NativeArray<int> m_MeshBatchIndexs;
        private NativeList<FPassMeshBatch> m_PassMeshBatchs;
        private NativeList<FMeshDrawCommand> m_MeshDrawCommands;

        public FMeshPassProcessor(FGPUScene gpuScene)
        {
            m_GPUScene = gpuScene;
            m_PropertyBlock = new MaterialPropertyBlock();
        }

        internal void DispatchSetup(ref FCullingData cullingData, in FMeshPassDesctiption meshPassDesctiption)
        {
            m_GatherState = false;
            m_ScheduleState = false;

            if (m_GPUScene.meshBatchs.IsCreated == false || cullingData.viewMeshBatchs.IsCreated == false || cullingData.isRendererView != true) { return; }

            if (cullingData.viewMeshBatchs.Length == 0) { return; }

            DispatchGatherInternal(ref m_GPUScene.meshBatchs, ref cullingData, meshPassDesctiption);

            m_GatherState = true;
        }

        internal void WaitSetupFinish()
        {
            if (m_ScheduleState == false) { return; }
            m_Handle.Complete();
        }

        internal void DispatchDraw(RDGContext graphContext, in int passIndex)
        {
            if (m_GatherState == false) { return; }

            using (new ProfilingScope(graphContext.CmdBuffer, ProfilingSampler.Get(CustomSamplerId.MeshBatch)))
            {
                BufferRef bufferRef = graphContext.ResourcePool.AllocateBuffer(new BufferDescription(10000, Marshal.SizeOf(typeof(int))));
                graphContext.CmdBuffer.SetBufferData(bufferRef.Buffer, m_MeshBatchIndexs);

                for (int i = 0; i < m_MeshDrawCommands.Length; ++i)
                {
                    FMeshDrawCommand meshDrawCommand = m_MeshDrawCommands[i];
                    Mesh mesh = graphContext.World.meshAssetList.Get(meshDrawCommand.meshIndex);
                    Material material = graphContext.World.materialAssetList.Get(meshDrawCommand.materialindex);

                    m_PropertyBlock.SetInt(InfinityShaderIDs.MeshBatchOffset, meshDrawCommand.countOffset.y);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchIndexs, bufferRef.Buffer);
                    m_PropertyBlock.SetBuffer(InfinityShaderIDs.MeshBatchBuffer, m_GPUScene.bufferRef.Buffer);
                    graphContext.CmdBuffer.DrawMeshInstancedProcedural(mesh, meshDrawCommand.submeshIndex, material, passIndex, meshDrawCommand.countOffset.x, m_PropertyBlock);
                }

                graphContext.ResourcePool.ReleaseBuffer(bufferRef);
            }

            m_MeshBatchIndexs.Dispose();
            m_PassMeshBatchs.Dispose();
            m_MeshDrawCommands.Dispose();
        }

        private void DispatchGatherInternal(ref NativeArray<FMeshBatch> meshBatchs, ref FCullingData cullingData, in FMeshPassDesctiption meshPassDesctiption)
        {
            m_ScheduleState = true;
            m_MeshBatchIndexs = new NativeArray<int>(cullingData.viewMeshBatchs.Length, Allocator.TempJob);
            m_PassMeshBatchs = new NativeList<FPassMeshBatch>(cullingData.viewMeshBatchs.Length, Allocator.TempJob);
            m_MeshDrawCommands = new NativeList<FMeshDrawCommand>(cullingData.viewMeshBatchs.Length, Allocator.TempJob);

            FMeshDrawCommandBuildJob meshDrawCommandBuildJob = new FMeshDrawCommandBuildJob();
            {
                meshDrawCommandBuildJob.Indexs = m_MeshBatchIndexs;
                meshDrawCommandBuildJob.MeshBatchs = meshBatchs;
                meshDrawCommandBuildJob.CullingData = cullingData;
                meshDrawCommandBuildJob.PassMeshBatchs = m_PassMeshBatchs;
                meshDrawCommandBuildJob.MeshDrawCommands = m_MeshDrawCommands;
                meshDrawCommandBuildJob.MeshPassDesctiption = meshPassDesctiption;
            }
            m_Handle = meshDrawCommandBuildJob.Schedule();
        }
    }
}
