#ifndef INFINITY_MOTION_VECTORS
#define INFINITY_MOTION_VECTORS
// Motion UV follows the sampled framebuffer convention (FlipY matrices), without jitter.
// Metadata (64-bit R32G32_SFloat): x = previous raw depth (or < 0 when invalid), y = current raw depth.
struct MotionOutput
{
    float2 velocity : SV_Target0;
    float2 metadata : SV_Target1;
};
float2 SurfaceVelocity(float4 currentClip, float4 previousClip)
{
    if (currentClip.w <= 0 || previousClip.w <= 0) return 0;
    return (currentClip.xy / currentClip.w - previousClip.xy / previousClip.w) * 0.5;
}
float2 SurfaceMotionMetadata(float4 currentClip, float4 previousClip, float currentDepth)
{
    bool valid = currentClip.w > 0 && previousClip.w > 0;
    float previousDepth = valid ? previousClip.z / previousClip.w : -1.0;
    valid = valid && previousDepth >= 0 && previousDepth <= 1;
    return float2(valid ? previousDepth : -1.0, currentDepth);
}
bool MotionMetadataIsValid(float2 metadata)
{
    return isfinite(metadata.x) && metadata.x >= 0.0 && metadata.x <= 1.0;
}
float MotionMetadataPreviousDepth(float2 metadata)
{
    return metadata.x;
}
float MotionMetadataCurrentDepth(float2 metadata)
{
    return metadata.y;
}
#endif
