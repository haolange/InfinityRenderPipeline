using System;
using UnityEngine;

namespace InfinityTech.Rendering.LightPipeline
{
    // The light types offered in the Infinity Light inspector (Unreal Engine form). Point covers punctual, sphere, line
    // and tube emitters; which one is decided by sourceRadius and sourceLength, not by a separate type.
    public enum EInfinityLightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
        Rect = 3,
        Disk = 4,
    }

    public enum ESpotShape
    {
        Cone = 0,
        Pyramid = 1,
        Box = 2,
    }

    // Per-light shadow filter (Shaders/Library/ShadowSampling.hlsl FilterShadow). PCF and PCSS read the depth
    // atlas; VSM and EVSM read the moment atlas that ShadowMomentPass builds for the slices that need it.
    // When a local light's shadow tiles are rendered. EveryFrame renders into the frame's shadow atlas; OnEnable and
    // OnDemand keep their tiles in the cached shadow atlas (static casters as of the last render) and copy them into
    // the frame atlas. OnEnable re-renders when the component is enabled or the light's shadow parameters change;
    // OnDemand re-renders on the first use and on InfinityAdditionalLightData.RequestShadowMapRendering().
    // Directional lights always render every frame.
    public enum EShadowUpdateMode
    {
        EveryFrame = 0,
        OnEnable = 1,
        OnDemand = 2,
    }

    public enum EShadowFilter
    {
        Hard = 0,
        PCF = 1,
        PCSS = 2,
        VSM = 3,
        EVSM = 4,
    }

    // Source shape of a light in Unreal Engine form. Lengths are meters, angles degrees.
    //   Point:       sourceRadius = 0 and sourceLength = 0 -> punctual; radius > 0 -> sphere; length > 0 -> line
    //                (radius 0) or tube (radius > 0). The tube axis is the light's local X (transform.right); its
    //                diameter is 2 * sourceRadius (there is no separate "thickness").
    //   Spot:        cone / pyramid / box; cone and pyramid may carry a spherical source (sourceRadius).
    //   Rect:        one-sided sourceWidth x sourceHeight emitter facing transform.forward, optional barn doors.
    //   Disk:        one-sided disk of sourceRadius facing transform.forward.
    //   Directional: sourceAngle is the angular diameter of the sun disk (Unreal default 0.5357 degrees).
    [Serializable]
    public struct LightSourceShape
    {
        public EInfinityLightType type;
        public ESpotShape spotShape;
        [Min(0), Tooltip("Radius of the spherical source (point, cone, pyramid), the tube radius, or the disk radius. Meters.")]
        public float sourceRadius;
        [Min(0), Tooltip("Widens specular and shadow penumbrae beyond sourceRadius. Meters.")]
        public float softSourceRadius;
        [Min(0), Tooltip("Point lights: length of the line or tube along the light's local X axis. Meters.")]
        public float sourceLength;
        [Min(0)] public float sourceWidth;
        [Min(0)] public float sourceHeight;
        [Range(0, 90), Tooltip("Rect barn door angle in degrees; 90 means no barn doors.")]
        public float barnDoorAngle;
        [Min(0)] public float barnDoorLength;
        [Range(0, 10), Tooltip("Angular diameter of the sun disk in degrees.")]
        public float sourceAngle;
        [Range(0, 10)] public float softSourceAngle;
        // Pyramid: width / height of the frustum cross-section. Box: emitter size in meters.
        [Min(0.01f)] public float pyramidAspect;
        public Vector2 boxSize;

        public const float DefaultSunAngle = 0.5357f;

        public static LightSourceShape Default(EInfinityLightType type)
        {
            return new LightSourceShape
            {
                type = type,
                spotShape = ESpotShape.Cone,
                sourceWidth = 1,
                sourceHeight = 1,
                barnDoorAngle = 90,
                sourceAngle = type == EInfinityLightType.Directional ? DefaultSunAngle : 0,
                pyramidAspect = 1,
                boxSize = Vector2.one,
            };
        }

        public bool IsTube => type == EInfinityLightType.Point && sourceLength > 0;
    }

    // Converts between Unity's native Light fields and LightSourceShape. Rendering resolves the shape once per light per
    // camera; Editor code writes the derived native fields after user edits (never on load).
    public static class LightShapeUtility
    {
        public static bool IsSupportedNativeType(LightType type)
        {
            switch (type)
            {
                case LightType.Directional:
                case LightType.Point:
                case LightType.Spot:
                case LightType.Rectangle:
                case LightType.Disc:
                case LightType.Pyramid:
                case LightType.Box:
                case LightType.Tube:
                    return true;
                default:
                    return false;
            }
        }

        // Shape of a light that has no authored Infinity shape, derived from Unity's native fields so lights created by
        // scripts or imported render with the shape Unity shows for them. Area size and sun angle exist only in the
        // Editor: a player build freezes each scene light's shape into its InfinityAdditionalLightData, and a light
        // created in a player without SetShape renders its type's default extent.
        public static LightSourceShape FromUnityLight(Light light)
        {
            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }
            LightSourceShape shape;
            switch (light.type)
            {
                case LightType.Directional:
                    shape = LightSourceShape.Default(EInfinityLightType.Directional);
                    break;
                case LightType.Tube:
                    shape = LightSourceShape.Default(EInfinityLightType.Point);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    break;
                case LightType.Spot:
                case LightType.Pyramid:
                case LightType.Box:
                    shape = LightSourceShape.Default(EInfinityLightType.Spot);
                    shape.spotShape = light.type == LightType.Spot ? ESpotShape.Cone : (light.type == LightType.Pyramid ? ESpotShape.Pyramid : ESpotShape.Box);
                    shape.sourceRadius = light.type == LightType.Box ? 0 : Mathf.Max(0, light.shapeRadius);
                    break;
                case LightType.Rectangle:
                    shape = LightSourceShape.Default(EInfinityLightType.Rect);
                    break;
                case LightType.Disc:
                    shape = LightSourceShape.Default(EInfinityLightType.Disk);
                    break;
                default:
                    shape = LightSourceShape.Default(EInfinityLightType.Point);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    break;
            }
#if UNITY_EDITOR
            ReadNativeExtent(light, ref shape);
#endif
            return shape;
        }

