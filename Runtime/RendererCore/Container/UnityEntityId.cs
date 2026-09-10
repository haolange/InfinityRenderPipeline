using UnityEngine;

namespace InfinityTech.Core
{
    /// <summary>Preserve Unity's complete entity identity, including the generation bits.</summary>
    public static class UnityEntityId
    {
        public static ulong ToUInt64(Object obj) => ReferenceEquals(obj, null) ? 0ul : EntityId.ToULong(obj.GetEntityId());
        public static EntityId FromUInt64(ulong id) => EntityId.FromULong(id);
        public static T ToObject<T>(ulong id) where T : Object => Resources.EntityIdToObject(FromUInt64(id)) as T;
    }
}
