#ifndef _AtmosphereCommonInclude
#define _AtmosphereCommonInclude

void CubemapDirectionToFaceUv(float3 dir, out uint face, out float2 uv)
{
    float3 absDir = abs(dir);
    float2 st;
    if (absDir.x >= absDir.y && absDir.x >= absDir.z)
    {
        face = dir.x > 0.0 ? 0 : 1;
        st = dir.x > 0.0 ? float2(-dir.z, -dir.y) : float2(dir.z, -dir.y);
        st /= max(absDir.x, 1e-6);
    }
    else if (absDir.y >= absDir.z)
    {
        face = dir.y > 0.0 ? 2 : 3;
        st = dir.y > 0.0 ? float2(dir.x, dir.z) : float2(dir.x, -dir.z);
        st /= max(absDir.y, 1e-6);
    }
    else
    {
        face = dir.z > 0.0 ? 4 : 5;
        st = dir.z > 0.0 ? float2(dir.x, -dir.y) : float2(-dir.x, -dir.y);
        st /= max(absDir.z, 1e-6);
    }

    uv = st * 0.5 + 0.5;
}

// Inverse of CubemapDirectionToFaceUv; uv may extend beyond the face for seamless filter taps.
float3 CubemapFaceDirection(uint face, float2 uv)
{
    float2 st = uv * 2.0 - 1.0;
    float3 dir;
    if (face == 0) dir = float3(1.0, -st.y, -st.x);
    else if (face == 1) dir = float3(-1.0, -st.y, st.x);
    else if (face == 2) dir = float3(st.x, 1.0, st.y);
    else if (face == 3) dir = float3(st.x, -1.0, -st.y);
    else if (face == 4) dir = float3(st.x, -st.y, 1.0);
    else dir = float3(-st.x, -st.y, -1.0);
    return normalize(dir);
}

float3 LoadCubemapArrayRemapped(Texture2DArray<float4> cube, uint firstSlice, uint face, int2 coord, uint size, uint mip)
{
    uint adjacentFace;
    float2 adjacentUV;
    CubemapDirectionToFaceUv(CubemapFaceDirection(face, (coord + 0.5) / size), adjacentFace, adjacentUV);
    int2 adjacentCoord = clamp(int2(floor(adjacentUV * size)), 0, int(size) - 1);
    return cube.Load(int4(adjacentCoord, firstSlice + adjacentFace, mip)).rgb;
}

float3 LoadCubemapArrayTap(Texture2DArray<float4> cube, uint firstSlice, uint face, int2 coord, uint size, uint mip)
{
    bool2 outside = (coord < 0) | (coord >= int(size));
    if (!any(outside)) return cube.Load(int4(coord, firstSlice + face, mip)).rgb;
    if (all(outside))
    {
        // A cube vertex belongs to three faces. A symmetric corner tap keeps the limit identical from all three.
        int2 corner = clamp(coord, 0, int(size) - 1);
        float3 a = cube.Load(int4(corner, firstSlice + face, mip)).rgb;
        float3 b = LoadCubemapArrayRemapped(cube, firstSlice, face, int2(coord.x, corner.y), size, mip);
        float3 c = LoadCubemapArrayRemapped(cube, firstSlice, face, int2(corner.x, coord.y), size, mip);
        return (a + b + c) / 3.0;
    }
    return LoadCubemapArrayRemapped(cube, firstSlice, face, coord, size, mip);
}

float3 SampleCubemapArrayMip(Texture2DArray<float4> cube, uint firstSlice, uint face, float2 uv, uint size, uint mip)
{
    float2 texel = uv * size - 0.5;
    int2 origin = int2(floor(texel));
    if (all(origin >= 0) && all(origin + 1 < int(size)))
        return cube.SampleLevel(Global_bilinear_clamp_sampler, float3(uv, firstSlice + face), mip).rgb;
    float2 blend = frac(texel);
    float3 a = LoadCubemapArrayTap(cube, firstSlice, face, origin, size, mip);
    float3 b = LoadCubemapArrayTap(cube, firstSlice, face, origin + int2(1, 0), size, mip);
    float3 c = LoadCubemapArrayTap(cube, firstSlice, face, origin + int2(0, 1), size, mip);
    float3 d = LoadCubemapArrayTap(cube, firstSlice, face, origin + int2(1, 1), size, mip);
    return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
}

