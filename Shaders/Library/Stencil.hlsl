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

#endif
