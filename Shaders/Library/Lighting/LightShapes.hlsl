#ifndef INFINITY_LIGHT_SHAPES
#define INFINITY_LIGHT_SHAPES
// Shape-aware evaluation of the light record (no LTC).
//   Specular: Karis 2013 representative point (sphere, line / tube, rect, disk, disk + cone, sun disk) with
//             quadrature energy widening and anisotropic two-axis normalization.
//   Diffuse:  Frostbite V3 horizon-clipped form factors: Sutherland-Hodgman clipped polygon for rect, exact tilted-disk
//             irradiance with horizon clip for disk / disk + cone, horizon-clipped Karis segment for line, and
//             Frostbite V3 Lambertian capsule (clipped ribbon + hemispherical end-caps) for tube.
// Units (D4):
//   Directional / Parallel Box -> Lux (direct illuminance at receiver).
//   Point / Sphere / Line / Tube / SpotCone / SpotPyramid -> Candela (radiant intensity I; far-field E / I = cos / d^2).
//   Rect / Disk / DiskCone -> Nits (luminance L; far-field E / L = area * cos_emit * cos / d^2).
// EvaluateLocalLightSourceShape returns multipliers relative to record.radiance.
#include "LightRecord.hlsl"

#define LIGHT_SHAPE_PI 3.14159265

struct LightSourceShapeSample
{
    // Diffuse: irradiance per unit record.radiance (E / I for Candela, E / L for Nits, N.L for Lux).
    float diffuseIrradiance;
    // Specular: representative direction, N.L along it, radiance falloff and energy normalization.
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
    float illuminance = 0.0;
    if (sinSigmaSqr > 0.0)
    {
        illuminance = LIGHT_SHAPE_PI * sinSigmaSqr * saturate(cosTheta);
        if (cosTheta * cosTheta <= sinSigmaSqr)
        {
            float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
            float x = sqrt(max(1.0 / sinSigmaSqr - 1.0, 1e-6));
            float y = -x * (cosTheta / max(sinTheta, 1e-6));
            float sinThetaSqrtY = sinTheta * sqrt(saturate(1.0 - y * y));
            illuminance = (cosTheta * acos(clamp(y, -1.0, 1.0)) - x * sinThetaSqrtY) * sinSigmaSqr + atan(sinThetaSqrtY / x);
            illuminance = max(illuminance, 0.0);
        }
    }
    return illuminance;
}

