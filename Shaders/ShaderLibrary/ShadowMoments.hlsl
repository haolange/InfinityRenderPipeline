#ifndef _ShadowMomentsInclude
#define _ShadowMomentsInclude
// Variance shadow maps (Donnelly and Lauritzen 2006) and exponential variance shadow maps (Lauritzen 2008).
// Moments are stored in RGBA16F: VSM (d, d^2, 0, 0); EVSM (e+, e+^2, e-, e-^2) with e+ = exp(c * (2d - 1)) and
// e- = -exp(-c * (2d - 1)). d is a linear depth in [0, 1] that grows away from the light. c <= 5.54 keeps e+^2 inside
// the half-float range.

#define SHADOW_MOMENT_VSM 0
#define SHADOW_MOMENT_EVSM 1

// Linear distance along the light from a stored depth. depthParams: x, y = m22, m23 of the GPU projection; w = 1 for
// orthographic.
float ShadowLinearDepth(float z, float4 depthParams)
{
    return depthParams.w != 0 ? (depthParams.y - z) / depthParams.x : depthParams.y / (z + depthParams.x);
}

// The moment depth: device depth for orthographic maps (already linear), linear distance times 1 / far otherwise.
float ShadowMomentDepth(float z, float4 depthParams, float invFar)
{
    float depth = 0;
    if (depthParams.w != 0)
    {
#if UNITY_REVERSED_Z
        depth = saturate(1.0 - z);
#else
        depth = saturate(z);
#endif
    }
    else
    {
        depth = saturate(ShadowLinearDepth(z, depthParams) * invFar);
    }
    return depth;
}

float2 ShadowMomentWarp(float depth, float exponent)
{
    float d = 2.0 * depth - 1.0;
    return float2(exp(exponent * d), -exp(-exponent * d));
}

float4 ShadowMomentsFromDepth(float depth, int mode, float exponent)
{
    float4 moments = float4(depth, depth * depth, 0, 0);
    if (mode == SHADOW_MOMENT_EVSM)
    {
        float2 warped = ShadowMomentWarp(depth, exponent);
        moments = float4(warped.x, warped.x * warped.x, warped.y, warped.y * warped.y);
    }
    return moments;
}

// One-sided Chebyshev upper bound of P(stored >= receiver).
float ChebyshevUpperBound(float2 moments, float receiver, float minVariance)
{
    float variance = max(moments.y - moments.x * moments.x, minVariance);
    // Half-float storage can lower an equal-depth mean. Bound that representation error, including
    // interpolation of same-sign means: half relative spacing plus the smallest subnormal step.
    float meanError = abs(moments.x) * (1.0 / 1024.0) + (1.0 / 16777216.0);
    float delta = max(receiver - moments.x - meanError, 0);
    float pMax = variance / (variance + delta * delta);
    return delta <= 0 ? 1.0 : pMax;
}

// Light bleeding reduction: maps [amount, 1] to [0, 1].
float ReduceLightBleeding(float pMax, float amount)
{
    return saturate((pMax - amount) / max(1.0 - amount, 1e-4));
}

// params: x = EVSM exponent, y = light-bleeding reduction, z = minimum standard deviation in depth units.
float ShadowMomentVisibility(float4 moments, float depth, int mode, float4 params)
{
    float visibility;
    if (mode == SHADOW_MOMENT_EVSM)
    {
        float exponent = params.x;
        float2 warped = ShadowMomentWarp(depth, exponent);
        // The minimum standard deviation is given in depth units; the warp scales it by its derivative 2c |e|.
        float2 scale = params.z * 2.0 * exponent * abs(warped);
        float positive = ChebyshevUpperBound(moments.xy, warped.x, scale.x * scale.x);
        float negative = ChebyshevUpperBound(moments.zw, warped.y, scale.y * scale.y);
        visibility = min(positive, negative);
    }
    else
    {
        visibility = ChebyshevUpperBound(moments.xy, depth, params.z * params.z);
    }
    return ReduceLightBleeding(visibility, params.y);
}
#endif
