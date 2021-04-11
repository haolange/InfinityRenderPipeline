using System;
using UnityEngine;
using InfinityTech.Core;

namespace InfinityTech.Rendering.MeshPipeline
{
    public enum EGatherMethod
    {
        DotsV1,
        DotsV2,
        DefaultV1,
        DefaultV2
    }

    public struct FMeshDrawCommand : IComparable<FMeshDrawCommand>, IEquatable<FMeshDrawCommand>
    {
        public int MeshID;
        public int MaterialID;
        public int SubmeshIndex;

        public FMeshDrawCommand(in int InMeshID, in int InMaterialID, in int InSubmeshIndex)
        {
            MeshID = InMeshID;
            MaterialID = InMaterialID;
            SubmeshIndex = InSubmeshIndex;
        }

        public int CompareTo(FMeshDrawCommand Target)
        {
            //return SubmeshIndex + (MeshID << 16 | MaterialID);
            return  (SubmeshIndex << 16) | ((MeshID << 16) | (MaterialID >> 16));
        }

        public bool Equals(FMeshDrawCommand Target)
        {
            int SelfValue = (SubmeshIndex << 16) | ((MeshID << 16) | (MaterialID >> 16));
            int TargetValue = (Target.SubmeshIndex << 16) | ((Target.MeshID << 16) | (Target.MaterialID >> 16));
            return SelfValue == TargetValue;
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshDrawCommand)obj);
        }

        public override int GetHashCode()
        {
            return (SubmeshIndex << 16) | ((MeshID << 16) | (MaterialID >> 16));
        }
    }
}
