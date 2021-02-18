using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InfinityTech.Core
{
    internal class HeapBlock
    {
        internal string name;
        internal int BlockSize;
        internal int StartIndex;
        internal uint[] ElementState;

        internal HeapBlock(string InName, int InBlockSize, int InStartIndex)
        {
            name = InName;
            BlockSize = InBlockSize;
            StartIndex = InStartIndex;

            ElementState = new uint[InBlockSize];
        }

        internal bool GetContinueSpaceIndex(int InCount, out int StartIndex)
        {
            int FreeSpaceCount = 0;
            int StartSearchIndex = 0;

            for (int i = 0; i < ElementState.Length; ++i)
            {
                if (ElementState[i] == 0) {
                    FreeSpaceCount++;
                    if (FreeSpaceCount >= InCount) {
                        StartSearchIndex = i;
                        break;
                    }
                } else {
                    FreeSpaceCount = 0;
                }
            }

            bool bAvalibleSpace = FreeSpaceCount >= InCount;
            if (bAvalibleSpace) {
                StartIndex = StartSearchIndex - (InCount - 1);
            } else {
                StartIndex = -1;
            }
    
            return bAvalibleSpace;
        }
    }
}
