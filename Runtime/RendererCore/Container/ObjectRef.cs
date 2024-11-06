using UnityEngine;
using System.Collections.Generic;

namespace InfinityTech.Core
{
    public struct ObjectRef<T> where T : class
    {
        public int Id;

        public ObjectRef(int id)
        {
            Id = id;
        }

        public bool Equals(in ObjectRef<T> target)
        {
            return (Id == target.Id);
        }

        public override bool Equals(object obj)
        {
            return Equals((ObjectRef<T>)obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    public struct UObjectRef<T> where T : Object
    {
        public int Id;
        public T light => (T)Resources.InstanceIDToObject(Id);

        public UObjectRef(int id)
        {
            Id = id;
        }

        public bool Equals(in ObjectRef<T> target)
        {
            return (Id == target.Id);
        }

        public override bool Equals(object obj)
        {
            return Equals((ObjectRef<T>)obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    public class ObjectRefFactory<T> where T : class
    {
        public readonly Dictionary<int, T> m_SharedRefs;
 
        public ObjectRefFactory(int initialCapacity = 256)
        {
            m_SharedRefs = new Dictionary<int, T>(initialCapacity);
        }
    
        public ObjectRef<T> Add(T obj, in int id)
        {
            m_SharedRefs[id] = obj;
            return new ObjectRef<T>(id);
        }

        public T Get(in int id)
        {
            return m_SharedRefs[id];
        }

        public T Get(in ObjectRef<T> objRef)
        {
            return m_SharedRefs[objRef.Id];
        }
    
        public void Remove(in ObjectRef<T> objRef)
        {
            m_SharedRefs.Remove(objRef.Id);
        }

        public void Clear()
        {
            m_SharedRefs.Clear();
        }
    }
}
