using System;
using System.Collections.Generic;

namespace InfinityTech.Runtime.Core
{
    public struct SharedRef<T> where T : class
    {
        public int Id;
        public int Hash;

        public SharedRef(int Index, int InHash)
        {
            Id = Index;
            Hash = InHash;
        }

        public bool Equals(in SharedRef<T> Target)
        {
            return (Target.Id == this.Id) && (Target.Hash == this.Hash);
        }

        public override bool Equals(object obj)
        {
            return Equals((SharedRef<T>)obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public class SharedRefFactory<T> where T : class
    {
        public readonly Dictionary<int, T> m_SharedRefs;
 
        public SharedRefFactory(int initialCapacity = 1024)
        {
            m_SharedRefs = new Dictionary<int, T>(initialCapacity);
        }
    
        public SharedRef<T> Add(T obj)
        {
            int id = obj.GetHashCode();
            m_SharedRefs[id] = obj;
            return new SharedRef<T>(id, id);
        }
    
        public T Get(SharedRef<T> objRef)
        {
            return m_SharedRefs[objRef.Id];
        }
    
        public void Remove(SharedRef<T> objRef)
        {
            m_SharedRefs.Remove(objRef.Id);
        }

        public void Reset()
        {
            m_SharedRefs.Clear();
        }
    }
}
