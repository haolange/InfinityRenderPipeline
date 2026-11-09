using UnityEngine;
using System.Collections.Generic;

namespace InfinityTech.Core
{
    public struct ObjectHandle<T> where T : class
    {
        public int Id;

        public ObjectHandle(int id)
        {
            Id = id;
        }

        public bool Equals(in ObjectHandle<T> target)
        {
            return (Id == target.Id);
        }

        public override bool Equals(object obj)
        {
            return Equals((ObjectHandle<T>)obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    public class ObjectHandleFactory<T> where T : class
    {
        public readonly Dictionary<int, T> m_SharedRefs;

        public ObjectHandleFactory(int initialCapacity = 256)
        {
            m_SharedRefs = new Dictionary<int, T>(initialCapacity);
        }

        public ObjectHandle<T> Add(T obj, in int id)
        {
            m_SharedRefs[id] = obj;
            return new ObjectHandle<T>(id);
        }

        public T Get(in int id)
        {
            return m_SharedRefs[id];
        }

        public T Get(in ObjectHandle<T> handle)
        {
            return m_SharedRefs[handle.Id];
        }

        public void Remove(in ObjectHandle<T> handle)
        {
            m_SharedRefs.Remove(handle.Id);
        }

        public void Clear()
        {
            m_SharedRefs.Clear();
        }
    }
}
