#ifndef INFINITY_LIT_MATERIAL
#define INFINITY_LIT_MATERIAL

// Shared UnityPerMaterial for every InfinityLit pass. SRP Batcher requires
// one identical CBUFFER layout; Depth/Shadow/Motion declare it even unused.
CBUFFER_START(UnityPerMaterial)
    float _SurfaceRoute;
    float _Roughness;
    float _Reflectance;
    float _NormalTile;
    float _BaseColorTile;
    float _SpecularLevel;
    float _Subsurface;
    float _SSSProfileIndex;
    float _SSSThickness;
    float4 _BaseColor;
    float4 _EmissionColor;
    float _RefractionStrength;
CBUFFER_END

#endif
