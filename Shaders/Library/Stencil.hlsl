// Stencil

#ifndef INFINITY_STENCIL
#define INFINITY_STENCIL

// Pass admission bits. The geometry and motion draws write them; the stencil holds no lighting, visibility or channel state.
// Bits 3–7 (STENCIL_USER_MASK) belong to user passes. Pipeline ReadMask and WriteMask stay inside STENCIL_PIPELINE_MASK.
#define STENCIL_BIT_OBJECT_MOTION 1u
#define STENCIL_BIT_DEFERRED 2u
#define STENCIL_BIT_SUBSURFACE 4u
#define STENCIL_PIPELINE_MASK 0x07u
#define STENCIL_USER_MASK 0xF8u

// Reads the stencil plane of a depth-stencil target bound with RenderTextureSubElement.Stencil.
uint LoadStencil(Texture2D<uint2> stencilTexture, int2 pixel)
{
    uint2 value = stencilTexture.Load(int3(pixel, 0));
#if defined(SHADER_API_D3D11) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_GAMECORE)
    return value.y;
#else
    return value.x;
#endif
}

#endif
