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
                return cacheLightProxys.IsCreated;
            }
        }
        internal NativeHashMap<int, FLightElement> cacheLightProxys;

        public FLightElementCollector() 
        {
            cacheLightProxys = new NativeHashMap<int, FLightElement>(64, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddLightElement(in FLightElement lightElement, in int key)
        {
            cacheLightProxys.Add(key, lightElement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateLightElement(in FLightElement lightElement, in int key)
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
