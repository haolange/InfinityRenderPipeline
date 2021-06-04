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
        public static FCullingData DispatchCull(this ScriptableRenderContext renderContext, FGPUScene gpuScene, in bool isRendererView, Camera view)
        {
            FCullingData cullingData = new FCullingData(isRendererView);
            cullingData.CullState = false;

            if(gpuScene.meshElements.IsCreated == false || isRendererView != true) { return cullingData; }

            cullingData.CullState = true;
            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(view);
            for (int i = 0; i < 6; ++i)
            {
                cullingData.viewFrustum[i] = FrustumPlane[i];
            }

            cullingData.viewMeshBatchs = new NativeList<int>(gpuScene.meshElements.Length, Allocator.TempJob);

            FMeshElementCullingJob meshElementCullingJob = new FMeshElementCullingJob();
            {
                meshElementCullingJob.meshElements = (FMeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                meshElementCullingJob.viewFrustum = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                meshElementCullingJob.viewMeshBatchs = cullingData.viewMeshBatchs;
            }
            meshElementCullingJob.Schedule(gpuScene.meshElements.Length, 256).Complete();
            return cullingData;
        }

        public static FCullingData DispatchCull(this ScriptableRenderContext renderContext, FGPUScene gpuScene, in bool isRendererView, ref ScriptableCullingParameters cullingParameters)
        {
            FCullingData cullingData = new FCullingData(isRendererView);
            cullingData.CullState = false;

            if(gpuScene.meshElements.IsCreated == false || isRendererView != true) { return cullingData; }

            cullingData.CullState = true;
            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            for (int i = 0; i < 6; ++i)
            {
                cullingData.viewFrustum[i] = cullingParameters.GetCullingPlane(i);
            }

            cullingData.viewMeshBatchs = new NativeArray<int>(gpuScene.meshElements.Length, Allocator.TempJob);

            FMeshElementCullingJob meshElementCullingJob = new FMeshElementCullingJob();
            {
                meshElementCullingJob.meshElements = (FMeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                meshElementCullingJob.viewFrustum = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                meshElementCullingJob.viewMeshBatchs = cullingData.viewMeshBatchs;
            }
            meshElementCullingJob.Schedule(gpuScene.meshElements.Length, 256).Complete();
            return cullingData;
        }
    }

    public struct FCullingData
    {
        public bool CullState;
        public bool isRendererView;
        public NativeArray<int> viewMeshBatchs;
        public NativeArray<FPlane> viewFrustum;

        public FCullingData(in bool isRendererView)
        {
            this.CullState = false;
            this.viewFrustum = default;
            this.viewMeshBatchs = default;
            this.isRendererView = isRendererView;
        }

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
