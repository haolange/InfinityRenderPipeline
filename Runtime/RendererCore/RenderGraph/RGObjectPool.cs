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

    public sealed class RGObjectPool
    {
        readonly List<(object array, (Type type, int length) key)> m_ExecutionArrays = new List<(object, (Type, int))>();
        readonly Dictionary<(Type, int), Stack<object>> m_ExecutionPool = new Dictionary<(Type, int), Stack<object>>();
        readonly List<(object array, (Type type, int length) key)> m_RecordingArrays = new List<(object, (Type, int))>();
        readonly Dictionary<(Type, int), Stack<object>> m_RecordingPool = new Dictionary<(Type, int), Stack<object>>();

        internal RGObjectPool()
        {
        }

        // Arrays borrowed while a pass executes. Released at the end of that pass.
        public T[] GetTempArray<T>(int size)
        {
            return Rent<T>(size, m_ExecutionPool, m_ExecutionArrays);
        }

        // Arrays captured during recording and read by a later pass. They outlive every pass execution.
        public T[] GetRecordingArray<T>(int size)
        {
            return Rent<T>(size, m_RecordingPool, m_RecordingArrays);
        }

        static T[] Rent<T>(int size, Dictionary<(Type, int), Stack<object>> pool, List<(object array, (Type type, int length) key)> outstanding)
        {
            var key = (typeof(T), size);
            if (!pool.TryGetValue(key, out Stack<object> stack))
            {
                stack = new Stack<object>();
                pool.Add(key, stack);
            }

            T[] result = stack.Count > 0 ? (T[])stack.Pop() : new T[size];
            outstanding.Add((result, key));
            return result;
        }

        internal void ReleaseAllTempAlloc()
        {
            ReturnAll(m_ExecutionArrays, m_ExecutionPool);
        }

        internal void ReleaseRecordingAlloc()
        {
            ReturnAll(m_RecordingArrays, m_RecordingPool);
        }

        static void ReturnAll(List<(object array, (Type type, int length) key)> outstanding, Dictionary<(Type, int), Stack<object>> pool)
        {
            for (int i = 0; i < outstanding.Count; ++i)
            {
                (object array, (Type type, int length) key) item = outstanding[i];
                if (!pool.TryGetValue(item.key, out Stack<object> stack))
                {
                    stack = new Stack<object>();
                    pool.Add(item.key, stack);
                }
                stack.Push(item.array);
            }
            outstanding.Clear();
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
