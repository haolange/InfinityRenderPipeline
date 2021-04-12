using System;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;

namespace InfinityTech.Rendering.MeshPipeline
{
    [Serializable]
    public enum EStateType
    {
        Static = 0,
        Dynamic = 1
    }

    [Serializable]
    public enum EMotionType
    {
        Camera = 0,
        Object = 1
    }

    [Serializable]
    public enum ECastShadowMethod
    {
        Off = 0,
        Static = 1,
        Dynamic = 2
    };

    public struct FMeshBatch : IComparable<FMeshBatch>, IEquatable<FMeshBatch>
    {
        public int submeshIndex;
        public SharedRef<Mesh> staticMeshRef;
        public SharedRef<Material> materialRef;
        public int visible;
        public int Priority;
        public int castShadow;
        public int motionType;
        public int renderLayer;
        public FBound boundBox;
        //public float4x4 CustomPrimitiveData;
        public float4x4 matrix_LocalToWorld;


        public bool Equals(FMeshBatch Target)
        {
            return submeshIndex.Equals(Target.submeshIndex) && staticMeshRef.Equals(Target.staticMeshRef) && materialRef.Equals(Target.materialRef);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshBatch)obj);
        }

        public int CompareTo(FMeshBatch MeshBatch)
        {
            return Priority.CompareTo(MeshBatch.Priority);
        }

        public static int MatchForDynamicInstance(ref FMeshBatch meshBatch)
        {
            return new int3(meshBatch.submeshIndex, meshBatch.staticMeshRef.Id, meshBatch.materialRef.Id).GetHashCode();
        }

        public static int MatchForCacheMeshBatch(ref FMeshBatch meshBatch, in int instanceID)
        {
            return instanceID + meshBatch.GetHashCode();
        }

        public override int GetHashCode()
        {
            int hashCode = submeshIndex;
            hashCode += staticMeshRef.GetHashCode();
            hashCode += materialRef.GetHashCode();
            //hashCode += CastShadow.GetHashCode();
            hashCode += visible.GetHashCode();
            //hashCode += RenderLayer.GetHashCode();
            hashCode += boundBox.GetHashCode();
            hashCode += matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }
    }

    public struct FViewMeshBatch : IComparable<FViewMeshBatch>
    {
        public int visible;


        public FViewMeshBatch(in int visible)
        {
            this.visible = visible;
        }

        public int CompareTo(FViewMeshBatch ViewMeshBatch)
        {
            return visible.CompareTo(ViewMeshBatch.visible);
        }

        public static implicit operator Int32(FViewMeshBatch ViewMeshBatch) { return ViewMeshBatch.visible; }
        public static implicit operator FViewMeshBatch(int index) { return new FViewMeshBatch(index); }
    }

    public struct FPassMeshBatch : IComparable<FPassMeshBatch>, IEquatable<FPassMeshBatch>
    {
        public int meshBatchId;
        public int instanceGroupId;


        public FPassMeshBatch(in int meshBatchId, in int instanceGroupId)
        {
            this.meshBatchId = meshBatchId;
            this.instanceGroupId = instanceGroupId;
        }

        public int CompareTo(FPassMeshBatch Target)
        {
            return instanceGroupId.CompareTo(Target.instanceGroupId);
        }

        public bool Equals(FPassMeshBatch Target)
        {
            return instanceGroupId.Equals(Target.instanceGroupId);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPassMeshBatch)obj);
        }

        public override int GetHashCode()
        {
            return instanceGroupId.GetHashCode();
        }
    }
}
