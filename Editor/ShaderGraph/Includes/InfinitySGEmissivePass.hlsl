#ifndef INFINITY_SG_EMISSIVE_PASS
#define INFINITY_SG_EMISSIVE_PASS
// Emission of a deferred opaque Shader Graph surface. A black graph output adds nothing.
float4 frag(PackedVaryings packedInput) : SV_Target
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    InfinitySurface surface = EvaluateInfinitySurface(input);
    return float4(surface.emission, 0);
}
#endif
