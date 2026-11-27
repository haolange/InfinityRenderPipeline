# Global Settings

`InfinityRenderPipelineGlobalSettings` is the project-wide owner for:

- Runtime compute shaders (`InfinityRenderPipelineRuntimeShaders`, one compute shader per feature)
- Runtime textures (blue-noise tiles for screen-space tracing)
- Runtime materials (blit, default Lit / Unlit shaders)
- Default Volume profile

Resources resolve with `[ResourcePath]` and `ResourceReloader`. A missing required resource throws at pipeline construction. The RP Asset never references runtime shaders; its only shader/material fields are optional overrides for the materials Unity assigns to objects created from the GameObject menu.

The Editor loads the RTAO shader by package path through `InfinityRayTracingEnvironment`. Unified Ray Tracing selects the imported compute or ray tracing subasset for the active backend. RTAO shader loading is currently available in the Editor.
