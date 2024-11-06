using UnityEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;
using InfinityTech.Core.Container;
using System.Runtime.CompilerServices;

namespace InfinityTech.Rendering.LightPipeline
{
    public class LightElementCollector
    {
        public bool collectorAvalible
        {
            get
            {
                return cacheLightProxys.IsCreated;
            }
        }
        internal NativeParallelHashMap<int, LightElement> cacheLightProxys;

        public LightElementCollector() 
        {
            cacheLightProxys = new NativeParallelHashMap<int, LightElement>(64, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddLightElement(in LightElement lightElement, in int key)
        {
            cacheLightProxys.Add(key, lightElement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateLightElement(in LightElement lightElement, in int key)
        {
            cacheLightProxys[key] = lightElement;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveLightElement(in int key)
        {
            cacheLightProxys.Remove(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            cacheLightProxys.Clear();
        }

        public void Release()
        {
            cacheLightProxys.Dispose();
        }
    }
}
