using System;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>One logical object: its transform, bounds and attributes. The index is the entity slot and the transform slot.</summary>
    public readonly struct MeshEntityId : IEquatable<MeshEntityId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshEntityId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Generation != 0;
        public static readonly MeshEntityId Invalid = default;

        public bool Equals(MeshEntityId other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is MeshEntityId other && Equals(other);
        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;
        public static bool operator ==(MeshEntityId a, MeshEntityId b) => a.Equals(b);
        public static bool operator !=(MeshEntityId a, MeshEntityId b) => !a.Equals(b);
    }

    /// <summary>One drawable part of an entity: a mesh section with one material.</summary>
    public readonly struct MeshElementId : IEquatable<MeshElementId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshElementId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Generation != 0;
        public static readonly MeshElementId Invalid = default;

        public bool Equals(MeshElementId other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is MeshElementId other && Equals(other);
        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;
        public static bool operator ==(MeshElementId a, MeshElementId b) => a.Equals(b);
        public static bool operator !=(MeshElementId a, MeshElementId b) => !a.Equals(b);
    }

    /// <summary>A shared (mesh, submesh) record referenced by elements.</summary>
    public readonly struct MeshSectionId : IEquatable<MeshSectionId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshSectionId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Generation != 0;
        public static readonly MeshSectionId Invalid = default;

        public bool Equals(MeshSectionId other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is MeshSectionId other && Equals(other);
        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;
        public static bool operator ==(MeshSectionId a, MeshSectionId b) => a.Equals(b);
        public static bool operator !=(MeshSectionId a, MeshSectionId b) => !a.Equals(b);
    }

    /// <summary>A shared material record referenced by elements.</summary>
    public readonly struct MeshMaterialId : IEquatable<MeshMaterialId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshMaterialId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Generation != 0;
        public static readonly MeshMaterialId Invalid = default;

        public bool Equals(MeshMaterialId other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is MeshMaterialId other && Equals(other);
        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;
        public static bool operator ==(MeshMaterialId a, MeshMaterialId b) => a.Equals(b);
        public static bool operator !=(MeshMaterialId a, MeshMaterialId b) => !a.Equals(b);
    }
}
