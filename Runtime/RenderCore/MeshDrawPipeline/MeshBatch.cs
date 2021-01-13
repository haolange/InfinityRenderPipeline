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

    public struct FMeshBatch : IComparable<FMeshBatch>, IEquatable<FMeshBatch>
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


        public bool Equals(FMeshBatch Target)
        {
            return SubmeshIndex.Equals(Target.SubmeshIndex) && Mesh.Equals(Target.Mesh) && Material.Equals(Target.Material);
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
            int hashCode = 1;
            hashCode += Mesh.GetHashCode();
            hashCode += Material.GetHashCode();
            hashCode += SubmeshIndex.GetHashCode();
            //hashCode += Visible.GetHashCode();
            //hashCode += CastShadow.GetHashCode();
            //hashCode += MotionType.GetHashCode();
            //hashCode += RenderLayer.GetHashCode();
            return hashCode;
        }

        public override int GetHashCode()
        {
            int hashCode = 2;
            hashCode += Mesh.GetHashCode();
            hashCode += Material.GetHashCode();
            hashCode += SubmeshIndex.GetHashCode();
            hashCode += CastShadow.GetHashCode();
            hashCode += MotionType.GetHashCode();
            hashCode += Visible.GetHashCode();
            hashCode += Priority.GetHashCode();
            hashCode += RenderLayer.GetHashCode();
            hashCode += BoundBox.GetHashCode();
            hashCode += Matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }

        public int GetHashCode(in int InstanceID)
        {
            int hashCode = InstanceID;
            hashCode += Mesh.GetHashCode();
            hashCode += Material.GetHashCode();
            hashCode += SubmeshIndex.GetHashCode();
            hashCode += CastShadow.GetHashCode();
            hashCode += MotionType.GetHashCode();
            hashCode += Visible.GetHashCode();
            hashCode += Priority.GetHashCode();
            hashCode += RenderLayer.GetHashCode();
            hashCode += BoundBox.GetHashCode();
            hashCode += Matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }
    }

    public struct FViewMeshBatch : IComparable<FViewMeshBatch>
    {
        public int Flag;


        public FViewMeshBatch(in int InFlag)
        {
            Flag = InFlag;
        }

        public int CompareTo(FViewMeshBatch ViewMeshBatch)
        {
            return Flag.CompareTo(ViewMeshBatch.Flag);
        }

        public static implicit operator Int32(FViewMeshBatch ViewMeshBatch) { return ViewMeshBatch.Flag; }
        public static implicit operator FViewMeshBatch(int index) { return new FViewMeshBatch(index); }
    }

    public struct FPassMeshBatch : IComparable<FPassMeshBatch>
    {
        public int index;


        public FPassMeshBatch(in int Index)
        {
            index = Index;
        }

        public int CompareTo(FPassMeshBatch PassMeshBatch)
        {
            return index.CompareTo(PassMeshBatch.index);
        }

        public static implicit operator FPassMeshBatch(int index) { return new FPassMeshBatch(index); }
    }

    /*float Priority = priority + distance;
    return Priority.CompareTo(VisibleMeshBatch.priority + VisibleMeshBatch.distance);*/
}
