#ifndef INFINITY_SG_SURFACE_LIGHTING
#define INFINITY_SG_SURFACE_LIGHTING
// Pre-graph include of the Forward pass: the shared light loop, shadows, cookies, reflection probes and sky IBL (as
// InfinityLit's ForwardPass).
#define INFINITY_SG_BAKED_LIGHTING 1
#if defined(INFINITY_FORWARD_AO)
Texture2D<float> SRV_ForwardOcclusion;
#endif
#include "Packages/com.infinity.render-pipeline/Shaders/Library/SurfaceLighting.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/BSDF.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/Lightmap.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/Lighting.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/ShadingModel.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/GBufferPack.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/ImageBasedLighting.hlsl"
StructuredBuffer<float4> _AtmosphereSkySH;
Texture2DArray<float4> _AtmosphereGGXPrefilter;
float _AtmosphereIBLMaxMip;
#endif
