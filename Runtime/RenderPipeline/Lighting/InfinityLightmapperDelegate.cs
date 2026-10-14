using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using InfinityTech.Component;
using InfinityTech.Rendering.LightPipeline;
using GILightType = UnityEngine.Experimental.GlobalIllumination.LightType;

namespace InfinityTech.Rendering.Pipeline
{
    // Hands Unity's lightmapper the same light the realtime path renders: the Infinity shape (sphere radius, sun
    // angle, rect, disk, pyramid, box), inverse-square falloff and the inner spot angle. InfinityRP's BRDF already
    // divides diffuse by pi, so intensities are standard radiometric units and are not premultiplied by pi the way
    // Unity's built-in delegate does for the Built-in pipeline. Tube and line lights are realtime only.
    public static class InfinityLightmapperDelegate
    {
        public static readonly Lightmapping.RequestLightsDelegate Delegate = RequestLights;

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
                Light light = requests[i];
                light.TryGetComponent(out InfinityAdditionalLightData ext);
                FLightShape shape = FLightRecordPack.ResolveShape(light, ext);
                if (!LightShapeUtility.IsBakeable(shape) || !LightShapeUtility.IsValid(shape))
                {
                    data.InitNoBake(light.GetEntityId());
                    lightsOutput[i] = data;
                    continue;
                }

                bool shadow = light.shadows != LightShadows.None;
                LightmapperUtils.Extract(light, out Cookie cookie);
                switch (shape.type)
                {
                    case EInfinityLightType.Directional:
                        LightmapperUtils.Extract(light, ref directional);
                        directional.penumbraWidthRadian = shadow ? shape.sourceAngle * Mathf.Deg2Rad : 0;
                        data.Init(ref directional, ref cookie);
                        break;
                    case EInfinityLightType.Point:
                        LightmapperUtils.Extract(light, ref point);
                        point.sphereRadius = shadow ? shape.sourceRadius : 0;
                        point.falloff = FalloffType.InverseSquared;
                        data.Init(ref point, ref cookie);
                        break;
                    case EInfinityLightType.Spot:
                        if (shape.spotShape == ESpotShape.Cone)
                        {
                            LightmapperUtils.Extract(light, ref spot);
                            spot.sphereRadius = shadow ? shape.sourceRadius : 0;
                            spot.innerConeAngle = Mathf.Min(light.innerSpotAngle, light.spotAngle) * Mathf.Deg2Rad;
                            spot.falloff = FalloffType.InverseSquared;
                            spot.angularFalloff = AngularFalloffType.AnalyticAndInnerAngle;
                            data.Init(ref spot, ref cookie);
                        }
                        else if (shape.spotShape == ESpotShape.Pyramid)
                        {
                            var pyramid = new SpotLightPyramidShape();
                            FillCommon(light, ref pyramid.entityId, ref pyramid.shadow, ref pyramid.mode, ref pyramid.position, ref pyramid.orientation,
                                ref pyramid.color, ref pyramid.indirectColor, ref pyramid.indirectMultiplier, ref pyramid.range);
                            pyramid.angle = light.spotAngle * Mathf.Deg2Rad;
                            pyramid.aspectRatio = shape.pyramidAspect;
                            pyramid.sphereRadius = shadow ? shape.sourceRadius : 0;
                            pyramid.falloff = FalloffType.InverseSquared;
                            data.Init(ref pyramid, ref cookie);
                        }
                        else
                        {
                            var box = new SpotLightBoxShape();
                            FillCommon(light, ref box.entityId, ref box.shadow, ref box.mode, ref box.position, ref box.orientation,
                                ref box.color, ref box.indirectColor, ref box.indirectMultiplier, ref box.range);
                            box.width = shape.boxSize.x;
                            box.height = shape.boxSize.y;
                            data.Init(ref box, ref cookie);
                        }
                        break;
                    case EInfinityLightType.Rect:
                        LightmapperUtils.Extract(light, ref rect);
                        rect.width = shape.sourceWidth;
                        rect.height = shape.sourceHeight;
                        rect.falloff = FalloffType.InverseSquared;
                        data.Init(ref rect, ref cookie);
                        break;
                    case EInfinityLightType.Disk:
                        LightmapperUtils.Extract(light, ref disc);
                        disc.radius = shape.sourceRadius;
                        disc.falloff = FalloffType.InverseSquared;
                        data.Init(ref disc, ref cookie);
                        break;
                }
                lightsOutput[i] = data;
            }
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
