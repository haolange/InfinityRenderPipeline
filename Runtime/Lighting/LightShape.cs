using System;
using Unity.Mathematics;
using UnityEngine;

namespace InfinityTech.Rendering.LightPipeline
{
    // Top-level light category in InfinityRP.
    public enum ELightKind
    {
        Directional = 0,
        Local = 1,
    }

    // Physical emitter geometry of a local light.
    // Round derives punctual Point (radius == 0, length == 0), Sphere (radius > 0, length == 0),
    // Line (radius == 0, length > 0 along local X), or Tube (radius > 0, length > 0 along local X).
    public enum ELocalLightEmitter
    {
        Round = 0,
        Rectangle = 1,
        Disk = 2,
    }

    // Angular/spatial light distribution of a local light.
    public enum ELocalLightDistribution
    {
        Omnidirectional = 0,
        Hemisphere = 1,
        Cone = 2,
        Pyramid = 3,
        ParallelBox = 4,
    }

    // Effective emitter + distribution shape resolved from (kind, emitter, distribution, sourceRadius, sourceLength).
    public enum EResolvedLightShape
    {
        Directional = 0,
        Point = 1,
        Sphere = 2,
        Line = 3,
        Tube = 4,
        SpotCone = 5,
        SpotPyramid = 6,
        SpotBox = 7,
        Rect = 8,
        Disk = 9,
        DiskCone = 10,
    }

    // Controls how changing emitter size or beam angle interacts with stored light intensity.
    public enum ELightIntensityEditMode
    {
        PhysicallyBased = 0,
        PreserveFlux = 1,
        Artistic = 2,
    }