float3 SampleCubemapArraySeamless(Texture2DArray<float4> cube, uint firstSlice, float3 direction, float lod)
{
    uint width, height, slices, mipCount;
    cube.GetDimensions(0, width, height, slices, mipCount);
    uint face;
    float2 uv;
    CubemapDirectionToFaceUv(normalize(direction), face, uv);
    float mip = clamp(lod, 0.0, float(mipCount - 1));
    uint low = (uint)floor(mip);
    float3 a = SampleCubemapArrayMip(cube, firstSlice, face, uv, max(width >> low, 1u), low);
    float blend = frac(mip);
    if (blend == 0.0) return a;
    uint high = low + 1;
    float3 b = SampleCubemapArrayMip(cube, firstSlice, face, uv, max(width >> high, 1u), high);
    return lerp(a, b, blend);
}

float2 AtmosphereRayToSkyViewUv(float3 rayDir)
{
    float azimuth = atan2(rayDir.x, rayDir.z);
    float elevation = asin(clamp(rayDir.y, -1.0, 1.0));
    float normalizedElevation = elevation / Half_Pi;
    float latitudeCoord = sign(normalizedElevation) * sqrt(abs(normalizedElevation));
    return float2(azimuth * Inv_Two_Pi + 0.5, latitudeCoord * 0.5 + 0.5);
}

float3 SampleAtmosphereSkyViewLUT(Texture2D<float4> skyView, float3 rayDir)
{
    uint width, height;
    skyView.GetDimensions(width, height);
    float2 uv = AtmosphereRayToSkyViewUv(normalize(rayDir));
    uv.y = clamp(uv.y, 0.5 / height, 1.0 - 0.5 / height);
    return skyView.SampleLevel(Global_bilinear_repeat_sampler, uv, 0).rgb;
}

float4 SampleAtmosphereAerialLUT(Texture3D<float4> aerial, float2 screenUV, float linearDepth, float aerialDistance)
{
    float sliceT = saturate(sqrt(linearDepth / max(aerialDistance, 1.0)));
    return aerial.SampleLevel(Global_trilinear_clamp_sampler, float3(screenUV, sliceT), 0);
}

float3 SampleAtmosphereTransmittanceLUT(Texture2D<float4> lut, float altitude, float cosAngle, float planetRadius, float atmosphereHeight)
{
    float thickness = atmosphereHeight;
    float planetDiameter = 2.0 * planetRadius;
    float H = sqrt(max(thickness * (planetDiameter + thickness), 0.0));
    float h = clamp(altitude, 0.0, thickness);
    float rho = sqrt(max(h * (planetDiameter + h), 0.0));
    float x_r = H > 0.0 ? rho / H : 0.0;

    float b = (planetRadius + h) * cosAngle;
    float c = (h - thickness) * (planetDiameter + h + thickness);
    float d = 0.0;
    float discriminant = b * b - c;
    if (discriminant >= 0.0)
    {
        float root = sqrt(discriminant);
        float wellConditioned = (b >= 0.0) ? (-b - root) : (-b + root);
        float other = abs(wellConditioned) > 1e-9 ? (c * rcp(wellConditioned)) : wellConditioned;
        d = max(max(wellConditioned, other), 0.0);
    }

    float d_min = thickness - h;
    float d_max = rho + H;
    float x_mu = (d - d_min) * rcp(max(d_max - d_min, 1e-6));
    return lut.SampleLevel(Global_bilinear_clamp_sampler, saturate(float2(x_mu, x_r)), 0).rgb;
}

#endif
