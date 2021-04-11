using System;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

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
        public int SubmeshIndex;
        public SharedRef<Mesh> Mesh;
        public SharedRef<Material> Material;

        public int CastShadow;
        public int MotionType;

        public int Visible;
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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchForDynamicInstance(ref FMeshBatch Target)
        {
            return (Target.SubmeshIndex << 16) | ((Target.Mesh.Id << 16) | (Target.Material.Id >> 16));
        }

        public static int MatchForCacheMeshBatch(ref FMeshBatch Target, in int InstanceID)
        {
            return InstanceID + Target.GetHashCode();
        }

        public override int GetHashCode()
        {
            int hashCode = SubmeshIndex;
            hashCode += Mesh.GetHashCode();
            hashCode += Material.GetHashCode();
            //hashCode += CastShadow.GetHashCode();
            //hashCode += MotionType.GetHashCode();
            hashCode += Visible.GetHashCode();
            //hashCode += Priority.GetHashCode();
            //hashCode += RenderLayer.GetHashCode();
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

    public struct FPassMeshBatch : IComparable<FPassMeshBatch>, IEquatable<FPassMeshBatch>
    {
        public int HashIndex;
        public int MeshBatchIndex;

        public FPassMeshBatch(in int InHashIndex, in int InMeshBatchIndex)
        {
            HashIndex = InHashIndex;
            MeshBatchIndex = InMeshBatchIndex;
        }

        public int CompareTo(FPassMeshBatch Target)
        {
            return HashIndex.CompareTo(Target.HashIndex);
        }

        public bool Equals(FPassMeshBatch Target)
        {
            return HashIndex.Equals(Target.HashIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPassMeshBatch)obj);
        }

        public override int GetHashCode()
        {
            return HashIndex;
        }
    }
}
