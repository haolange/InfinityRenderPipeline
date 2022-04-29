using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace InfinityTech.Rendering.MeshPipeline
{
    internal unsafe static class CullingUtility
    {
        public static FCullingData DispatchCull(this ScriptableRenderContext renderContext, GPUScene gpuScene, in bool isSceneView, Camera view)
        {
            FCullingData cullingData = new FCullingData(isSceneView);
            cullingData.cullState = false;

            if(gpuScene.meshElements.IsCreated == false || isSceneView != true) { return cullingData; }

            cullingData.cullState = true;
            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            cullingData.viewMeshElements = new NativeArray<int>(gpuScene.count, Allocator.TempJob);

            Plane[] frustumPlane = GeometryUtility.CalculateFrustumPlanes(view);
            for (int i = 0; i < 6; ++i) 
            { 
                cullingData.viewFrustum[i] = frustumPlane[i]; 
            }

            if(gpuScene.count != 0)
            {
                MeshElementCullingJob meshElementCullingJob = new MeshElementCullingJob();
                {
                    meshElementCullingJob.viewFrustum = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.meshElements = (MeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewMeshElements = cullingData.viewMeshElements;
                }
                meshElementCullingJob.Schedule(gpuScene.count, 256).Complete();
            }
            
            return cullingData;
        }

        public static FCullingData DispatchCull(this ScriptableRenderContext renderContext, GPUScene gpuScene, in bool isSceneView, ref ScriptableCullingParameters cullingParameters)
        {
            FCullingData cullingData = new FCullingData(isSceneView);
            cullingData.cullState = false;

            if(gpuScene.meshElements.IsCreated == false || isSceneView != true) { return cullingData; }

            cullingData.cullState = true;
            cullingData.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            cullingData.viewMeshElements = new NativeArray<int>(gpuScene.count, Allocator.TempJob);
            
            for (int i = 0; i < 6; ++i) 
            { 
                cullingData.viewFrustum[i] = cullingParameters.GetCullingPlane(i); 
            }
            
            if(gpuScene.count != 0)
            {
                MeshElementCullingJob meshElementCullingJob = new MeshElementCullingJob();
                {
                    meshElementCullingJob.viewFrustum = (FPlane*)cullingData.viewFrustum.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.meshElements = (MeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewMeshElements = cullingData.viewMeshElements;
                }
                meshElementCullingJob.Schedule(gpuScene.count, 256).Complete();
            }

            return cullingData;
        }
    }

    public struct FCullingData
    {
        public bool cullState;
        public bool isSceneView;
        public NativeArray<int> viewMeshElements;
        public NativeArray<FPlane> viewFrustum;

        public FCullingData(in bool isSceneView)
        {
            this.cullState = false;
            this.viewFrustum = default;
            this.isSceneView = isSceneView;
            this.viewMeshElements = default;
        }

        public void Release()
        {
            if(cullState)
            {
                viewFrustum.Dispose();
                viewMeshElements.Dispose();
            }
        }
    }
}
