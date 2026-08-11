using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    public struct MeshViewCullingResult
    {
        public NativeArray<byte> instanceVisibility;
        public NativeArray<FPlane> frustum;
        public bool isValid;

        public void Release()
        {
            if (!isValid)
            {
                return;
            }

            if (instanceVisibility.IsCreated) instanceVisibility.Dispose();
            if (frustum.IsCreated) frustum.Dispose();
            isValid = false;
        }
    }

    public static class MeshVisibilityUtility
    {
        public static MeshViewCullingResult CullInstances(MeshScene scene, Camera camera, bool enable)
        {
            if (!enable || scene == null || camera == null)
            {
                return default;
            }

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return CullInstances(scene, planes, enable);
        }

        public static MeshViewCullingResult CullInstances(MeshScene scene, Plane[] planes, bool enable)
        {
            if (!enable || scene == null || planes == null || planes.Length < 6)
            {
                MeshPipelineDiagnostics.CulledPassSkippedBuilds++;
                return default;
            }

            int instanceCount = scene.InstanceHighWater;
            var result = new MeshViewCullingResult
            {
                isValid = true,
                frustum = new NativeArray<FPlane>(6, Allocator.TempJob),
                instanceVisibility = new NativeArray<byte>(math.max(1, instanceCount), Allocator.TempJob)
            };
            MeshPipelineDiagnostics.TempAllocCount += 2;

            for (int i = 0; i < 6; ++i)
            {
                result.frustum[i] = planes[i];
            }

            if (instanceCount > 0)
            {
                unsafe
                {
                    var job = new MeshInstanceCullingJob
                    {
                        viewFrustum = (FPlane*)result.frustum.GetUnsafeReadOnlyPtr(),
                        instances = scene.GetInstances(),
                        generations = scene.GetInstanceGenerations(),
                        instanceVisibility = result.instanceVisibility
                    };
                    job.Schedule(instanceCount, 256).Complete();
                }
            }

            return result;
        }

        public static MeshViewCullingResult CullInstances(MeshScene scene, ref ScriptableCullingParameters cullingParameters, bool enable)
        {
            if (!enable || scene == null)
            {
                MeshPipelineDiagnostics.CulledPassSkippedBuilds++;
                return default;
            }

            var planes = new Plane[6];
            for (int i = 0; i < 6; ++i)
            {
                planes[i] = cullingParameters.GetCullingPlane(i);
            }

            return CullInstances(scene, planes, enable);
        }
    }

    internal static class MeshVisibilityDispatch
    {
        public static MeshViewCullingResult DispatchCull(this ScriptableRenderContext renderContext, MeshScene scene, in bool enable, Camera view)
        {
            return MeshVisibilityUtility.CullInstances(scene, view, enable);
        }

        public static MeshViewCullingResult DispatchCull(this ScriptableRenderContext renderContext, MeshScene scene, in bool enable, ref ScriptableCullingParameters cullingParameters)
        {
            return MeshVisibilityUtility.CullInstances(scene, ref cullingParameters, enable);
        }
    }

    [BurstCompile]
    public unsafe struct MeshInstanceCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* viewFrustum;

        [ReadOnly]
        public NativeArray<MeshInstanceRecord> instances;

        [ReadOnly]
        public NativeArray<uint> generations;

        [WriteOnly]
        public NativeArray<byte> instanceVisibility;

        public void Execute(int index)
        {
            byte visible = 0;
            if (generations[index] != 0)
            {
                MeshInstanceRecord instance = instances[index];
                bool flagVisible = (instance.flags & EMeshInstanceFlags.Visible) != 0;
                if (flagVisible)
                {
                    int inside = 1;
                    FBound bound = instance.worldBounds;
                    for (int i = 0; i < 6; ++i)
                    {
                        ref FPlane plane = ref viewFrustum[i];
                        float2 distRadius;
                        distRadius.x = math.dot(math.abs(plane.normalDist.xyz), bound.extents);
                        distRadius.y = math.dot(plane.normalDist.xyz, bound.center) + plane.normalDist.w;
                        inside = math.select(inside, 0, distRadius.x + distRadius.y < 0);
                    }

                    visible = (byte)inside;
                }
            }

            instanceVisibility[index] = visible;
        }
    }
}
