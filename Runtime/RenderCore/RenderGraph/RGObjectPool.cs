﻿using System;
using System.Collections.Generic;

namespace InfinityTech.Rendering.RenderGraph
{
    internal class RGSharedObjectPool<T> where T : new()
    {
        Stack<T> m_Pool = new Stack<T>();

        public T Get()
        {
            var result = m_Pool.Count == 0 ? new T() : m_Pool.Pop();
            return result;
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
        List<(object, (Type, int))> m_AllocatedArrays = new List<(object, (Type, int))>();
        Dictionary<(Type, int), Stack<object>> m_ArrayPool = new Dictionary<(Type, int), Stack<object>>();

        internal RGObjectPool()
        { 

        }

        public T[] GetTempArray<T>(int size)
        {
            if (!m_ArrayPool.TryGetValue((typeof(T), size), out var stack))
            {
                stack = new Stack<object>();
                m_ArrayPool.Add((typeof(T), size), stack);
            }

            var result = stack.Count > 0 ? (T[])stack.Pop() : new T[size];
            m_AllocatedArrays.Add((result, (typeof(T), size)));
            return result;
        }

        internal void ReleaseAllTempAlloc()
        {
            foreach (var arrayDesc in m_AllocatedArrays)
            {
                bool result = m_ArrayPool.TryGetValue(arrayDesc.Item2, out var stack);
                stack.Push(arrayDesc.Item1);
            }

            m_AllocatedArrays.Clear();
        }

        internal T Get<T>() where T : new()
        {
            var toto = RGSharedObjectPool<T>.sharedPool;
            return toto.Get();
        }

        internal void Release<T>(T value) where T : new()
        {
            var toto = RGSharedObjectPool<T>.sharedPool;
            toto.Release(value);
        }
    }
}
