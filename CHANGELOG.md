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
- Area light shading (`Lighting/LightShapes.hlsl`): Karis representative-point specular with energy normalization for
  sphere, line, tube, rect, disk and sun disk; Frostbite sphere / disk form factor, Karis line irradiance and the rect
  polygon form factor for diffuse. `CreateAreaLightsValidationScene` (Window/Infinity/Create Area Lights Fixture).
- Shadow settings on the RP Asset: cascade count 1–4, cascade blend width, shadow filter quality presets (Low..Ultra +
  Custom) with a read-only preset view and Copy Preset To Custom.
- Shadow filtering: GatherRed tent PCF (3×3 to 7×7, no comparison sampler) and PCSS (Vogel blocker search, penumbra
  from the light's source radius or sun angle), selected per light; cascades blend into the next cascade.
- Realtime shadows for pyramid and box spots (perspective / orthographic tiles) and rect / disk lights; line and tube
  lights use six cube faces like points. Local shadow frustums include a filter guard band.
- Shadow Update Mode per local light (EveryFrame, OnEnable, OnDemand + `RequestShadowMapRendering()`): cached tiles
  live in a persistent atlas (RP Asset Cached Shadow Atlas Width), are re-rendered only when stale and are copied into
  the frame atlas; slot state commits only with a submitted frame.
- Shadowmask mode: realtime shadows of Mixed Shadowmask lights draw dynamic casters only (static lightmapped casters
  come from the shadowmask), for RendererList objects and MeshScene instances alike. Distance Shadowmask is unchanged.
- Rendering Debugger → Debug View: Cascade / Local / Cached Shadow Atlas and Shadow Moments; Infinity Lighting panel
  counts cached shadow tiles and re-renders.
- Baked GI: Adaptive Probe Volumes (RP Asset Light Probe System, CoreRP streaming / scenarios, ProbeVolumesOptions in
  the default Volume profile) and Light Probe Proxy Volumes feed unlightmapped surfaces in Deferred and Forward, for
  RendererList objects and MeshScene instances; terrain receives lightmaps, Shadowmask and probes; InfinityLit-
  Instanced and TerrainLit get ShadowCaster and Meta passes. Unused APV variants are stripped from builds.
  MeshScene instances interpolate light probes in one batch per frame (static meshes only after a probe bake).
  Window/Infinity/Lighting/Use Atmosphere As Baking Sky makes the lightmapper's environment the pipeline's atmosphere.
- Light cookies and IES profiles: one mip-mapped atlas with buddy-allocated blocks, compute uploads (2D copy, cube
  to octahedral, mips), cookies on spot / pyramid / box / point / directional (tiled) lights and emitter textures on
  rect / disk lights, IES profiles by direction (Cube) or projected (2D spots); Deferred, Forward, fog and clouds share
  the evaluation. CoreRP's IES importer creates Infinity light prefabs. RP Asset: Cookie Atlas Resolution, Cookie Max
  Size. Debug View: Light Cookie Atlas.
- Contact shadows for local lights: up to 24 contact-enabled lights per camera trace toward their position into a
  per-pixel bit mask shared by Deferred and Forward; the sun keeps its own trace. Thickness is tested in linear view
  depth, ray starts are jittered, and the Contact Shadow Volume has quality presets and a separate sun step count.
- Reflection probes: baked, custom and realtime probes join the clustered list as a second index space (own z-bins
  and tile bitmasks) and blend front to back by importance and size, with box or sphere influence, blend distance and
  box projection; the sky fills the remaining weight. Deferred and Forward share the evaluation. Probe cubes are
  GGX-filtered into one cube-array atlas (RP Asset Reflection Probe Resolution, Max Reflection Probes) by
  `Compute_CubeFilter.compute`, which the sky prefilter now uses as well.
- SSGI rebuilt on the stochastic SSR core: tile classification (trace / skip) with indirect dispatch, half-resolution
  cosine-distributed rays traced with the Hi-Z walk, ray-reuse resolve with filtered importance sampling, temporal
  accumulation with per-pixel history age and an optional a-trous pass; Quality presets on the Volume. Missed rays
  keep the baked, probe or sky indirect diffuse. SSR and SSGI share `ScreenSpaceTraceTiles.hlsl`.
- Shader Graph target "Infinity" with Lit, Unlit and Decal materials: Lit graphs render through every InfinityLit pass
  (Deferred / Forward routes, translucent T0–T2, shadows, motion, lightmaps, probes, APV, decals, Meta) with Unreal-style
  Roughness and Specular Level blocks and a material GUI for routing and subsurface.
- Render Pipeline Wizard (Window/Infinity/Render Pipeline Wizard) with Fix / Fix All: color space, graphics API, pipeline
  asset and quality levels, GlobalSettings, default Volume profile, SRP Batcher, mixed lighting mode, baking sky, light
  shape sync and realtime-only tube lights. Window/Infinity/Append Missing Default Volume Components.
- Frame Settings: RP Asset defaults for cameras and for reflection probe captures, per-camera overrides on the Camera
  inspector, and a Rendering Debugger list of the effective values and their source.
- Light Explorer tabs for Infinity lights, Infinity probe data and Volumes; Lighting window Environment section for the
  atmosphere and the baking sky.
- Light inspector sections with Additional Properties and Scene view handles for range, angles and every source shape;
  sphere-influence handle for reflection probes; decal box handle, UV scale / bias and angle fade; Help links to the
  documentation on assets and components.
- Rendering Debugger panels Infinity Material, Infinity Lighting, Infinity Screen Space, Volume (CoreRP) and Infinity
  Timings (per-pass GPU / CPU time); new debug views Metallic, Specular, Shading Model, Rendering Layers, Light and
  Reflection Probe Complexity heat maps, Baked Diffuse, Indirect Diffuse, Indirect Specular, Reflection Probe Atlas and
  Contact Shadows. Scene view Draw Modes list every debug view per panel and apply to that Scene view only. Reflection
  probe captures and previews no longer render the active debug view.
- Render Graph Viewer (Window/Analysis/Infinity Render Graph Viewer): the last compiled graph per camera with passes,
  queues, culling, resource reads / writes and pool lifetimes; captured only while the window is open.
- Material Conversion Wizard (Window/Infinity) and Edit/Rendering/Materials converters from Built-in, URP and HDRP
  Lit / Unlit materials to InfinityLit / InfinityUnlit. GameObject/Light/Infinity for every light shape,
  GameObject/Rendering/Infinity for volumes, decal projector, reflection probes and light probe group;
  Assets/Create/Shader Graph/Infinity graphs. The asset create menus moved from Create/InfinityRenderPipeline to
  Create/Rendering/Infinity.
- Validation fixture creators for shadows, cookies / IES, reflection probes, baked lighting and screen-space
  reflections / GI (Window/Infinity/Create … Fixture).
- Image regression (Window/Infinity/Image Regression): records or compares the DisplayColor of every validation fixture
  in Play mode against per-graphics-API baselines kept outside `Assets`.
- Documentation pages for shadows, baked lighting, screen-space effects, quality, Shader Graph, editor tools and the
  0.4 → 0.5 upgrade.
- VSM and EVSM shadow filters: a half-resolution moment atlas per shadow atlas, built only for the slices that need it,
  with a separable Gaussian blur and light-bleeding reduction (`Compute_ShadowMoment.compute`, `ShadowMoments.hlsl`).

### Fixed
- SSGI verification now distinguishes final post-TAA display evidence from raw GI/debug signals. Production-kernel regression coverage checks reset samples, configured temporal accumulation, premultiplied confidence, invalid history and spatial normalization without requiring an unrequested first-frame prefilter. No production filtering algorithm is changed.
- Camera culling requests reflection probes explicitly. Overwriting Unity's native culling options without `NeedsReflectionProbes` returned an empty visible-probe list, so the configured atlas and reflection consumers never received scene probes.
- Present GPU contract tests queue readback behind the tested draw and yield Editor updates instead of blocking Metal submission. The same three dimensions, five output policies, finite values, coverage/padding and transfer equations remain asserted; timeout retains resources until its callback drains.
- Reflection probe atlas versions include full Editor image hashes and sampling format/dimensions/mips; reimport may preserve identity and updateCount while changing the filtered result.
- Reflection probe uploads release unqueued reservations after a camera fails, allowing another camera to produce the same version in that frame. Queue acceptance invalidates overwritten committed contents so a later failed update cannot revive stale probe data; uncertain queue failures retain pending state for frame completion.
- Reflection probe layout tests ignore HLSL line/block comments before parsing the struct. Comment prose containing semicolons is not a GPU field; all field count, order, type and stride assertions remain enabled.
- Complete the default Volume registry with APV Options through the explicit current-schema asset save. Preserve existing effective component values, local IDs and references; the same bounded save materializes current defaults and retires inactive legacy SSR/SSGI fields. Editor load and script reload do not perform this save.
- GBuffer GPU contract tests queue their own readbacks with the dispatch and yield Editor updates instead of blocking on process-wide `WaitAllRequests`, which stalled the Metal full suite. The same color, silhouette, rendering-layer and normal assertions run through UnityTest; callback data is copied before expiry, and timed-out resources retire only after their callbacks drain. The menu uses the existing XML TestRunner for every case.
- Terrain supplies the basemap shader and generator Unity needs to collect its baked material. The base material samples the generated albedo in Meta and retains the Terrain raster passes.
- Newly enabled MeshComponents join the existing world lists before the shared probe interpolation batch, then register through dirty synchronization before upload. Static invalidation also batches before proxy updates. A CPU profiler marker counts both batch and explicit single-sample interpolation paths.
- Static MeshComponent probe samples invalidate when asynchronous tetrahedralization completes, after the new interpolation topology becomes available.
- Baking sky outputs are keyed by the saved scene GUID so scenes with the same name do not share faces or materials. The explicit command saves only its output assets; all six faces are read before updating the durable texture, and resize keeps the existing object through `CopySerialized`. A failed refresh or mismatched scene material cancels the bake instead of continuing with stale faces.
- APV shader stripping queries the global `PROBE_VOLUMES_L1/L2` keywords used by the pipeline, rather than treating them as shader-local keywords. The default-profile test now distinguishes CoreRP sampling options from Infinity effect enable switches; the RP Asset still owns APV activation.
- Cookie and IES atlas versions compare full Editor image hashes and sampling format/dimensions/mips/channels as well as runtime updateCount. Reimporting a texture can preserve identity and updateCount; it now refreshes the atlas once instead of retaining stale decoded colors.
- A camera that fails after recording a cookie upload returns its unqueued reservation before the next camera. Shared textures still upload once, accepted production stays reusable, and a failed unqueued update preserves the previously committed contents while the new version is retried. Queue acceptance invalidates the overwritten committed version, preventing a later failed update from reviving stale contents.
- Normal-frame capture accepts the explicit ContactShadowSun (R8) and ContactShadowMask (R32 UInt) outputs. Captures keep integer mask bits, use the normal RenderGraph resource lifetime, and fail when a requested producer is absent; no contact feature or history setting is changed by capture.
- Deferred MeshComponent registration now checks its active state and component registration epoch. Disabling before
  the queued registration runs cannot resurrect an inactive mesh; re-enabling with changed Static/Dynamic mobility
  cannot leave duplicate world-list membership. Captured old instance IDs still retire through the existing path.
- OnDemand local-shadow depth now retains its produced projection, decode, cube-face origin, source size and face
  topology until an explicit request. Complete queued groups are transactional with their depth; missing/mixed faces
  rebuild together, undersized caches draw the entire group normally, and an OnEnable pending overwrite cannot fall
  back to metadata for an already overwritten committed tile.
- PCSS receiver-plane correction uses the texel actually read by point sampling in both blocker search and filtering,
  preventing a sloped surface from shadowing itself. Local blocker search covers the emitter footprint projected from
  the shadow near plane to cover that emitter footprint within the configured search bound.
- VSM/EVSM Gaussian blur accumulates differences from the center moment. This preserves constant half-float moments
  and avoids the observed EVSM second-moment drift through a normalized blur.
- Shadow-filter presets keep moment blur at one texel and increase light-bleeding suppression from Low to Ultra.
  Previously higher presets widened the blur and weakened suppression, increasing VSM leakage between blocker depths.
  Both VSM and EVSM use these settings; Custom retains its authored blur and suppression values.
- VSM/EVSM visibility accounts for the half-float mean's representation error, including interpolation, fixing the
  reproduced quantization self-shadow cases while retaining occlusion for separate receivers behind the surface.
  The normal-frame sun EVSM comparison retains a reported single-half-step residual.
- Shadow allocation includes eligible MeshScene casters when no native renderer casts shadows. Directional splits
  use one light-space fit for both draw paths; local candidates test their actual guarded shadow frustums before
  consuming the atlas budget. Native culling indices remain compact when the camera near plane skips early cascades.
- Cached local shadow tiles use separate Raster depth-save/depth-restore passes. Depth-only region CopyTexture is unsupported and produced native errors on Metal; slot transactions and atlas formats are preserved. Actual GPU tile round-trip coverage includes nonzero offsets, neighbour sentinels and an independent color-copy oracle.
- Initialize the owned VolumeManager before registering Rendering Debugger panels. The CoreRP Volume panel added in S15 otherwise reads its component registry before initialization during a fresh script domain and aborts pipeline construction.
- Light-shape verification now checks independent diffuse/specular weights in the actual Deferred kernel with a nonzero specular input; the earlier mask-loop regression exercised diffuse only. The six controls share inputs, clear the emissive/output target between dispatches and preserve the original eight mask-count cases.
- CPU light-tile rectangles use the same compute projection convention as sampled-depth reconstruction, preserving
  upper/lower local-light coverage in Deferred, Forward and volumetrics.
- Clustered-light loop initialization now writes caller-owned integer state through `out`, including Forward,
  Deferred, contact-shadow, fog, cloud and complexity consumers. This avoids the incorrect integer state observed
  when returning the loop struct in the Unity 6000.6 Metal Deferred kernel; the tile/bin algorithm is unchanged.
- Normal-frame diagnostics can capture `DeferredLighting` before Forward/atmosphere and `LightTileMask` from the
  actual graph buffer with declared read ownership, using exact low-to-high uint bytes in RGBA32F channels.
- Cone spot lights square their angular window, matching the smooth inner/outer-cone attenuation used by CoreRP's
  punctual lighting. Surface, fog and cloud consumers share the corrected function; Point, Pyramid and Box support is unchanged.
- Authored Disk lights use an explicit native Rectangle culling carrier, retaining their Additional Disk shape and GI Disc emitter. Shape and mode-only edits retain native bake modes through Undo/Redo; opening/loading a native Disc does not convert it. The native carrier gizmo is rectangular and the Infinity shape handle is circular.
- Realtime light radiance linearizes the authored Unity color before applying color temperature and intensity, matching
  Unity's lightmapper extraction for gray and colored lights. The prior raw-color packing test now checks linear radiance;
  additional tests compare the installed Unity extraction with and without temperature, rather than assuming a pi factor.
- Light Inspector adoption of an existing native shape now owns an Undo entry. Pipeline disposal restores the previous
  lightmapper delegate while preserving a later replacement, including an older disposed pipeline in the owner chain.

- Normal-frame SSR capture exposes classification and history metadata without changing Debug View or history. Requested
  classification uses graph-owned lifetime through Transfer readback; ordinary frames retain pass-local classification.

- Volumetric clouds use the Atmospherical Profile planet radius and a consistent planet-centered shell for view and
  light rays. Cloud intervals exclude the inner cavity and planet, clip at opaque depth, and retain both visible grazing
  segments; layer rejection uses unclamped height. Sky depth no longer clips clouds at the camera far plane.
- S3 resource verification no longer assumes ordinary reload completes existing GlobalSettings or recommends whole-container Reset for one missing slot. Historical replay uses an explicit temporary binding and restoration; persistent upgrades must account for native fields absent from the current schema before saving.
- Inspector section foldouts allow nested serialized arrays, including Diffusion Profiles in Lighting & Shadows,
  and keep layout groups paired when selecting mixed Light types.
- Preview and Reflection camera uniforms now obey the camera policy when preparing jitter, including after successful history commits.
- Custom Mesh visibility retains the full Unity scene mask through instance metadata, CPU/GPU culling, shadow views and cache identity; static scene changes refresh without requiring proxy updates. Cached local shadows distinguish cameras with different scene masks.
- Pending-verification tests use NUnit's legal `Unverified_0_5` category. The former hyphenated name made NUnit
  reject tests as Failed:Invalid before execution; the rename preserves all pending markers and assertions.

- Shader stripping compares native `ShaderTagId` identities instead of case-sensitive names, preserving used
  native passes when Unity returns canonicalized tag spellings. Callback fixtures now select real single-pass
  shaders instead of changing a snippet name while still addressing an unrelated pass.
- The shared EditMode runner selects exact categories and all Example assemblies, checkpoints actual test identities
  and XML outcomes across script reloads, and reattaches without restarting the run. Missing or inconsistent evidence
  fails closed. Raw framework XML stays unchanged; any recovered checkpointed skips have a separate derived report.
- Completed test runs unregister their global callback even when Unity has already destroyed the API object;
  later runs cannot overwrite earlier XML or zero-match/failure results.
- Offline validation ignores hidden macOS metadata before parsing assets and ignores commented-out LightMode tags.
  Historical replay can report individually reviewed, SHA-bound missing evidence links as warnings; changed sources
  and unlisted broken links still fail. Real orphan metadata, duplicate GUIDs and unknown live LightModes still fail.
- Shadow casters projected with the camera matrix: caster shaders read `Matrix_ViewFlipYProj`, which the shadow passes
  never replaced (they only set `Matrix_ViewProj`). Both are now set to the shadow matrix and restored afterwards.
- The InfinityLit Meta pass assigned to a read-only global (`unity_OneOverOutputBoost`).
- Forward lighting ignored contact shadows, and `g_SunRecordIndex` was never bound for Deferred, Forward and the
  volumetrics (only the contact pass set it).
- Volumetric fog and clouds filtered cascade shadows with twice the real texel size (they passed the per-cascade tile
  resolution as the atlas size). Deferred, Forward, fog and clouds now bind shadows through one `FShadowInputs`.
- Rect lights contributed no diffuse light: the polygon form factor was wound backwards and clamped to zero. Rect
  lights also applied the inverse-square falloff twice.
- Specular highlights were clamped to 1 (`saturate` on the BRDF); HDR highlights now keep their energy.
- Diffuse / Specular light weights were applied to the whole BRDF (diffuse weight) for punctual lights; each now scales
  its own term.
- RuntimeTextures: the three blue-noise Sobol textures (imported uncompressed, single channel).
- Reflection probe captures and preview cameras rendered the active Rendering Debugger view (a baked probe kept it).

### Changed
- Source layout: render passes live in `Runtime/RenderPipeline/Pass/<domain>/` and Volume components in
  `Runtime/Volume/<domain>/` (moved with their `.meta`; GUIDs, class names and namespaces are unchanged).
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
- Screen Space Indirect Diffuse parameters `NumRays`, `SpatialRadius`, `SpatialSample`, `BilateralSample`,
  `BilateralColorWeight`, `BilateralDepthWeight`, `BilateralNormalWeight`; the SSGI moments and depth-normal
  histories; capture stages `SSGIRadiance` and `SSGISpatial` (now `SSGIRayHit`, `SSGIResolved`).

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
