using System.Collections.Generic;

namespace InfinityTech.Core.Container
{
    public static class ListUtility
    {
        public static void AddUnique<T>(this List<T> list, T item)
        {
            bool IsUnique = true;

            for (int i = 0; i < list.Count; ++i)
            {
                if (item.Equals(list[i]))
                {
                    IsUnique = false;
                    break;
                }
            }

            if (IsUnique)
            {
                list.Add(item);
            }
        }
    }
}
