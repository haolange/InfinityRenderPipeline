using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    public struct FCullingData
    {
        private JobHandle JobRef;
        private NativeArray<FPlane> ViewFrustum;
        public NativeList<int> ViewMeshBatchList;

        public FCullingData(Camera RenderCamera, NativeArray<FMeshBatch> MeshBatchArray)
        {
            ViewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            ViewMeshBatchList = new NativeList<int>(MeshBatchArray.Length, Allocator.TempJob);

            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(RenderCamera);
            for (int PlaneIndex = 0; PlaneIndex < 6; PlaneIndex++)
            {
                ViewFrustum[PlaneIndex] = FrustumPlane[PlaneIndex];
            }

            FCullMeshBatchJob CullTask = new FCullMeshBatchJob();
            {
                CullTask.ViewFrustum = ViewFrustum;
                CullTask.MeshBatchArray = MeshBatchArray;
            }
            JobRef = CullTask.ScheduleAppend(ViewMeshBatchList, MeshBatchArray.Length, 256);
        }

        public void Sync()
        {
            JobRef.Complete();
        }

        public void Release()
        {
            ViewFrustum.Dispose();
            ViewMeshBatchList.Dispose();
        }
    }
}
