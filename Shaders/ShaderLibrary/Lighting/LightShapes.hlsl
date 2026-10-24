#ifndef INFINITY_LIGHT_SHAPES
#define INFINITY_LIGHT_SHAPES
// Shape-aware evaluation of the light record (no LTC).
//   Specular: Karis 2013 representative point (sphere, line / tube, rect, disk, sun disk) with energy normalization
//             (alpha / alpha')^n, alpha' widened by the source's angular size.
//   Diffuse:  sphere and disk use the Frostbite horizon-clipped form factor (Lagarde and de Rousiers 2014), lines and
//             tubes the Karis line irradiance, rects the polygon form factor.
// Units: record.radiance is the light's on-axis intensity I (punctual convention). An area source of area A emits the
// luminance I / A, so every shape converges to the punctual result I * cos / d^2 far away. The functions below return
// factors relative to I.
#include "LightRecord.hlsl"

#define LIGHT_SHAPE_PI 3.14159265

struct FLightShapeSample
{
    // Diffuse: E / I (irradiance per unit intensity), including N.L and the horizon.
    float diffuseIrradiance;
    // Specular: representative direction, N.L along it, intensity falloff and energy normalization.
    float3 specularL;
    float specularNoL;
    float specularFalloff;
    float specularEnergy;
    // Rect / disk: representative point on the emitter in [0, 1]^2 (emitter textures); 0.5 for other shapes.
    float2 emitterUV;
};

// Frostbite illuminance of a sphere or disk subtending sin^2(sigma), with the horizon clip (returns E / L).
float IlluminanceSphereOrDisk(float cosTheta, float sinSigmaSqr)
{
    float illuminance = LIGHT_SHAPE_PI * sinSigmaSqr * saturate(cosTheta);
    if (cosTheta * cosTheta <= sinSigmaSqr)
    {
        float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
        float x = sqrt(max(1.0 / sinSigmaSqr - 1.0, 1e-6));
        float y = -x * (cosTheta / max(sinTheta, 1e-6));
        float sinThetaSqrtY = sinTheta * sqrt(saturate(1.0 - y * y));
        illuminance = (cosTheta * acos(clamp(y, -1.0, 1.0)) - x * sinThetaSqrtY) * sinSigmaSqr + atan(sinThetaSqrtY / x);
        illuminance = max(illuminance, 0.0);
    }
    return illuminance;
}

// Energy conservation for a lobe widened by a source of the given angular half-size (tangent): (alpha / alpha')^power.
float ShapeEnergy(float alpha, float angularSize, float power)
{
    float widened = saturate(alpha + 0.5 * angularSize);
    return pow(alpha / max(widened, 1e-4), power);
}

// Closest point to the reflection ray on a sphere around center (Karis 2013). Positions are relative to the shaded point.
float3 SphereRepresentativePoint(float3 center, float3 R, float radius)
{
    float3 centerToRay = dot(center, R) * R - center;
    return center + centerToRay * saturate(radius * rsqrt(max(dot(centerToRay, centerToRay), 1e-8)));
}

// Closest point on segment [p0, p1] to the reflection ray (Karis 2013). Positions relative to the shaded point.
float3 SegmentRepresentativePoint(float3 p0, float3 p1, float3 R)
{
    float3 segment = p1 - p0;
    float RoS = dot(R, segment);
    float t = (dot(R, p0) * RoS - dot(p0, segment)) / max(dot(segment, segment) - RoS * RoS, 1e-8);
    return p0 + segment * saturate(t);
}

// Point of a planar emitter closest to where the reflection ray meets its plane; rect (halfSize) or disk (radius).
float3 PlanarRepresentativePoint(FLightRecord light, float3 center, float3 R, bool rect, float2 halfSize, float radius, out float2 emitterUV)
{
    float3 normal = light.forward.xyz;
    float denominator = dot(R, normal);
    float t = dot(center, normal) / (abs(denominator) > 1e-4 ? denominator : 1e-4);
    float3 local = R * max(t, 0.0) - center;
    float3 right = light.right.xyz;
    float3 up = LightUp(light);
    float2 planar = float2(dot(local, right), dot(local, up));
    if (rect)
    {
        planar = clamp(planar, -halfSize, halfSize);
    }
    else
    {
        float len = length(planar);
        planar *= len > radius ? radius / len : 1.0;
    }
    emitterUV = planar / (rect ? 2.0 * max(halfSize, 1e-4) : 2.0 * max(radius, 1e-4)) + 0.5;
    return center + right * planar.x + up * planar.y;
}

