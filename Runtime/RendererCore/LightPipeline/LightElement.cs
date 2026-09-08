using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
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

    public enum EShadowType
    {
        Hard = 0,
        PCF = 1
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
        public Vector4 attenuation;
        public int lightType;
        public int lightLayer;
        public int flags;
        public int shadowMatrixIndex;
        public int shadowSliceCount;
        public int shadowType;
        public int visibleLightIndex;
        public int padding;
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
                default:
                    throw new NotSupportedException($"Infinity does not implement light shape {unityType}.");
            }
        }

        public static FLightRecord FromUnityLight(Light light, LightComponent ext, ELightType type, int visibleIndex)
        {
            FLightRecord record = default;
            Color color = light.color;
            if (light.useColorTemperature)
                color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            record.radiance = Radiance(color, light.intensity);
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
                if (width <= 0 || height <= 0)
                    throw new ArgumentOutOfRangeException(nameof(light), "Rectangle lights require positive native areaSize.");

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

            float fade = 1.0f;
            float diffuse = 1.0f;
            float specular = 1.0f;
            float volIntensity = 1.0f;
            float volOcclusion = 1.0f;
            float maxDrawDistance = 128.0f;
            int flags = 0;
            ERenderingLayer layer = ERenderingLayer.LightLayerDefault;

            if (ext != null)
            {
                diffuse = ext.diffuse;
                specular = ext.specular;
                fade = ext.maxDrawDistanceFade;
                volIntensity = ext.volumetricIntensity;
                volOcclusion = ext.volumetricOcclusion;
                maxDrawDistance = ext.maxDrawDistance;
                layer = ext.lightLayer;
                if (ext.enableContactShadow)
                {
                    flags |= FLightRecordFlags.EnableContactShadow;
                }

                if (ext.enableVolumetric)
                {
                    flags |= FLightRecordFlags.EnableVolumetric;
                }

            }
            else
            {
                flags |= FLightRecordFlags.EnableVolumetric;
            }

            record.shape = new Vector4(type == ELightType.Rect ? width : innerCos, type == ELightType.Rect ? height : 0.0f, 0, 0);
            record.directionSpot.w = outerCos;
            record.axisX.w = diffuse;
            record.axisY.w = specular;
            record.attenuation = new Vector4(maxDrawDistance, fade, volIntensity, volOcclusion);
            record.lightLayer = (int)RenderingLayerUtility.Validate((uint)layer);
            record.flags = flags;
            record.shadowType = light.shadows == LightShadows.Hard ? (int)EShadowType.Hard : (int)EShadowType.PCF;
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

    }
}
