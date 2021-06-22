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
        public static FCullingData DispatchCull(this ScriptableRenderContext renderContext, FGPUScene gpuScene, in bool isSceneView, Camera view)
        {
            FCullingData cullingData = new FCullingData(isSceneView);
            cullingData.cullState = false;

            if(gpuScene.meshElements.IsCreated == false || isSceneView != true) { return cullingData; }

            cullingData.cullState = true;
            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            cullingData.viewMeshBatchs = new NativeList<int>(gpuScene.meshElements.Length, Allocator.TempJob);

            Plane[] frustumPlane = GeometryUtility.CalculateFrustumPlanes(view);
            for (int i = 0; i < 6; ++i) 
            { 
                cullingData.viewFrustum[i] = frustumPlane[i]; 
            }

            if(gpuScene.meshElements.Length != 0)
            {
                FMeshElementCullingJob meshElementCullingJob = new FMeshElementCullingJob();
                {
                    meshElementCullingJob.meshElements = (FMeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewFrustum = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewMeshBatchs = cullingData.viewMeshBatchs;
                }
                meshElementCullingJob.Schedule(gpuScene.meshElements.Length, 256).Complete();
            }
            
            return cullingData;
        }

        public static FCullingData DispatchCull(this ScriptableRenderContext renderContext, FGPUScene gpuScene, in bool isSceneView, ref ScriptableCullingParameters cullingParameters)
        {
            FCullingData cullingData = new FCullingData(isSceneView);
            cullingData.cullState = false;

            if(gpuScene.meshElements.IsCreated == false || isSceneView != true) { return cullingData; }

            cullingData.cullState = true;
            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            cullingData.viewMeshBatchs = new NativeArray<int>(gpuScene.meshElements.Length, Allocator.TempJob);
            
            for (int i = 0; i < 6; ++i) 
            { 
                cullingData.viewFrustum[i] = cullingParameters.GetCullingPlane(i); 
            }
            
            if(gpuScene.meshElements.Length != 0)
            {
                FMeshElementCullingJob meshElementCullingJob = new FMeshElementCullingJob();
                {
                    meshElementCullingJob.meshElements = (FMeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewFrustum = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewMeshBatchs = cullingData.viewMeshBatchs;
                }
                meshElementCullingJob.Schedule(gpuScene.meshElements.Length, 256).Complete();
            }

            return cullingData;
        }
    }

    public struct FCullingData
    {
        public bool cullState;
        public bool isSceneView;
        public NativeArray<int> viewMeshBatchs;
        public NativeArray<FPlane> viewFrustum;

        public FCullingData(in bool isSceneView)
        {
            this.cullState = false;
            this.viewFrustum = default;
            this.viewMeshBatchs = default;
            this.isSceneView = isSceneView;
        }

        public void Release()
        {
            if(cullState)
            {
                viewFrustum.Dispose();
                viewMeshBatchs.Dispose();
            }
        }
    }
}
