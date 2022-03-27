using System;
using UnityEngine;
using Unity.Mathematics;
using InfinityTech.Core;
using InfinityTech.Core.Geometry;
using System.Runtime.InteropServices;

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

    [StructLayout(LayoutKind.Sequential)]
    public struct FMeshElement : IComparable<FMeshElement>, IEquatable<FMeshElement>
    {
        public int sectionIndex;
        public SharedRef<Mesh> staticMeshRef;
        public SharedRef<Material> materialRef;
        public int visible;
        public int priority;
        public int castShadow;
        public int motionType;
        public int renderLayer;
        public int pending1;
        public int pending2;
        public FBound boundBox;
        //public float4x4 CustomPrimitiveData;
        public float4x4 matrix_LocalToWorld;


        public bool Equals(FMeshElement target)
        {
            return sectionIndex.Equals(target.sectionIndex) && staticMeshRef.Equals(target.staticMeshRef) && materialRef.Equals(target.materialRef);
        }

        public override bool Equals(object target)
        {
            return Equals((FMeshElement)target);
        }

        public int CompareTo(FMeshElement meshElement)
        {
            return priority.CompareTo(meshElement.priority);
        }

        public static int MatchForDynamicInstance(ref FMeshElement meshElement)
        {
            return new int3(meshElement.sectionIndex, meshElement.staticMeshRef.Id, meshElement.materialRef.Id).GetHashCode();
        }

        public static int MatchForCacheMeshBatch(ref FMeshElement meshElement, in int instanceID)
        {
            return instanceID + meshElement.GetHashCode();
        }

        public override int GetHashCode()
        {
            int hashCode = sectionIndex;
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

        public int CompareTo(FViewMeshBatch viewMeshBatch)
        {
            return visible.CompareTo(viewMeshBatch.visible);
        }

        public static implicit operator Int32(FViewMeshBatch viewMeshBatch) { return viewMeshBatch.visible; }
        public static implicit operator FViewMeshBatch(int index) { return new FViewMeshBatch(index); }
    }

    public struct FPassMeshSection : IComparable<FPassMeshSection>, IEquatable<FPassMeshSection>
    {
        public int meshElementId;
        public int instanceGroupId;

        public FPassMeshSection(in int meshElementId, in int instanceGroupId)
        {
            this.meshElementId = meshElementId;
            this.instanceGroupId = instanceGroupId;
        }

        public int CompareTo(FPassMeshSection target)
        {
            return instanceGroupId.CompareTo(target.instanceGroupId);
        }

        public bool Equals(FPassMeshSection target)
        {
            return instanceGroupId.Equals(target.instanceGroupId);
        }

        public override bool Equals(object target)
        {
            return Equals((FPassMeshSection)target);
        }

        public override int GetHashCode()
        {
            return instanceGroupId.GetHashCode();
        }
    }
}
