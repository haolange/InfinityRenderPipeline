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
    public struct MeshElement : IComparable<MeshElement>, IEquatable<MeshElement>
    {
        public int sectionIndex;
        public UObjectRef<Mesh> meshRef;
        public UObjectRef<Material> materialRef;
        public int visible;
        public int priority;
        public int castShadow;
        public int motionType;
        public int renderLayer;
        public FBound boundBox;
        //public float4x4 CustomPrimitiveData;
        //public float4x4 matrix_LocalToWorld;


        public bool Equals(MeshElement target)
        {
            return sectionIndex.Equals(target.sectionIndex) && meshRef.Equals(target.meshRef) && materialRef.Equals(target.materialRef);
        }

        public override bool Equals(object target)
        {
            return Equals((MeshElement)target);
        }

        public int CompareTo(MeshElement meshElement)
        {
            return priority.CompareTo(meshElement.priority);
        }

        public static int MatchForDynamicInstance(ref MeshElement meshElement)
        {
            return new int3(meshElement.sectionIndex, meshElement.meshRef.Id, meshElement.materialRef.Id).GetHashCode();
        }

        public static int MatchForCacheMeshBatch(ref MeshElement meshElement, in int instanceID)
        {
            return instanceID + meshElement.GetHashCode();
        }

        public override int GetHashCode()
        {
            int hashCode = sectionIndex;
            hashCode += meshRef.GetHashCode();
            hashCode += materialRef.GetHashCode();
            hashCode += castShadow.GetHashCode();
            hashCode += visible.GetHashCode();
            hashCode += renderLayer.GetHashCode();
            hashCode += boundBox.GetHashCode();
            //hashCode += matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }
    }

    public struct ViewMeshElement : IComparable<ViewMeshElement>
    {
        public int visible;

        public ViewMeshElement(in int visible)
        {
            this.visible = visible;
        }

        public int CompareTo(ViewMeshElement viewMeshBatch)
        {
            return visible.CompareTo(viewMeshBatch.visible);
        }

        public static implicit operator Int32(ViewMeshElement viewMeshBatch) { return viewMeshBatch.visible; }
        public static implicit operator ViewMeshElement(int index) { return new ViewMeshElement(index); }
    }

    public struct PassMeshSection : IComparable<PassMeshSection>, IEquatable<PassMeshSection>
    {
        public int meshElementId;
        public int instanceGroupId;

        public PassMeshSection(in int meshElementId, in int instanceGroupId)
        {
            this.meshElementId = meshElementId;
            this.instanceGroupId = instanceGroupId;
        }

        public int CompareTo(PassMeshSection target)
        {
            return instanceGroupId.CompareTo(target.instanceGroupId);
        }

        public bool Equals(PassMeshSection target)
        {
            return instanceGroupId.Equals(target.instanceGroupId);
        }

        public override bool Equals(object target)
        {
            return Equals((PassMeshSection)target);
        }

        public override int GetHashCode()
        {
            return instanceGroupId.GetHashCode();
        }
    }
}