// Energy conservation for a lobe widened in quadrature by a source of angular half-size sin(theta): (alpha / alpha')^power.
float ShapeEnergy(float alpha, float angularSize, float power)
{
    float widened = sqrt(alpha * alpha + 0.25 * angularSize * angularSize);
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
// Continuous across grazing reflection rays (dot(R, normal) == 0).
float3 PlanarRepresentativePoint(LightRecord light, float3 center, float3 R, bool rect, float2 halfSize, float radius, out float2 emitterUV)
{
    float3 normal = light.forward.xyz;
    float3 right = light.right.xyz;
    float3 up = LightUp(light);
    float zCenter = max(-dot(center, normal), 0.0);
    float approach = -dot(R, normal);
    float2 centerPlane = float2(dot(-center, right), dot(-center, up));
    float2 rPlane = float2(dot(R, right), dot(R, up));
    float extent = max(max(halfSize.x, halfSize.y), max(radius, 1e-4));
    float t = approach > 1e-4
        ? (zCenter / approach)
        : (zCenter + max(-approach, 0.0) * (length(centerPlane) + extent)) * 1e4;
    float2 planar = centerPlane + rPlane * t;
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

// Lambert form factor of the horizon-clipped rect polygon, times pi (E / L).
float RectIlluminance(float3 center, float3 right, float3 up, float2 halfSize, float3 N)
{
    float3 dx = right * halfSize.x;
    float3 dy = up * halfSize.y;
    float dC = dot(center, N);
    float dX = dot(dx, N);
    float dY = dot(dy, N);
    float result = 0.0;
    if (dC + abs(dX) + abs(dY) > 0.0)
    {
        float3 v[4];
        v[0] = center - dx - dy;
        v[1] = center + dx - dy;
        v[2] = center + dx + dy;
        v[3] = center - dx + dy;

        // Affine horizon distances avoid float32 rounding sign flips on nearly coplanar quads.
        float A = dX + dY;
        float B = dX - dY;
        float d[4] = { dC - A, dC + B, dC + A, dC - B };

        float3 poly[6] = (float3[6])0;
        int count = 0;
        float3 prev = v[3];
        float prevD = d[3];
        [unroll]
        for (int i = 0; i < 4; ++i)
        {
            float3 curr = v[i];
            float currD = d[i];
            if ((prevD >= 0.0) != (currD >= 0.0))
            {
                float denom = prevD - currD;
                float safeDenom = abs(denom) > 1e-12 ? denom : 1.0;
                float t = abs(denom) > 1e-12 ? saturate(prevD / safeDenom) : 0.0;
                poly[count++] = lerp(prev, curr, t);
            }
            if (currD >= 0.0)
            {
                poly[count++] = curr;
            }
            prev = curr;
            prevD = currD;
        }
        if (count >= 3)
        {
            float form = 0.0;
            [loop]
            for (int e = 0; e < count; ++e)
            {
                float3 a = poly[e];
                float3 b = poly[(e + 1) < count ? (e + 1) : 0];
                float lenProd = length(a) * length(b);
                float3 c = cross(b, a);
                float cLen = length(c);
                if (cLen > 1e-6 * lenProd)
                {
                    form += atan2(cLen, dot(a, b)) * (dot(c, N) / cLen);
                }
            }
            result = max(form, 0.0) * 0.5;
        }
    }
    return result;
}

// Exact tilted-disk vector irradiance with Frostbite horizon clip (returns E / L).
float DiskIlluminance(float3 center, float3 diskNormal, float radius, float3 N)
{
    float result = 0.0;
    float h = max(-dot(center, diskNormal), 0.0);
    if (h > 1e-6 && radius > 1e-6)
    {
        float invR = rcp(radius);
        float H = h * invR;
        float3 pPara = center + h * diskNormal;
        float R2 = dot(pPara, pPara) * invR * invR;
        float H2 = H * H;
        float a = H2 + R2;
        float X = 1.0 + a;
        float S = sqrt(max(X * X - 4.0 * R2, 1e-12));
        float fPerp = (a >= 1.0)
            ? (2.0 * LIGHT_SHAPE_PI * H2) / max(S * (S + (a - 1.0)), 1e-12)
            : 0.5 * LIGHT_SHAPE_PI * (1.0 + (1.0 - a) / S);
        float3 E = fPerp * (-diskNormal) + ((2.0 * LIGHT_SHAPE_PI * H) / max(S * (S + X), 1e-12)) * (pPara * invR);
        float lenE = length(E);
        if (lenE > 1e-12)
        {
            float3 Le = E / lenE;
            float sinSigmaSqr = min(lenE / LIGHT_SHAPE_PI, 0.9999);
            result = IlluminanceSphereOrDisk(dot(N, Le), sinSigmaSqr);
        }
    }
    return result;
}

// Range window, spot window, emitter side and barn doors of a local light (no inverse-square term).
float LocalLightWindow(LightRecord light, float3 positionWS)
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
            float distSqr = dot(fromLight, fromLight);
            if (distSqr <= 1e-12)
            {
                window = 0;
            }
            else
            {
                float facing = dot(fromLight * rsqrt(distSqr), light.forward.xyz);
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
    }
    return window;
}

LightSourceShapeSample EvaluateLocalLightSourceShape(LightRecord light, float3 positionWS, float3 N, float3 V, float roughness)
{
    LightSourceShapeSample s = (LightSourceShapeSample)0;
    float3 R = reflect(-V, N);
    float alpha = max(roughness * roughness, 1e-3);
    float3 center = light.positionRange.xyz - positionWS;
    float centerDistSqr = max(dot(center, center), 1e-8);
    float centerDist = sqrt(centerDistSqr);
    float3 Lc = center / centerDist;
    float radius = light.shape.x;
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
        s.diffuseIrradiance = rect
            ? RectIlluminance(center, light.right.xyz, LightUp(light), halfSize, N)
            : DiskIlluminance(center, light.forward.xyz, radius, N);

        float3 representative = PlanarRepresentativePoint(light, center, R, rect, halfSize, radius, s.emitterUV);
        float repDistSqr = max(dot(representative, representative), 1e-8);
        s.specularL = representative * rsqrt(repDistSqr);
        s.specularNoL = saturate(dot(N, s.specularL));
        float cosEmit = saturate(dot(light.forward.xyz, -s.specularL));
        if (rect)
        {
            float cosRight = dot(s.specularL, light.right.xyz);
            float cosUp = dot(s.specularL, LightUp(light));
            float wProj = halfSize.x * sqrt(saturate(1.0 - cosRight * cosRight));
            float hProj = halfSize.y * sqrt(saturate(1.0 - cosUp * cosUp));
            float invScale = 4.0 / LIGHT_SHAPE_PI;
            float denomX = sqrt(repDistSqr + invScale * wProj * wProj);
            float denomY = sqrt(repDistSqr + invScale * hProj * hProj);
            float dC = dot(center, N);
            float dExtent = abs(dot(light.right.xyz * halfSize.x, N)) + abs(dot(LightUp(light) * halfSize.y, N));
            float horizonFactor = (dC - dExtent) >= 0.0 ? 1.0 : saturate((dC + dExtent) / max(2.0 * dExtent, 1e-6));
            s.specularFalloff = (area * cosEmit / max(denomX * denomY, 1e-8)) * horizonFactor;
            float thetaX = wProj * rsqrt(repDistSqr + wProj * wProj);
            float thetaY = hProj * rsqrt(repDistSqr + hProj * hProj);
            s.specularEnergy = ShapeEnergy(alpha, thetaX, 1.0) * ShapeEnergy(alpha, thetaY, 1.0);
        }
        else
        {
            float rMinor = radius * cosEmit;
            float denomMaj = sqrt(repDistSqr + radius * radius);
            float denomMin = sqrt(repDistSqr + rMinor * rMinor);
            s.specularFalloff = area * cosEmit / max(denomMaj * denomMin, 1e-8);
            s.specularEnergy = ShapeEnergy(alpha, radius / denomMaj, 1.0) * ShapeEnergy(alpha, rMinor / denomMin, 1.0);
        }
    }
    else if (sourceLength > 0)
    {
        // Line or tube along the light's right axis (Candela convention).
        float3 u = light.right.xyz;
        float3 axis = u * (0.5 * sourceLength);
        float3 p0 = center - axis;
        float3 p1 = center + axis;
        float len0 = max(sqrt(dot(p0, p0)), 1e-4);
        float len1 = max(sqrt(dot(p1, p1)), 1e-4);

        // Horizon-clip [p0, p1] against dot(p, N) >= 0.
        float n0 = dot(N, p0);
        float n1 = dot(N, p1);
        float3 c0 = p0;
        float3 c1 = p1;
        float clipFraction = 1.0;
        if (n0 <= 0.0 && n1 <= 0.0)
        {
            clipFraction = 0.0;
        }
        else if (n0 < 0.0)
        {
            float t = saturate(n0 / min(n0 - n1, -1e-6));
            c0 = lerp(p0, p1, t);
            clipFraction = 1.0 - t;
        }
        else if (n1 < 0.0)
        {
            float t = saturate(n1 / min(n1 - n0, -1e-6));
            c1 = lerp(p1, p0, t);
            clipFraction = 1.0 - t;
        }

        if (radius > 0.0)
        {
            float avgArea = 0.5 * LIGHT_SHAPE_PI * radius * sourceLength + LIGHT_SHAPE_PI * radius * radius;
            float3 pPerp = center - dot(center, u) * u;
            float hSqr = dot(pPerp, pPerp);
            float h = sqrt(hSqr);
            float sMid = clamp(-dot(center, u), -0.5 * sourceLength, 0.5 * sourceLength);
            float minDist = sqrt(max(hSqr + (dot(center, u) + sMid) * (dot(center, u) + sMid), 1e-8));
            float eRibbon = 0.0;
            if (h > 1e-5)
            {
                if (radius / minDist < 0.02)
                {
                    if (clipFraction > 0.0)
                    {
                        float s0 = dot(c0, u);
                        float s1 = dot(c1, u);
                        float d0Sqr = hSqr + s0 * s0;
                        float d1Sqr = hSqr + s1 * s1;
                        float angle = atan2(h * (s1 - s0), hSqr + s0 * s1);
                        float3 pPerpDir = pPerp / h;
                        float termPerp = (s1 / d1Sqr - s0 / d0Sqr) + angle / h;
                        float termAxis = h * (1.0 / d0Sqr - 1.0 / d1Sqr);
                        eRibbon = max(radius * (dot(N, pPerpDir) * termPerp + dot(N, u) * termAxis), 0.0);
                    }
                }
                else
                {
                    float3 v = normalize(cross(u, pPerp));
                    eRibbon = RectIlluminance(center, u, v, float2(0.5 * sourceLength, radius), N);
                }
            }
            float cap0 = IlluminanceSphereOrDisk(dot(N, p0 / len0), min(radius * radius / max(dot(p0, p0), 1e-8), 0.9999));
            float cap1 = IlluminanceSphereOrDisk(dot(N, p1 / len1), min(radius * radius / max(dot(p1, p1), 1e-8), 0.9999));
            s.diffuseIrradiance = (eRibbon + 0.5 * (cap0 + cap1)) / max(avgArea, 1e-6);
        }
        else if (clipFraction > 0.0)
        {
            float cLen0 = max(length(c0), 1e-4);
            float cLen1 = max(length(c1), 1e-4);
            float cDenom = max(cLen0 * cLen1 + dot(c0, c1), 1e-6);
            s.diffuseIrradiance = clipFraction * max(dot(N, c0) / cLen0 + dot(N, c1) / cLen1, 0.0) / cDenom;
        }

        float3 onSegment = SegmentRepresentativePoint(p0, p1, R);
        float3 representative = radius > 0.0 ? SphereRepresentativePoint(onSegment, R, radius) : onSegment;
        float repDistSqr = max(dot(representative, representative), 1e-8);
        float repDist = sqrt(repDistSqr);
        s.specularL = representative / repDist;
        s.specularNoL = saturate(dot(N, s.specularL));
        s.specularFalloff = 2.0 / max(len0 * len1 + dot(p0, p1), max(2.0 * radius * radius, 1e-6));
        float segDistSqr = max(dot(onSegment, onSegment), 1e-8);
        float segDist = sqrt(segDistSqr);
        float cosAxis = dot(s.specularL, u);
        float sinAxis = sqrt(saturate(1.0 - cosAxis * cosAxis));
        float halfL = 0.5 * sourceLength * sinAxis;
        float thetaPerp = min(radius / segDist, 1.0);
        float thetaPara = min(halfL * rsqrt(segDistSqr + halfL * halfL) + radius / segDist, 1.0);
        s.specularEnergy = ShapeEnergy(alpha, thetaPerp, 1.0) * ShapeEnergy(alpha, thetaPara, 1.0);
    }
    else if (radius > 0)
    {
        float sinSigmaSqr = min(radius * radius / centerDistSqr, 0.9999);
        s.diffuseIrradiance = IlluminanceSphereOrDisk(dot(N, Lc), sinSigmaSqr) / (LIGHT_SHAPE_PI * radius * radius);
        float3 representative = SphereRepresentativePoint(center, R, radius);
        s.specularL = normalize(representative);
        s.specularNoL = saturate(dot(N, s.specularL));
        s.specularFalloff = 1.0 / max(centerDistSqr, radius * radius);
        s.specularEnergy = ShapeEnergy(alpha, min(radius / centerDist, 1.0), 2.0);
    }
    else
    {
        // Punctual.
        s.diffuseIrradiance = saturate(dot(N, Lc)) / centerDistSqr;
        s.specularL = Lc;
        s.specularNoL = saturate(dot(N, Lc));
        s.specularFalloff = 1.0 / centerDistSqr;
        s.specularEnergy = 1.0;
    }
    return s;
}

