# Changelog

## [0.4.0] - 2026-09-11

### Added
- `InfinityRenderPipelineGlobalSettings` with `IRenderPipelineResources` containers and `[ResourcePath]` for shaders, textures, and materials.
- Quality Volume profile on the RP Asset; project default Volume lives on GlobalSettings.
- `InfinityAdditionalCameraData` / `InfinityAdditionalLightData` with dedicated Camera and Light inspectors.
- Rendering Debugger panels for Rendering, Lighting, Mesh, and Temporal.
- Film tonemap `mode` (None / Film) and `IsActive()` gating for optional Volumes.
- Overlay UI pass after OutputTransform (`DrawUIOverlay`).
- `InfinityUnlit` plus default-material slots for Create-menu objects.
- UnifiedRayTracing-aware RTAO owner and compute visibility pass.
- Build preprocessor and Infinity shader/compute strippers.
- `Documentation~/` getting-started set.

### Changed
- Package targets Unity 6000.6 and CoreRP / Shader Graph / VFX 17.6.
- Optional features gate on `VolumeComponent.IsActive()` (enable / intensity), not `overrideState`.
- SSS quality is Volume `numSamples` only; Diffusion Profiles own distance / albedo / radius.
- Shader properties `_NomralTexture` → `_NormalTexture` and `_PixelDepthOffsetVaule` → `_PixelDepthOffset`.
- Validation menus live under `Window/Infinity`. Completed one-off migration menus are retired.

### Removed
- Shader / blit / debug fields on the RP Asset.
- `CameraComponent` / `LightComponent` (GUID-preserving rename to additional-data types).
- `VolumeHasOverrides`, destructive `EnsureAsset`, Probe/Foliage stubs, placeholder RenderGraph window, random wizards.
- Hardware `.raytrace` RTAO path as the AO owner.
