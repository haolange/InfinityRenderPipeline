#ifndef INFINITY_SCREEN_SPACE_RAY_TRACE
#define INFINITY_SCREEN_SPACE_RAY_TRACE
#include "../../ScreenSpaceDepth.hlsl"
#include "../../HiZ.hlsl"
#include "../../Common.hlsl"
#include "../../Random.hlsl"
#include "../../Montcalo.hlsl"

// A screen-space ray is clipped to the view near/far slab, then to the screen and to raw depth.
// There is no world-space maximum distance. NDC z is interpolated across that clipped segment.
// SCREEN_TRACE_BEHIND keeps walking after the ray is past a surface. SCREEN_TRACE_TOWARD allows
// rays that move toward the camera (view +Z). Both flags are uniform for a dispatch.
#define SCREEN_TRACE_BEHIND 1u
#define SCREEN_TRACE_TOWARD 2u

float GetScreenFadeBord(float2 uv, float width)
{
    return saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) / max(width, 1e-5));
}

struct FScreenSpaceRay
{
    float3 start;
    float3 delta;
    float pixelLength;
    bool valid;
};

// 0 hit, 1 left the screen or the clipped segment, 2 stopped behind a surface, 3 exhausted the step budget.
#define SCREEN_TRACE_EXIT_HIT 0
#define SCREEN_TRACE_EXIT_CLIP 1
#define SCREEN_TRACE_EXIT_BEHIND 2
#define SCREEN_TRACE_EXIT_BUDGET 3

struct FScreenSpaceHit
{
    float2 uv;
    float rawDepth;
    int exitReason;
};

bool ClipRange(float start, float delta, float low, float high, inout float t0, inout float t1)
{
    bool valid = true;
    if (abs(delta) <= 1e-8)
    {
        valid = start >= low && start <= high;
    }
    else
    {
        float a = (low - start) / delta;
        float b = (high - start) / delta;
        t0 = max(t0, min(a, b));
        t1 = min(t1, max(a, b));
        valid = t1 > t0;
    }
    return valid;
}

// Leaves the origin surface by 1cm, clips to the near/far view planes, then to UV and raw depth.
bool InitScreenSpaceRay(float3 originVS, float3 directionVS, float4x4 projection, out FScreenSpaceRay ray)
{
    ray = (FScreenSpaceRay)0;
    float dirLength = length(directionVS);
    bool valid = dirLength > 1e-6;
    float3 direction = valid ? directionVS / dirLength : float3(0, 0, 1);
    float3 origin = originVS + direction * 0.01;
    float t0 = 0;
    float t1 = 1e7;
    float nearZ = -ScreenSpaceDepthParams.x;
    float farZ = -ScreenSpaceDepthParams.y;
    if (abs(direction.z) <= 1e-8)
    {
        valid = valid && origin.z <= nearZ && origin.z >= farZ;
    }
    else
    {
        float tNear = (nearZ - origin.z) / direction.z;
        float tFar = (farZ - origin.z) / direction.z;
        t0 = max(t0, min(tNear, tFar));
        t1 = min(t1, max(tNear, tFar));
        valid = valid && t1 > max(t0, 0);
    }
    t0 = max(t0, 0);
    float4 startClip = mul(projection, float4(origin + direction * t0, 1));
    float4 endClip = mul(projection, float4(origin + direction * t1, 1));
    valid = valid && startClip.w > 1e-5 && endClip.w > 1e-5;
    float3 start = valid ? startClip.xyz / startClip.w : 0;
    float3 end = valid ? endClip.xyz / endClip.w : 0;
    start.xy = start.xy * 0.5 + 0.5;
    end.xy = end.xy * 0.5 + 0.5;
    float u0 = 0;
    float u1 = 1;
    valid = valid && ClipRange(start.x, end.x - start.x, 0, 1, u0, u1);
    valid = valid && ClipRange(start.y, end.y - start.y, 0, 1, u0, u1);
    valid = valid && ClipRange(start.z, end.z - start.z, 0, 1, u0, u1);
    float3 clippedStart = start + (end - start) * u0;
    float3 clippedEnd = start + (end - start) * u1;
    ray.start = clippedStart;
    ray.delta = clippedEnd - clippedStart;
    ray.valid = valid;
    return valid;
}

