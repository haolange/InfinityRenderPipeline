using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityTech.Core
{
    internal class HeapAllocator
    {
        internal string name;
        internal int BlockSize;
        internal int AllocatorSize;
        internal HeapBlock[] Blocks;

        internal HeapAllocator(string InName, int InBlockSize, int InAllocatorSize)
        {
            name = InName;
            BlockSize = InBlockSize;
            AllocatorSize = InAllocatorSize;

            Blocks = new HeapBlock[InAllocatorSize];
            int BlockStartIndex = 0;

            for(int i = 0; i < InAllocatorSize; ++i)
            {
                Blocks[i] = new HeapBlock(name + "_Block_" + i.ToString(), InBlockSize, BlockStartIndex);
                BlockStartIndex += InBlockSize;
            }
        }
    }
}
