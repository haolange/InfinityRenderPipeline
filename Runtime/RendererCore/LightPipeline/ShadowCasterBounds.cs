using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.LightPipeline
{
    // Shadow casters are the entities with members in the Shadow pass cache; no second transform, bounds or filter registry.
    internal sealed class ShadowCasterBounds
    {
        readonly List<int> m_Casters = new List<int>(256);
        MeshWorld m_World;

        internal static bool ExcludesStatic(Light light)
        {
            LightBakingOutput baking = light.bakingOutput;
            return baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Mixed
                && baking.mixedLightingMode == MixedLightingMode.Shadowmask
                && QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask;
        }

        internal void Collect(MeshWorld world)
        {
            m_World = world;
            m_Casters.Clear();
            NativeArray<int> members = world.GetPassMembers(MeshPassPresets.Shadow);
            NativeArray<MeshEntity> entities = world.entities;
            for (int entity = 0; entity < members.Length; ++entity)
                if (members[entity] > 0 && (entities[entity].desc.flags & EMeshEntityFlags.Visible) != 0) m_Casters.Add(entity);
        }

        bool TryGetEligibleBounds(int entity, Light light, ulong sceneMask, uint renderingMask, out Bounds bounds)
        {
            bounds = default;
            MeshEntityDesc desc = m_World.entities[entity].desc;
            if ((desc.layerMask & light.cullingMask) == 0
                || (desc.renderingLayerMask & renderingMask) == 0
                || (desc.sceneCullingMask & sceneMask) == 0) return false;
            FBound bound = m_World.entityBounds[entity];
            bounds = new Bounds((Vector3)bound.center, (Vector3)(bound.extents * 2));
            return true;
        }

        internal bool TryGetBounds(Light light, Camera camera, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            ulong sceneMask = MeshView.SceneCullingMaskFor(camera);
            uint renderingMask = RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask));
            foreach (int id in m_Casters)
            {
                if (!TryGetEligibleBounds(id, light, sceneMask, renderingMask, out Bounds item)) continue;
                if (found) bounds.Encapsulate(item);
                else { bounds = item; found = true; }
            }
            return found;
        }

        readonly Plane[] m_LocalPlanes = new Plane[6];

        internal bool HasLocalCaster(in FLightRecord record, Light light, Camera camera, int tileResolution, float guardTexels)
        {
            ulong sceneMask = MeshView.SceneCullingMaskFor(camera);
            uint renderingMask = RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask));
            int faces = FLightRecordPack.LocalShadowSliceCount(record);
            for (int face = 0; face < faces; ++face)
            {
                var frustum = ShadowAllocator.LocalShadowFrustum(record, light, face, tileResolution, guardTexels);
                if (!frustum.valid) continue;
                GeometryUtility.CalculateFrustumPlanes(frustum.projection * frustum.view, m_LocalPlanes);
                foreach (int id in m_Casters)
                    if (TryGetEligibleBounds(id, light, sceneMask, renderingMask, out Bounds bounds)
                        && GeometryUtility.TestPlanesAABB(m_LocalPlanes, bounds)) return true;
            }
            return false;
        }
    }
}
