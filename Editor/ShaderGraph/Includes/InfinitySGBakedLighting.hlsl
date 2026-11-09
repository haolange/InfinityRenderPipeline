#ifndef INFINITY_SG_BAKED_LIGHTING_INCLUDED
#define INFINITY_SG_BAKED_LIGHTING_INCLUDED
// Pre-graph include of the GBuffer pass: baked lighting, GBuffer encoding and DBuffer decals (as InfinityLit's GBufferPass).
#define INFINITY_SG_BAKED_LIGHTING 1
#include "Packages/com.infinity.render-pipeline/Shaders/Library/Lightmap.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/GBufferPack.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/DBuffer.hlsl"
#include "Packages/com.infinity.render-pipeline/Shaders/Library/BSDF.hlsl"
#endif
