using System;
using System.Collections.Generic;

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

    // One arena for a graph's recording arrays and execution scratch. The cursor resets when the graph is cleared.
    // A slot is reused when the element type and length match; recording arrays stay live through execution.
    public sealed class RGObjectPool
    {
        readonly List<object> m_Slots = new List<object>(64);
        int m_Cursor;

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
            if (m_Cursor < m_Slots.Count)
            {
                if (m_Slots[m_Cursor] is T[] existing && existing.Length == size)
                {
                    m_Cursor++;
                    if (size > 0)
                    {
                        Array.Clear(existing, 0, size);
                    }
                    return existing;
                }
                var replaced = new T[size];
                m_Slots[m_Cursor] = replaced;
                m_Cursor++;
                return replaced;
            }
            var created = new T[size];
            m_Slots.Add(created);
            m_Cursor++;
            return created;
        }

        internal void Reset()
        {
            m_Cursor = 0;
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
