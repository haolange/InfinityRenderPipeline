using System.Runtime.InteropServices;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.LightPipeline
{
    public enum ELightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
        Rect = 3
    }

    public enum ELightState
    {
        Static = 0,
        Mixed = 1,
        Dynamic = 2
    }

    public enum ELightUnit
    {
        Lumen,
        Candela,
        Lux,
        Luminance,
        Ev100,
    }

    public enum EShadowResolution
    {
        X512 = 0,
        X1024 = 1,
        X2048 = 2,
        X4096 = 3,
        X8192 = 4
    }

    public enum EShadowType
    {
        Hard = 0,
        PCF = 1,
        PCSS = 2
    }

    public enum EShadowCascade
    {
        One = 0,
        Two = 1,
        Three = 2,
        Four = 3
    }

    public static class FLightRecordFlags
    {
        public const int EnableShadow = 1 << 0;
        public const int EnableContactShadow = 1 << 1;
        public const int EnableVolumetric = 1 << 2;
        public const int EnableIndirect = 1 << 3;
    }

    /// <summary>
    /// Packed visible-light record. Buffer convention: first <c>directionalCount</c>
    /// entries are directional; remaining <c>localCount</c> entries are Point/Spot/Rect
    /// in visibleLights pack order. ZBin compact indices are absolute record indices
    /// (local records start at directionalCount).
    /// radiance.rgb = Light.color.rgb * Light.intensity applied once on the CPU; radiance.a = 1.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FLightRecord
    {
        public Vector4 radiance;
        public Vector4 positionRange;
        public Vector4 directionSpot;
        public Vector4 shape;
        public Vector4 axisX;
        public Vector4 axisY;
        public Vector4 shadowAtlasRect;
        public Vector4 shadowSoftVol;
        public Vector4 extra;
        public int lightType;
        public int lightLayer;
        public int flags;
        public int shadowMatrixIndex;
        public int shadowSliceCount;
        public int shadowType;
        public int visibleLightIndex;
        public int unused0;
    }

    /// <summary>
    /// Local-light bounds uploaded as SRV_LightBoundsBuffer (one entry per local record, same order).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FLightBounds
    {
        public Vector4 centerRadius;
        public Vector4 zRange;
    }

    public static class FLightRecordPack
    {
        public static Vector4 Radiance(Color color, float intensity)
        {
            return new Vector4(color.r * intensity, color.g * intensity, color.b * intensity, 1.0f);
        }

        public static ELightType MapUnityType(LightType unityType)
        {
            switch (unityType)
            {
                case LightType.Directional:
                    return ELightType.Directional;
                case LightType.Point:
                    return ELightType.Point;
                case LightType.Spot:
                    return ELightType.Spot;
                case LightType.Rectangle:
                    return ELightType.Rect;
                case LightType.Disc:
                    return ELightType.Spot;
                default:
                    return ELightType.Point;
            }
        }

        public static FLightRecord FromUnityLight(Light light, LightComponent ext, ELightType type, int visibleIndex)
        {
            FLightRecord record = default;
            record.radiance = Radiance(light.color, light.intensity);
            record.visibleLightIndex = visibleIndex;
            record.lightType = (int)type;
            record.shadowMatrixIndex = -1;
            record.shadowSliceCount = 0;

            Transform transform = light.transform;
            Vector3 position = transform.position;
            Vector3 toLight = -transform.forward;
            record.positionRange = new Vector4(position.x, position.y, position.z, light.range);
            record.directionSpot = new Vector4(toLight.x, toLight.y, toLight.z, 0.0f);

            float innerCos = 1.0f;
            float outerCos = 0.0f;
            if (type == ELightType.Spot)
            {
                float outerRad = light.spotAngle * 0.5f * Mathf.Deg2Rad;
                float innerRad = light.innerSpotAngle * 0.5f * Mathf.Deg2Rad;
                outerCos = Mathf.Cos(outerRad);
                innerCos = Mathf.Cos(innerRad);
            }

            float width = 0.0f;
            float height = 0.0f;
            if (type == ELightType.Rect)
            {
                Vector2 area = light.areaSize;
                width = area.x;
                height = area.y;
                if (ext != null)
                {
                    if (width <= 0.0f)
                    {
                        width = ext.width;
                    }

                    if (height <= 0.0f)
                    {
                        height = ext.height;
                    }
                }

                Vector3 right = transform.right;
                Vector3 up = transform.up;
                record.axisX = new Vector4(right.x, right.y, right.z, 1.0f);
                record.axisY = new Vector4(up.x, up.y, up.z, 1.0f);
            }
            else
            {
                record.axisX = new Vector4(0, 0, 0, 1.0f);
                record.axisY = new Vector4(0, 0, 0, 1.0f);
            }

            float sourceRadius = 0.0f;
            float fade = 1.0f;
            float diffuse = 1.0f;
            float specular = 1.0f;
            float minSoft = 0.1f;
            float maxSoft = 1.0f;
            float volIntensity = 1.0f;
            float volOcclusion = 1.0f;
            float contactLength = 0.05f;
            float maxDrawDistance = 128.0f;
            float indirectIntensity = 1.0f;
            int flags = 0;
            ERenderingLayer layer = ERenderingLayer.LightLayerDefault;
            EShadowType shadowType = EShadowType.PCF;

            if (ext != null)
            {
                diffuse = ext.diffuse;
                specular = ext.specular;
                fade = ext.maxDrawDistanceFade;
                minSoft = ext.minSoftness;
                maxSoft = ext.maxSoftness;
                volIntensity = ext.volumetricIntensity;
                volOcclusion = ext.volumetricOcclusion;
                contactLength = ext.contactShadowLength;
                maxDrawDistance = ext.maxDrawDistance;
                indirectIntensity = ext.indirectIntensity;
                layer = ext.lightLayer;
                shadowType = ext.shadowType;
                if (ext.enableContactShadow)
                {
                    flags |= FLightRecordFlags.EnableContactShadow;
                }

                if (ext.enableVolumetric)
                {
                    flags |= FLightRecordFlags.EnableVolumetric;
                }

                if (ext.enableIndirect)
                {
                    flags |= FLightRecordFlags.EnableIndirect;
                }
            }
            else
            {
                flags |= FLightRecordFlags.EnableVolumetric | FLightRecordFlags.EnableIndirect;
                indirectIntensity = light.bounceIntensity;
            }

            record.shape = new Vector4(type == ELightType.Rect ? width : innerCos, type == ELightType.Rect ? height : 0.0f, sourceRadius, fade);
            record.directionSpot.w = outerCos;
            record.axisX.w = diffuse;
            record.axisY.w = specular;
            record.shadowSoftVol = new Vector4(minSoft, maxSoft, volIntensity, volOcclusion);
            record.extra = new Vector4(contactLength, maxDrawDistance, indirectIntensity, 0.0f);
            record.lightLayer = (int)layer;
            record.flags = flags;
            record.shadowType = (int)shadowType;
            record.unused0 = WantsShadow(light, ext, type) ? 1 : 0;
            return record;
        }

        public static FLightBounds LocalBounds(in FLightRecord record, Matrix4x4 worldToView)
        {
            Vector3 center = new Vector3(record.positionRange.x, record.positionRange.y, record.positionRange.z);
            float range = Mathf.Max(record.positionRange.w, 0.01f);
            Vector3 view = worldToView.MultiplyPoint3x4(center);
            float viewZ = -view.z;
            FLightBounds bounds;
            bounds.centerRadius = new Vector4(center.x, center.y, center.z, range);
            bounds.zRange = new Vector4(viewZ - range, viewZ + range, 0.0f, 0.0f);
            return bounds;
        }

        public static bool WantsShadow(Light light, LightComponent ext, ELightType type)
        {
            if (type == ELightType.Rect || light == null || light.shadows == LightShadows.None)
            {
                return false;
            }

            if (ext != null && !ext.enableShadow)
            {
                return false;
            }

            return true;
        }
    }
}
