namespace InfinityTech.Rendering
{
    // Admission bits in Shaders/Library/Stencil.hlsl. Bits 3–7 belong to user passes; pipeline masks stay inside PipelineMask.
    public static class StencilBits
    {
        public const int PipelineMask = 0x07;
        public const int UserMask = 0xF8;
        public const int ObjectMotion = 1;
        public const int Deferred = 2;
        public const int Subsurface = 4;
    }
}
