// Stencil

#ifndef INFINITY_STENCIL
#define INFINITY_STENCIL

// Pass admission bits. The geometry and motion draws write them; the stencil holds no lighting, visibility or channel state.
#define STENCIL_BIT_OBJECT_MOTION 1u
#define STENCIL_BIT_DEFERRED 2u
#define STENCIL_BIT_SUBSURFACE 4u

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
