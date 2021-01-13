using System;
using UnityEngine;
using InfinityTech.Runtime.Core;

namespace InfinityTech.Runtime.Rendering.MeshDrawPipeline
{
    public struct FMeshDrawCommandKey : IComparable<FMeshDrawCommandKey>, IEquatable<FMeshDrawCommandKey>
    {
        public int MeshID;
        public int MaterialID;
        public int SubmeshIndex;
        public int MatchHashCode;

        public FMeshDrawCommandKey(in int InMeshID, in int InMaterialID, in int InSubmeshIndex, in int InMatchHashCode)
        {
            MeshID = InMeshID;
            MaterialID = InMaterialID;
            SubmeshIndex = InSubmeshIndex;
            MatchHashCode = InMatchHashCode;
        }

        public int CompareTo(FMeshDrawCommandKey MeshDrawCommandKey)
        {
            return MeshID.CompareTo(MeshDrawCommandKey.MeshID) + MaterialID.CompareTo(MeshDrawCommandKey.MaterialID) + SubmeshIndex.CompareTo(MeshDrawCommandKey.SubmeshIndex);
        }

        public bool Equals(FMeshDrawCommandKey Target)
        {
            return MatchHashCode.Equals(Target.MatchHashCode);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshBatch)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = 3;
            hashCode += MeshID.GetHashCode();
            hashCode += MaterialID.GetHashCode();
            hashCode += SubmeshIndex.GetHashCode();

            return hashCode;
        }
    }

    public struct FMeshDrawCommandValue : IComparable<FMeshDrawCommandValue>, IEquatable<FMeshDrawCommandValue>
    {
        public int MeshBatchIndex;


        public FMeshDrawCommandValue(in int InMeshBatchIndex)
        {
            MeshBatchIndex = InMeshBatchIndex;
        }

        public int CompareTo(FMeshDrawCommandValue MeshDrawCommandValue)
        {
            return MeshBatchIndex.CompareTo(MeshDrawCommandValue.MeshBatchIndex);
        }

        public bool Equals(FMeshDrawCommandValue Target)
        {
            return MeshBatchIndex.Equals(Target.MeshBatchIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshBatch)obj);
        }

        public override int GetHashCode()
        {
            return MeshBatchIndex.GetHashCode() + 5;
        }

        public static implicit operator Int32(FMeshDrawCommandValue MDCValue) { return MDCValue.MeshBatchIndex; }
        public static implicit operator FMeshDrawCommandValue(int index) { return new FMeshDrawCommandValue(index); }
    }
}
