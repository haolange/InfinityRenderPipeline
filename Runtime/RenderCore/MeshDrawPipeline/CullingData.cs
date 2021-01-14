using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshDrawPipeline
{
    public enum ECullingMethod
    {
        VisibleMark,
        FillterList
    }

    public struct FCullingData
    {
        private JobHandle CullingJobRef;
        private NativeArray<FPlane> ViewFrustum;
        public NativeList<int> ViewMeshBatchs;
        public ECullingMethod CullMethod;

        public void Run(Camera RenderCamera, NativeArray<FMeshBatch> MeshBatchs, in ECullingMethod CullingMethod = ECullingMethod.FillterList, in bool bParallel = true)
        {
            ViewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(RenderCamera);
            for (int PlaneIndex = 0; PlaneIndex < 6; PlaneIndex++)
            {
                ViewFrustum[PlaneIndex] = FrustumPlane[PlaneIndex];
            }

            CullMethod = CullingMethod;

            switch (CullingMethod)
            {
                case ECullingMethod.VisibleMark:
                    ViewMeshBatchs = new NativeList<int>(MeshBatchs.Length, Allocator.TempJob);
                    ViewMeshBatchs.Resize(MeshBatchs.Length, NativeArrayOptions.ClearMemory);

                    FCullMeshBatchForMarkJob MarkCullingJob = new FCullMeshBatchForMarkJob();
                    {
                        MarkCullingJob.ViewFrustum = ViewFrustum;
                        MarkCullingJob.MeshBatchs = MeshBatchs;
                        MarkCullingJob.ViewMeshBatchs = ViewMeshBatchs;
                    }
                    CullingJobRef = MarkCullingJob.Schedule(MeshBatchs.Length, 256);
                    break;

                case ECullingMethod.FillterList:
                    ViewMeshBatchs = new NativeList<int>(MeshBatchs.Length, Allocator.TempJob);

                    FCullMeshBatchForFilterJob FilterCullingJob = new FCullMeshBatchForFilterJob();
                    {
                        FilterCullingJob.ViewFrustum = ViewFrustum;
                        FilterCullingJob.MeshBatchs = MeshBatchs;
                    }
                    CullingJobRef = FilterCullingJob.ScheduleAppend(ViewMeshBatchs, MeshBatchs.Length, 256);
                    break;
            }

            if (bParallel) { JobHandle.ScheduleBatchedJobs(); }
        }

        public void Sync()
        {
            CullingJobRef.Complete();
        }

        public void Release()
        {
            ViewFrustum.Dispose();
            ViewMeshBatchs.Dispose();
        }
    }
}