void ScreenTraceSize(Texture2D<float> surfaceDepth, float4 surfaceView, Texture2D<float> depthPyramid, float4 pyramidView,
    out uint2 fullSize, out uint mipCount)
{
    uint width, height, pyramidMips;
    depthPyramid.GetDimensions(0, width, height, pyramidMips);
    if (pyramidView.x > 0) pyramidMips = (uint)(1 + floor(log2(max(pyramidView.x, pyramidView.y))));
    surfaceDepth.GetDimensions(width, height);
    if (surfaceView.x > 0)
    {
        width = (uint)surfaceView.x;
        height = (uint)surfaceView.y;
    }
    fullSize = uint2(width, height);
    mipCount = pyramidMips + 1; // Level 0 is the original surface, before the quad reduction.
}

float LoadTraceDepth(Texture2D<float> surfaceDepth, Texture2D<float> pyramid, uint2 cell, uint level)
{
    return level == 0 ? surfaceDepth.Load(int3(cell, 0)).r : pyramid.Load(int3(cell, (int)level - 1)).r;
}

// 0 = the segment is still in front of the depth interval, 1 = it crosses that interval, 2 = it is past it.
// A full-resolution texel is an interval whose closest and furthest depths are the same value.
int ClassifyTraceCell(float z0, float z1, float closest, float furthest, bool toward)
{
    int cls = 1;
#if UNITY_REVERSED_Z
    float closerRaw = max(z0, z1);
    float fartherRaw = min(z0, z1);
    if (fartherRaw > closest) cls = 0;
    else if (closerRaw < furthest) cls = 2;
#else
    float closerRaw = min(z0, z1);
    float fartherRaw = max(z0, z1);
    if (fartherRaw < closest) cls = 0;
    else if (closerRaw > furthest) cls = 2;
#endif
    // Sky is empty, not a surface the ray has passed. Only a real surface swaps for a toward ray.
    if (toward && !ScreenSpaceIsFarDepth(closest) && cls != 1) cls = 2 - cls;
    return cls;
}

// A plane's raw depth is linear in screen position. Adjacent texels of one plane therefore form a depth step,
// and a discrete march can enter the next texel already behind it. k is a fraction of that step, not meters.
static const float kTraceRiserTolerance = 0.25;
static const float kTraceRelativeThickness = 0.02;

bool DepthStepColinear(float a, float mid, float c)
{
    return abs(mid - lerp(a, c, 0.5)) <= kTraceRiserTolerance * abs(c - a) + 1e-5;
}

bool LoadTexelDepth(Texture2D<float> surface, uint2 fullSize, int2 texel, out float depth)
{
    depth = 0;
    bool inside = texel.x >= 0 && texel.y >= 0 && texel.x < (int)fullSize.x && texel.y < (int)fullSize.y;
    if (inside) depth = surface.Load(int3(texel, 0)).r;
    return inside;
}

// The segment is already behind this full-resolution texel. Accept it when the shared edge still lies on the
// depth interval of a locally planar neighbor, or, in the relative-thickness variant, within 2% of eye depth.
bool AcceptDepthRiser(FScreenSpaceRay ray, float3 samplePos, uint2 cell, uint2 fullSize,
    Texture2D<float> surfaceDepth, float surface, out FScreenSpaceHit hit)
{
    hit = (FScreenSpaceHit)0;
    bool accepted = false;
    bool surfaceReal = !ScreenSpaceIsFarDepth(surface);
#if defined(SCREEN_TRACE_RELATIVE_THICKNESS)
    float surfaceEye = ScreenSpaceLinearEyeDepth(surface);
    float rayEye = ScreenSpaceLinearEyeDepth(samplePos.z);
    accepted = surfaceReal && rayEye >= surfaceEye && (rayEye - surfaceEye) <= kTraceRelativeThickness * max(surfaceEye, 1e-5);
#else
    float spanX = abs(ray.delta.x) * fullSize.x;
    float spanY = abs(ray.delta.y) * fullSize.y;
    bool alongX = spanX >= spanY && spanX > 1e-4;
    bool alongY = !alongX && spanY > 1e-4;
    int dir = 0;
    if (alongX) dir = ray.delta.x >= 0 ? 1 : -1;
    else if (alongY) dir = ray.delta.y >= 0 ? 1 : -1;
    float dC = surface;
    float dP = 0, dBack = 0, dForward = 0;
    int2 texel = int2(cell);
    int2 back = texel, further = texel, forward = texel;
    if (alongX)
    {
        back.x -= dir; further.x -= 2 * dir; forward.x += dir;
    }
    else if (alongY)
    {
        back.y -= dir; further.y -= 2 * dir; forward.y += dir;
    }
    bool hasP = (alongX || alongY) && LoadTexelDepth(surfaceDepth, fullSize, back, dP);
    bool hasBack = LoadTexelDepth(surfaceDepth, fullSize, further, dBack);
    bool hasForward = LoadTexelDepth(surfaceDepth, fullSize, forward, dForward);
    float2 lower, upper;
    HiZCellBounds(cell, fullSize, 0, lower, upper);
    float edge = 0, startAxis = 0, deltaAxis = 0;
    if (alongX)
    {
        edge = dir > 0 ? lower.x : upper.x;
        startAxis = ray.start.x;
        deltaAxis = ray.delta.x;
    }
    else if (alongY)
    {
        edge = dir > 0 ? lower.y : upper.y;
        startAxis = ray.start.y;
        deltaAxis = ray.delta.y;
    }
    float zBoundary = samplePos.z;
    if (abs(deltaAxis) > 1e-8)
        zBoundary = ray.start.z + ray.delta.z * clamp((edge - startAxis) / deltaAxis, 0, 1);
    bool between = hasP && zBoundary >= min(dP, dC) && zBoundary <= max(dP, dC);
    bool lineBack = hasP && hasBack && DepthStepColinear(dBack, dP, dC);
    bool lineForward = hasP && hasForward && DepthStepColinear(dP, dC, dForward);
    accepted = surfaceReal && between && (lineBack || lineForward);
#endif
    if (accepted)
    {
        hit.uv = (float2(cell) + 0.5) / float2(fullSize);
        hit.rawDepth = surface;
        hit.exitReason = SCREEN_TRACE_EXIT_HIT;
    }
    return accepted;
}

