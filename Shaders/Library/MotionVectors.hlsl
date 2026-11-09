#ifndef INFINITY_MOTION_VECTORS
#define INFINITY_MOTION_VECTORS
// Motion UV follows the sampled framebuffer convention (FlipY matrices), without jitter.
// Metadata is previous raw depth, current raw depth, valid, reserved.
struct MotionOutput
{
    float2 velocity : SV_Target0;
    float4 metadata : SV_Target1;
};
float2 SurfaceVelocity(float4 currentClip, float4 previousClip)
{
    if (currentClip.w <= 0 || previousClip.w <= 0) return 0;
    return (currentClip.xy / currentClip.w - previousClip.xy / previousClip.w) * 0.5;
}
float4 SurfaceMotionMetadata(float4 currentClip, float4 previousClip, float currentDepth)
{
    bool valid = currentClip.w > 0 && previousClip.w > 0;
    float previousDepth = valid ? previousClip.z / previousClip.w : 0;
    valid = valid && previousDepth >= 0 && previousDepth <= 1;
    return float4(previousDepth, currentDepth, valid ? 1 : 0, 0);
}
#endif
