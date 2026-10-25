# Getting started

1. Install **Unity 6000.6** with CoreRP / Shader Graph / VFX **17.6**.
2. Assign an `InfinityRenderPipelineAsset` in Project Settings > Graphics and Quality.
3. Open Project Settings > Graphics > Infinity RP. Explicitly create or assign Global Settings through Graphics Settings or the Infinity Wizard. Runtime shaders load through `[ResourcePath]`; opening the project does not repair assets.
4. Confirm the default Volume profile is assigned on Global Settings. Optional quality overrides live on the RP Asset.
5. Add a Camera or Light (GameObject menu, Add Component, or script through `ObjectFactory`). Infinity additional-data components are added with Undo.
6. Use `Window > Analysis > Rendering Debugger` for Infinity panels, and `Window > Infinity` for remaining validation entry points (fixtures, dumps, Validate Default Volume Profile).

The pipeline never migrates or rewrites assets when the Editor loads them.

Create objects from `GameObject > Light > Infinity` (every light shape) and `GameObject > Rendering > Infinity`
(volumes, decal projector, reflection probes, light probe group); assets from `Assets > Create > Rendering > Infinity`
and `Assets > Create > Shader Graph > Infinity`. Convert existing Built-in / URP / HDRP materials with
`Window > Infinity > Material Conversion Wizard`.