bool WriteTraceHit(Texture2D<float> surfaceDepth, uint2 fullSize, float2 uv, out FScreenSpaceHit hit)
{
    hit = (FScreenSpaceHit)0;
    uint2 texel = min((uint2)(uv * float2(fullSize)), fullSize - 1);
    float surface = surfaceDepth.Load(int3(texel, 0)).r;
    bool found = !ScreenSpaceIsFarDepth(surface);
    if (found)
    {
        hit.uv = (float2(texel) + 0.5) / float2(fullSize);
        hit.rawDepth = surface;
    }
    return found;
}

bool RefineTraceCrossing(FScreenSpaceRay ray, float t0, float t1, uint binarySteps, bool toward,
    Texture2D<float> surfaceDepth, uint2 fullSize, out FScreenSpaceHit hit)
{
    float lo = t0;
    float hi = t1;
    // Zero binary steps keep the texel under the crossing sample.
    [loop]
    for (uint i = 0; i < binarySteps; ++i)
    {
        float mid = 0.5 * (lo + hi);
        float3 samplePos = ray.start + ray.delta * mid;
        uint2 texel = min((uint2)(samplePos.xy * float2(fullSize)), fullSize - 1);
        float surface = surfaceDepth.Load(int3(texel, 0)).r;
        int cls = ClassifyTraceCell(samplePos.z, samplePos.z, surface, surface, toward);
        if (cls == 0) lo = mid;
        else hi = mid;
    }
    // The last segment must still cross the texel it landed on. A silhouette whose depth only
    // overlapped the long stride is not a hit; the march then follows the trace-behind rule.
    float3 endPos = ray.start + ray.delta * hi;
    float3 startPos = ray.start + ray.delta * lo;
    uint2 endTexel = min((uint2)(endPos.xy * float2(fullSize)), fullSize - 1);
    float endSurface = surfaceDepth.Load(int3(endTexel, 0)).r;
    int endClass = ClassifyTraceCell(startPos.z, endPos.z, endSurface, endSurface, toward);
    bool crossed = endClass == 1;
    hit = (FScreenSpaceHit)0;
    if (crossed) crossed = WriteTraceHit(surfaceDepth, fullSize, endPos.xy, hit);
    if (!crossed && endClass == 2)
        crossed = AcceptDepthRiser(ray, endPos, endTexel, fullSize, surfaceDepth, endSurface, hit);
    return crossed;
}

