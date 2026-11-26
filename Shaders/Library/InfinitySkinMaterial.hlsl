#ifndef INFINITY_SKIN_MATERIAL
#define INFINITY_SKIN_MATERIAL

// Shared UnityPerMaterial for every InfinitySkin pass. SRP Batcher requires one identical CBUFFER layout; Depth, Shadow
// and Motion declare it even unused.
CBUFFER_START(UnityPerMaterial)
    float _SurfaceRoute;
    float _TranslucentStage;
    float _BaseColorTile;
    float _NormalTile;
    float _OcclusionStrength;
    float _Roughness;
    float _SpecularLevel;
    float _SecondLobeMix;
    float _SecondLobeRoughnessScale;
    float _DiffusionProfileIndex;
    float4 _BaseColor;
CBUFFER_END

#endif
