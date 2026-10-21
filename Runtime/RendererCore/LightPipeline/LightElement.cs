using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.LightPipeline
{
    public enum EShadowCascade
    {
        One = 0,
        Two = 1,
        Three = 2,
        Four = 3
    }

    // Mirrors the LIGHT_FLAG_* defines in Shaders/ShaderLibrary/Lighting/LightRecord.hlsl.
    public static class FLightRecordFlags
    {
        public const int EnableShadow = 1 << 0;
        public const int EnableContactShadow = 1 << 1;
        public const int EnableVolumetric = 1 << 2;
        public const int MixedShadowmask = 1 << 3;
        // Direct light is in the lightmaps; the realtime loop skips it.
        public const int Baked = 1 << 4;
        public const int Directional = 1 << 5;
        public const int SpotCone = 1 << 6;
        public const int SpotPyramid = 1 << 7;
        public const int SpotBox = 1 << 8;
        // One-sided surface emitter facing forward: rect when area.xy > 0, otherwise a disk of shape.x.
        public const int Planar = 1 << 9;
    }

    /// <summary>
    /// Packed visible-light record. The first <c>directionalCount</c> entries are directional, the remaining
    /// <c>localCount</c> entries are local lights in visibleLights order; light-list indices are absolute record
    /// indices. There is no shape enum: the shape is the combination of flags and shape parameters (see
    /// <see cref="FLightShape"/>).
    /// </summary>
    // GPU-RECORD: FLightRecord Shaders/ShaderLibrary/Lighting/LightRecord.hlsl
    [StructLayout(LayoutKind.Sequential)]
    public struct FLightRecord
    {
        // rgb = color x temperature x intensity x distance fade, applied once on the CPU; a = 1.
        public Vector4 radiance;
        // xyz = world position; w = range (0 for directional lights).
        public Vector4 positionRange;
        // xyz = emission axis (transform.forward); w = cos(outer spot half angle) for cones.
        public Vector4 forward;
        // xyz = transform.right; w = 1 / (cos inner - cos outer) for cones.
        public Vector4 right;
        // x = sourceRadius, y = softSourceRadius, z = sourceLength (along right), w = tan(sourceAngle / 2).
        public Vector4 shape;
        // Rect: x = width, y = height, z = cos(barn door angle), w = barn door length.
        // Pyramid: x, y = half-angle tangents. Box: x, y = size.
        public Vector4 area;
        // x = diffuse weight, y = specular weight, z = volumetric intensity, w = volumetric shadow weight.
        public Vector4 weights;
        // x = shadow strength, y = tan(softSourceAngle / 2), zw reserved.
        public Vector4 misc;
        public Vector4 shadowAtlasRect;
        public int lightLayer;
        public int flags;
        public int shadowMatrixIndex;
        public int shadowSliceCount;
        public int shadowFilter;
        public int visibleLightIndex;
        public int bakedOcclusionChannel;
        // -1 until the cookie / IES atlas lands (0.5.0 S11).
        public int cookieIndex;

        public bool IsDirectional => (flags & FLightRecordFlags.Directional) != 0;
    }

    public static class FLightRecordPack
    {
        public static Vector4 Radiance(Color color, float intensity)
        {
            return new Vector4(color.r * intensity, color.g * intensity, color.b * intensity, 1.0f);
        }

        // Fraction of a local light left after Maximum Distance fading (1 = full, 0 = culled).
        public static float DistanceFade(float distanceToCamera, float maxDrawDistance, float fadeLength)
        {
            if (maxDrawDistance <= 0) return 1;
            return Mathf.Clamp01((maxDrawDistance - distanceToCamera) / Mathf.Max(fadeLength, 1e-3f));
        }

        public static FLightShape ResolveShape(Light light, InfinityAdditionalLightData ext)
        {
            return ext != null ? ext.shape : LightShapeUtility.FromUnityLight(light);
        }

        // Returns false when the light contributes nothing this camera (invalid shape or faded out by distance).
        public static bool TryPack(Light light, InfinityAdditionalLightData ext, in FLightShape shape, int visibleIndex, Vector3 cameraPosition, out FLightRecord record)
        {
            record = default;
            if (!LightShapeUtility.IsValid(shape))
            {
                return false;
            }

            Transform transform = light.transform;
            Vector3 position = transform.position;
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            bool directional = shape.type == EInfinityLightType.Directional;

            float fade = 1;
            if (!directional && ext != null)
            {
                fade = DistanceFade(Vector3.Distance(cameraPosition, position), ext.maxDrawDistance, ext.maxDrawDistanceFade);
                if (fade <= 0) return false;
            }

            // Unity Light colors are authored in sRGB; the renderer and lightmapper consume linear radiance.
            Color color = light.color.linear;
            if (light.useColorTemperature)
                color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            record.radiance = Radiance(color, light.intensity * fade);
            record.visibleLightIndex = visibleIndex;
            record.shadowMatrixIndex = -1;
            record.shadowSliceCount = 0;
            record.cookieIndex = -1;
            record.positionRange = new Vector4(position.x, position.y, position.z, directional ? 0 : light.range);
            record.forward = new Vector4(forward.x, forward.y, forward.z, 0);
            record.right = new Vector4(right.x, right.y, right.z, 0);
            record.shape = new Vector4(shape.sourceRadius, shape.softSourceRadius, shape.sourceLength, Mathf.Tan(Mathf.Clamp(shape.sourceAngle, 0, 90) * 0.5f * Mathf.Deg2Rad));

            int flags = 0;
            switch (shape.type)
            {
                case EInfinityLightType.Directional:
                    flags |= FLightRecordFlags.Directional;
                    break;
                case EInfinityLightType.Spot:
                    if (shape.spotShape == ESpotShape.Cone)
                    {
                        flags |= FLightRecordFlags.SpotCone;
                        float outerCos = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                        float innerCos = Mathf.Cos(Mathf.Min(light.innerSpotAngle, light.spotAngle) * 0.5f * Mathf.Deg2Rad);
                        record.forward.w = outerCos;
                        record.right.w = 1.0f / Mathf.Max(innerCos - outerCos, 1e-4f);
                    }
                    else if (shape.spotShape == ESpotShape.Pyramid)
                    {
                        flags |= FLightRecordFlags.SpotPyramid;
                        Vector2 tangents = LightShapeUtility.PyramidTangents(light.spotAngle, shape.pyramidAspect);
                        record.area = new Vector4(tangents.x, tangents.y, 0, 0);
                    }
                    else
                    {
                        flags |= FLightRecordFlags.SpotBox;
                        record.area = new Vector4(shape.boxSize.x, shape.boxSize.y, 0, 0);
                    }
                    break;
                case EInfinityLightType.Rect:
                    flags |= FLightRecordFlags.Planar;
                    record.area = new Vector4(shape.sourceWidth, shape.sourceHeight,
                        Mathf.Cos(Mathf.Clamp(shape.barnDoorAngle, 0, 90) * Mathf.Deg2Rad), Mathf.Max(0, shape.barnDoorLength));
                    break;
                case EInfinityLightType.Disk:
                    flags |= FLightRecordFlags.Planar;
                    break;
            }

            float diffuse = 1.0f;
            float specular = 1.0f;
            float volIntensity = 1.0f;
            float volOcclusion = 1.0f;
            ERenderingLayer layer = ERenderingLayer.LightLayerDefault;
            if (ext != null)
            {
                diffuse = ext.diffuse;
                specular = ext.specular;
                volIntensity = ext.volumetricIntensity;
                volOcclusion = ext.volumetricOcclusion;
                layer = ext.lightLayer;
                if (ext.enableContactShadow) flags |= FLightRecordFlags.EnableContactShadow;
                if (ext.enableVolumetric) flags |= FLightRecordFlags.EnableVolumetric;
            }
            else
            {
                flags |= FLightRecordFlags.EnableVolumetric;
            }

            record.weights = new Vector4(diffuse, specular, volIntensity, volOcclusion);
            record.misc = new Vector4(Mathf.Clamp01(light.shadowStrength), Mathf.Tan(Mathf.Clamp(shape.softSourceAngle, 0, 90) * 0.5f * Mathf.Deg2Rad), 0, 0);
            record.lightLayer = (int)RenderingLayerUtility.Validate((uint)layer);

            var baking = light.bakingOutput;
            record.bakedOcclusionChannel = -1;
            if (baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Mixed && baking.mixedLightingMode == MixedLightingMode.Shadowmask)
            {
                flags |= FLightRecordFlags.MixedShadowmask;
                record.bakedOcclusionChannel = baking.occlusionMaskChannel;
            }
            if (baking.isBaked && baking.lightmapBakeType == LightmapBakeType.Baked) flags |= FLightRecordFlags.Baked;
            record.flags = flags;

            EShadowFilter filter = ext != null ? ext.shadowFilter : (light.shadows == LightShadows.Hard ? EShadowFilter.Hard : EShadowFilter.PCF);
            record.shadowFilter = (int)filter;
            return true;
        }

        // Shadow-map slices a local light needs: 6 cube faces for point / sphere / line / tube lights, one tile for
        // cone, pyramid and box spots, rects and disks; directional lights use the cascades.
        public static int LocalShadowSliceCount(in FLightRecord record)
        {
            int flags = record.flags;
            if ((flags & FLightRecordFlags.Directional) != 0)
                return 0;
            return (flags & (FLightRecordFlags.SpotCone | FLightRecordFlags.SpotPyramid | FLightRecordFlags.SpotBox | FLightRecordFlags.Planar)) != 0 ? 1 : 6;
        }

        public static bool IsConeSpot(in FLightRecord record) => (record.flags & FLightRecordFlags.SpotCone) != 0;
    }
}
