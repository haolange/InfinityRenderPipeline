using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.LightPipeline
{
    // Per-allocation references into the existing MeshScene; no second transform/bounds registry.
    internal sealed class ShadowCasterBounds
    {
        readonly List<MeshInstanceId> m_Casters = new List<MeshInstanceId>(256);
        readonly HashSet<MeshInstanceId> m_Seen = new HashSet<MeshInstanceId>();
        MeshScene m_Scene;

        internal static bool ExcludesStatic(Light light)
        {
            LightBakingOutput baking = light.bakingOutput;
            return baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Mixed
                && baking.mixedLightingMode == MixedLightingMode.Shadowmask
                && QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask;
        }
        internal void Collect(MeshWorld world)
        {
            m_Scene = world.Scene;
            m_Casters.Clear();
            m_Seen.Clear();
            MeshPassDefinition pass = world.Registry.Get(MeshPassId.Shadow);
            var draws = m_Scene.GetDraws();
            for (int i = 0; i < m_Scene.DrawHighWater; ++i)
            {
                if (!m_Scene.IsDrawSlotLive(i)) continue;
                MeshDraw draw = draws[i];
                if ((draw.eligibility & pass.requiredEligibility) != pass.requiredEligibility
                    || draw.renderQueue < pass.renderQueueMin || draw.renderQueue > pass.renderQueueMax
                    || !m_Scene.IsSectionAlive(draw.section) || !m_Scene.IsMaterialAlive(draw.material)
                    || !m_Scene.TryGetInstance(draw.instance, out MeshInstanceRecord instance)) continue;
                if ((instance.flags & (EMeshInstanceFlags.CastShadow | EMeshInstanceFlags.Visible))
                    != (EMeshInstanceFlags.CastShadow | EMeshInstanceFlags.Visible)
                    || (pass.excludeCameraMotionOnly && instance.motionType == EMotionType.Camera)) continue;
                Material material = UnityEntityId.ToObject<Material>(draw.materialUnityId);
                if (material == null || !material.GetShaderPassEnabled(pass.lightModeTag)
                    || MeshPassShaderUtility.ResolvePassIndex(material, pass.lightModeTag, pass.shaderPassIndex) < 0) continue;
                Mesh mesh = UnityEntityId.ToObject<Mesh>(draw.meshUnityId);
                if (mesh == null || draw.sectionIndex < 0 || draw.sectionIndex >= mesh.subMeshCount) continue;
                if (m_Seen.Add(draw.instance)) m_Casters.Add(draw.instance);
            }
        }

        bool TryGetEligibleBounds(MeshInstanceId id, Light light, ulong sceneMask, uint renderingMask, out Bounds bounds)
        {
            bounds = default;
            if (!m_Scene.TryGetInstance(id, out MeshInstanceRecord instance)
                || (instance.layerMask & light.cullingMask) == 0
                || (instance.renderingLayerMask & renderingMask) == 0
                || (instance.sceneCullingMask & sceneMask) == 0) return false;
            bounds = new Bounds((Vector3)instance.worldBounds.center, (Vector3)(instance.worldBounds.extents * 2));
            return true;
        }

        internal bool TryGetBounds(Light light, Camera camera, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            ulong sceneMask = MeshView.SceneCullingMaskFor(camera);
            uint renderingMask = RenderingLayerUtility.Validate(unchecked((uint)light.renderingLayerMask));
            foreach (MeshInstanceId id in m_Casters)
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
                foreach (MeshInstanceId id in m_Casters)
                    if (TryGetEligibleBounds(id, light, sceneMask, renderingMask, out Bounds bounds)
                        && GeometryUtility.TestPlanesAABB(m_LocalPlanes, bounds)) return true;
            }
            return false;
        }
    }
}
