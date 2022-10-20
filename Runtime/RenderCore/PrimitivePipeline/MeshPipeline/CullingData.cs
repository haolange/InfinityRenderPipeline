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
        public static CullingDatas DispatchCull(this ScriptableRenderContext renderContext, GPUScene gpuScene, in bool isSceneView, Camera view)
        {
            CullingDatas cullingDatas = new CullingDatas(isSceneView);
            cullingDatas.cullState = false;

            if(gpuScene.meshElements.IsCreated == false || isSceneView != true) { return cullingDatas; }

            cullingDatas.cullState = true;
            cullingDatas.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            cullingDatas.viewMeshElements = new NativeArray<int>(gpuScene.count, Allocator.TempJob);

            Plane[] frustumPlane = GeometryUtility.CalculateFrustumPlanes(view);
            for (int i = 0; i < 6; ++i) 
            { 
                cullingDatas.viewFrustum[i] = frustumPlane[i]; 
            }

            if(gpuScene.count != 0)
            {
                MeshElementCullingJob meshElementCullingJob = new MeshElementCullingJob();
                {
                    meshElementCullingJob.viewFrustum = (FPlane*)cullingDatas.viewFrustum.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.meshElements = (MeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewMeshElements = cullingDatas.viewMeshElements;
                }
                meshElementCullingJob.Schedule(gpuScene.count, 256).Complete();
            }
            
            return cullingDatas;
        }

        public static CullingDatas DispatchCull(this ScriptableRenderContext renderContext, GPUScene gpuScene, in bool isSceneView, ref ScriptableCullingParameters cullingParameters)
        {
            CullingDatas cullingDatas = new CullingDatas(isSceneView);
            cullingDatas.cullState = false;

            if(gpuScene.meshElements.IsCreated == false || isSceneView != true) { return cullingDatas; }

            cullingDatas.cullState = true;
            cullingDatas.viewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            cullingDatas.viewMeshElements = new NativeArray<int>(gpuScene.count, Allocator.TempJob);
            
            for (int i = 0; i < 6; ++i) 
            { 
                cullingDatas.viewFrustum[i] = cullingParameters.GetCullingPlane(i); 
            }
            
            if(gpuScene.count != 0)
            {
                MeshElementCullingJob meshElementCullingJob = new MeshElementCullingJob();
                {
                    meshElementCullingJob.viewFrustum = (FPlane*)cullingDatas.viewFrustum.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.meshElements = (MeshElement*)gpuScene.meshElements.GetUnsafeReadOnlyPtr();
                    meshElementCullingJob.viewMeshElements = cullingDatas.viewMeshElements;
                }
                meshElementCullingJob.Schedule(gpuScene.count, 256).Complete();
            }

            return cullingDatas;
        }
    }

    public struct CullingDatas
    {
        public bool cullState;
        public bool isSceneView;
        public NativeArray<int> viewMeshElements;
        public NativeArray<FPlane> viewFrustum;

        public CullingDatas(in bool isSceneView)
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