// Lambert form factor of the rect polygon, times pi (E / L). Edges are wound so a receiver in front of the emitter
// gets a positive result (checked against brute-force integration).
float RectIlluminance(float3 center, float3 right, float3 up, float2 halfSize, float3 N)
{
    float3 v0 = normalize(center - right * halfSize.x - up * halfSize.y);
    float3 v1 = normalize(center + right * halfSize.x - up * halfSize.y);
    float3 v2 = normalize(center + right * halfSize.x + up * halfSize.y);
    float3 v3 = normalize(center - right * halfSize.x + up * halfSize.y);
    float form = acos(clamp(dot(v0, v1), -1.0, 1.0)) * dot(normalize(cross(v1, v0)), N)
               + acos(clamp(dot(v1, v2), -1.0, 1.0)) * dot(normalize(cross(v2, v1)), N)
               + acos(clamp(dot(v2, v3), -1.0, 1.0)) * dot(normalize(cross(v3, v2)), N)
               + acos(clamp(dot(v3, v0), -1.0, 1.0)) * dot(normalize(cross(v0, v3)), N);
    return max(form, 0.0) * 0.5;
}

// Range window, spot window, emitter side and barn doors of a local light (no inverse-square term).
float LocalLightWindow(FLightRecord light, float3 positionWS)
{
    float3 fromLight = positionWS - light.positionRange.xyz;
    float window = 0;
    if (LightHasFlag(light, LIGHT_FLAG_SPOT_BOX))
    {
        window = RangeWindow(dot(fromLight, light.forward.xyz), light.positionRange.w) * SpotWindow(light, positionWS);
    }
    else
    {
        window = RangeWindow(length(fromLight), light.positionRange.w) * SpotWindow(light, positionWS);
        if (IsPlanarLight(light))
        {
            float facing = dot(normalize(fromLight), light.forward.xyz);
            if (facing <= 0) window = 0;
            // Barn doors narrow the emission cone as they get longer relative to the emitter.
            else if (light.area.w > 0)
            {
                float extent = max(max(light.area.x, light.area.y), 2.0 * light.shape.x);
                float cutoff = light.area.z * saturate(light.area.w / max(extent, 1e-4));
                window *= smoothstep(cutoff - 0.05, cutoff + 0.05, facing);
            }
        }
    }
    return window;
}

