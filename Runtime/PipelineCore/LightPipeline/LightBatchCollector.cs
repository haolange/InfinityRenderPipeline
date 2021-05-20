using UnityEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.LightPipeline
{
    public class FLightBatchCollector
    {
        public NativeHashMap<int, FLightBatch> CacheLightBatchStateBuckets;


        public FLightBatchCollector() 
        { 

        }

        public bool CollectorAvalible()
        {
            return CacheLightBatchStateBuckets.IsCreated;
        }

        public void AddLightBatch(in FLightBatch LightBatch, in int AddKey)
        {
            CacheLightBatchStateBuckets.Add(AddKey, LightBatch);
        }

        public void UpdateLightBatch(in FLightBatch LightBatch, in int UpdateKey)
        {
            CacheLightBatchStateBuckets[UpdateKey] = LightBatch;
        }

        public void RemoveLightBatch(in int RemoveKey)
        {
            CacheLightBatchStateBuckets.Remove(RemoveKey);
        }

        public void Reset()
        {
            CacheLightBatchStateBuckets.Clear();
        }

        public void Release()
        {
            CacheLightBatchStateBuckets.Dispose();
        }
    }
}
