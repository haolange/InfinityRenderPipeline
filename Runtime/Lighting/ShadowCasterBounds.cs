using System.Collections.Generic;
using UnityEngine;
using InfinityTech.Core;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.LightPipeline
{
    // Shadow casters are the entities with members in the Shadow pass cache; no second transform, bounds or filter registry.
    // The caster index list follows the world's structural revision. Bounds and local-frustum answers also follow the
    // transform revision and the light, so a static world does not walk the entities again. A slot is addressed by
    // light id and scene mask. A slot that was not queried for more than one frame is dropped, so the live count stays
    // within the lights and scene masks actually queried.
    internal sealed class ShadowCasterBounds
    {
        readonly List<int> m_Casters = new List<int>(256);
        MeshWorld m_World;
        int m_Structural = int.MinValue;
        BoundsSlot[] m_Bounds = System.Array.Empty<BoundsSlot>();
        int m_BoundsCount;
        LocalSlot[] m_Local = System.Array.Empty<LocalSlot>();
        int m_LocalCount;
        SlotMap m_BoundsMap;
        SlotMap m_LocalMap;
        int m_Frame = int.MinValue;

        internal int BoundsSlotCount => m_BoundsCount;
        internal int LocalSlotCount => m_LocalCount;

        internal static bool ExcludesStatic(Light light)
        {
            LightBakingOutput baking = light.bakingOutput;
            return baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Mixed
                && baking.mixedLightingMode == MixedLightingMode.Shadowmask
                && QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask;
        }

        internal void Collect(MeshWorld world, int frame)
        {
            if (frame != m_Frame)
            {
                m_Frame = frame;
                CompactBounds(frame);
                CompactLocal(frame);
            }
            if (m_World == world && m_Structural == world.structuralRevision)
            {
                return;
            }
            m_World = world;
            m_Structural = world.structuralRevision;
            m_BoundsCount = 0;
            m_LocalCount = 0;
            m_BoundsMap.Clear();
            m_LocalMap.Clear();
            m_Casters.Clear();
            NativeArray<int> members = world.GetPassMembers(MeshPassTable.Shadow);
            NativeArray<MeshEntity> entities = world.entities;
            for (int entity = 0; entity < members.Length; ++entity)
            {
                if (members[entity] > 0 && (entities[entity].desc.flags & EMeshEntityFlags.Visible) != 0)
                {
                    m_Casters.Add(entity);
                }
            }
        }

        bool TryGetEligibleBounds(int entity, Light light, ulong sceneMask, uint renderingMask, out Bounds bounds)
        {
            bounds = default;
            MeshEntityDesc desc = m_World.entities[entity].desc;
            if ((desc.layerMask & light.cullingMask) == 0
                || (desc.renderingLayerMask & renderingMask) == 0
                || (desc.sceneCullingMask & sceneMask) == 0)
            {
                return false;
            }
            BoundingBox bound = m_World.entityBounds[entity];
            bounds = new Bounds((Vector3)bound.center, (Vector3)(bound.extents * 2));
            return true;
        }

        internal bool TryGetBounds(Light light, Camera camera, out Bounds bounds)
        {
            bounds = default;
            ulong sceneMask = CameraSceneCulling.Mask(camera);
            uint renderingMask = RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask));
            ulong lightId = UnityEntityId.ToUInt64(light);
            ulong transform = m_World.transformRevision;
            int slot = m_BoundsMap.Find(lightId, sceneMask);
            if (slot >= 0 && SameBounds(m_Bounds[slot], m_Structural, light, renderingMask, transform))
            {
                m_Bounds[slot].frame = m_Frame;
                bounds = m_Bounds[slot].bounds;
                return m_Bounds[slot].found;
            }
            bool found = false;
            foreach (int id in m_Casters)
            {
                if (!TryGetEligibleBounds(id, light, sceneMask, renderingMask, out Bounds item))
                {
                    continue;
                }
                if (found)
                {
                    bounds.Encapsulate(item);
                }
                else
                {
                    bounds = item;
                    found = true;
                }
            }
            var stored = new BoundsSlot
            {
                lightId = lightId, sceneMask = sceneMask, structural = m_Structural, transform = transform,
                cullingMask = light.cullingMask, renderingMask = renderingMask, found = found, bounds = bounds, frame = m_Frame
            };
            if (slot >= 0)
            {
                m_Bounds[slot] = stored;
            }
            else
            {
                Add(ref m_Bounds, ref m_BoundsCount, stored);
                m_BoundsMap.Insert(lightId, sceneMask, m_BoundsCount - 1);
            }
            return found;
        }

        readonly Plane[] m_LocalPlanes = new Plane[6];

        internal bool HasLocalCaster(in LightRecord record, Light light, Camera camera, int tileResolution, float guardTexels)
        {
            ulong sceneMask = CameraSceneCulling.Mask(camera);
            uint renderingMask = RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask));
            ulong lightId = UnityEntityId.ToUInt64(light);
            int slot = m_LocalMap.Find(lightId, sceneMask);
            if (slot >= 0 && SameLocal(m_Local[slot], record, light, renderingMask, tileResolution, guardTexels))
            {
                m_Local[slot].frame = m_Frame;
                return m_Local[slot].found;
            }
            bool found = false;
            int faces = LightRecordPack.LocalShadowSliceCount(record);
            for (int face = 0; face < faces && !found; ++face)
            {
                var frustum = ShadowAllocator.LocalShadowFrustum(record, light, face, tileResolution, guardTexels);
                if (!frustum.valid)
                {
                    continue;
                }
                GeometryUtility.CalculateFrustumPlanes(frustum.projection * frustum.view, m_LocalPlanes);
                foreach (int id in m_Casters)
                {
                    if (TryGetEligibleBounds(id, light, sceneMask, renderingMask, out Bounds bounds)
                        && GeometryUtility.TestPlanesAABB(m_LocalPlanes, bounds))
                    {
                        found = true;
                        break;
                    }
                }
            }
            var stored = new LocalSlot
            {
                lightId = lightId, sceneMask = sceneMask, structural = m_Structural, transform = m_World.transformRevision,
                cullingMask = light.cullingMask, renderingMask = renderingMask, tileResolution = tileResolution, guardTexels = guardTexels,
                position = record.positionRange, forward = record.forward, right = record.right, area = record.area, flags = record.flags,
                shadowNear = light.shadowNearPlane, spotAngle = light.spotAngle, found = found, frame = m_Frame
            };
            if (slot >= 0)
            {
                m_Local[slot] = stored;
            }
            else
            {
                Add(ref m_Local, ref m_LocalCount, stored);
                m_LocalMap.Insert(lightId, sceneMask, m_LocalCount - 1);
            }
            return found;
        }

        struct BoundsSlot
        {
            internal ulong lightId, sceneMask, transform;
            internal int structural, cullingMask;
            internal uint renderingMask;
            internal bool found;
            internal Bounds bounds;
            internal int frame;
        }

        struct LocalSlot
        {
            internal ulong lightId, sceneMask, transform;
            internal int structural, cullingMask, flags, tileResolution;
            internal uint renderingMask;
            internal float guardTexels, shadowNear, spotAngle;
            internal Vector4 position, forward, right, area;
            internal bool found;
            internal int frame;
        }

        static bool SameBounds(in BoundsSlot slot, int structural, Light light, uint renderingMask, ulong transform)
            => slot.structural == structural && slot.transform == transform && slot.cullingMask == light.cullingMask && slot.renderingMask == renderingMask;

        bool SameLocal(in LocalSlot slot, in LightRecord record, Light light, uint renderingMask, int tileResolution, float guardTexels)
            => slot.structural == m_Structural && slot.transform == m_World.transformRevision && slot.cullingMask == light.cullingMask
                && slot.renderingMask == renderingMask && slot.tileResolution == tileResolution && slot.guardTexels == guardTexels
                && slot.flags == record.flags && slot.shadowNear == light.shadowNearPlane && slot.spotAngle == light.spotAngle
                && slot.position == record.positionRange && slot.forward == record.forward && slot.right == record.right && slot.area == record.area;

        void CompactBounds(int frame)
        {
            int write = 0;
            for (int i = 0; i < m_BoundsCount; ++i)
            {
                if (frame - m_Bounds[i].frame <= 1)
                {
                    m_Bounds[write++] = m_Bounds[i];
                }
            }
            m_BoundsCount = write;
            m_BoundsMap.Clear();
            for (int i = 0; i < write; ++i)
            {
                m_BoundsMap.Insert(m_Bounds[i].lightId, m_Bounds[i].sceneMask, i);
            }
        }

        void CompactLocal(int frame)
        {
            int write = 0;
            for (int i = 0; i < m_LocalCount; ++i)
            {
                if (frame - m_Local[i].frame <= 1)
                {
                    m_Local[write++] = m_Local[i];
                }
            }
            m_LocalCount = write;
            m_LocalMap.Clear();
            for (int i = 0; i < write; ++i)
            {
                m_LocalMap.Insert(m_Local[i].lightId, m_Local[i].sceneMask, i);
            }
        }

        struct SlotMap
        {
            ulong[] m_Light;
            ulong[] m_Mask;
            int[] m_Slot;
            int m_Capacity;
            int m_Count;

            public int Find(ulong light, ulong mask)
            {
                if (m_Capacity == 0)
                {
                    return -1;
                }
                int index = Hash(light, mask) & (m_Capacity - 1);
                for (int n = 0; n < m_Capacity; ++n)
                {
                    int slot = m_Slot[index];
                    if (slot < 0)
                    {
                        return -1;
                    }
                    if (m_Light[index] == light && m_Mask[index] == mask)
                    {
                        return slot;
                    }
                    index = (index + 1) & (m_Capacity - 1);
                }
                return -1;
            }

            public void Insert(ulong light, ulong mask, int slot)
            {
                if (m_Count * 2 >= m_Capacity)
                {
                    Grow();
                }
                int index = Hash(light, mask) & (m_Capacity - 1);
                while (m_Slot[index] >= 0)
                {
                    index = (index + 1) & (m_Capacity - 1);
                }
                m_Light[index] = light;
                m_Mask[index] = mask;
                m_Slot[index] = slot;
                m_Count++;
            }

            public void Clear()
            {
                for (int i = 0; i < m_Capacity; ++i)
                {
                    m_Slot[i] = -1;
                }
                m_Count = 0;
            }

            void Grow()
            {
                int capacity = m_Capacity == 0 ? 4 : m_Capacity * 2;
                var light = new ulong[capacity];
                var mask = new ulong[capacity];
                var slot = new int[capacity];
                for (int i = 0; i < capacity; ++i)
                {
                    slot[i] = -1;
                }
                ulong[] oldLight = m_Light;
                ulong[] oldMask = m_Mask;
                int[] oldSlot = m_Slot;
                int oldCapacity = m_Capacity;
                m_Light = light;
                m_Mask = mask;
                m_Slot = slot;
                m_Capacity = capacity;
                m_Count = 0;
                for (int i = 0; i < oldCapacity; ++i)
                {
                    if (oldSlot[i] >= 0)
                    {
                        Insert(oldLight[i], oldMask[i], oldSlot[i]);
                    }
                }
            }

            static int Hash(ulong light, ulong mask)
            {
                ulong mixed = light * 0x9E3779B185EBCA87UL ^ (mask + 0xC2B2AE3D27D4EB4FUL);
                mixed ^= mixed >> 33;
                return (int)mixed;
            }
        }

        static void Add<T>(ref T[] slots, ref int count, T value)
        {
            if (count == slots.Length)
            {
                System.Array.Resize(ref slots, math.max(4, slots.Length * 2));
            }
            slots[count++] = value;
        }
    }
}
