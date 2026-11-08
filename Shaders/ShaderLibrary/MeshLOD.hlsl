#ifndef INFINITY_MESH_LOD
#define INFINITY_MESH_LOD

// LOD selection on the GPU; mirrors MeshLODSelection in C#. LODGroup semantics: level i draws while the screen size is at
// least its lower bound; the cross-fade band sits above each positive bound and is fadeWidth of the gap to the previous
// bound (1 for level 0). Screen size is the largest half-extent * scale * bias / distance, with distance 1 for
// orthographic views; the largest half-extent is LODGroup size / 2.
struct MeshLODChain
{
    float4 screenSize0;
    float4 screenSize1;
    float fadeWidth;
    int count;
};

StructuredBuffer<MeshLODChain> _EntityLodChains;
float4 _LodOrigin;
float _LodScale;
float _LodBias;
uint _LodOrthographic;

#define MESH_LOD_STABLE 65535u

float MeshLODScreenSize(MeshLODChain chain, uint level)
{
    // float4[variable] is out of range on Metal even when the other side of a ternary is taken.
    float size = chain.screenSize0.x;
    if (level == 1u) size = chain.screenSize0.y;
    else if (level == 2u) size = chain.screenSize0.z;
    else if (level == 3u) size = chain.screenSize0.w;
    else if (level == 4u) size = chain.screenSize1.x;
    else if (level == 5u) size = chain.screenSize1.y;
    else if (level == 6u) size = chain.screenSize1.z;
    else if (level == 7u) size = chain.screenSize1.w;
    return size;
}

// Packed visibility of one entity: 0 when no level draws, else 1 | level << 8 | fade << 16, where fade is
// MESH_LOD_STABLE outside the band and the band position quantized to [0, 65534) inside it.
uint MeshPackLODVisibility(uint entity, float3 center, float3 extent, float3 origin, float scale, float bias, uint orthographic)
{
    MeshLODChain chain = _EntityLodChains[entity];
    float distance = orthographic != 0u ? 1.0 : max(length(center - origin), 1e-6);
    float screen = max(max(extent.x, extent.y), extent.z) * scale * bias / distance;
    uint packed = 0u;
    bool found = false;
    [loop]
    for (uint level = 0u; level < (uint)chain.count && !found; ++level)
    {
        float threshold = MeshLODScreenSize(chain, level);
        if (screen >= threshold)
        {
            found = true;
            uint fade = MESH_LOD_STABLE;
            if (threshold > 0)
            {
                float previous = level == 0u ? 1.0 : MeshLODScreenSize(chain, level - 1u);
                float position = (screen - threshold) / (chain.fadeWidth * (previous - threshold));
                if (position < 1.0) fade = (uint)(saturate(position) * 65534.0);
            }
            packed = 1u | (level << 8) | (fade << 16);
        }
    }
    return packed;
}

uint MeshPackLODVisibility(uint entity, float3 center, float3 extent)
{
    return MeshPackLODVisibility(entity, center, extent, _LodOrigin.xyz, _LodScale, _LodBias, _LodOrthographic);
}

#endif
