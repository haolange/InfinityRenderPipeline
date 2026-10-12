#ifndef INFINITY_LIGHT_RECORD
#define INFINITY_LIGHT_RECORD
// Light record v2. Must match C# FLightRecord (Runtime/RendererCore/LightPipeline/LightElement.cs, GPU-RECORD marker).
// There is no shape enum: a shape is a combination of flags and shape parameters.
//   directional                       LIGHT_FLAG_DIRECTIONAL; shape.w = tan(sun half angle)
//   punctual / sphere / line / tube   no spot or planar flag; shape.x = radius, shape.z = length along right
//   cone / pyramid / box spot         LIGHT_FLAG_SPOT_*; cone angles in forward.w / right.w, pyramid tangents and box
//                                     size in area.xy; cone and pyramid may carry a spherical source (shape.x)
//   rect                              LIGHT_FLAG_PLANAR, area.xy = size, area.zw = barn door cos / length
//   disk                              LIGHT_FLAG_PLANAR, area.xy = 0, shape.x = radius

#define LIGHT_FLAG_SHADOW 1
#define LIGHT_FLAG_CONTACT 2
#define LIGHT_FLAG_VOLUMETRIC 4
#define LIGHT_FLAG_SHADOWMASK 8
#define LIGHT_FLAG_BAKED 16
#define LIGHT_FLAG_DIRECTIONAL 32
#define LIGHT_FLAG_SPOT_CONE 64
#define LIGHT_FLAG_SPOT_PYRAMID 128
#define LIGHT_FLAG_SPOT_BOX 256
#define LIGHT_FLAG_PLANAR 512

#define SHADOW_FILTER_HARD 0
#define SHADOW_FILTER_PCF 1
#define SHADOW_FILTER_PCSS 2
#define SHADOW_FILTER_VSM 3
#define SHADOW_FILTER_EVSM 4

struct FLightRecord
{
    float4 radiance;
    float4 positionRange;
    float4 forward;
    float4 right;
    float4 shape;
    float4 area;
    float4 weights;
    float4 misc;
    float4 shadowAtlasRect;
    int lightLayer;
    int flags;
    int shadowMatrixIndex;
    int shadowSliceCount;
    int shadowFilter;
    int visibleLightIndex;
    int bakedOcclusionChannel;
    int cookieIndex;
};

bool LightHasFlag(FLightRecord light, int flag) { return (light.flags & flag) != 0; }
bool IsDirectionalLight(FLightRecord light) { return LightHasFlag(light, LIGHT_FLAG_DIRECTIONAL); }
bool IsPlanarLight(FLightRecord light) { return LightHasFlag(light, LIGHT_FLAG_PLANAR); }
bool IsRectLight(FLightRecord light) { return IsPlanarLight(light) && light.area.x > 0 && light.area.y > 0; }

// Unit vector from a surface toward a directional light.
float3 DirectionalLightL(FLightRecord light) { return -light.forward.xyz; }
float3 LightUp(FLightRecord light) { return cross(light.forward.xyz, light.right.xyz); }

float LightDiffuseWeight(FLightRecord light) { return light.weights.x; }
float LightSpecularWeight(FLightRecord light) { return light.weights.y; }
float LightShadowStrength(FLightRecord light) { return light.misc.x; }

// Realtime radiance; zero when the direct contribution is baked into the lightmaps.
float3 LightRadiance(FLightRecord light)
{
    return LightHasFlag(light, LIGHT_FLAG_BAKED) ? 0 : light.radiance.rgb;
}

float3 VolumeLightRadiance(FLightRecord light)
{
    return LightHasFlag(light, LIGHT_FLAG_VOLUMETRIC) ? light.radiance.rgb * light.weights.z : 0;
}

float VolumeShadowWeight(FLightRecord light, float shadow)
{
    return lerp(1.0, shadow, light.weights.w);
}

// Windowed inverse-square falloff (Karis 2013). Box spots are collimated and use only the range window.
float DistanceAttenuation(float distance, float range)
{
    float invRange = rcp(max(range, 1e-4));
    float d = saturate(1.0 - pow(distance * invRange, 4.0));
    return d * d * rcp(max(distance * distance, 1e-4));
}

float RangeWindow(float distance, float range)
{
    float d = saturate(1.0 - pow(distance * rcp(max(range, 1e-4)), 4.0));
    return d * d;
}

// Angular window of the spot shapes; 1 for lights without a spot flag. positionWS is the shaded point.
float SpotWindow(FLightRecord light, float3 positionWS)
{
    float3 fromLight = positionWS - light.positionRange.xyz;
    float3 forward = light.forward.xyz;
    if (LightHasFlag(light, LIGHT_FLAG_SPOT_CONE))
    {
        float cosAngle = dot(normalize(fromLight), forward);
        return saturate((cosAngle - light.forward.w) * light.right.w);
    }
    float z = dot(fromLight, forward);
    if (LightHasFlag(light, LIGHT_FLAG_SPOT_PYRAMID))
    {
        float x = dot(fromLight, light.right.xyz);
        float y = dot(fromLight, LightUp(light));
        return z > 0 && abs(x) <= z * light.area.x && abs(y) <= z * light.area.y ? 1 : 0;
    }
    if (LightHasFlag(light, LIGHT_FLAG_SPOT_BOX))
    {
        float x = dot(fromLight, light.right.xyz);
        float y = dot(fromLight, LightUp(light));
        return z > 0 && abs(x) <= 0.5 * light.area.x && abs(y) <= 0.5 * light.area.y ? 1 : 0;
    }
    return 1;
}

// Distance and angular attenuation of a local light at positionWS (shape-independent part).
float LocalLightAttenuation(FLightRecord light, float3 positionWS)
{
    float distance = length(light.positionRange.xyz - positionWS);
    float falloff = LightHasFlag(light, LIGHT_FLAG_SPOT_BOX)
        ? RangeWindow(dot(positionWS - light.positionRange.xyz, light.forward.xyz), light.positionRange.w)
        : DistanceAttenuation(distance, light.positionRange.w);
    return falloff * SpotWindow(light, positionWS);
}

// Direction toward a local light from positionWS. Box spots are collimated along -forward.
float3 LocalLightL(FLightRecord light, float3 positionWS)
{
    return LightHasFlag(light, LIGHT_FLAG_SPOT_BOX) ? -light.forward.xyz : normalize(light.positionRange.xyz - positionWS);
}
#endif
