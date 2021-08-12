using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;

namespace InfinityTech.Core.Native
{
    public static class FSortFactory 
    {
        public const int QUICKSORT_THRESHOLD_LENGTH = 512;
 //
        public static JobHandle ParallelSort<T>(NativeArray<T> array, JobHandle parentHandle = default) where T : struct, IComparable<T> 
        {
            return MergeSort(array, new FSortRange(0, array.Length - 1), parentHandle);
        }
 
        private static JobHandle MergeSort<T>(NativeArray<T> array, FSortRange range, JobHandle parentHandle = default) where T : struct, IComparable<T> 
        {
            if (range.Length <= QUICKSORT_THRESHOLD_LENGTH) 
            {
                return new FQuicksortJob<T>() { array = array, left = range.left, right = range.right }.Schedule(parentHandle);
            }
 
            FSortRange left = new FSortRange(range.left, range.Middle);
            JobHandle leftHandle = MergeSort(array, left, parentHandle);
            FSortRange right = new FSortRange(range.Middle + 1, range.right);
            JobHandle rightHandle = MergeSort(array, right, parentHandle);
            JobHandle dependency = JobHandle.CombineDependencies(leftHandle, rightHandle);
            return new FMergeSort<T>(){ array = array, first = left, second = right }.Schedule(dependency);
        }
 
        public readonly struct FSortRange 
        {
            public readonly int left;
            public readonly int right;
 
            public FSortRange(int left, int right) 
            {
                this.left = left;
                this.right = right;
            }

            public int Max { get { return this.right; } }
            public int Length { get { return this.right - this.left + 1; } }
            public int Middle { get { return (this.left + this.right) >> 1; } }
        }
 
        [BurstCompile]
        public struct FMergeSort<T> : IJob where T : struct, IComparable<T> 
        {
            public FSortRange first;
            public FSortRange second;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<T> array;

            [BurstDiscard]
            void Compare(in T src, in T target, out bool state)
            {
                state = src.CompareTo(target) < 0;
            }

            public void Execute() 
            {
                int firstIndex = this.first.left;
                int secondIndex = this.second.left;
                int resultIndex = this.first.left;
 
                //Copy first
                NativeArray<T> copy = new NativeArray<T>(this.second.right - this.first.left + 1, Allocator.Temp);
                for (int i = this.first.left; i <= this.second.right; ++i) 
                {
                    int copyIndex = i - this.first.left; 
                    copy[copyIndex] = this.array[i];
                }
 
                while (firstIndex <= this.first.Max || secondIndex <= this.second.Max) 
                {
                    if (firstIndex <= this.first.Max && secondIndex <= this.second.Max) 
                    {
                        //both subranges still have elements
                        T firstValue = copy[firstIndex - this.first.left];
                        T secondValue = copy[secondIndex - this.first.left];

                        Compare(firstValue, secondValue, out bool state);
                        if (state) 
                        {
                            // first value is lesser
                            this.array[resultIndex] = firstValue;
                            ++firstIndex;
                            ++resultIndex;
                        } else {
                            this.array[resultIndex] = secondValue;
                            ++secondIndex;
                            ++resultIndex;
                        }
                    } else if (firstIndex <= this.first.Max) {
                        //Only the first range has remaining elements
                        T firstValue = copy[firstIndex - this.first.left];
                        this.array[resultIndex] = firstValue;
                        ++firstIndex;
                        ++resultIndex;
                    } else if (secondIndex <= this.second.Max) {
                        //Only the second range has remaining elements
                        T secondValue = copy[secondIndex - this.first.left];
                        this.array[resultIndex] = secondValue;
                        ++secondIndex;
                        ++resultIndex;
                    }
                }
 
                copy.Dispose();
            }
        }

        [BurstCompile]
        public struct FQuicksortJob<T> : IJob where T : struct, IComparable<T> 
        {
            public int left;
            public int right;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<T> array;
 
            public void Execute() 
            { 
                Quicksort(left, right);
            }

            [BurstDiscard]
            void CompareAdd(ref int index, in T target)
            {
                while (array[index].CompareTo(target) < 0)
                {
                    ++index;
                }
            }

            [BurstDiscard]
            void CompareSub(ref int index, in T target)
            {
                while (array[index].CompareTo(target) > 0)
                {
                    --index;
                }
            }

            void Quicksort(in int leftValue, in int rightValue) 
            {
                int i = leftValue;
                int j = rightValue;
                T pivot = array[(leftValue + rightValue) / 2];

                while (i <= j) 
                {
                    // Lesser
                    CompareAdd(ref i, pivot);

                    // Greater
                    CompareSub(ref j, pivot);

                    if (i <= j) 
                    {
                        // Swap
                        T temp = array[i];
                        array[i] = array[j];
                        array[j] = temp;
 
                        ++i;
                        --j;
                    }
                }
 
                // Recurse
                if (leftValue < j) 
                {
                    Quicksort(leftValue, j);
                }
 
                if (i < rightValue) 
                {
                    Quicksort(i, rightValue);
                }
            }
        }
    }
}
