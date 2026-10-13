#ifndef INFINITY_SG_META_PASS
#define INFINITY_SG_META_PASS
// Meta pass of InfinityLit: albedo and emission for the lightmapper.
float4 frag(PackedVaryings packedInput) : SV_Target
{
    Varyings input = UnpackVaryings(packedInput);
    FInfinitySurface surface = EvaluateInfinitySurface(input);
    float4 result = 0;
    if (unity_MetaFragmentControl.x)
    {
        // Metals reflect their albedo specularly; the lightmapper bounces the diffuse part.
        float3 albedo = surface.baseColor * (1 - surface.metallic);
        result = float4(clamp(PositivePow(albedo, saturate(unity_OneOverOutputBoost)), 0, unity_MaxOutputValue), 1.0);
    }
    if (unity_MetaFragmentControl.y)
    {
        result = float4(unity_UseLinearSpace ? surface.emission : LinearToSRGB(surface.emission), 1.0);
    }
    return result;
}
#endif