    public readonly struct LightCapabilities
    {
        public readonly EResolvedLightShape resolvedShape;
        public readonly bool validCombination;
        public readonly bool realtimeShading;
        public readonly bool realtimeShadow;
        public readonly bool baking;
        public readonly string bakeDisabledReason;
        public readonly bool hasSunAngle;
        public readonly bool hasSourceRadius;
        public readonly bool hasSourceLength;
        public readonly bool hasRectSize;
        public readonly bool hasBarnDoors;
        public readonly bool hasSpotAngle;
        public readonly bool hasInnerSpotAngle;
        public readonly bool hasPyramidAspect;
        public readonly bool hasBoxSize;
        public readonly bool isPlanar;
        public readonly bool supportsCubeCookie;
        public readonly bool supportsPlanarCookie;
        public readonly bool supports2DIes;

        public LightCapabilities(
            EResolvedLightShape resolvedShape,
            bool validCombination,
            bool realtimeShading,
            bool realtimeShadow,
            bool baking,
            string bakeDisabledReason,
            bool hasSunAngle = false,
            bool hasSourceRadius = false,
            bool hasSourceLength = false,
            bool hasRectSize = false,
            bool hasBarnDoors = false,
            bool hasSpotAngle = false,
            bool hasInnerSpotAngle = false,
            bool hasPyramidAspect = false,
            bool hasBoxSize = false,
            bool isPlanar = false,
            bool supportsCubeCookie = false,
            bool supportsPlanarCookie = false,
            bool supports2DIes = false)
        {
            this.resolvedShape = resolvedShape;
            this.validCombination = validCombination;
            this.realtimeShading = realtimeShading;
            this.realtimeShadow = realtimeShadow;
            this.baking = baking;
            this.bakeDisabledReason = bakeDisabledReason;
            this.hasSunAngle = hasSunAngle;
            this.hasSourceRadius = hasSourceRadius;
            this.hasSourceLength = hasSourceLength;
            this.hasRectSize = hasRectSize;
            this.hasBarnDoors = hasBarnDoors;
            this.hasSpotAngle = hasSpotAngle;
            this.hasInnerSpotAngle = hasInnerSpotAngle;
            this.hasPyramidAspect = hasPyramidAspect;
            this.hasBoxSize = hasBoxSize;
            this.isPlanar = isPlanar;
            this.supportsCubeCookie = supportsCubeCookie;
            this.supportsPlanarCookie = supportsPlanarCookie;
            this.supports2DIes = supports2DIes;
        }
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

    // Two-dimensional light shape specification (Kind + Emitter x Distribution). Lengths are meters, angles degrees.
    //   Directional:                 sourceAngle is the angular diameter of the sun disk (default 0.5357 degrees).
    //   Local / Round + Omni:        radius == 0 & length == 0 -> Point; radius > 0 -> Sphere; length > 0 along X -> Line or Tube.
    //   Local / Round + Cone:        cone beam with optional spherical sourceRadius.
    //   Local / Round + Pyramid:     rectangular pyramid beam (spotAngle + pyramidAspect) with optional spherical sourceRadius.
    //   Local / Round + ParallelBox: collimated box beam of boxSize.xy in meters.
    //   Local / Rectangle + Hemi:    one-sided sourceWidth x sourceHeight emitter facing transform.forward, optional barn doors.
    //   Local / Disk + Hemi:         one-sided circular disk emitter of sourceRadius facing transform.forward.
    //   Local / Disk + Cone:         one-sided circular disk emitter with shifted-apex cone beam originating at the disk rim.
    [Serializable]
    public struct LightSourceShape
    {
        public ELightKind kind;
        public ELocalLightEmitter emitter;
        public ELocalLightDistribution distribution;
        [Min(0), Tooltip("Radius of a Round spherical/tube emitter or a Disk emitter. Meters.")]
        public float sourceRadius;
        [Min(0), Tooltip("Round + Omnidirectional lights: length of the line or tube along the light's local X axis. Meters.")]
        public float sourceLength;
        [Min(0)] public float sourceWidth;
        [Min(0)] public float sourceHeight;
        [Range(0, 90), Tooltip("Rect barn door angle in degrees; 90 means no barn doors.")]
        public float barnDoorAngle;
        [Min(0)] public float barnDoorLength;
        [Range(0, 10), Tooltip("Angular diameter of the sun disk in degrees.")]
        public float sourceAngle;
        // Pyramid: width / height of the frustum cross-section. ParallelBox: emitter size in meters.
        [Min(0.01f)] public float pyramidAspect;
        public Vector2 boxSize;

        public const float DefaultSunAngle = 0.5357f;

        public static LightSourceShape DefaultDirectional()
        {
            return new LightSourceShape
            {
                kind = ELightKind.Directional,
                emitter = ELocalLightEmitter.Round,
                distribution = ELocalLightDistribution.Omnidirectional,
                sourceWidth = 1,
                sourceHeight = 1,
                barnDoorAngle = 90,
                sourceAngle = DefaultSunAngle,
                pyramidAspect = 1,
                boxSize = Vector2.one,
            };
        }

        public static LightSourceShape DefaultLocal(
            ELocalLightEmitter emitter = ELocalLightEmitter.Round,
            ELocalLightDistribution distribution = ELocalLightDistribution.Omnidirectional)
        {
            if (!LightShapeUtility.IsValidCombination(ELightKind.Local, emitter, distribution))
            {
                throw new ArgumentOutOfRangeException(nameof(distribution),
                    $"Invalid local light combination: {emitter} + {distribution}.");
            }

            return new LightSourceShape
            {
                kind = ELightKind.Local,
                emitter = emitter,
                distribution = distribution,
                sourceRadius = emitter == ELocalLightEmitter.Disk ? 0.5f : 0f,
                sourceWidth = 1,
                sourceHeight = 1,
                barnDoorAngle = 90,
                pyramidAspect = 1,
                boxSize = Vector2.one,
            };
        }

        public bool IsLineOrTube => LightShapeUtility.IsValidCombination(kind, emitter, distribution)
            && LightShapeUtility.Resolve(this) is EResolvedLightShape.Line or EResolvedLightShape.Tube;
    }

    // Converts between Unity's native Light fields and LightSourceShape and resolves emitter x distribution capabilities.
    // Rendering resolves the shape once per light per camera; Editor code writes derived native fields after user edits.
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

        public static bool IsValidCombination(ELightKind kind, ELocalLightEmitter emitter, ELocalLightDistribution distribution)
        {
            if (kind == ELightKind.Directional)
            {
                return true;
            }
            if (kind != ELightKind.Local)
            {
                return false;
            }

            switch (emitter)
            {
                case ELocalLightEmitter.Round:
                    return distribution == ELocalLightDistribution.Omnidirectional
                        || distribution == ELocalLightDistribution.Cone
                        || distribution == ELocalLightDistribution.Pyramid
                        || distribution == ELocalLightDistribution.ParallelBox;
                case ELocalLightEmitter.Rectangle:
                    return distribution == ELocalLightDistribution.Hemisphere;
                case ELocalLightEmitter.Disk:
                    return distribution == ELocalLightDistribution.Hemisphere
                        || distribution == ELocalLightDistribution.Cone;
                default:
                    return false;
            }
        }

        public static (ELocalLightEmitter emitter, ELocalLightDistribution distribution) NormalizeCombination(
            ELocalLightEmitter emitter,
            ELocalLightDistribution distribution,
            bool keepEmitter)
        {
            if (IsValidCombination(ELightKind.Local, emitter, distribution))
            {
                return (emitter, distribution);
            }

            if (keepEmitter)
            {
                return emitter switch
                {
                    ELocalLightEmitter.Round => (ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                    ELocalLightEmitter.Rectangle => (ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere),
                    ELocalLightEmitter.Disk => (ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere),
                    _ => (ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                };
            }

            return distribution switch
            {
                ELocalLightDistribution.Omnidirectional => (ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
                ELocalLightDistribution.Pyramid => (ELocalLightEmitter.Round, ELocalLightDistribution.Pyramid),
                ELocalLightDistribution.ParallelBox => (ELocalLightEmitter.Round, ELocalLightDistribution.ParallelBox),
                ELocalLightDistribution.Cone => (ELocalLightEmitter.Round, ELocalLightDistribution.Cone),
                ELocalLightDistribution.Hemisphere => (ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere),
                _ => (ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional),
            };
        }

        public static EResolvedLightShape Resolve(in LightSourceShape shape)
        {
            if (shape.kind == ELightKind.Directional)
            {
                return EResolvedLightShape.Directional;
            }
            if (shape.kind != ELightKind.Local)
            {
                throw new ArgumentOutOfRangeException(nameof(shape), $"Unsupported light kind: {shape.kind}");
            }

            switch (shape.emitter)
            {
                case ELocalLightEmitter.Round:
                    switch (shape.distribution)
                    {
                        case ELocalLightDistribution.Omnidirectional:
                            if (shape.sourceLength > 0)
                            {
                                return shape.sourceRadius > 0 ? EResolvedLightShape.Tube : EResolvedLightShape.Line;
                            }
                            return shape.sourceRadius > 0 ? EResolvedLightShape.Sphere : EResolvedLightShape.Point;
                        case ELocalLightDistribution.Cone:
                            return EResolvedLightShape.SpotCone;
                        case ELocalLightDistribution.Pyramid:
                            return EResolvedLightShape.SpotPyramid;
                        case ELocalLightDistribution.ParallelBox:
                            return EResolvedLightShape.SpotBox;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(shape),
                                $"Unsupported Round distribution: {shape.distribution}");
                    }
                case ELocalLightEmitter.Rectangle:
                    if (shape.distribution == ELocalLightDistribution.Hemisphere)
                    {
                        return EResolvedLightShape.Rect;
                    }
                    throw new ArgumentOutOfRangeException(nameof(shape),
                        $"Unsupported Rectangle distribution: {shape.distribution}");
                case ELocalLightEmitter.Disk:
                    return shape.distribution switch
                    {
                        ELocalLightDistribution.Hemisphere => EResolvedLightShape.Disk,
                        ELocalLightDistribution.Cone => EResolvedLightShape.DiskCone,
                        _ => throw new ArgumentOutOfRangeException(nameof(shape),
                            $"Unsupported Disk distribution: {shape.distribution}"),
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), $"Unsupported emitter: {shape.emitter}");
            }
        }

        public static LightCapabilities GetCapabilities(in LightSourceShape shape)
        {
            if (!IsValidCombination(shape.kind, shape.emitter, shape.distribution))
            {
                return new LightCapabilities(
                    EResolvedLightShape.Point,
                    validCombination: false,
                    realtimeShading: false,
                    realtimeShadow: false,
                    baking: false,
                    bakeDisabledReason: "Unsupported emitter and distribution combination.");
            }

            EResolvedLightShape resolved = Resolve(shape);
            switch (resolved)
            {
                case EResolvedLightShape.Directional:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasSunAngle: true, supportsPlanarCookie: true);
                case EResolvedLightShape.Point:
                case EResolvedLightShape.Sphere:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasSourceRadius: true, hasSourceLength: true, supportsCubeCookie: true);
                case EResolvedLightShape.Line:
                case EResolvedLightShape.Tube:
                    return new LightCapabilities(resolved, true, true, true, false,
                        "Line and Tube emitters have no lightmapper representation and are Realtime only.",
                        hasSourceRadius: true, hasSourceLength: true, supportsCubeCookie: true);
                case EResolvedLightShape.SpotCone:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasSourceRadius: true, hasSpotAngle: true, hasInnerSpotAngle: true, supports2DIes: true);
                case EResolvedLightShape.SpotPyramid:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasSourceRadius: true, hasSpotAngle: true, hasPyramidAspect: true, supports2DIes: true);
                case EResolvedLightShape.SpotBox:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasBoxSize: true);
                case EResolvedLightShape.Rect:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasRectSize: true, hasBarnDoors: true, isPlanar: true, supportsPlanarCookie: true);
                case EResolvedLightShape.Disk:
                    return new LightCapabilities(resolved, true, true, true, true, null,
                        hasSourceRadius: true, isPlanar: true, supportsPlanarCookie: true);
                case EResolvedLightShape.DiskCone:
                    return new LightCapabilities(resolved, true, true, true, false,
                        "Disk + Cone lights use shifted-apex cone beam clipping and are Realtime only.",
                        hasSourceRadius: true, hasSpotAngle: true, hasInnerSpotAngle: true,
                        isPlanar: true, supportsPlanarCookie: true, supports2DIes: true);
                default:
                    return new LightCapabilities(resolved, false, false, false, false, "Unsupported light shape.");
            }
        }

        // True when the light has a non-zero physical emitter dimension (PCSS produces penumbra; zero-size lights fall back to PCF).
        public static bool HasPCSSSize(in LightSourceShape shape)
        {
            if (!IsValid(shape))
                return false;

            switch (Resolve(shape))
            {
                case EResolvedLightShape.Directional:
                    return shape.sourceAngle > 0f;
                case EResolvedLightShape.Sphere:
                case EResolvedLightShape.SpotCone:
                case EResolvedLightShape.SpotPyramid:
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
                    return shape.sourceRadius > 0f;
                case EResolvedLightShape.Line:
                    return shape.sourceLength > 0f;
                case EResolvedLightShape.Tube:
                    return shape.sourceRadius > 0f || shape.sourceLength > 0f;
                case EResolvedLightShape.Rect:
                    return shape.sourceWidth > 0f && shape.sourceHeight > 0f;
                default:
                    return false;
            }
        }

        // Virtual apex offset behind a Disk + Cone emitter along -forward so the outer cone boundary passes through the disk rim.
        public static float DiskConeApexOffset(float sourceRadius, float spotAngleDegrees)
        {
            float clampedRadius = Mathf.Max(0f, sourceRadius);
            if (clampedRadius <= 0f)
            {
                return 0f;
            }
            float halfTan = Mathf.Tan(Mathf.Clamp(spotAngleDegrees, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            return clampedRadius / Mathf.Max(halfTan, 1e-4f);
        }

        // World bounding sphere radius centered at light.positionRange.xyz for tile binning and Z-binning.
        public static float BoundingRadius(in LightRecord record)
        {
            float range = math.max(record.positionRange.w, 0.01f);
            if ((record.flags & LightRecordFlags.SpotBox) != 0)
            {
                float hx = 0.5f * math.max(record.area.x, 0f);
                float hy = 0.5f * math.max(record.area.y, 0f);
                return math.sqrt(hx * hx + hy * hy + range * range);
            }
            if ((record.flags & LightRecordFlags.Planar) != 0)
            {
                float halfExtent = record.area.x > 0f && record.area.y > 0f
                    ? 0.5f * math.sqrt(record.area.x * record.area.x + record.area.y * record.area.y)
                    : math.max(record.shape.x, 0f);
                return range + halfExtent;
            }
            return range + 0.5f * math.max(record.shape.z, 0f) + math.max(record.shape.x, 0f);
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
                    shape = LightSourceShape.DefaultDirectional();
                    break;
                case LightType.Tube:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    break;
                case LightType.Spot:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Cone);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    break;
                case LightType.Pyramid:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Pyramid);
                    shape.sourceRadius = Mathf.Max(0, light.shapeRadius);
                    break;
                case LightType.Box:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.ParallelBox);
                    break;
                case LightType.Rectangle:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Rectangle, ELocalLightDistribution.Hemisphere);
                    break;
                case LightType.Disc:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Disk, ELocalLightDistribution.Hemisphere);
                    break;
                default:
                    shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
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
            switch (Resolve(shape))
            {
                case EResolvedLightShape.Directional:
                    return LightType.Directional;
                case EResolvedLightShape.Point:
                case EResolvedLightShape.Sphere:
                    return LightType.Point;
                case EResolvedLightShape.Line:
                case EResolvedLightShape.Tube:
                    return LightType.Tube;
                case EResolvedLightShape.SpotCone:
                    return LightType.Spot;
                case EResolvedLightShape.SpotPyramid:
                    return LightType.Pyramid;
                case EResolvedLightShape.SpotBox:
                    return LightType.Box;
                case EResolvedLightShape.Rect:
                // Native Disc is omitted from realtime culling and coerces bake mode on Undo/reload.
                // Additional data retains Disk / DiskCone identity; the lightmapper receives DiscLight or NoBake.
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
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
            EResolvedLightShape resolved = Resolve(shape);
            bool ownsRadius = resolved is EResolvedLightShape.Point or EResolvedLightShape.Sphere
                or EResolvedLightShape.Line or EResolvedLightShape.Tube
                or EResolvedLightShape.SpotCone or EResolvedLightShape.SpotPyramid;
            shapeRadius = ownsRadius ? shape.sourceRadius : light.shapeRadius;
        }