// Sun disk: the direction inside the disk closest to the reflection ray (Karis 2013), energy widened by the disk.
float3 SunDiskSpecularL(LightRecord light, float3 R, float roughness, out float energy)
{
    float3 L = DirectionalLightL(light);
    float tanHalf = light.shape.w;
    energy = 1;
    float3 specularL = L;
    if (tanHalf > 0)
    {
        float cosHalf = rsqrt(1.0 + tanHalf * tanHalf);
        float sinHalf = tanHalf * cosHalf;
        float DoR = dot(L, R);
        float3 S = R - DoR * L;
        specularL = DoR < cosHalf ? normalize(cosHalf * L + normalize(S + 1e-6) * sinHalf) : R;
        energy = ShapeEnergy(max(roughness * roughness, 1e-3), sinHalf, 2.0);
    }
    return specularL;
}

// Shape-consistent local light direction and radiance attenuation for volumetric fog / clouds.
void EvaluateVolumeLocalLight(LightRecord light, float3 worldPos, out float3 L, out float attenuation)
{
    attenuation = LocalLightWindow(light, worldPos);
    L = float3(0.0, 1.0, 0.0);
    if (attenuation > 0.0)
    {
        float3 toLight = light.positionRange.xyz - worldPos;
        float distSqr = dot(toLight, toLight);
        if (LightHasFlag(light, LIGHT_FLAG_SPOT_BOX))
        {
            L = -light.forward.xyz;
        }
        else if (IsPlanarLight(light))
        {
            bool rect = IsRectLight(light);
            float area = rect ? light.area.x * light.area.y : LIGHT_SHAPE_PI * light.shape.x * light.shape.x;
            float safeDistSqr = max(distSqr, 1e-8);
            L = toLight * rsqrt(safeDistSqr);
            float cosEmit = saturate(dot(light.forward.xyz, -L));
            attenuation *= area * cosEmit / (safeDistSqr + area / LIGHT_SHAPE_PI);
        }
        else if (light.shape.z > 0.0)
        {
            float halfLen = 0.5 * light.shape.z;
            float3 u = light.right.xyz;
            float3 axis = u * halfLen;
            float3 p0 = toLight - axis;
            float3 p1 = toLight + axis;
            float proj = clamp(-dot(toLight, u), -halfLen, halfLen);
            float3 closest = toLight + u * proj;
            float closestDistSqr = max(dot(closest, closest), 1e-8);
            L = closest * rsqrt(closestDistSqr);
            float len0 = max(sqrt(dot(p0, p0)), 1e-4);
            float len1 = max(sqrt(dot(p1, p1)), 1e-4);
            attenuation *= 2.0 / max(len0 * len1 + dot(p0, p1), max(2.0 * light.shape.x * light.shape.x, 1e-4));
        }
        else
        {
            float safeDistSqr = max(distSqr, 1e-8);
            L = toLight * rsqrt(safeDistSqr);
            attenuation /= max(distSqr, max(light.shape.x * light.shape.x, 1e-4));
        }
    }
}
#endif
