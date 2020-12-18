using System.Collections.Generic;

namespace InfinityTech.Runtime.Rendering.RDG
{
    internal class RDGResourceScope<Type> where Type : struct
    {
        internal Dictionary<int, Type> ResourceMap;

        internal RDGResourceScope()
        {
            ResourceMap = new Dictionary<int, Type>(64);
        }

        internal void Set(int InKey, Type InValue)
        {
            ResourceMap.Add(InKey, InValue);
        }

        internal Type Get(int InKey)
        {
            Type OutHandle;
            ResourceMap.TryGetValue(InKey, out OutHandle);
            return OutHandle;
        }

        internal void ClearScope()
        {
            ResourceMap.Clear();
        }
    }
}
