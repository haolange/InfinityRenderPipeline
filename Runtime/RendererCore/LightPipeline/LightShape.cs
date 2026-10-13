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

    // Per-light shadow filter (Shaders/ShaderLibrary/ShadowSampling.hlsl FilterShadow). PCF and PCSS read the depth
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
    public struct FLightShape
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

        public static FLightShape Default(EInfinityLightType type)
        {
            return new FLightShape
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

    // Converts between Unity's native Light fields and FLightShape. Rendering resolves the shape once per light per
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

        // Shape of a light that has no authored Infinity shape: derived from Unity's native fields, so lights created
        // by scripts, imported or authored before 0.5.0 render with the shape Unity shows for them.
        public static FLightShape FromUnityLight(Light light)
        {
            if (light == null) throw new ArgumentNullException(nameof(light));
            Vector2 area = light.areaSize;
            FLightShape shape;
            switch (light.type)
            {
                case LightType.Directional:
                    shape = FLightShape.Default(EInfinityLightType.Directional);
                    shape.sourceAngle = Mathf.Max(0, light.shadowAngle);
                    break;
                case LightType.Tube:
                    shape = FLightShape.Default(EInfinityLightType.Point);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    shape.sourceLength = Mathf.Max(0, area.x);
                    break;
                case LightType.Spot:
                case LightType.Pyramid:
                case LightType.Box:
                    shape = FLightShape.Default(EInfinityLightType.Spot);
                    shape.spotShape = light.type == LightType.Spot ? ESpotShape.Cone : (light.type == LightType.Pyramid ? ESpotShape.Pyramid : ESpotShape.Box);
                    shape.sourceRadius = light.type == LightType.Box ? 0 : Mathf.Max(0, light.shapeRadius);
                    if (light.type == LightType.Pyramid) shape.pyramidAspect = area.x > 0 ? area.x : 1;
                    if (light.type == LightType.Box) shape.boxSize = new Vector2(Mathf.Max(area.x, 1e-3f), Mathf.Max(area.y, 1e-3f));
                    break;
                case LightType.Rectangle:
                    shape = FLightShape.Default(EInfinityLightType.Rect);
                    shape.sourceWidth = Mathf.Max(0, area.x);
                    shape.sourceHeight = Mathf.Max(0, area.y);
                    break;
                case LightType.Disc:
                    shape = FLightShape.Default(EInfinityLightType.Disk);
                    shape.sourceRadius = Mathf.Max(0, area.x);
                    break;
                default:
                    shape = FLightShape.Default(EInfinityLightType.Point);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    break;
            }
            return shape;
        }

        public static LightType NativeType(in FLightShape shape)
        {
            switch (shape.type)
            {
                case EInfinityLightType.Directional: return LightType.Directional;
                case EInfinityLightType.Point: return shape.sourceLength > 0 ? LightType.Tube : LightType.Point;
                case EInfinityLightType.Spot:
                    return shape.spotShape == ESpotShape.Pyramid ? LightType.Pyramid : (shape.spotShape == ESpotShape.Box ? LightType.Box : LightType.Spot);
                case EInfinityLightType.Rect: return LightType.Rectangle;
                case EInfinityLightType.Disk: return LightType.Disc;
                default: throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        // Writes the native fields Unity derives from the shape (type, shapeRadius, areaSize, shadowAngle). Returns
        // true when anything changed. Callers record Undo on the Light first.
        public static bool ApplyToUnityLight(Light light, in FLightShape shape)
        {
            if (light == null) throw new ArgumentNullException(nameof(light));
            bool changed = false;
            LightType type = NativeType(shape);
            if (light.type != type) { light.type = type; changed = true; }

            float shapeRadius = light.shapeRadius;
            Vector2 areaSize = light.areaSize;
            float shadowAngle = light.shadowAngle;
            switch (shape.type)
            {
                case EInfinityLightType.Directional:
                    shadowAngle = shape.sourceAngle;
                    break;
                case EInfinityLightType.Point:
                    shapeRadius = shape.sourceRadius;
                    if (shape.sourceLength > 0) areaSize = new Vector2(shape.sourceLength, areaSize.y);
                    break;
                case EInfinityLightType.Spot:
                    if (shape.spotShape != ESpotShape.Box) shapeRadius = shape.sourceRadius;
                    if (shape.spotShape == ESpotShape.Pyramid) areaSize = new Vector2(shape.pyramidAspect, 1);
                    if (shape.spotShape == ESpotShape.Box) areaSize = shape.boxSize;
                    break;
                case EInfinityLightType.Rect:
                    areaSize = new Vector2(shape.sourceWidth, shape.sourceHeight);
                    break;
                case EInfinityLightType.Disk:
                    areaSize = new Vector2(shape.sourceRadius, areaSize.y);
                    break;
            }

            if (!Mathf.Approximately(light.shapeRadius, shapeRadius)) { light.shapeRadius = shapeRadius; changed = true; }
            if (light.areaSize != areaSize) { light.areaSize = areaSize; changed = true; }
            if (!Mathf.Approximately(light.shadowAngle, shadowAngle)) { light.shadowAngle = shadowAngle; changed = true; }
            return changed;
        }

        // Unity's lightmapper has no tube or line emitter; like HDRP, those shapes are realtime only.
        public static bool IsBakeable(in FLightShape shape) => !shape.IsTube;

        // True when the shape describes an emitter with a positive area or a valid punctual setup.
        public static bool IsValid(in FLightShape shape)
        {
            switch (shape.type)
            {
                case EInfinityLightType.Rect: return shape.sourceWidth > 0 && shape.sourceHeight > 0;
                case EInfinityLightType.Disk: return shape.sourceRadius > 0;
                case EInfinityLightType.Spot: return shape.spotShape != ESpotShape.Box || (shape.boxSize.x > 0 && shape.boxSize.y > 0);
                default: return true;
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
