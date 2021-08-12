using System.Collections.Generic;

namespace InfinityTech.Rendering.RDG
{
    internal class RDGResourceScope<Type> where Type : struct
    {
        internal Dictionary<int, Type> resourceMap;

        internal RDGResourceScope()
        {
            resourceMap = new Dictionary<int, Type>(64);
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

        internal void ClearScope()
        {
            resourceMap.Clear();
        }
    }
}
