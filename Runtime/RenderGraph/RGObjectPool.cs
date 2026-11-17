using System;
using System.Collections.Generic;
using System.Threading;

namespace InfinityTech.Rendering.RenderGraph
{
    internal class RGSharedObjectPool<T> where T : new()
    {
        readonly Stack<T> m_Pool = new Stack<T>();

        public T Get()
        {
            return m_Pool.Count == 0 ? new T() : m_Pool.Pop();
        }

        public void Release(T value)
        {
            m_Pool.Push(value);
        }

        static readonly Lazy<RGSharedObjectPool<T>> s_Instance = new Lazy<RGSharedObjectPool<T>>();
        public static RGSharedObjectPool<T> sharedPool => s_Instance.Value;
    }

    // One arena for a graph's recording arrays and execution scratch. Slots are grouped by element type and length.
    // Each group has its own cursor. Reset returns every cursor to zero. A group allocates only when one graph rents
    // that key more times than it has before. Recording and scratch share the cursors, so scratch rented during
    // execution cannot replace an array recorded earlier in the same graph.
    public sealed class RGObjectPool
    {
        static int s_NextType;
        Arena[] m_Arenas = new Arena[8];
        readonly List<Arena> m_Live = new List<Arena>(8);

        static class TypeIndex<T>
        {
            internal static readonly int Index = Interlocked.Increment(ref s_NextType) - 1;
        }

        abstract class Arena
        {
            public abstract void Reset();
        }

        sealed class Arena<T> : Arena
        {
            struct Bucket
            {
                public int length;
                public int cursor;
                public int count;
                public T[][] slots;
            }

            Bucket[] m_Buckets = new Bucket[4];
            int m_Count;

            public T[] Rent(int size)
            {
                for (int i = 0; i < m_Count; ++i)
                {
                    if (m_Buckets[i].length == size)
                    {
                        return Take(ref m_Buckets[i]);
                    }
                }
                if (m_Count == m_Buckets.Length)
                {
                    Array.Resize(ref m_Buckets, m_Buckets.Length * 2);
                }
                int index = m_Count++;
                m_Buckets[index].length = size;
                m_Buckets[index].slots = new T[4][];
                return Take(ref m_Buckets[index]);
            }

            static T[] Take(ref Bucket bucket)
            {
                if (bucket.cursor < bucket.count)
                {
                    T[] existing = bucket.slots[bucket.cursor++];
                    if (bucket.length > 0)
                    {
                        Array.Clear(existing, 0, bucket.length);
                    }
                    return existing;
                }
                if (bucket.count == bucket.slots.Length)
                {
                    Array.Resize(ref bucket.slots, Math.Max(4, bucket.slots.Length * 2));
                }
                var created = new T[bucket.length];
                bucket.slots[bucket.count++] = created;
                bucket.cursor++;
                return created;
            }

            public override void Reset()
            {
                for (int i = 0; i < m_Count; ++i)
                {
                    m_Buckets[i].cursor = 0;
                }
            }
        }

        internal RGObjectPool()
        {
        }

        public T[] GetTempArray<T>(int size) => Rent<T>(size);

        public T[] GetRecordingArray<T>(int size) => Rent<T>(size);

        T[] Rent<T>(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }
            return ArenaFor<T>().Rent(size);
        }

        Arena<T> ArenaFor<T>()
        {
            int index = TypeIndex<T>.Index;
            if (index >= m_Arenas.Length)
            {
                var grown = new Arena[Math.Max(index + 1, m_Arenas.Length * 2)];
                Array.Copy(m_Arenas, grown, m_Arenas.Length);
                m_Arenas = grown;
            }
            if (m_Arenas[index] == null)
            {
                var created = new Arena<T>();
                m_Arenas[index] = created;
                m_Live.Add(created);
            }
            return (Arena<T>)m_Arenas[index];
        }

        internal void Reset()
        {
            for (int i = 0; i < m_Live.Count; ++i)
            {
                m_Live[i].Reset();
            }
        }

        internal T Get<T>() where T : new()
        {
            return RGSharedObjectPool<T>.sharedPool.Get();
        }

        internal void Release<T>(T value) where T : new()
        {
            RGSharedObjectPool<T>.sharedPool.Release(value);
        }
    }
}
