# Getting started

1. Install Unity 6000.6 with Core RP, Shader Graph and VFX Graph 17.6.
2. Assign an `InfinityRenderPipelineAsset` in Project Settings > Graphics and Quality.
3. Open Project Settings > Graphics > Infinity RP. Create or assign Global Settings through Graphics Settings or the Render Pipeline Wizard. Runtime shaders load through `[ResourcePath]`. Opening the project does not repair assets.
4. Confirm the default Volume profile is assigned on Global Settings. An optional quality profile lives on the pipeline asset.
5. Add a camera or a light from the GameObject menu, Add Component, or `ObjectFactory`. Infinity additional-data components are added with Undo.
6. Use Window > Analysis > Rendering Debugger for the Infinity panels, and Window > Infinity for the wizard, material conversion and validation fixtures.

The pipeline does not migrate or rewrite assets when the Editor loads them.

Create lights from GameObject > Light > Infinity and scene objects from GameObject > Rendering > Infinity. Create assets from Assets > Create > Rendering > Infinity and Shader Graphs from Assets > Create > Shader Graph > Infinity. Convert Built-in, URP and HDRP materials with Window > Infinity > Material Conversion Wizard. The wizard lists those source shaders by name.

Mesh content, native renderers, lights and the frame order are specified in the [architecture](../ARCHITECTURE.md). Authoring for mesh instances is in [Mesh content](mesh.md).
