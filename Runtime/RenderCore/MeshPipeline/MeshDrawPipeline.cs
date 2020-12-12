using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Runtime.Core;
using System.Runtime.CompilerServices;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    public enum EStateType
    {
        Static = 0,
        Dynamic = 1
    }

    public enum EMotionType
    {
        Camera = 0,
        Object = 1
    }

    public enum ECastShadowMethod
    {
        Off = 0,
        Static = 1,
        Dynamic = 2
    };

    [BurstCompile]
    internal unsafe struct ConvertHashMapToArray : IJob
    {
        [WriteOnly]
        public NativeArray<FMeshBatch> StaticMeshBatchList;

        [ReadOnly]
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public void Execute()
        {
            CacheMeshBatchStateBuckets.GetValueArray(StaticMeshBatchList);
        }
    }

    [BurstCompile]
    internal struct CopyStaticMeshBatch : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<FMeshBatch> StaticMeshBatchList;

        [WriteOnly]
        public NativeArray<FMeshBatch> DynamicMeshBatchList;

        public void Execute(int index)
        {
            DynamicMeshBatchList[index] = StaticMeshBatchList[index];
        }
    }

    [BurstCompile]
    internal struct CullMeshBatch : IJobParallelFor
    {
        [ReadOnly]
        public float3 ViewOrigin;

        [ReadOnly]
        public NativeArray<FPlane> ViewFrustum;

        [ReadOnly]
        public NativeList<FMeshBatch> MeshBatchList;

        [WriteOnly]
        public NativeArray<FVisibleMeshBatch> VisibleMeshBatchList;

        public void Execute(int index)
        {
            FMeshBatch MeshBatch = MeshBatchList[index];
            
            bool Visibility = true;
            float Distance = math.distance(ViewOrigin, MeshBatch.BoundBox.center);

            for (int i = 0; i < 6; i++)
            {
                float3 normal = ViewFrustum[i].normal;
                float distance = ViewFrustum[i].distance;

                float dist = math.dot(normal, MeshBatch.BoundBox.center) + distance;
                float radius = math.dot(math.abs(normal), MeshBatch.BoundBox.extents);

                if (dist + radius < 0) {
                    Visibility = false;
                }
            }

            VisibleMeshBatchList[index] = new FVisibleMeshBatch(index, MeshBatch.Priority, MeshBatch.Visible && Visibility, Distance);
        }
    }

    [BurstCompile]
    internal struct SortMeshBatch : IJob
    {
        [WriteOnly]
        public NativeArray<FVisibleMeshBatch> VisibleMeshBatchList;

        public void Execute()
        {
            VisibleMeshBatchList.Sort();
        }
    }

    [Serializable]
    public struct FMeshBatch : IComparable<FMeshBatch>
    {
        public int SubmeshIndex;
        public SharedRef<Mesh> Mesh;
        public SharedRef<Material> Material;

        public int CastShadow;
        public int MotionType;

        public bool Visible;
        public int Priority;
        public int RenderLayer;
        public FBound BoundBox;
        //public float4x4 CustomPrimitiveData;
        public float4x4 Matrix_LocalToWorld;


        public bool Equals(in FMeshBatch Target)
        {
            return  this.SubmeshIndex.Equals(Target.SubmeshIndex) && this.Mesh.Equals(Target.Mesh) && this.Material.Equals(Target.Material);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshBatch)obj);
        }

        public int CompareTo(FMeshBatch MeshBatch)
        {
            return Priority.CompareTo(MeshBatch.Priority);
        }

        public int MatchForDynamicInstance()
        {
            int hashCode = 2;
            hashCode = hashCode + SubmeshIndex;
            hashCode = hashCode + Mesh.GetHashCode();
            hashCode = hashCode + Material.GetHashCode();

            return hashCode;
        }

        public override int GetHashCode()
        {
            int hashCode = 2;
            hashCode = hashCode + SubmeshIndex;
            hashCode = hashCode + Mesh.GetHashCode();
            hashCode = hashCode + Material.GetHashCode();
            hashCode = hashCode + CastShadow.GetHashCode();
            hashCode = hashCode + MotionType.GetHashCode();
            hashCode = hashCode + Visible.GetHashCode();
            hashCode = hashCode + Priority.GetHashCode();
            hashCode = hashCode + RenderLayer.GetHashCode();
            hashCode = hashCode + BoundBox.GetHashCode();
            hashCode = hashCode + Matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }

        public int GetHashCode(in int InstanceID)
        {
            int hashCode = InstanceID;
            hashCode = hashCode + SubmeshIndex;
            hashCode = hashCode + Mesh.GetHashCode();
            hashCode = hashCode + Material.GetHashCode();
            hashCode = hashCode + CastShadow.GetHashCode();
            hashCode = hashCode + MotionType.GetHashCode();
            hashCode = hashCode + Visible.GetHashCode();
            hashCode = hashCode + Priority.GetHashCode();
            hashCode = hashCode + RenderLayer.GetHashCode();
            hashCode = hashCode + BoundBox.GetHashCode();
            hashCode = hashCode + Matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }
    }

    [Serializable]
    public struct FVisibleMeshBatch : IComparable<FVisibleMeshBatch>
    {
        public int index;
        public int priority;
        public bool visible;
        public float distance;


        public FVisibleMeshBatch(in int Index, in int Priority, in bool Visible, in float Distance)
        {
            index = Index;
            visible = Visible;
            distance = Distance;
            priority = Priority;
        }

        public int CompareTo(FVisibleMeshBatch VisibleMeshBatch)
        {
            float Priority = priority + distance;
            return Priority.CompareTo(VisibleMeshBatch.priority + VisibleMeshBatch.distance);
        }
    }

    [Serializable]
    public class FMeshBatchCollector
    {
        public NativeList<FMeshBatch> DynamicMeshBatchList;
        public NativeHashMap<int, FMeshBatch> CacheMeshBatchStateBuckets;

        public FMeshBatchCollector() { }

        public void Initializ()
        {
            DynamicMeshBatchList = new NativeList<FMeshBatch>(1000, Allocator.Persistent);
            CacheMeshBatchStateBuckets = new NativeHashMap<int, FMeshBatch>(10000, Allocator.Persistent);
        }

        public void CopyStaticToDynamic()
        {
            if(CacheMeshBatchStateBuckets.Count() == 0) { return; }

            //Copy Cache MeshBatch StateMap to NativeArray
            NativeArray<FMeshBatch> StaticMeshBatchList = new NativeArray<FMeshBatch>(CacheMeshBatchStateBuckets.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            ConvertHashMapToArray ConvertHashMapToArrayTask = new ConvertHashMapToArray();
            {
                ConvertHashMapToArrayTask.StaticMeshBatchList = StaticMeshBatchList;
                ConvertHashMapToArrayTask.CacheMeshBatchStateBuckets = CacheMeshBatchStateBuckets;
            }
            ConvertHashMapToArrayTask.Run();

            //Resize DynamicMeshBatchSize to StaticSize
            if(DynamicMeshBatchList.Length < StaticMeshBatchList.Length) {
                DynamicMeshBatchList.Resize(StaticMeshBatchList.Length + 2000, NativeArrayOptions.ClearMemory);
            }

            //Parallel copy StaticData to DynamicData
            CopyStaticMeshBatch CopyTask = new CopyStaticMeshBatch();
            {
                CopyTask.StaticMeshBatchList = StaticMeshBatchList;
                CopyTask.DynamicMeshBatchList = DynamicMeshBatchList.AsDeferredJobArray();
            }
            CopyTask.Schedule(StaticMeshBatchList.Length, 256).Complete();
            StaticMeshBatchList.Dispose();
        }

        public bool StaticListAvalible()
        {
            return CacheMeshBatchStateBuckets.IsCreated;
        }

        public void AddStaticMeshBatch(in FMeshBatch MeshBatch, in int AddKey)
        {
            CacheMeshBatchStateBuckets.Add(AddKey, MeshBatch);
        }

        public void UpdateStaticMeshBatch(in FMeshBatch MeshBatch, in int UpdateKey)
        {
            CacheMeshBatchStateBuckets[UpdateKey] = MeshBatch;
        }

        public void RemoveStaticMeshBatch(in int RemoveKey)
        {
            CacheMeshBatchStateBuckets.Remove(RemoveKey);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetDynamicCollector()
        {
            DynamicMeshBatchList.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDynamicMeshBatch(in FMeshBatch MeshBatch)
        {
            DynamicMeshBatchList.Add(MeshBatch);
        }

        public NativeList<FMeshBatch> GetMeshBatchList()
        {
            return DynamicMeshBatchList;
        }

        public void Reset()
        {
            DynamicMeshBatchList.Clear();
            CacheMeshBatchStateBuckets.Clear();
        }

        public void Release()
        {
            DynamicMeshBatchList.Clear();
            DynamicMeshBatchList.Dispose();
            CacheMeshBatchStateBuckets.Clear();
            CacheMeshBatchStateBuckets.Dispose();
        }
    }
}
