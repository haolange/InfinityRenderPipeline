using Unity.Collections;
using System.Collections.Generic;

namespace InfinityTech.Rendering.RDG
{
    internal class FRDGResourceScoper<Type> where Type : struct
    {
        internal NativeHashMap<int, Type> resourceMap;

        internal FRDGResourceScoper()
        {
            resourceMap = new NativeHashMap<int, Type>(64, Allocator.Persistent);
        }

        internal void Set(in int key, in Type value)
        {
            resourceMap.TryAdd(key, value);
        }

        internal Type Get(in int key)
        {
            Type output;
            resourceMap.TryGetValue(key, out output);
            return output;
        }

        internal void Clear()
        {
            resourceMap.Clear();
        }

        internal void Dispose()
        {
            resourceMap.Dispose();
        }
    }
}
