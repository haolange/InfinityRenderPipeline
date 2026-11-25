using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.LightPipeline
{
    // Photometric unit conversion and physical edit-mode scaling anchored to canonical Light.intensity storage:
    //   Directional, SpotBox                     -> Lux
    //   Point, Sphere, Line, Tube, Cone, Pyramid -> Candela
    //   Rect, Disk, DiskCone                     -> Nits (cd / m^2)
    public static class LightIntensityUtility
    {
        const float MinPhotometricDenominator = 1e-12f;
        static readonly LightUnit[] s_LuxOnly = { LightUnit.Lux };
        static readonly LightUnit[] s_LumensCandelaLux = { LightUnit.Lumen, LightUnit.Candela, LightUnit.Lux };
        static readonly LightUnit[] s_LumensCandelaNitsLux = { LightUnit.Lumen, LightUnit.Candela, LightUnit.Nits, LightUnit.Lux };
        static readonly LightUnit[] s_LumensNits = { LightUnit.Lumen, LightUnit.Nits };

        public static LightUnit GetCanonicalUnit(EResolvedLightShape resolved)
        {
            switch (resolved)
            {
                case EResolvedLightShape.Directional:
                case EResolvedLightShape.SpotBox:
                    return LightUnit.Lux;
                case EResolvedLightShape.Rect:
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
                    return LightUnit.Nits;
                default:
                    return LightUnit.Candela;
            }
        }

        public static LightUnit GetCanonicalUnit(in LightSourceShape shape)
        {
            return LightShapeUtility.IsValidCombination(shape.kind, shape.emitter, shape.distribution)
                ? GetCanonicalUnit(LightShapeUtility.Resolve(shape))
                : LightUnit.Candela;
        }

        public static IReadOnlyList<LightUnit> GetSupportedUnits(in LightSourceShape shape)
        {
            if (!LightShapeUtility.IsValidCombination(shape.kind, shape.emitter, shape.distribution))
            {
                return s_LumensCandelaLux;
            }

            switch (LightShapeUtility.Resolve(shape))
            {
                case EResolvedLightShape.Directional:
                case EResolvedLightShape.SpotBox:
                    return s_LuxOnly;
                case EResolvedLightShape.Sphere:
                case EResolvedLightShape.Tube:
                    return s_LumensCandelaNitsLux;
                case EResolvedLightShape.Rect:
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
                    return s_LumensNits;
                default:
                    return s_LumensCandelaLux;
            }
        }

        public static bool IsUnitSupported(in LightSourceShape shape, LightUnit unit)
        {
            IReadOnlyList<LightUnit> supported = GetSupportedUnits(shape);
            for (int i = 0; i < supported.Count; ++i)
            {
                if (supported[i] == unit)
                {
                    return true;
                }
            }
            return false;
        }

        // Reads Light.lightUnit without mutating the Light or marking the asset/scene dirty.
        public static LightUnit ResolveDisplayUnit(Light light, in LightSourceShape shape)
        {
            if (light != null && IsUnitSupported(shape, light.lightUnit))
            {
                return light.lightUnit;
            }
            return GetCanonicalUnit(shape);
        }

        public static float GetEffectiveLuxDistance(Light light)
        {
            return light != null ? Mathf.Max(light.luxAtDistance, 0.01f) : 1.0f;
        }

        public static float GetEffectiveSpotAngle(Light light)
        {
            return light != null ? Mathf.Clamp(light.spotAngle, 1.0f, 179.0f) : 60.0f;
        }

        public static float GetEffectiveInnerSpotAngle(Light light, float outerSpotAngleDeg)
        {
            return light != null ? Mathf.Clamp(light.innerSpotAngle, 0.0f, outerSpotAngleDeg) : 0.0f;
        }

        // Effective solid angle of SpotWindow's quadratic transition between inner and outer cone half-angles.
        public static float GetSpotConeSolidAngle(float innerSpotAngleDeg, float outerSpotAngleDeg)
        {
            float clampedOuter = Mathf.Clamp(outerSpotAngleDeg, 0.0f, 180.0f);
            float clampedInner = Mathf.Clamp(innerSpotAngleDeg, 0.0f, clampedOuter);
            float cosOuter = Mathf.Cos(clampedOuter * 0.5f * Mathf.Deg2Rad);
            float cosInner = Mathf.Cos(clampedInner * 0.5f * Mathf.Deg2Rad);
            return 2.0f * Mathf.PI * ((1.0f - cosInner) + (cosInner - cosOuter) / 3.0f);
        }

        public static float GetPyramidSolidAngle(float spotAngleDeg, float pyramidAspect)
        {
            Vector2 tangents = LightShapeUtility.PyramidTangents(spotAngleDeg, pyramidAspect);
            float sinX = tangents.x / Mathf.Sqrt(1.0f + tangents.x * tangents.x);
            float sinY = tangents.y / Mathf.Sqrt(1.0f + tangents.y * tangents.y);
            return 4.0f * Mathf.Asin(Mathf.Clamp(sinX * sinY, 0.0f, 1.0f));
        }

        // Projected solid angle of a one-sided disk weighted by SpotWindow's quadratic cone attenuation.
        public static float GetDiskConeProjectedSolidAngle(float innerSpotAngleDeg, float outerSpotAngleDeg)
        {
            float clampedOuter = Mathf.Clamp(outerSpotAngleDeg, 0.0f, 180.0f);
            float clampedInner = Mathf.Clamp(innerSpotAngleDeg, 0.0f, clampedOuter);
            float cosOuter = Mathf.Cos(clampedOuter * 0.5f * Mathf.Deg2Rad);
            float cosInner = Mathf.Cos(clampedInner * 0.5f * Mathf.Deg2Rad);
            return 2.0f * Mathf.PI * (0.5f * (1.0f - cosInner * cosInner)
                + (cosInner - cosOuter) * (3.0f * cosInner + cosOuter) / 12.0f);
        }

        // Direction-averaged projected emitter area relating luminance (Nits) to radiant intensity (Candela) / flux:
        // Sphere: pi * r^2, Capsule/Tube: pi * r^2 + 0.5 * pi * r * L (Cauchy theorem: A_capsule / 4), Rect: W * H, Disk: pi * r^2.
        public static float GetSurfaceProjectedArea(in LightSourceShape shape)
        {
            if (!LightShapeUtility.IsValidCombination(shape.kind, shape.emitter, shape.distribution))
            {
                return 0.0f;
            }

            switch (LightShapeUtility.Resolve(shape))
            {
                case EResolvedLightShape.Sphere:
                {
                    float r = Mathf.Max(0.0f, shape.sourceRadius);
                    return Mathf.PI * r * r;
                }
                case EResolvedLightShape.Tube:
                {
                    float r = Mathf.Max(0.0f, shape.sourceRadius);
                    float len = Mathf.Max(0.0f, shape.sourceLength);
                    return Mathf.PI * r * r + 0.5f * Mathf.PI * r * len;
                }
                case EResolvedLightShape.Rect:
                    return Mathf.Max(0.0f, shape.sourceWidth) * Mathf.Max(0.0f, shape.sourceHeight);
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
                {
                    float r = Mathf.Max(0.0f, shape.sourceRadius);
                    return Mathf.PI * r * r;
                }
                default:
                    return 0.0f;
            }
        }

        public static float GetSolidAngle(in LightSourceShape shape, float innerSpotAngleDeg, float outerSpotAngleDeg)
        {
            if (!LightShapeUtility.IsValidCombination(shape.kind, shape.emitter, shape.distribution))
            {
                return 4.0f * Mathf.PI;
            }

            switch (LightShapeUtility.Resolve(shape))
            {
                case EResolvedLightShape.Point:
                case EResolvedLightShape.Sphere:
                case EResolvedLightShape.Line:
                case EResolvedLightShape.Tube:
                    return 4.0f * Mathf.PI;
                case EResolvedLightShape.SpotCone:
                    return GetSpotConeSolidAngle(innerSpotAngleDeg, outerSpotAngleDeg);
                case EResolvedLightShape.SpotPyramid:
                    return GetPyramidSolidAngle(outerSpotAngleDeg, shape.pyramidAspect);
                case EResolvedLightShape.Rect:
                case EResolvedLightShape.Disk:
                    return Mathf.PI;
                case EResolvedLightShape.DiskCone:
                    return GetDiskConeProjectedSolidAngle(innerSpotAngleDeg, outerSpotAngleDeg);
                default:
                    return 0.0f;
            }
        }

        public static bool TryGetLumenFactor(
            in LightSourceShape shape,
            float innerSpotAngleDeg,
            float outerSpotAngleDeg,
            out float lumensPerCanonicalUnit)
        {
            lumensPerCanonicalUnit = 0.0f;
            if (!LightShapeUtility.IsValidCombination(shape.kind, shape.emitter, shape.distribution))
            {
                return false;
            }

            EResolvedLightShape resolved = LightShapeUtility.Resolve(shape);
            LightUnit canonical = GetCanonicalUnit(resolved);
            if (canonical == LightUnit.Lux)
            {
                return false;
            }
            if (canonical == LightUnit.Candela)
            {
                lumensPerCanonicalUnit = GetSolidAngle(shape, innerSpotAngleDeg, outerSpotAngleDeg);
                return lumensPerCanonicalUnit > MinPhotometricDenominator;
            }

            // Planar emitters store Nits; total nominal flux is projectedArea * effectiveProjectedSolidAngle * Nits.
            lumensPerCanonicalUnit = GetSurfaceProjectedArea(shape) * GetSolidAngle(shape, innerSpotAngleDeg, outerSpotAngleDeg);
            return lumensPerCanonicalUnit > MinPhotometricDenominator;
        }

        public static float ToDisplayIntensity(
            Light light,
            in LightSourceShape shape,
            LightUnit targetUnit)
        {
            if (light == null)
            {
                return 0.0f;
            }
            float outerAngle = GetEffectiveSpotAngle(light);
            float innerAngle = GetEffectiveInnerSpotAngle(light, outerAngle);
            return ToDisplayIntensity(light.intensity, shape, targetUnit, innerAngle, outerAngle, GetEffectiveLuxDistance(light));
        }

        public static float ToDisplayIntensity(
            float canonicalIntensity,
            in LightSourceShape shape,
            LightUnit targetUnit,
            float innerSpotAngleDeg,
            float outerSpotAngleDeg,
            float luxAtDistance)
        {
            LightUnit canonical = GetCanonicalUnit(shape);
            if (targetUnit == canonical || !IsUnitSupported(shape, targetUnit))
            {
                return canonicalIntensity;
            }

            if (targetUnit == LightUnit.Lumen)
            {
                return TryGetLumenFactor(shape, innerSpotAngleDeg, outerSpotAngleDeg, out float factor)
                    ? canonicalIntensity * factor
                    : canonicalIntensity;
            }

            if (canonical == LightUnit.Candela)
            {
                if (targetUnit == LightUnit.Nits)
                {
                    float area = GetSurfaceProjectedArea(shape);
                    return area > MinPhotometricDenominator ? canonicalIntensity / area : canonicalIntensity;
                }
                if (targetUnit == LightUnit.Lux)
                {
                    float d = Mathf.Max(luxAtDistance, 0.01f);
                    return canonicalIntensity / (d * d);
                }
            }

            return canonicalIntensity;
        }

        public static float ToCanonicalIntensity(
            Light light,
            in LightSourceShape shape,
            LightUnit sourceUnit,
            float displayIntensity)
        {
            float outerAngle = GetEffectiveSpotAngle(light);
            float innerAngle = GetEffectiveInnerSpotAngle(light, outerAngle);
            return ToCanonicalIntensity(displayIntensity, shape, sourceUnit, innerAngle, outerAngle, GetEffectiveLuxDistance(light));
        }

        public static float ToCanonicalIntensity(
            float displayIntensity,
            in LightSourceShape shape,
            LightUnit sourceUnit,
            float innerSpotAngleDeg,
            float outerSpotAngleDeg,
            float luxAtDistance)
        {
            LightUnit canonical = GetCanonicalUnit(shape);
            if (sourceUnit == canonical)
            {
                return displayIntensity;
            }

            if (sourceUnit == LightUnit.Lumen)
            {
                return TryGetLumenFactor(shape, innerSpotAngleDeg, outerSpotAngleDeg, out float factor)
                    ? displayIntensity / factor
                    : displayIntensity;
            }

            if (canonical == LightUnit.Candela)
            {
                if (sourceUnit == LightUnit.Nits)
                {
                    float area = GetSurfaceProjectedArea(shape);
                    return area > MinPhotometricDenominator ? displayIntensity * area : displayIntensity;
                }
                if (sourceUnit == LightUnit.Lux)
                {
                    float d = Mathf.Max(luxAtDistance, 0.01f);
                    return displayIntensity * (d * d);
                }
            }

            // Convert cross-unit inputs cleanly if an importer passes e.g. Lux on a planar emitter without Lux UI support.
            if (canonical == LightUnit.Nits && sourceUnit == LightUnit.Candela)
            {
                float area = GetSurfaceProjectedArea(shape);
                return area > MinPhotometricDenominator ? displayIntensity / area : displayIntensity;
            }
            if (canonical == LightUnit.Nits && sourceUnit == LightUnit.Lux)
            {
                float d = Mathf.Max(luxAtDistance, 0.01f);
                float area = GetSurfaceProjectedArea(shape);
                return area > MinPhotometricDenominator ? (displayIntensity * d * d) / area : displayIntensity;
            }

            return displayIntensity;
        }

        public static float AdjustIntensityForShapeEdit(
            float previousCanonicalIntensity,
            in LightSourceShape previousShape,
            in LightSourceShape currentShape,
            float previousInnerAngleDeg,
            float previousOuterAngleDeg,
            float currentInnerAngleDeg,
            float currentOuterAngleDeg,
            ELightIntensityEditMode editMode)
        {
            if (editMode == ELightIntensityEditMode.Artistic || !float.IsFinite(previousCanonicalIntensity))
            {
                return previousCanonicalIntensity;
            }

            if (editMode == ELightIntensityEditMode.PreserveFlux)
            {
                if (TryGetLumenFactor(previousShape, previousInnerAngleDeg, previousOuterAngleDeg, out float prevFactor)
                    && TryGetLumenFactor(currentShape, currentInnerAngleDeg, currentOuterAngleDeg, out float currFactor))
                {
                    return ScaleFinite(previousCanonicalIntensity, prevFactor / currFactor);
                }
                return previousCanonicalIntensity;
            }

            // PhysicallyBased: preserve surface luminance when both emitter shapes have finite positive area;
            // keep on-axis candela when editing beam angles on Candela lights or transitioning out of zero area;
            // preserve total luminous flux across Candela <-> Nits family switches without surface area.
            float prevArea = GetSurfaceProjectedArea(previousShape);
            float currArea = GetSurfaceProjectedArea(currentShape);
            LightUnit prevCanonical = GetCanonicalUnit(previousShape);
            LightUnit currCanonical = GetCanonicalUnit(currentShape);

            if (prevArea > MinPhotometricDenominator && currArea > MinPhotometricDenominator)
            {
                float prevNits = prevCanonical == LightUnit.Candela ? previousCanonicalIntensity / prevArea : previousCanonicalIntensity;
                float result = currCanonical == LightUnit.Candela ? prevNits * currArea : prevNits;
                return float.IsFinite(result) ? result : previousCanonicalIntensity;
            }

            if (prevCanonical == currCanonical)
            {
                return previousCanonicalIntensity;
            }

            if (TryGetLumenFactor(previousShape, previousInnerAngleDeg, previousOuterAngleDeg, out float oldLumenFactor)
                && TryGetLumenFactor(currentShape, currentInnerAngleDeg, currentOuterAngleDeg, out float newLumenFactor))
            {
                return ScaleFinite(previousCanonicalIntensity, oldLumenFactor / newLumenFactor);
            }

            return previousCanonicalIntensity;
        }

        static float ScaleFinite(float value, float ratio)
        {
            float scaled = value * ratio;
            return float.IsFinite(scaled) ? scaled : value;
        }
    }
}
