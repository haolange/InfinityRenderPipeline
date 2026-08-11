using System;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.MeshPipeline
{
    public readonly struct MeshInstanceId : IEquatable<MeshInstanceId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshInstanceId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Generation != 0;
        }

        public static readonly MeshInstanceId Invalid = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MeshInstanceId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) => obj is MeshInstanceId other && Equals(other);

        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;

        public static bool operator ==(MeshInstanceId a, MeshInstanceId b) => a.Equals(b);
        public static bool operator !=(MeshInstanceId a, MeshInstanceId b) => !a.Equals(b);
    }

    public readonly struct TransformId : IEquatable<TransformId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public TransformId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Generation != 0;
        }

        public static readonly TransformId Invalid = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(TransformId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) => obj is TransformId other && Equals(other);

        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;

        public static bool operator ==(TransformId a, TransformId b) => a.Equals(b);
        public static bool operator !=(TransformId a, TransformId b) => !a.Equals(b);
    }

    public readonly struct MeshDrawId : IEquatable<MeshDrawId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshDrawId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Generation != 0;
        }

        public static readonly MeshDrawId Invalid = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MeshDrawId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) => obj is MeshDrawId other && Equals(other);

        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;

        public static bool operator ==(MeshDrawId a, MeshDrawId b) => a.Equals(b);
        public static bool operator !=(MeshDrawId a, MeshDrawId b) => !a.Equals(b);
    }

    public readonly struct MeshSectionId : IEquatable<MeshSectionId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshSectionId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Generation != 0;
        }

        public static readonly MeshSectionId Invalid = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MeshSectionId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) => obj is MeshSectionId other && Equals(other);

        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;

        public static bool operator ==(MeshSectionId a, MeshSectionId b) => a.Equals(b);
        public static bool operator !=(MeshSectionId a, MeshSectionId b) => !a.Equals(b);
    }

    public readonly struct MaterialDataId : IEquatable<MaterialDataId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MaterialDataId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Generation != 0;
        }

        public static readonly MaterialDataId Invalid = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MaterialDataId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) => obj is MaterialDataId other && Equals(other);

        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;

        public static bool operator ==(MaterialDataId a, MaterialDataId b) => a.Equals(b);
        public static bool operator !=(MaterialDataId a, MaterialDataId b) => !a.Equals(b);
    }

    public readonly struct MeshPassDrawId : IEquatable<MeshPassDrawId>
    {
        public readonly uint Index;
        public readonly uint Generation;

        public MeshPassDrawId(uint index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Generation != 0;
        }

        public static readonly MeshPassDrawId Invalid = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MeshPassDrawId other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) => obj is MeshPassDrawId other && Equals(other);

        public override int GetHashCode() => (int)(Index * 397u) ^ (int)Generation;

        public static bool operator ==(MeshPassDrawId a, MeshPassDrawId b) => a.Equals(b);
        public static bool operator !=(MeshPassDrawId a, MeshPassDrawId b) => !a.Equals(b);
    }
}
