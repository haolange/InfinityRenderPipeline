# Global Settings

`InfinityRenderPipelineGlobalSettings` is the project-wide owner for:

- Runtime shaders (22 compute kernels, blit, RTAO)
- Runtime textures (`LUT_BestFit`)
- Runtime materials (blit, default Lit / Unlit shaders)
- Default Volume profile

Resources resolve with `[ResourcePath]` and `ResourceReloader`. A missing required resource throws at pipeline construction. Do not put shader references back on the RP Asset.
