using UnityEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;
using InfinityTech.Core.Container;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.LightPipeline
{
    public class FLightElementCollector
    {
        public bool collectorAvalible
        {
            get
            {
                return cacheLightElements.IsCreated;
            }
        }

        public TNativeSparseArray<FLightElement> cacheLightElements;

        public FLightElementCollector() 
        { 

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddLightElement(in FLightElement lightElement)
        {
            return cacheLightElements.Add(lightElement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateLightElement(in FLightElement lightElement, in int key)
        {
            cacheLightElements[key] = lightElement;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveLightElement(in int key)
        {
            cacheLightElements.Remove(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            
        }

        public void Release()
        {
            cacheLightElements.Dispose();
        }
    }
}
