# Getting started

1. Install **Unity 6000.6** with CoreRP / Shader Graph / VFX **17.6**.
2. Assign an `InfinityRenderPipelineAsset` in Project Settings > Graphics and Quality.
3. Open Project Settings > Graphics > Infinity RP. Global Settings auto-create in the Editor and load runtime shaders through `[ResourcePath]`.
4. Confirm the default Volume profile is assigned on Global Settings. Optional quality overrides live on the RP Asset.
5. Add a Camera or Light (GameObject menu, Add Component, or script through `ObjectFactory`). Infinity additional-data components are added with Undo.
6. Use `Window > Analysis > Rendering Debugger` for Infinity panels, and `Window > Infinity` for remaining validation entry points (fixtures, dumps, Validate Default Volume Profile).

The pipeline never migrates or rewrites assets when the Editor loads them.
