using System;
using InfinityTech.Core;
using InfinityTech.Rendering.Spatial;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.AnalyticShadow
{
    /// <summary>
    /// Analytic primitive occluder participating in indirect hemisphere occlusion and direct soft shadowing.
    /// Registered via <see cref="s_Registry"/> and consumed by <see cref="AnalyticShadowContext"/>.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("InfinityRenderer/Analytic Occluder")]
    public sealed class InfinityAnalyticOccluder : MonoBehaviour
    {
        internal static readonly ComponentRegistry<InfinityAnalyticOccluder> s_Registry =
            new ComponentRegistry<InfinityAnalyticOccluder>();

        [SerializeField] ESpatialVolumeKind m_Shape = ESpatialVolumeKind.Sphere;
        [SerializeField] Vector3 m_CenterOffset = Vector3.zero;
        [SerializeField] Vector3 m_HalfExtents = new Vector3(0.5f, 1.0f, 0.5f);
        [Min(0.01f)]
        [SerializeField] float m_Radius = 0.5f;

        [SerializeField] bool m_CastIndirectOcclusion = true;
        [Range(0.0f, 1.0f)]
        [SerializeField] float m_IndirectStrength = 1.0f;
        [Min(0.05f)]
        [SerializeField] float m_IndirectFalloffDistance = 3.0f;

        [SerializeField] bool m_CastDirectShadow = true;
        [Range(0.0f, 1.0f)]
        [SerializeField] float m_DirectStrength = 1.0f;
        [Range(0.01f, 1.0f)]
        [SerializeField] float m_DirectSoftness = 0.2f;
        [Min(0.1f)]
        [SerializeField] float m_DirectMaxDistance = 12.0f;

        [SerializeField] ERenderingLayer m_RenderingLayerMask = ERenderingLayer.Everything;

        ulong m_RegistryId;
        bool m_Registered;

        public ESpatialVolumeKind shape
        {
            get => m_Shape;
            set => m_Shape = value == ESpatialVolumeKind.RoundedBox ? ESpatialVolumeKind.Box : value;
        }

        public Vector3 centerOffset
        {
            get => m_CenterOffset;
            set => m_CenterOffset = value;
        }

        public Vector3 halfExtents
        {
            get => m_HalfExtents;
            set => m_HalfExtents = value;
        }

        public float radius
        {
            get => m_Radius;
            set => m_Radius = Mathf.Max(value, 0.01f);
        }

        public bool castIndirectOcclusion
        {
            get => m_CastIndirectOcclusion;
            set => m_CastIndirectOcclusion = value;
        }

        public float indirectStrength
        {
            get => m_IndirectStrength;
            set => m_IndirectStrength = Mathf.Clamp01(value);
        }

        public float indirectFalloffDistance
        {
            get => m_IndirectFalloffDistance;
            set => m_IndirectFalloffDistance = Mathf.Max(value, 0.05f);
        }

        public bool castDirectShadow
        {
            get => m_CastDirectShadow;
            set => m_CastDirectShadow = value;
        }

        public float directStrength
        {
            get => m_DirectStrength;
            set => m_DirectStrength = Mathf.Clamp01(value);
        }

        public float directSoftness
        {
            get => m_DirectSoftness;
            set => m_DirectSoftness = Mathf.Clamp(value, 0.01f, 1.0f);
        }

        public float directMaxDistance
        {
            get => m_DirectMaxDistance;
            set => m_DirectMaxDistance = Mathf.Max(value, 0.1f);
        }

        public ERenderingLayer renderingLayerMask
        {
            get => m_RenderingLayerMask;
            set => m_RenderingLayerMask = value;
        }

        void OnEnable()
        {
            if (!m_Registered)
            {
                m_Registered = true;
                m_RegistryId = UnityEntityId.ToUInt64(this);
                s_Registry.Register(m_RegistryId, this);
            }
        }

        void OnDisable()
        {
            if (m_Registered)
            {
                m_Registered = false;
                s_Registry.Unregister(m_RegistryId);
            }
        }

        void OnValidate()
        {
            m_Radius = Mathf.Max(m_Radius, 0.01f);
            m_HalfExtents = Vector3.Max(m_HalfExtents, new Vector3(0.01f, 0.01f, 0.01f));
            m_IndirectStrength = Mathf.Clamp01(m_IndirectStrength);
            m_IndirectFalloffDistance = Mathf.Max(m_IndirectFalloffDistance, 0.05f);
            m_DirectStrength = Mathf.Clamp01(m_DirectStrength);
            m_DirectSoftness = Mathf.Clamp(m_DirectSoftness, 0.01f, 1.0f);
            m_DirectMaxDistance = Mathf.Max(m_DirectMaxDistance, 0.1f);
            if (m_Shape == ESpatialVolumeKind.RoundedBox)
            {
                m_Shape = ESpatialVolumeKind.Box;
            }
        }

        public SpatialVolume ComputeSpatialVolume()
        {
            Transform t = transform;
            float3 scale = math.max(math.abs((float3)t.lossyScale), new float3(1e-3f));
            float3 worldCenter = (float3)t.TransformPoint(m_CenterOffset);
            quaternion worldRot = t.rotation;
            return BuildSpatialVolume(m_Shape, worldCenter, worldRot, (float3)m_HalfExtents * scale, m_Radius, scale);
        }

        internal static SpatialVolume BuildSpatialVolume(ESpatialVolumeKind kind, float3 centerWS, quaternion rotationWS,
            float3 scaledExtents, float baseRadius, float3 scale)
        {
            switch (kind)
            {
                case ESpatialVolumeKind.Sphere:
                    return SpatialVolume.CreateSphere(centerWS, math.max(baseRadius * math.cmax(scale), 1e-3f));
                case ESpatialVolumeKind.Ellipsoid:
                    return SpatialVolume.CreateEllipsoid(centerWS, rotationWS, math.max(scaledExtents, new float3(1e-3f)));
                case ESpatialVolumeKind.Capsule:
                    return SpatialVolume.CreateCapsule(centerWS, rotationWS, math.max(scaledExtents.y, 0.0f),
                        math.max(baseRadius * math.max(scale.x, scale.z), 1e-3f));
                default:
                    return SpatialVolume.CreateBox(centerWS, rotationWS, math.max(scaledExtents, new float3(1e-3f)));
            }
        }

        public SpatialBounds ComputeGeometryBounds()
        {
            return ComputeSpatialVolume().ComputeWorldBounds();
        }

        public SpatialBounds ComputeIndirectInfluenceBounds()
        {
            SpatialBounds geometry = ComputeGeometryBounds();
            return ExpandForIndirectOcclusion(in geometry, m_IndirectFalloffDistance);
        }

        internal static SpatialBounds ExpandForIndirectOcclusion(in SpatialBounds geometryBounds, float falloffDistance)
        {
            float3 pad = new float3(math.max(falloffDistance, 0.05f));
            return new SpatialBounds(geometryBounds.center, geometryBounds.halfExtents + pad);
        }

        /// <summary>
        /// Computes the world-space shadow-receiver influence bounds extruded away from a light source.
        /// Direct shadow influence extends far beyond geometry bounds along the light ray (PRD §8.4).
        /// </summary>
        internal static SpatialBounds ComputeDirectShadowBounds(in SpatialBounds geometryBounds, float3 toLightDirectionWS,
            float softness, float maxShadowDistance)
        {
            float safeDist = math.max(maxShadowDistance, 0.1f);
            float3 shadowDir = -math.normalizesafe(toLightDirectionWS, new float3(0.0f, 1.0f, 0.0f));
            float3 penumbraPad = new float3(safeDist * math.clamp(softness, 0.01f, 1.0f));

            float3 geomMin = geometryBounds.center - geometryBounds.halfExtents;
            float3 geomMax = geometryBounds.center + geometryBounds.halfExtents;
            float3 farCenter = geometryBounds.center + shadowDir * safeDist;
            float3 farExtents = geometryBounds.halfExtents + penumbraPad;
            float3 farMin = farCenter - farExtents;
            float3 farMax = farCenter + farExtents;

            return SpatialBounds.FromMinMax(math.min(geomMin, farMin), math.max(geomMax, farMax));
        }

        internal static SpatialBounds ComputeDirectShadowBoundsForPointLight(in SpatialBounds geometryBounds, float3 lightPosWS,
            float lightRange, float softness, float maxShadowDistance, out bool reachesLight)
        {
            float3 delta = lightPosWS - geometryBounds.center;
            float dist = math.length(delta);
            float radius = geometryBounds.BoundingRadius();
            if (dist > lightRange + radius || dist < 1e-4f)
            {
                reachesLight = dist < 1e-4f;
                return geometryBounds;
            }
            reachesLight = true;
            float remainingRange = math.max(lightRange - math.max(dist - radius, 0.0f), 0.1f);
            float shadowDist = math.min(maxShadowDistance, remainingRange);
            return ComputeDirectShadowBounds(in geometryBounds, delta / dist, softness, shadowDist);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            SpatialBounds geom = ComputeGeometryBounds();
            Gizmos.color = new Color(0.25f, 0.85f, 0.95f, 0.8f);
            Gizmos.DrawWireCube(geom.center, geom.halfExtents * 2.0f);
            if (m_CastIndirectOcclusion)
            {
                SpatialBounds indirect = ExpandForIndirectOcclusion(in geom, m_IndirectFalloffDistance);
                Gizmos.color = new Color(0.95f, 0.75f, 0.25f, 0.45f);
                Gizmos.DrawWireCube(indirect.center, indirect.halfExtents * 2.0f);
            }
        }
#endif
    }
}
