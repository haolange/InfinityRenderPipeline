#ifndef INFINITY_MESH_LOD
#define INFINITY_MESH_LOD

// LOD selection on the GPU; mirrors MeshLodSelection in C#. LODGroup semantics: level i draws while the screen size is at
// least its lower bound; the cross-fade band sits above each positive bound and is fadeWidth of the gap to the previous
// bound (1 for level 0). Screen size is radius * scale * bias / distance, with distance 1 for orthographic views.
struct FMeshLodChain
{
    float4 screenSize0;
    float4 screenSize1;
    float fadeWidth;
    int count;
};

StructuredBuffer<FMeshLodChain> _EntityLodChains;
float4 _LodOrigin;
float _LodScale;
float _LodBias;
uint _LodOrthographic;

#define MESH_LOD_STABLE 65535u

float MeshLodScreenSize(FMeshLodChain chain, uint level)
{
    return level < 4u ? chain.screenSize0[level] : chain.screenSize1[level - 4u];
}

// Packed visibility of one entity: 0 when no level draws, else 1 | level << 8 | fade << 16, where fade is
// MESH_LOD_STABLE outside the band and the band position quantized to [0, 65534) inside it.
uint MeshPackLodVisibility(uint entity, float3 center, float3 extent)
{
    FMeshLodChain chain = _EntityLodChains[entity];
    float distance = _LodOrthographic != 0u ? 1.0 : max(length(center - _LodOrigin.xyz), 1e-6);
    float screen = length(extent) * _LodScale * _LodBias / distance;
    uint packed = 0u;
    bool found = false;
    [loop]
    for (uint level = 0u; level < (uint)chain.count && !found; ++level)
    {
        float threshold = MeshLodScreenSize(chain, level);
        if (screen >= threshold)
        {
            found = true;
            uint fade = MESH_LOD_STABLE;
            if (threshold > 0)
            {
                float previous = level == 0u ? 1.0 : MeshLodScreenSize(chain, level - 1u);
                float position = (screen - threshold) / (chain.fadeWidth * (previous - threshold));
                if (position < 1.0) fade = (uint)(saturate(position) * 65534.0);
            }
            packed = 1u | (level << 8) | (fade << 16);
        }
    }
    return packed;
}

#endif