FLightShapeSample EvaluateLocalLightShape(FLightRecord light, float3 positionWS, float3 N, float3 V, float roughness)
{
    FLightShapeSample s = (FLightShapeSample)0;
    float3 R = reflect(-V, N);
    float alpha = max(roughness * roughness, 1e-3);
    float3 center = light.positionRange.xyz - positionWS;
    float centerDistSqr = max(dot(center, center), 1e-8);
    float centerDist = sqrt(centerDistSqr);
    float3 Lc = center / centerDist;
    float radius = light.shape.x;
    float softRadius = light.shape.y;
    float sourceLength = light.shape.z;
    s.specularEnergy = 1;
    s.emitterUV = 0.5;

    if (LightHasFlag(light, LIGHT_FLAG_SPOT_BOX))
    {
        // Collimated: constant irradiance along -forward.
        float3 L = -light.forward.xyz;
        s.diffuseIrradiance = saturate(dot(N, L));
        s.specularL = L;
        s.specularNoL = saturate(dot(N, L));
        s.specularFalloff = 1;
    }
    else if (IsPlanarLight(light))
    {
        bool rect = IsRectLight(light);
        float2 halfSize = 0.5 * light.area.xy;
        float area = rect ? light.area.x * light.area.y : LIGHT_SHAPE_PI * radius * radius;
        float illuminance;
        if (rect)
        {
            illuminance = RectIlluminance(center, light.right.xyz, LightUp(light), halfSize, N);
        }
        else
        {
            float sinSigmaSqr = radius * radius / (radius * radius + centerDistSqr);
            illuminance = IlluminanceSphereOrDisk(dot(N, Lc), sinSigmaSqr) * saturate(dot(light.forward.xyz, -Lc));
        }
        s.diffuseIrradiance = illuminance / max(area, 1e-6);
        float3 representative = PlanarRepresentativePoint(light, center, R, rect, halfSize, radius, s.emitterUV);
        float repDistSqr = max(dot(representative, representative), 1e-8);
        s.specularL = representative * rsqrt(repDistSqr);
        s.specularNoL = saturate(dot(N, s.specularL));
        // Intensity toward the representative point carries the emitter cosine; distance is at least the source size.
        s.specularFalloff = saturate(dot(light.forward.xyz, -s.specularL)) / max(repDistSqr, area / LIGHT_SHAPE_PI);
        s.specularEnergy = ShapeEnergy(alpha, sqrt(area / LIGHT_SHAPE_PI) / centerDist, 2.0);
    }
    else if (sourceLength > 0)
    {
        // Line or tube along the light's right axis. Diffuse: Karis line irradiance; specular: closest segment point,
        // then a sphere of the tube radius around it.
        float3 axis = light.right.xyz * (0.5 * sourceLength);
        float3 p0 = center - axis;
        float3 p1 = center + axis;
        float len0 = max(sqrt(dot(p0, p0)), 1e-4);
        float len1 = max(sqrt(dot(p1, p1)), 1e-4);
        float denominator = max(len0 * len1 + dot(p0, p1), 1e-6);
        s.diffuseIrradiance = max(dot(N, p0) / len0 + dot(N, p1) / len1, 0.0) / denominator;
        float3 onSegment = SegmentRepresentativePoint(p0, p1, R);
        float3 representative = radius > 0 ? SphereRepresentativePoint(onSegment, R, radius) : onSegment;
        s.specularL = normalize(representative);
        s.specularNoL = saturate(dot(N, s.specularL));
        s.specularFalloff = 2.0 / denominator;
        s.specularEnergy = ShapeEnergy(alpha, (radius + softRadius) / centerDist, 2.0) * ShapeEnergy(alpha, 0.5 * sourceLength / centerDist, 1.0);
    }
    else if (radius > 0)
    {
        float sinSigmaSqr = min(radius * radius / centerDistSqr, 0.9999);
        s.diffuseIrradiance = IlluminanceSphereOrDisk(dot(N, Lc), sinSigmaSqr) / (LIGHT_SHAPE_PI * radius * radius);
        float3 representative = SphereRepresentativePoint(center, R, radius);
        s.specularL = normalize(representative);
        s.specularNoL = saturate(dot(N, s.specularL));
        s.specularFalloff = 1.0 / max(centerDistSqr, radius * radius);
        s.specularEnergy = ShapeEnergy(alpha, (radius + softRadius) / centerDist, 2.0);
    }
    else
    {
        // Punctual.
        s.diffuseIrradiance = saturate(dot(N, Lc)) / centerDistSqr;
        s.specularL = Lc;
        s.specularNoL = saturate(dot(N, Lc));
        s.specularFalloff = 1.0 / centerDistSqr;
        s.specularEnergy = softRadius > 0 ? ShapeEnergy(alpha, softRadius / centerDist, 2.0) : 1.0;
    }
    return s;
}

// Sun disk: the direction inside the disk closest to the reflection ray (Karis 2013), energy widened by the disk.
float3 SunDiskSpecularL(FLightRecord light, float3 R, float roughness, out float energy)
{
    float3 L = DirectionalLightL(light);
    float tanHalf = light.shape.w;
    energy = 1;
    float3 specularL = L;
    if (tanHalf > 0 || light.misc.y > 0)
    {
        float cosHalf = rsqrt(1.0 + tanHalf * tanHalf);
        float sinHalf = tanHalf * cosHalf;
        float DoR = dot(L, R);
        float3 S = R - DoR * L;
        specularL = DoR < cosHalf ? normalize(cosHalf * L + normalize(S + 1e-6) * sinHalf) : R;
        energy = ShapeEnergy(max(roughness * roughness, 1e-3), tanHalf + light.misc.y, 2.0);
    }
    return specularL;
}
#endif
