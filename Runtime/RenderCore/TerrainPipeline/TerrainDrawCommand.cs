using System;
using UnityEngine;
using InfinityTech.Core;

namespace InfinityTech.Rendering.TerrainPipeline
{
    public struct FTerrainDrawCommand : IComparable<FTerrainDrawCommand>, IEquatable<FTerrainDrawCommand>
    {
        public int MeshID;
        public int HashCode;
        public int MaterialID;
        public int SubmeshIndex;

        public FTerrainDrawCommand(in int InMeshID, in int InMaterialID, in int InSubmeshIndex, in int InHashCode)
        {
            MeshID = InMeshID;
            HashCode = InHashCode;
            MaterialID = InMaterialID;
            SubmeshIndex = InSubmeshIndex;
        }

        public int CompareTo(FTerrainDrawCommand Target)
        {
            return HashCode.CompareTo(Target.HashCode);
        }

        public bool Equals(FTerrainDrawCommand Target)
        {
            return HashCode == Target.HashCode;
        }

        public override bool Equals(object obj)
        {
            return Equals((FTerrainDrawCommand)obj);
        }

        public override int GetHashCode()
        {
            return HashCode;
        }
    }
}
