using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct FCullingData
    {
        public bool CullState;
        private NativeArray<FPlane> ViewFrustum;
        public NativeList<int> ViewMeshBatchs;

        public void DispatchCull(Camera RenderCamera, FGPUScene GPUScene)
        {
            CullState = false;
            if(GPUScene.MeshBatchs.IsCreated == false) { return; }
            CullState = true;

            ViewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(RenderCamera);
            for (int PlaneIndex = 0; PlaneIndex < 6; PlaneIndex++)
            {
                ViewFrustum[PlaneIndex] = FrustumPlane[PlaneIndex];
            }

            ViewMeshBatchs = new NativeList<int>(GPUScene.MeshBatchs.Length, Allocator.TempJob);
            ViewMeshBatchs.Resize(GPUScene.MeshBatchs.Length, NativeArrayOptions.ClearMemory);

            FMarkMeshBatchCullJob MarkCullingJob = new FMarkMeshBatchCullJob();
            {
                MarkCullingJob.ViewFrustum = ViewFrustum;
                MarkCullingJob.MeshBatchs = GPUScene.MeshBatchs;
                MarkCullingJob.ViewMeshBatchs = ViewMeshBatchs;
            }
            MarkCullingJob.Schedule(GPUScene.MeshBatchs.Length, 256).Complete();
        }

        public void Release()
        {
            if(CullState)
            {
                ViewFrustum.Dispose();
                ViewMeshBatchs.Dispose();
            }
        }
    }
}
