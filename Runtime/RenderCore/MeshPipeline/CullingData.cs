using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct FCullingData
    {
        public bool CullState;
        public NativeList<int> ViewMeshBatchs;
        private NativeArray<FPlane> ViewFrustum;

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

            FMeshBatchCullingJob MeshBatchCullingJob = new FMeshBatchCullingJob();
            {
                MeshBatchCullingJob.ViewFrustum = ViewFrustum;
                MeshBatchCullingJob.MeshBatchs = GPUScene.MeshBatchs;
                MeshBatchCullingJob.ViewMeshBatchs = ViewMeshBatchs;
            }
            MeshBatchCullingJob.Schedule(GPUScene.MeshBatchs.Length, 256).Complete();
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
