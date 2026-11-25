using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using InfinityTech.Component;
using InfinityTech.Core;
using InfinityTech.Rendering.LightPipeline;
using GILightType = UnityEngine.Experimental.GlobalIllumination.LightType;

namespace InfinityTech.Rendering.Pipeline
{
    // Hands Unity's lightmapper the same light the realtime path renders: the Infinity shape (sphere radius, sun
    // angle, rect, disk, pyramid, box), inverse-square falloff and the inner spot angle. InfinityRP's BRDF already
    // divides diffuse by pi, so intensities are standard radiometric units and are not premultiplied by pi the way
    // Unity's built-in delegate does for the Built-in pipeline. Line, Tube and Disk+Cone lights are realtime only.
    public static class InfinityLightmapperDelegate
    {
        public static readonly Lightmapping.RequestLightsDelegate Delegate = RequestLights;
        static readonly Dictionary<ulong, string> s_LastLoggedIssue = new Dictionary<ulong, string>();

        static void RequestLights(Light[] requests, NativeArray<LightDataGI> lightsOutput)
        {
            var directional = new DirectionalLight();
            var point = new PointLight();
            var spot = new SpotLight();
            var rect = new RectangleLight();
            var disc = new DiscLight();
            var data = new LightDataGI();
            for (int i = 0; i < requests.Length; ++i)
            {
                data = default;
                Light light = requests[i];
                if (light == null)
                {
                    lightsOutput[i] = data;
                    continue;
                }
                light.TryGetComponent(out InfinityAdditionalLightData ext);
                LightSourceShape shape = LightRecordPack.ResolveShape(light, ext);
                bool valid = LightShapeUtility.IsValid(shape);
                LightCapabilities capabilities = valid ? LightShapeUtility.GetCapabilities(shape) : default;
                if (!valid || !capabilities.baking)
                {
                    ReportUnsupportedBakeLight(light, valid ? capabilities.bakeDisabledReason : "The light has an invalid shape or zero emitting area.");
                    data.InitNoBake(light.GetEntityId());
                    lightsOutput[i] = data;
                    continue;
                }

                ClearLoggedIssue(light);
                bool shadow = light.shadows != LightShadows.None;
                LightmapperUtils.Extract(light, out Cookie cookie);
                switch (capabilities.resolvedShape)
                {
                    case EResolvedLightShape.Directional:
                        LightmapperUtils.Extract(light, ref directional);
                        directional.penumbraWidthRadian = shadow ? shape.sourceAngle * Mathf.Deg2Rad : 0;
                        data.Init(ref directional, ref cookie);
                        break;
                    case EResolvedLightShape.Point:
                    case EResolvedLightShape.Sphere:
                        LightmapperUtils.Extract(light, ref point);
                        point.sphereRadius = shadow ? shape.sourceRadius : 0;
                        point.falloff = FalloffType.InverseSquared;
                        data.Init(ref point, ref cookie);
                        break;
                    case EResolvedLightShape.SpotCone:
                        LightmapperUtils.Extract(light, ref spot);
                        spot.sphereRadius = shadow ? shape.sourceRadius : 0;
                        spot.innerConeAngle = Mathf.Min(light.innerSpotAngle, light.spotAngle) * Mathf.Deg2Rad;
                        spot.falloff = FalloffType.InverseSquared;
                        spot.angularFalloff = AngularFalloffType.AnalyticAndInnerAngle;
                        data.Init(ref spot, ref cookie);
                        break;
                    case EResolvedLightShape.SpotPyramid:
                        var pyramid = new SpotLightPyramidShape();
                        FillCommon(light, ref pyramid.entityId, ref pyramid.shadow, ref pyramid.mode, ref pyramid.position, ref pyramid.orientation,
                            ref pyramid.color, ref pyramid.indirectColor, ref pyramid.indirectMultiplier, ref pyramid.range);
                        pyramid.angle = light.spotAngle * Mathf.Deg2Rad;
                        pyramid.aspectRatio = shape.pyramidAspect;
                        pyramid.sphereRadius = shadow ? shape.sourceRadius : 0;
                        pyramid.falloff = FalloffType.InverseSquared;
                        data.Init(ref pyramid, ref cookie);
                        break;
                    case EResolvedLightShape.SpotBox:
                        var box = new SpotLightBoxShape();
                        FillCommon(light, ref box.entityId, ref box.shadow, ref box.mode, ref box.position, ref box.orientation,
                            ref box.color, ref box.indirectColor, ref box.indirectMultiplier, ref box.range);
                        box.width = shape.boxSize.x;
                        box.height = shape.boxSize.y;
                        data.Init(ref box, ref cookie);
                        break;
                    case EResolvedLightShape.Rect:
                        LightmapperUtils.Extract(light, ref rect);
                        rect.width = shape.sourceWidth;
                        rect.height = shape.sourceHeight;
                        rect.falloff = FalloffType.InverseSquared;
                        data.Init(ref rect, ref cookie);
                        break;
                    case EResolvedLightShape.Disk:
                        LightmapperUtils.Extract(light, ref disc);
                        disc.radius = shape.sourceRadius;
                        disc.falloff = FalloffType.InverseSquared;
                        data.Init(ref disc, ref cookie);
                        break;
                    default:
                        data.InitNoBake(light.GetEntityId());
                        break;
                }
                lightsOutput[i] = data;
            }
        }

        static void ReportUnsupportedBakeLight(Light light, string reason)
        {
            if (light == null || light.lightmapBakeType == LightmapBakeType.Realtime)
            {
                return;
            }

            ulong key = UnityEntityId.ToUInt64(light);
            string message = $"[InfinityRP] Light '{GetHierarchyPath(light)}' ({light.lightmapBakeType}) cannot be baked and was set to NoBake: {reason}";
            if (s_LastLoggedIssue.TryGetValue(key, out string previous) && previous == message)
            {
                return;
            }
            s_LastLoggedIssue[key] = message;
            Debug.LogWarning(message, light);
        }

        static void ClearLoggedIssue(Light light)
        {
            if (light != null)
            {
                s_LastLoggedIssue.Remove(UnityEntityId.ToUInt64(light));
            }
        }

        static string GetHierarchyPath(Light light)
        {
            Transform current = light.transform;
            if (current.parent == null)
            {
                return current.name;
            }
            var sb = new StringBuilder(current.name);
            while (current.parent != null)
            {
                current = current.parent;
                sb.Insert(0, '/');
                sb.Insert(0, current.name);
            }
            return sb.ToString();
        }

        // Same extraction as LightmapperUtils for the shapes it has no helper for.
        static void FillCommon(Light light, ref EntityId entityId, ref bool shadow, ref LightMode mode, ref Vector3 position, ref Quaternion orientation,
            ref LinearColor color, ref LinearColor indirectColor, ref float indirectMultiplier, ref float range)
        {
            var reference = new SpotLight();
            LightmapperUtils.Extract(light, ref reference);
            entityId = reference.entityId;
            shadow = reference.shadow;
            mode = reference.mode;
            position = reference.position;
            orientation = reference.orientation;
            color = reference.color;
            indirectColor = reference.indirectColor;
            indirectMultiplier = reference.indirectMultiplier;
            range = reference.range;
        }
    }
}
