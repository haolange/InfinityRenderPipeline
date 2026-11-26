#ifndef INFINITY_SG_MOTION_PASS
#define INFINITY_SG_MOTION_PASS
// MotionPass of InfinityLit: jitter-free surface velocity.
MotionOutput frag(PackedVaryings packedInput)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    EvaluateInfinitySurface(input);
    float4 currentClip, previousClip;
    InfinityMotionClip(input.positionWS, currentClip, previousClip);
    MotionOutput output;
    output.velocity = SurfaceVelocity(currentClip, previousClip);
    return output;
}
#endif