#if UNITY_EDITOR
        // The native extent fields only the Editor has (area size, sun angle), derived from the shape; a field the shape
        // does not own keeps the light's current value.
        static void DeriveNativeExtent(Light light, in LightSourceShape shape, out Vector2 areaSize, out float shadowAngle)
        {
            areaSize = light.areaSize;
            shadowAngle = light.shadowAngle;
            switch (Resolve(shape))
            {
                case EResolvedLightShape.Directional:
                    shadowAngle = shape.sourceAngle;
                    break;
                case EResolvedLightShape.Line:
                case EResolvedLightShape.Tube:
                    areaSize = new Vector2(shape.sourceLength, areaSize.y);
                    break;
                case EResolvedLightShape.SpotPyramid:
                    areaSize = new Vector2(shape.pyramidAspect, 1);
                    break;
                case EResolvedLightShape.SpotBox:
                    areaSize = shape.boxSize;
                    break;
                case EResolvedLightShape.Rect:
                    areaSize = new Vector2(shape.sourceWidth, shape.sourceHeight);
                    break;
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
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

        public static bool IsBakeable(in LightSourceShape shape) => GetCapabilities(shape).baking;

        // True when the shape has a valid emitter + distribution combination and required positive extents.
        public static bool IsValid(in LightSourceShape shape)
        {
            if (!IsValidCombination(shape.kind, shape.emitter, shape.distribution))
            {
                return false;
            }

            switch (Resolve(shape))
            {
                case EResolvedLightShape.Rect:
                    return shape.sourceWidth > 0 && shape.sourceHeight > 0;
                case EResolvedLightShape.Disk:
                case EResolvedLightShape.DiskCone:
                    return shape.sourceRadius > 0;
                case EResolvedLightShape.SpotPyramid:
                    return shape.pyramidAspect > 0;
                case EResolvedLightShape.SpotBox:
                    return shape.boxSize.x > 0 && shape.boxSize.y > 0;
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
