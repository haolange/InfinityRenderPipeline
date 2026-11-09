#ifndef INFINITY_INDIRECT_COMPOSITION
#define INFINITY_INDIRECT_COMPOSITION
float3 ReplaceIndirectContribution(float3 lighting, float3 existing, float3 premultipliedRadiance, float confidence)
{
    return lighting - existing * saturate(confidence) + premultipliedRadiance;
}
#endif
