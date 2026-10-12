# Changelog

## [0.5.0] - Unreleased

Work in progress on branch `claude/infinityrp-0.5`. See [PLAN.md](PLAN.md) for stages and
[Docs/FeatureStatus.md](Docs/FeatureStatus.md) for verification state. Entries are added as stages land.

### Added
- Stochastic screen-space reflections (Frostbite): tile classification into Hi-Z / linear / skip with indirect
  dispatch, half-resolution tracing, VNDF sampling with the blue-noise Sobol textures, ray-reuse resolve with
  filtered importance sampling, virtual-point temporal reprojection with per-pixel history age, optional a-trous pass,
  quality presets and four SSR debug views.
- Shared screen-space denoise library `Shaders/ShaderLibrary/ScreenSpaceDenoise/`.
- Light shapes in Unreal form on `InfinityAdditionalLightData` (directional sun angle; point sphere / line / tube; cone,
  pyramid and box spots; rect with barn doors; disk) with the native Light fields derived on edit, a lightmapper
  delegate that bakes the same shapes, per-light shadow filter selection and Maximum Distance culling with fade.
- Light record v2 (`Shaders/ShaderLibrary/Lighting/LightRecord.hlsl`): no shape enum, shapes are flags plus parameters.
- Clustered light list v2: CPU depth sort, logarithmic z-bins and conservative tile rectangles, GPU tile bitmasks and
  one shared light loop (`Lighting/LightLoop.hlsl`) for Deferred, Forward, fog and clouds. Forward now uses the list
  instead of looping every local light. Contact shadows follow the sun record instead of record 0.
- RuntimeTextures: the three blue-noise Sobol textures (imported uncompressed, single channel).

### Changed
- `EFilmTonemapMode.Film` is renamed `ACES`. The serialized value is unchanged (1). Tonemapping is None or ACES only.
- The shader stripper now strips passes whose LightMode the pipeline never draws inside Infinity-tagged SubShaders.
- `ObjectFactory` adds `InfinityAdditionalCameraData` (with Undo) when a Camera is added, as it already did for Lights.
- `BuildMeshAssetFromPrefab` accepts a prefab with a LODGroup or with a MeshFilter + MeshRenderer, and rejects others.

### Removed
- Rendering Debugger toggles that nothing consumed: Prefer Native Motion Vectors, Fuse TAA Sharpen, Show Temporal Overlay.
- One-off migration and diagnosis tools in `Editor/Tools/Validation` that had no entry point.
- The commented-out RTAO ray-tracing pass in InfinityLit, the unused `PreintegratedDGF_LUT` function, the RenderGraph
  GraphView stub and 21 orphan `.meta` files.
- Unused package dependencies `com.unity.addressables` and `com.unity.terrain-tools`.
- Screen Space Reflection parameters `NumRays`, `SpatialRadius`, `SpatialSample`, `BilateralSample`,
  `BilateralColorWeight`, `BilateralDepthWeight`, `BilateralNormalWeight`; `VarianceEstimator.hlsl` and the
  unreferenced `Compute_SpatialTemporalVarianceFilter.compute`.
- The z-binning compute (`Compute_ZBinningLightList`), the ZBin overflow counter and its capture path
  (`includeZBinOverflow`, counter readback).

## [0.4.0] - 2026-09-11

### Added
- `InfinityRenderPipelineGlobalSettings` with `IRenderPipelineResources` containers and `[ResourcePath]` for shaders, textures, and materials.
- Quality Volume profile on the RP Asset; project default Volume lives on GlobalSettings.
- `InfinityAdditionalCameraData` / `InfinityAdditionalLightData` with dedicated Camera and Light inspectors.
- Rendering Debugger panels for Rendering, Lighting, Mesh, and Temporal.
- Film tonemap `mode` (None / Film) and `IsActive()` gating for optional Volumes.
- Screen Space Overlay UI pass (`CreateUIOverlayRendererList`) on the display-linear color buffer before Present.
- GlobalSettings now uses the 17.6 `RenderPipelineGlobalSettings<TSettings, TPipeline>` order and owns resource containers through `settingsList`.
- `InfinityUnlit` plus optional default-material override slots on the RP Asset for Create-menu objects.
- RTAO Volume records UnifiedRayTracing visibility when the backend and accel are ready; otherwise it does not record (no HiZ stand-in).
- Shader stripper (`Editor/ShaderStripper/`): `IPreprocessBuildWithReport` + Infinity `IPreprocessShaders` / `IPreprocessComputeShaders`. Strips shaders whose SubShaders are all tagged for another render pipeline (URP/HDRP package shaders). Infinity compute variants are counted, not stripped.
- `Documentation~/` getting-started set.

### Changed
- Package targets Unity 6000.6 and CoreRP / Shader Graph / VFX 17.6.
- Optional features gate on `VolumeComponent.IsActive()` (enable / intensity), not `overrideState`.
- SSS quality is Volume `numSamples` only; Diffusion Profiles own distance / albedo / radius.
- Shader properties `_NomralTexture` → `_NormalTexture` and `_PixelDepthOffsetVaule` → `_PixelDepthOffset`.
- Validation menus live under `Window/Infinity`.
- The separate OutputTransform compute pass is retired; transfer encoding happens in the final Present pass, which reads the display-linear `DisplayColorBuffer` (2026-09-13).
- The Screen Space Overlay UI list records only for Game cameras without a target texture; SceneView never draws it (2026-09-13).

### Removed
- Runtime shader, blit-material and debug fields on the RP Asset (these live in GlobalSettings resources).
- `CameraComponent` / `LightComponent` (GUID-preserving rename to additional-data types).
- `VolumeHasOverrides`, destructive `EnsureAsset`, Probe/Foliage stubs, placeholder RenderGraph window, random wizards.
- Hardware `.raytrace` RTAO path as the AO owner.
