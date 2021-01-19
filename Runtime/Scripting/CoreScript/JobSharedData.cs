using System;
using System.Collections.Generic;

namespace InfinityTech.Core
{
    public struct SharedRef<T> where T : class
    {
        public int Id;
        //public int HashCode;

        public SharedRef(int Index)
        {
            Id = Index;
        }

        /*public SharedRef(int Index, int InHash)
        {
            Id = Index;
            Hash = InHash;
        }*/

        public bool Equals(in SharedRef<T> Target)
        {
            return (Id == Target.Id) /*&& (HashCode == Target.HashCode)*/;
        }

        public override bool Equals(object obj)
        {
            return Equals((SharedRef<T>)obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    public class SharedRefFactory<T> where T : class
    {
        public readonly Dictionary<int, T> m_SharedRefs;
 
        public SharedRefFactory(int initialCapacity = 1024)
        {
            m_SharedRefs = new Dictionary<int, T>(initialCapacity);
        }
    
        public SharedRef<T> Add(T obj, in int ID)
        {
            m_SharedRefs[ID] = obj;
            return new SharedRef<T>(ID);
            //return new SharedRef<T>(id, id);
        }

        public T Get(in int Index)
        {
            return m_SharedRefs[Index];
        }

        public T Get(in SharedRef<T> objRef)
        {
            return m_SharedRefs[objRef.Id];
        }
    
        public void Remove(in SharedRef<T> objRef)
        {
            m_SharedRefs.Remove(objRef.Id);
        }

        public void Reset()
        {
            m_SharedRefs.Clear();
        }
    }
}
