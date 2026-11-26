#ifndef INFINITY_MOTION_VECTORS
#define INFINITY_MOTION_VECTORS
// Motion UV (RG16F) follows the sampled framebuffer convention (FlipY matrices), without jitter.
struct MotionOutput
{
    float2 velocity : SV_Target0;
};
float2 SurfaceVelocity(float4 currentClip, float4 previousClip)
{
    if (currentClip.w <= 0 || previousClip.w <= 0) return 0;
    return (currentClip.xy / currentClip.w - previousClip.xy / previousClip.w) * 0.5;
}
#endif
