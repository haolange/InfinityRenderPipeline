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
