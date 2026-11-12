using Unity.Mathematics;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>Which compiled gather writes an instance key. The comparer does not read this.</summary>
    internal enum EMeshSortKey : byte
    {
        State,
        TranslucentColor
    }

    /// <summary>The values a pass may pack into one instance key.</summary>
    internal struct MeshSortKeyInput
    {
        public ulong stateKey;
        public int priority;
        public float distance;
        public float fade;
    }

    /// <summary>A pass-owned formula that writes one instance key.</summary>
    internal interface IMeshSortKey
    {
        ulong Eval(in MeshSortKeyInput input);
    }

    /// <summary>Fading in bit 63 and the command state key below it.</summary>
    internal struct StateSortKey : IMeshSortKey
    {
        public ulong Eval(in MeshSortKeyInput input)
        {
            ulong fading = input.fade != 0f ? 1ul : 0ul;
            return (fading << 63) | (input.stateKey & ((1ul << 63) - 1ul));
        }
    }

    /// <summary>Priority in the high 16 bits, then distance from far to near in the low 32.</summary>
    internal struct TranslucentColorSortKey : IMeshSortKey
    {
        public ulong Eval(in MeshSortKeyInput input)
        {
            ulong priority = SortKeyPacking.SaturateSigned(input.priority, 16);
            return (priority << 48) | DistanceFarFirst(input.distance);
        }

        /// <summary>Nonnegative distance as 32 bits; farther is smaller. NaN and negatives sort as nearest.</summary>
        public static ulong DistanceFarFirst(float distance)
        {
            if (!(distance > 0f))
            {
                return 0xFFFFFFFFul;
            }
            return 0xFFFFFFFFul - math.asuint(distance);
        }
    }
}