bool TraceScreenSpaceHiZ(float3 originVS, float3 directionVS, uint budget, float jitter, uint flags,
    float4x4 projection, Texture2D<float> surfaceDepth, float4 surfaceView, Texture2D<float> closestPyramid, float4 closestView,
    Texture2D<float> furthestPyramid, out FScreenSpaceHit hit)
{
    hit = (FScreenSpaceHit)0;
    bool found = false;
    int exitReason = SCREEN_TRACE_EXIT_CLIP;
    bool allow = (flags & SCREEN_TRACE_TOWARD) != 0 || directionVS.z <= 0;
    FScreenSpaceRay ray;
    bool valid = allow && InitScreenSpaceRay(originVS, directionVS, projection, ray);
    if (valid)
    {
        uint2 fullSize;
        uint mipCount;
        ScreenTraceSize(surfaceDepth, surfaceView, closestPyramid, closestView, fullSize, mipCount);
        ray.pixelLength = max(abs(ray.delta.x) * fullSize.x, abs(ray.delta.y) * fullSize.y);
        bool behind = (flags & SCREEN_TRACE_BEHIND) != 0;
        // Depth increases toward the camera under reversed Z. That is what the cell test has to follow.
        bool toward = UNITY_REVERSED_Z ? ray.delta.z > 1e-6 : ray.delta.z < -1e-6;
        // A ray with almost no screen motion still has a depth segment, so it starts at the origin.
        float t = ray.pixelLength >= 1 ? min(1, (1.01 + saturate(jitter)) / ray.pixelLength) : 0;
        int level = min(2, (int)mipCount - 1);
        bool walking = true;
        // Trace-behind does not stop on a cell the ray has passed. Remember that full-resolution event so a later
        // budget or screen exit is not mistaken for a ray that was still in front of every surface.
        bool passedSurface = false;
        exitReason = SCREEN_TRACE_EXIT_BUDGET;
        [loop]
        for (uint iteration = 0; iteration < budget * 4 && walking && !found; ++iteration)
        {
            float3 samplePos = ray.start + ray.delta * min(t, 1);
            walking = all(samplePos.xy >= 0) && all(samplePos.xy < 1);
            if (!walking)
            {
                exitReason = SCREEN_TRACE_EXIT_CLIP;
            }
            else
            {
                uint2 cell = HiZCell(samplePos.xy, fullSize, (uint)level);
                float2 lower, upper;
                HiZCellBounds(cell, fullSize, (uint)level, lower, upper);
                float2 boundary = float2(ray.delta.x >= 0 ? upper.x : lower.x, ray.delta.y >= 0 ? upper.y : lower.y);
                float tx = abs(ray.delta.x) > 1e-8 ? (boundary.x - samplePos.x) / ray.delta.x : 1e7;
                float ty = abs(ray.delta.y) > 1e-8 ? (boundary.y - samplePos.y) / ray.delta.y : 1e7;
                float nextT = min(1, t + max(1e-7, min(tx, ty)));
                float endZ = ray.start.z + ray.delta.z * nextT;
                float closest = LoadTraceDepth(surfaceDepth, closestPyramid, cell, (uint)level);
                float furthest = level == 0 ? closest : LoadTraceDepth(surfaceDepth, furthestPyramid, cell, (uint)level);
                int cls = ClassifyTraceCell(samplePos.z, endZ, closest, furthest, toward);
                // A coarse cell that the ray has entered can still contain the crossing. Only a full-resolution
                // texel decides that the ray is past the surface.
                if ((cls == 1 || cls == 2) && level > 0)
                {
                    level--;
                }
                else if (cls == 1)
                {
                    hit.uv = (float2(cell) + 0.5) / float2(fullSize);
                    hit.rawDepth = closest;
                    found = true;
                    walking = false;
                    exitReason = SCREEN_TRACE_EXIT_HIT;
                }
                else if (cls == 2 && level == 0)
                {
                    FScreenSpaceHit riser = (FScreenSpaceHit)0;
                    bool accepted = AcceptDepthRiser(ray, samplePos, cell, fullSize, surfaceDepth, closest, riser);
                    if (accepted)
                    {
                        hit = riser;
                        found = true;
                        walking = false;
                        exitReason = SCREEN_TRACE_EXIT_HIT;
                    }
                    else
                    {
                        passedSurface = true;
                        if (!behind)
                        {
                            walking = false;
                            exitReason = SCREEN_TRACE_EXIT_BEHIND;
                        }
                        else
                        {
                            t = nextT + (ray.pixelLength >= 1 ? 1e-3 / ray.pixelLength : 1);
                            walking = t < 1;
                            if (!walking) exitReason = SCREEN_TRACE_EXIT_CLIP;
                            level = min(level + 1, (int)mipCount - 1);
                        }
                    }
                }
                else
                {
                    t = nextT + (ray.pixelLength >= 1 ? 1e-3 / ray.pixelLength : 1);
                    walking = t < 1;
                    if (!walking) exitReason = SCREEN_TRACE_EXIT_CLIP;
                    level = min(level + 1, (int)mipCount - 1);
                }
            }
        }
        if (!found && passedSurface) exitReason = SCREEN_TRACE_EXIT_BEHIND;
    }
    hit.exitReason = found ? SCREEN_TRACE_EXIT_HIT : exitReason;
    return found;
}

