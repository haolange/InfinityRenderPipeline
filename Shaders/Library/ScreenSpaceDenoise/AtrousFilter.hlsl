#ifndef INFINITY_SCREEN_SPACE_ATROUS_FILTER
#define INFINITY_SCREEN_SPACE_ATROUS_FILTER
// Edge-stopping weights for the a-trous spatial filters of the screen-space signals (Dammertz et al. 2010).
// The 5x5 kernel is the separable B3 spline (1/16, 1/4, 3/8, 1/4, 1/16); callers scale the tap spacing.

static const float kAtrousB3[3] = { 3.0 / 8.0, 1.0 / 4.0, 1.0 / 16.0 };

float AtrousKernelWeight(int2 tap)
{
    return kAtrousB3[abs(tap.x)] * kAtrousB3[abs(tap.y)];
}

// Relative eye-depth difference; tolerance is a fraction of the center depth so the filter behaves the same near and far.
float AtrousDepthWeight(float centerEyeDepth, float sampleEyeDepth, float relativeTolerance)
{
    return exp(-abs(centerEyeDepth - sampleEyeDepth) / max(centerEyeDepth * relativeTolerance, 1e-4));
}

float AtrousNormalWeight(float3 centerNormal, float3 sampleNormal, float power)
{
    return pow(saturate(dot(centerNormal, sampleNormal)), power);
}

// Roughness changes the lobe and therefore the signal; neighbors with a different roughness are not averaged in.
float AtrousRoughnessWeight(float centerRoughness, float sampleRoughness)
{
    return exp(-abs(centerRoughness - sampleRoughness) * 16.0);
}
#endif
