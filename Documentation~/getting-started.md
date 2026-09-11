# Getting started

1. Install **Unity 6000.6** with CoreRP / Shader Graph / VFX **17.6**.
2. Assign an `InfinityRenderPipelineAsset` in Project Settings > Graphics and Quality.
3. Open Project Settings > Graphics > Infinity RP. Global Settings auto-create in the Editor and load runtime shaders through `[ResourcePath]`.
4. Confirm the default Volume profile is assigned on Global Settings. Optional quality overrides live on the RP Asset.
5. Create objects from `GameObject > Camera` / Light. Infinity additional-data components are added with Undo.
6. Use `Window > Analysis > Rendering Debugger` for Infinity panels, and `Window > Infinity` for remaining validation entry points.

Example-project asset migrations still use the backup / exact-delta / second-save / no-op protocol on the Mac worker.
