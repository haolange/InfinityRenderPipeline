using System;
using Unity.Collections;

namespace InfinityTech.Rendering.MeshPipeline
{
    /// <summary>
    /// Fixed-index slot storage. A slot keeps its index for its whole life. Generations are odd while a slot is live and
    /// even while it is dead, so an id never matches a dead or reused slot. A killed slot returns to the free list only
    /// through <see cref="Reclaim"/>, which the owning transaction calls at commit; a slot killed and revived by rollback
    /// was therefore never reused, and rollback never truncates the free list.
    /// </summary>
    internal struct SlotTable<T> : IDisposable where T : unmanaged
    {
        NativeList<T> m_Items;
        NativeList<uint> m_Generations;
        NativeList<int> m_FreeList;
        int m_LiveCount;

        public SlotTable(int capacity)
        {
            m_Items = new NativeList<T>(capacity, Allocator.Persistent);
            m_Generations = new NativeList<uint>(capacity, Allocator.Persistent);
            m_FreeList = new NativeList<int>(capacity, Allocator.Persistent);
            m_LiveCount = 0;
        }

        public bool IsCreated => m_Items.IsCreated;
        public int HighWater => m_Items.Length;
        public int LiveCount => m_LiveCount;
        public int FreeCount => m_FreeList.Length;

        /// <summary>Valid until the next allocation that grows the table.</summary>
        public NativeArray<T> Items => m_Items.AsArray();
        public NativeArray<uint> Generations => m_Generations.AsArray();

        public T this[int index]
        {
            get => m_Items[index];
            set => m_Items[index] = value;
        }

        public uint GenerationOf(int index) => m_Generations[index];

        public bool IsLive(int index) => (uint)index < (uint)m_Items.Length && (m_Generations[index] & 1u) != 0;

        public bool IsLive(int index, uint generation)
            => (uint)index < (uint)m_Items.Length && (generation & 1u) != 0 && m_Generations[index] == generation;

        public int Allocate(in T value, out uint generation)
        {
            int index;
            if (m_FreeList.Length > 0)
            {
                index = m_FreeList[m_FreeList.Length - 1];
                m_FreeList.RemoveAt(m_FreeList.Length - 1);
                m_Items[index] = value;
            }
            else
            {
                index = m_Items.Length;
                m_Items.Add(value);
                m_Generations.Add(0u);
            }

            generation = NextGeneration(m_Generations[index]);
            m_Generations[index] = generation;
            m_LiveCount++;
            return index;
        }

        /// <summary>Ends the slot's life. The slot is not reusable until <see cref="Reclaim"/>.</summary>
        public void Kill(int index)
        {
            if (!IsLive(index))
            {
                throw new InvalidOperationException($"Slot {index} is not live.");
            }
            m_Generations[index] = NextGeneration(m_Generations[index]);
            m_Items[index] = default;
            m_LiveCount--;
        }

        public void Reclaim(int index)
        {
            if (IsLive(index))
            {
                throw new InvalidOperationException($"Slot {index} is live and cannot be reclaimed.");
            }
            m_FreeList.Add(index);
        }

        /// <summary>Rollback of a kill inside the same transaction: the slot was never reclaimed.</summary>
        public void Revive(int index, uint generation, in T value)
        {
            if (IsLive(index) || (generation & 1u) == 0)
            {
                throw new InvalidOperationException($"Slot {index} cannot be revived.");
            }
            m_Generations[index] = generation;
            m_Items[index] = value;
            m_LiveCount++;
        }

        static uint NextGeneration(uint generation)
        {
            unchecked
            {
                generation++;
            }
            return generation;
        }

        public void Dispose()
        {
            if (m_Items.IsCreated)
            {
                m_Items.Dispose();
            }
            if (m_Generations.IsCreated)
            {
                m_Generations.Dispose();
            }
            if (m_FreeList.IsCreated)
            {
                m_FreeList.Dispose();
            }
            m_LiveCount = 0;
        }
    }
}
