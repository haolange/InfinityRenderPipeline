using System;
using Unity.Collections;

namespace InfinityTech.Core.Container
{
    public class TSparseArray<T>
    {
        public int length
        {
            get
            {
                return m_Array.length;
            }
        }
        public ref T this[int index]
        {
            get
            {
                return ref m_Array[index];
            }
        }

        private TArray<T> m_Array;
        private TArray<int> m_PoolArray;

        public TSparseArray()
        {
            m_Array = new TArray<T>();
            m_PoolArray = new TArray<int>();
        }

        public TSparseArray(in int capacity)
        {
            m_Array = new TArray<T>(capacity);
            m_PoolArray = new TArray<int>(capacity / 2);
        }

        public int Add(in T value)
        {
            if(m_PoolArray.length != 0)
            {
                int poolIndex = m_PoolArray[m_PoolArray.length - 1];
                m_PoolArray.RemoveSwapAtIndex(m_PoolArray.length - 1);

                m_Array[poolIndex] = value;
                return poolIndex;
            }
            return m_Array.Add(value);
        }

        public void Remove(in int index)
        {
            m_Array[index] = default(T);
            m_PoolArray.Add(index);
        }
    }

    public unsafe struct TNativeSparseArray<T> : IDisposable where T : unmanaged
    {
        public int length
        {
            get
            {
                return m_Array->length;
            }
        }
        public ref T this[int index]
        {
            get
            {
                TNativeArray<T> array = *m_Array;
                return ref array[index];
            }
        }

        internal TNativeArray<T>* m_Array;
        internal TNativeArray<int>* m_PoolArray;

        public TNativeSparseArray(in Allocator allocator, in int capacity = 64)
        {
            m_Array = default;
            m_PoolArray = default;
            //m_Array = new TNativeArray<T>(allocator, capacity);
            //m_PoolArray = new TNativeArray<int>(allocator, capacity / 2);
        }

        public int Add(in T value)
        {
            TNativeArray<T> array = *m_Array;
            TNativeArray<int> poolArray = *m_PoolArray;

            if (poolArray.length != 0)
            {
                int poolIndex = poolArray[poolArray.length - 1];
                poolArray.RemoveSwapAtIndex(poolArray.length - 1);

                array[poolIndex] = value;
                return poolIndex;
            }
            return array.Add(value);
        }

        public void Remove(in int index)
        {
            TNativeArray<T> array = *m_Array;
            array[index] = default(T);
            m_PoolArray->Add(index);
        }

        public void Dispose()
        {
            m_Array->Dispose();
            m_PoolArray->Dispose();
        }
    }
}
