# Global Settings

`InfinityRenderPipelineGlobalSettings` is the project-wide owner for:

- Runtime compute shaders (`InfinityRenderPipelineRuntimeShaders`, one compute shader per feature)
- Runtime textures (`LUT_BestFit`)
- Runtime materials (blit, default Lit / Unlit shaders)
- Default Volume profile

Resources resolve with `[ResourcePath]` and `ResourceReloader`. A missing required resource throws at pipeline construction. The RP Asset never references runtime shaders; its only shader/material fields are optional overrides for the materials Unity assigns to objects created from the GameObject menu.

The RTAO UnifiedRayTracing shader is loaded by path in the Editor (`InfinityRayTracingEnvironment`); Player loading is unverified.