// 2D march along the clipped segment. The stride is max(1, pixelLength / steps) pixels.
// A crossing is refined with binarySteps samples, then snapped to the surface texel.
bool TraceScreenSpaceLinear(float3 originVS, float3 directionVS, uint steps, uint binarySteps, float jitter, uint flags,
    float4x4 projection, Texture2D<float> surfaceDepth, float4 surfaceView, Texture2D<float> closestPyramid, float4 closestView,
    Texture2D<float> furthestPyramid, out FScreenSpaceHit hit)
{
    hit = (FScreenSpaceHit)0;
    bool found = false;
    int exitReason = SCREEN_TRACE_EXIT_CLIP;
    bool allow = (flags & SCREEN_TRACE_TOWARD) != 0 || directionVS.z <= 0;
    FScreenSpaceRay ray;
    bool valid = allow && InitScreenSpaceRay(originVS, directionVS, projection, ray);
    if (valid)
    {
        uint2 fullSize;
        uint mipCount;
        ScreenTraceSize(surfaceDepth, surfaceView, closestPyramid, closestView, fullSize, mipCount);
        // Both pyramids stay referenced in this kernel. The march itself reads full-resolution depth.
        float pyramidsLive = closestPyramid.Load(int3(0, 0, 0)) + furthestPyramid.Load(int3(0, 0, 0));
        ray.pixelLength = max(abs(ray.delta.x) * fullSize.x, abs(ray.delta.y) * fullSize.y);
        ray.pixelLength = isfinite(pyramidsLive) ? ray.pixelLength : 0;
        bool behind = (flags & SCREEN_TRACE_BEHIND) != 0;
        bool toward = UNITY_REVERSED_Z ? ray.delta.z > 1e-6 : ray.delta.z < -1e-6;
        steps = max(steps, 1u);
        // No screen motion still has a depth segment: test that one texel, then the march has nothing to do.
        if (ray.pixelLength < 1)
        {
            float z1 = ray.start.z + ray.delta.z;
            uint2 texel = min((uint2)(ray.start.xy * float2(fullSize)), fullSize - 1);
            float surface = surfaceDepth.Load(int3(texel, 0)).r;
            int cls = ClassifyTraceCell(ray.start.z, z1, surface, surface, toward);
            if (cls == 1)
            {
                hit.uv = (float2(texel) + 0.5) / float2(fullSize);
                hit.rawDepth = surface;
                found = true;
                exitReason = SCREEN_TRACE_EXIT_HIT;
            }
            else exitReason = cls == 2 ? SCREEN_TRACE_EXIT_BEHIND : SCREEN_TRACE_EXIT_CLIP;
        }
        float pixelT = ray.pixelLength >= 1 ? 1.0 / ray.pixelLength : 1;
        float stride = max(pixelT, 1.0 / steps);
        float t = min(1, pixelT * 1.01 + stride * saturate(jitter));
        float previous = 0;
        bool walking = !found && ray.pixelLength >= 1;
        bool passedSurface = false;
        if (walking) exitReason = SCREEN_TRACE_EXIT_BUDGET;
        [loop]
        for (uint i = 0; i < steps && walking && !found; ++i)
        {
            float3 samplePos = ray.start + ray.delta * t;
            walking = all(samplePos.xy >= 0) && all(samplePos.xy <= 1) && samplePos.z >= 0 && samplePos.z <= 1;
            if (!walking)
            {
                exitReason = SCREEN_TRACE_EXIT_CLIP;
            }
            else
            {
                uint2 texel = min((uint2)(samplePos.xy * float2(fullSize)), fullSize - 1);
                float surface = surfaceDepth.Load(int3(texel, 0)).r;
                float z0 = ray.start.z + ray.delta.z * previous;
                int cls = ClassifyTraceCell(z0, samplePos.z, surface, surface, toward);
                if (cls == 1)
                    found = RefineTraceCrossing(ray, previous, t, binarySteps, toward, surfaceDepth, fullSize, hit);
                if (found) exitReason = SCREEN_TRACE_EXIT_HIT;
                if (!found && cls == 2) passedSurface = true;
                if (!found && cls == 2 && !behind)
                {
                    walking = false;
                    exitReason = SCREEN_TRACE_EXIT_BEHIND;
                }
                previous = t;
                t += stride;
                walking = walking && t <= 1;
                if (!found && !walking && exitReason != SCREEN_TRACE_EXIT_BEHIND) exitReason = SCREEN_TRACE_EXIT_CLIP;
            }
        }
        if (!found && passedSurface) exitReason = SCREEN_TRACE_EXIT_BEHIND;
    }
    hit.exitReason = found ? SCREEN_TRACE_EXIT_HIT : exitReason;
    return found;
}
#endif
