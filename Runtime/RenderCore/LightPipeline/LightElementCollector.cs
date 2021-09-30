using UnityEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.LightPipeline
{
    public class FLightElementCollector
    {
        public NativeHashMap<int, FLightElement> cacheLightBatchStateBuckets;


        public FLightElementCollector() 
        { 

        }

        public bool CollectorAvalible()
        {
            return cacheLightBatchStateBuckets.IsCreated;
        }

        public void AddLightBatch(in FLightElement lightElement, in int key)
        {
            cacheLightBatchStateBuckets.Add(key, lightElement);
        }

        public void UpdateLightBatch(in FLightElement lightElement, in int key)
        {
            cacheLightBatchStateBuckets[key] = lightElement;
        }

        public void RemoveLightBatch(in int removeKey)
        {
            cacheLightBatchStateBuckets.Remove(removeKey);
        }

        public void Reset()
        {
            cacheLightBatchStateBuckets.Clear();
        }

        public void Release()
        {
            cacheLightBatchStateBuckets.Dispose();
        }
    }
}
