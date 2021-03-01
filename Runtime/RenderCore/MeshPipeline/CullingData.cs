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
        public static void DispatchCull(this ScriptableRenderContext RenderContext, FGPUScene GPUScene, Camera RenderCamera, ref FCullingData CullingData)
        {
            CullingData.CullState = false;
            if(GPUScene.MeshBatchs.IsCreated == false) { return; }
            CullingData.CullState = true;

            CullingData.ViewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(RenderCamera);
            for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
            {
                CullingData.ViewFrustum[PlaneIndex] = FrustumPlane[PlaneIndex];
            }

            CullingData.ViewMeshBatchs = new NativeList<int>(GPUScene.MeshBatchs.Length, Allocator.TempJob);
            CullingData.ViewMeshBatchs.Resize(GPUScene.MeshBatchs.Length, NativeArrayOptions.ClearMemory);

            FMeshBatchCullingJob MeshBatchCullingJob = new FMeshBatchCullingJob();
            {
                MeshBatchCullingJob.MeshBatchs = (FMeshBatch*)GPUScene.MeshBatchs.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.FrustumPlanes = (FPlane*)CullingData.ViewFrustum.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.ViewMeshBatchs = CullingData.ViewMeshBatchs;
            }
            MeshBatchCullingJob.Schedule(GPUScene.MeshBatchs.Length, 256).Complete();
        }

        public static void DispatchCull(this ScriptableRenderContext RenderContext, FGPUScene GPUScene, ref ScriptableCullingParameters CullingParameters, ref FCullingData CullingData)
        {
            CullingData.CullState = false;
            if(GPUScene.MeshBatchs.IsCreated == false || CullingData.bRendererView != true) { return; }
            CullingData.CullState = true;

            CullingData.ViewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
            {
                CullingData.ViewFrustum[PlaneIndex] = CullingParameters.GetCullingPlane(PlaneIndex);
            }

            CullingData.ViewMeshBatchs = new NativeList<int>(GPUScene.MeshBatchs.Length, Allocator.TempJob);
            CullingData.ViewMeshBatchs.Resize(GPUScene.MeshBatchs.Length, NativeArrayOptions.ClearMemory);

            FMeshBatchCullingJob MeshBatchCullingJob = new FMeshBatchCullingJob();
            {
                MeshBatchCullingJob.MeshBatchs = (FMeshBatch*)GPUScene.MeshBatchs.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.FrustumPlanes = (FPlane*)CullingData.ViewFrustum.GetUnsafeReadOnlyPtr();
                MeshBatchCullingJob.ViewMeshBatchs = CullingData.ViewMeshBatchs;
            }
            MeshBatchCullingJob.Schedule(GPUScene.MeshBatchs.Length, 256).Complete();
        }
    }

    public struct FCullingData
    {
        public bool CullState;
        public bool bRendererView;
        public NativeList<int> ViewMeshBatchs;
        public NativeArray<FPlane> ViewFrustum;

        /*public void DispatchCull(FGPUScene GPUScene, Camera RenderCamera)
        {
            CullState = false;
            if(GPUScene.MeshBatchs.IsCreated == false) { return; }
            CullState = true;

            ViewFrustum = new NativeArray<FPlane>(6, Allocator.TempJob);
            Plane[] FrustumPlane = GeometryUtility.CalculateFrustumPlanes(RenderCamera);
            for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
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
        }*/

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
