using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal unsafe static class FCullingUtility
    {
        public static void DispatchCull(this ScriptableRenderContext renderContext, FGPUScene gpuScene, Camera view, ref FCullingData cullingData)
        {
            cullingData.CullState = false;
            if(gpuScene.meshBatchs.IsCreated == false) { return; }
            cullingData.CullState = true;

            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(view);
            for (int i = 0; i < 6; ++i)
            {
                cullingData.viewFrustum[i] = FrustumPlane[i];
            }

            cullingData.viewMeshBatchs = new NativeList<int>(gpuScene.meshBatchs.Length, Allocator.TempJob);

            FMeshBatchCullingJob MeshBatchCullingJob = new FMeshBatchCullingJob();
            {
                MeshBatchCullingJob.MeshBatchs = (FMeshBatch*)gpuScene.meshBatchs.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.FrustumPlanes = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.ViewMeshBatchs = cullingData.viewMeshBatchs;
            }
            MeshBatchCullingJob.Schedule(gpuScene.meshBatchs.Length, 256).Complete();
        }

        public static void DispatchCull(this ScriptableRenderContext renderContext, FGPUScene gpuScene, ref ScriptableCullingParameters cullingParameters, ref FCullingData cullingData)
        {
            cullingData.CullState = false;
            if(gpuScene.meshBatchs.IsCreated == false || cullingData.isRendererView != true) { return; }
            cullingData.CullState = true;

            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            for (int i = 0; i < 6; ++i)
            {
                cullingData.viewFrustum[i] = cullingParameters.GetCullingPlane(i);
            }

            cullingData.viewMeshBatchs = new NativeArray<int>(gpuScene.meshBatchs.Length, Allocator.TempJob);

            FMeshBatchCullingJob MeshBatchCullingJob = new FMeshBatchCullingJob();
            {
                MeshBatchCullingJob.MeshBatchs = (FMeshBatch*)gpuScene.meshBatchs.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.FrustumPlanes = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.ViewMeshBatchs = cullingData.viewMeshBatchs;
            }
            MeshBatchCullingJob.Schedule(gpuScene.meshBatchs.Length, 256).Complete();
        }
    }

    public struct FCullingData
    {
        public bool CullState;
        public bool isRendererView;
        public NativeArray<int> viewMeshBatchs;
        public NativeArray<FPlane> viewFrustum;

        public void Release()
        {
            if(CullState)
            {
                viewFrustum.Dispose();
                viewMeshBatchs.Dispose();
            }
        }
    }
}