#if UNITY_EDITOR
        // The native extent fields only the Editor has (the lightmapper reads them): area size and sun angle.
        static void ReadNativeExtent(Light light, ref LightSourceShape shape)
        {
            Vector2 area = light.areaSize;
            switch (light.type)
            {
                case LightType.Directional:
                    shape.sourceAngle = Mathf.Max(0, light.shadowAngle);
                    break;
                case LightType.Tube:
                    shape.sourceLength = Mathf.Max(0, area.x);
                    break;
                case LightType.Pyramid:
                    shape.pyramidAspect = area.x > 0 ? area.x : 1;
                    break;
                case LightType.Box:
                    shape.boxSize = new Vector2(Mathf.Max(area.x, 1e-3f), Mathf.Max(area.y, 1e-3f));
                    break;
                case LightType.Rectangle:
                    shape.sourceWidth = Mathf.Max(0, area.x);
                    shape.sourceHeight = Mathf.Max(0, area.y);
                    break;
                case LightType.Disc:
                    shape.sourceRadius = Mathf.Max(0, area.x);
                    break;
            }
        }
#endif

        public static LightType NativeType(in LightSourceShape shape)
        {
            switch (shape.type)
            {
                case EInfinityLightType.Directional:
                    return LightType.Directional;
                case EInfinityLightType.Point:
                    return shape.sourceLength > 0 ? LightType.Tube : LightType.Point;
                case EInfinityLightType.Spot:
                    return shape.spotShape == ESpotShape.Pyramid ? LightType.Pyramid : (shape.spotShape == ESpotShape.Box ? LightType.Box : LightType.Spot);
                case EInfinityLightType.Rect:
                    return LightType.Rectangle;
                // Native Disc is omitted from realtime culling and coerces bake mode on Undo/reload.
                // Additional data retains Disk identity; the lightmapper receives DiscLight.
                case EInfinityLightType.Disk:
                    return LightType.Rectangle;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        // The native fields a player has (type, shape radius), derived from the shape; a field the shape does not own
        // keeps the light's current value.
        static void DeriveNativeType(Light light, in LightSourceShape shape, out LightType type, out float shapeRadius)
        {
            type = NativeType(shape);
            bool ownsRadius = shape.type == EInfinityLightType.Point || (shape.type == EInfinityLightType.Spot && shape.spotShape != ESpotShape.Box);
            shapeRadius = ownsRadius ? shape.sourceRadius : light.shapeRadius;
        }

#if UNITY_EDITOR
        // The native extent fields only the Editor has (area size, sun angle), derived from the shape; a field the shape
        // does not own keeps the light's current value.
        static void DeriveNativeExtent(Light light, in LightSourceShape shape, out Vector2 areaSize, out float shadowAngle)
        {
            areaSize = light.areaSize;
            shadowAngle = light.shadowAngle;
            switch (shape.type)
            {
                case EInfinityLightType.Directional:
                    shadowAngle = shape.sourceAngle;
                    break;
                case EInfinityLightType.Point:
                    if (shape.sourceLength > 0)
                    {
                        areaSize = new Vector2(shape.sourceLength, areaSize.y);
                    }
                    break;
                case EInfinityLightType.Spot:
                    if (shape.spotShape == ESpotShape.Pyramid)
                    {
                        areaSize = new Vector2(shape.pyramidAspect, 1);
                    }
                    if (shape.spotShape == ESpotShape.Box)
                    {
                        areaSize = shape.boxSize;
                    }
                    break;
                case EInfinityLightType.Rect:
                    areaSize = new Vector2(shape.sourceWidth, shape.sourceHeight);
                    break;
                case EInfinityLightType.Disk:
                    float diskSide = Mathf.Sqrt(Mathf.PI) * shape.sourceRadius;
                    areaSize = new Vector2(diskSide, diskSide);
                    break;
            }
        }

        // True when the light's native fields already match the shape (nothing for ApplyToUnityLight to write).
        public static bool IsInSync(Light light, in LightSourceShape shape)
        {
            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }
            DeriveNativeType(light, shape, out LightType type, out float shapeRadius);
            DeriveNativeExtent(light, shape, out Vector2 areaSize, out float shadowAngle);
            return light.type == type && Mathf.Approximately(light.shapeRadius, shapeRadius) && light.areaSize == areaSize
                && Mathf.Approximately(light.shadowAngle, shadowAngle);
        }
#endif

        // Writes the native fields Unity derives from the shape: type and shape radius everywhere, and in the Editor the
        // area size and sun angle the lightmapper reads. Returns true when anything changed. Editor callers record Undo
        // on the Light first.
        public static bool ApplyToUnityLight(Light light, in LightSourceShape shape)
        {
            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }
            DeriveNativeType(light, shape, out LightType type, out float shapeRadius);
#if UNITY_EDITOR
            DeriveNativeExtent(light, shape, out Vector2 areaSize, out float shadowAngle);
            // Native Disc assignment changes the bake mode; the shape does not own that setting.
            LightmapBakeType bakeType = light.lightmapBakeType;
#endif
            bool changed = false;
            if (light.type != type)
            {
                light.type = type;
                changed = true;
            }
            if (!Mathf.Approximately(light.shapeRadius, shapeRadius))
            {
                light.shapeRadius = shapeRadius;
                changed = true;
            }
#if UNITY_EDITOR
            if (light.areaSize != areaSize)
            {
                light.areaSize = areaSize;
                changed = true;
            }
            if (!Mathf.Approximately(light.shadowAngle, shadowAngle))
            {
                light.shadowAngle = shadowAngle;
                changed = true;
            }
            if (light.lightmapBakeType != bakeType)
            {
                light.lightmapBakeType = bakeType;
            }
            if (light.lightmapBakeType != bakeType)
            {
                throw new InvalidOperationException("Unity rejected the existing light bake mode after a shape edit.");
            }
#endif
            return changed;
        }

        // Unity's lightmapper has no tube or line emitter; like HDRP, those shapes are realtime only.
        public static bool IsBakeable(in LightSourceShape shape) => !shape.IsTube;

        // True when the shape describes an emitter with a positive area or a valid punctual setup.
        public static bool IsValid(in LightSourceShape shape)
        {
            switch (shape.type)
            {
                case EInfinityLightType.Rect:
                    return shape.sourceWidth > 0 && shape.sourceHeight > 0;
                case EInfinityLightType.Disk:
                    return shape.sourceRadius > 0;
                case EInfinityLightType.Spot:
                    return shape.spotShape != ESpotShape.Box || (shape.boxSize.x > 0 && shape.boxSize.y > 0);
                default:
                    return true;
            }
        }

        // Pyramid half-angle tangents (x, y) from the spot angle and aspect ratio; the spot angle spans the larger side.
        public static Vector2 PyramidTangents(float spotAngle, float aspect)
        {
            float tanHalf = Mathf.Tan(Mathf.Clamp(spotAngle, 1, 179) * 0.5f * Mathf.Deg2Rad);
            aspect = Mathf.Max(aspect, 1e-3f);
            return aspect >= 1 ? new Vector2(tanHalf, tanHalf / aspect) : new Vector2(tanHalf * aspect, tanHalf);
        }
    }
}
