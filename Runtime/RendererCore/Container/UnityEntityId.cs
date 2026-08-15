using UnityEngine;

namespace InfinityTech.Core
{
    /// <summary>
    /// Unity 6.5 treats EntityId &lt;-&gt; int implicit casts as errors.
    /// Mesh/light IDs stay int for Burst records; this is the supported raw-bit bridge.
    /// </summary>
    public static class UnityEntityId
    {
        public static int ToInt32(Object obj)
        {
            return ReferenceEquals(obj, null) ? 0 : ToInt32(obj.GetEntityId());
        }

        public static int ToInt32(EntityId entityId)
        {
            return unchecked((int)EntityId.ToULong(entityId));
        }

        public static ulong ToUInt64(Object obj)
        {
            return ReferenceEquals(obj, null) ? 0ul : EntityId.ToULong(obj.GetEntityId());
        }

        public static EntityId FromInt32(int id)
        {
            return EntityId.FromULong(unchecked((ulong)(uint)id));
        }

        public static T ToObject<T>(int id) where T : Object
        {
            return Resources.EntityIdToObject(FromInt32(id)) as T;
        }
    }
}
