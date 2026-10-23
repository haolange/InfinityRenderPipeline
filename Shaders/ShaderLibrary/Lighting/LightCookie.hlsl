#include "Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/TextureView.hlsl"
#ifndef INFINITY_LIGHT_COOKIE
#define INFINITY_LIGHT_COOKIE
// Light cookies and IES profiles from the cookie atlas (LightCookieAtlas.cs). record.cookieIndex selects an
// FLightCookie (C# FLightCookieGPU); rects are atlas uv (offset xy, scale zw), z = 0 means none. Every block is a
// power-of-two square at a multiple of its size, so its mips never leave it.
//   Cone / pyramid / box spots: projected through the spot frustum (box: orthographic).
//   Point lights: cubemap cookie stored octahedral, sampled by the light-space direction.
//   Directional: tiled over the light's right / up plane every cookieSize2D meters.
//   Rect / disk: the cookie is the emitter texture, sampled at the specular representative point (blurred by
//   roughness) and averaged (last mip) for diffuse.
//   IES: cube profiles by light-space direction for every local light; 2D profiles projected for cone and pyramid.
#include "../Common.hlsl"
#include "LightRecord.hlsl"

struct FLightCookie
{
    float4 cookieRect;
    float4 iesRect;
    // x = 1 when the IES block is octahedral; y = cookie block mip count - 1; z = IES block mip count - 1.
    float4 layout;
    // Directional: x, y = 1 / size; z, w = light position along right / up divided by the size.
    float4 tiling;
};

StructuredBuffer<FLightCookie> SRV_LightCookies;
Texture2D<float4> SRV_LightCookieAtlas;

bool LightHasCookie(FLightRecord light) { return light.cookieIndex >= 0; }

// Octahedral mapping of a unit direction to [0, 1]^2 (the cube re-projection of Compute_LightCookie uses the inverse).
float2 CookieOctahedralEncode(float3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    float2 e = n.z >= 0 ? n.xy : (1.0 - abs(n.yx)) * float2(n.x >= 0 ? 1.0 : -1.0, n.y >= 0 ? 1.0 : -1.0);
    return e * 0.5 + 0.5;
}

float3 SampleCookieBlock(float4 rect, float2 uv, float lod, float maxMip)
{
    uint width, height;
    InfinityGetDimensions(SRV_LightCookieAtlas, width, height);
    lod = clamp(lod, 0.0, maxMip);
    // Keep bilinear taps inside the block at this mip.
    float texels = max(rect.z * width * exp2(-lod), 1.0);
    uv = clamp(uv, 0.5 / texels, 1.0 - 0.5 / texels);
    return InfinitySampleLevelTrilinear(SRV_LightCookieAtlas,Global_trilinear_clamp_sampler,rect.xy + uv * rect.zw,lod).rgb;
}

// Position relative to the light in its own frame (right, up, forward).
float3 LightLocalPosition(FLightRecord light, float3 worldPos)
{
    float3 d = worldPos - light.positionRange.xyz;
    return float3(dot(d, light.right.xyz), dot(d, LightUp(light)), dot(d, light.forward.xyz));
}

// Uv of a projected (spot) texture; false behind the light.
bool SpotProjectionUV(FLightRecord light, float3 local, out float2 uv)
{
    uv = 0.5;
    if (LightHasFlag(light, LIGHT_FLAG_SPOT_BOX))
    {
        uv = local.xy / max(light.area.xy, 1e-4) + 0.5;
        return true;
    }
    if (local.z <= 1e-4) return false;
    float2 tangents;
    if (LightHasFlag(light, LIGHT_FLAG_SPOT_PYRAMID))
    {
        tangents = light.area.xy;
    }
    else
    {
        float cosOuter = clamp(light.forward.w, 1e-4, 1.0);
        tangents = sqrt(1.0 - cosOuter * cosOuter) / cosOuter;
    }
    uv = local.xy / (local.z * max(tangents, 1e-4)) * 0.5 + 0.5;
    return true;
}

// Cookie x IES color of a directional, spot or point light at worldPos (1 without either). Rect / disk cookies are
// emitter textures: see LightPlanarCookie.
float3 LightCookieColor(FLightRecord light, float3 worldPos)
{
    if (light.cookieIndex < 0) return 1.0;
    FLightCookie cookie = SRV_LightCookies[light.cookieIndex];
    float3 color = 1.0;
    if (IsDirectionalLight(light))
    {
        if (cookie.cookieRect.z > 0)
        {
            float2 uv = float2(dot(worldPos, light.right.xyz) * cookie.tiling.x - cookie.tiling.z,
                               dot(worldPos, LightUp(light)) * cookie.tiling.y - cookie.tiling.w) + 0.5;
            color *= SampleCookieBlock(cookie.cookieRect, frac(uv), 0, cookie.layout.y);
        }
        return color;
    }

    float3 local = LightLocalPosition(light, worldPos);
    bool spot = LightHasFlag(light, LIGHT_FLAG_SPOT_CONE | LIGHT_FLAG_SPOT_PYRAMID | LIGHT_FLAG_SPOT_BOX);
    if (cookie.cookieRect.z > 0 && !IsPlanarLight(light))
    {
        if (spot)
        {
            float2 uv;
            color *= SpotProjectionUV(light, local, uv) ? SampleCookieBlock(cookie.cookieRect, uv, 0, cookie.layout.y) : 0;
        }
        else
        {
            color *= SampleCookieBlock(cookie.cookieRect, CookieOctahedralEncode(normalize(local + 1e-6)), 0, cookie.layout.y);
        }
    }
    if (cookie.iesRect.z > 0)
    {
        if (cookie.layout.x != 0)
        {
            color *= SampleCookieBlock(cookie.iesRect, CookieOctahedralEncode(normalize(local + 1e-6)), 0, cookie.layout.z);
        }
        else
        {
            float2 uv;
            color *= SpotProjectionUV(light, local, uv) ? SampleCookieBlock(cookie.iesRect, uv, 0, cookie.layout.z) : 0;
        }
    }
    return color;
}

// Emitter texture of a rect / disk light: emitterUV is the representative point on the emitter ([0, 1]^2), blurred by
// roughness for specular; diffuse sees the average (last mip).
void LightPlanarCookie(FLightRecord light, float2 emitterUV, float roughness, out float3 diffuse, out float3 specular)
{
    diffuse = 1.0;
    specular = 1.0;
    if (light.cookieIndex < 0 || !IsPlanarLight(light)) return;
    FLightCookie cookie = SRV_LightCookies[light.cookieIndex];
    if (cookie.cookieRect.z <= 0) return;
    float maxMip = cookie.layout.y;
    diffuse = SampleCookieBlock(cookie.cookieRect, 0.5, maxMip, maxMip);
    specular = SampleCookieBlock(cookie.cookieRect, emitterUV, saturate(roughness) * maxMip, maxMip);
}
#endif
