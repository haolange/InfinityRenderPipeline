#ifndef INFINITY_SG_DEPTH_ONLY_PASS
#define INFINITY_SG_DEPTH_ONLY_PASS
// DepthPass, ShadowPass / ShadowCaster and TranslucentDepthPass: depth only, alpha clipped.
float4 frag(PackedVaryings packedInput) : SV_Target
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    EvaluateInfinitySurface(input);
    return 0;
}
#endif
