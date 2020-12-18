using System;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Runtime.Core;
using InfinityTech.Runtime.Core.Geometry;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
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
}
