#ifndef INFINITY_MOTION_VECTORS
#define INFINITY_MOTION_VECTORS
// Motion UV (RG16F) follows the sampled framebuffer convention (FlipY matrices), without jitter.
struct MotionOutput
{
    float2 velocity : SV_Target0;
};
bool SurfaceMotionIsValid(float2 velocity)
{
    return all(isfinite(velocity)) && all(abs(velocity) <= 1.0);
}
float2 SurfaceVelocity(float4 currentClip, float4 previousClip)
{
    // A previous point behind the camera or an invalid clip cannot own temporal history.
    // This finite RG16F marker lies outside the range that can reproject onto the previous screen.
    float2 result = float2(2.0, 2.0);
    if (all(isfinite(currentClip)) && all(isfinite(previousClip)) && currentClip.w > 0 && previousClip.w > 0)
    {
        float2 velocity = (currentClip.xy / currentClip.w - previousClip.xy / previousClip.w) * 0.5;
        if (SurfaceMotionIsValid(velocity)) result = velocity;
    }
    return result;
}
#endif
