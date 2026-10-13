# InfinityRP 0.5.0 — upgrade and convergence ledger

This is the only active task ledger. The 0.4.0 C0–C7 Editor convergence ledger is archived verbatim in
[Docs/History/PLAN-0.4.0-editor-convergence.md](Docs/History/PLAN-0.4.0-editor-convergence.md); its receipts are
historical and do not pass any 0.5.0 gate.

## Goals (user direction, 2026-10-11)

1. **SSR** rebuilt on Frostbite's stochastic SSR: tile classification picks Hi-Z or linear tracing, half-resolution
   tracing, ray-reuse resolve, temporal accumulation and denoising.
2. **Documents agree with each other and with the code.**
3. **Post-processing** gets no new features. Tonemapping is None or ACES only.
4. **Lighting baseline**: baked GI (lightmaps, Shadowmask, Distance Shadowmask, light probes, LPPV, APV), reflection
   probes in the same clustered list structure as lights (one structure and one evaluation for Deferred and Forward),
   cookies and IES, PCF/PCSS/VSM/EVSM shadows, screen-space traced (contact) shadows, and area lights shaded with the
   Karis representative-point approximation (no LTC). Sphere, tube, line, rect and disk shapes are derived from light
   parameters, not hardcoded light classes.
5. **Editor parity with HDRP**, including Shader Graph support.
6. **Architecture and governance healthy.** Volume quality uses hardcoded preset tables in code that resolve to
   low-level settings; passes consume only low-level settings; `Custom` lets experts drive the low-level values.

Out of scope: new post effects, LTC, Player builds and packaging.

## Delivery rules

- Branch `claude/infinityrp-0.5`, based on `main` at c92be77. Each stage has one or more commits whose subject starts
  with `SNN:`. Commits are never squashed.
- Every commit compiles on its own: the Runtime, Editor and Tests consumers of a change move in the same commit. New
  subsystems land unwired with CPU tests first, then a separate commit wires them in.
- No Editor is available while 0.5.0 is written. Every stage passes the offline gate before the next stage starts:
  1. Differential C# semantic compile against UnityCsReference + CoreRP 17.6 sources (no new diagnostics).
  2. DXC compile of every compute kernel and shader pass entry (no new failures).
  3. `Tools/StaticCheck/static_check.py` clean.
- A Unity or CoreRP API that cannot be verified from source is not wired; the stage records it as "designed, not wired".
- Image results stay `TODO(UNVERIFIED)` until the user captures them. New tests carry `[Category("Unverified-0.5")]`.
- Before writing code, each stage records its interface contract below (record layouts, buffer names, pass read/write sets).

## Stages

| Stage | Scope | Depends on | Status |
|---|---|---|---|
| S0 | Offline gate (`Tools/StaticCheck/`) | — | done |
| S1 | Documents and governance; repository hygiene; Film→ACES | S0 | done |
| S2 | Contract defects: Reflection/Preview camera policy, every `LightType` mapped, hardcoded shadow values, feature-gate gaps, DebugView enum, Mesh GPU table invalidation | S1 | done |
| S3 | RenderGraph: indirect dispatch on `RGBufferRef`, utility kernels, typed resource keys, two-phase feature registry | S2 | done |
| S4 | Quality presets: `EQualityLevel`, low-level settings structs, hardcoded preset tables, Volume `Resolve()` | S3 | done |
| S5 | Stochastic SSR (classify, Hi-Z/linear trace, ray-reuse resolve, temporal, spatial) + shared denoise library | S3, S4 | done |
| S6 | Light data model v2 (UE-form shape data, implicit Unity Light sync, lightmapper delegate, record v2) | S2 | done |
| S7 | Clustered light list v2 (CPU z-bins + GPU tile bitmasks) and one shared light loop | S6, S3 | done |
| S8 | Area shapes: sphere, tube, line, rect, disk, sun disk (Karis representative point + Frostbite diffuse) | S7 | done |
| S9 | Shadows: settings on the RP Asset, Gather-PCF, PCSS, VSM/EVSM moment atlas, cached shadows, area-light shadows, Shadowmask object filtering | S6, S7 | done |
| S10 | Screen-space traced shadows (sun + up to 24 local lights) | S7 | done |
| S11 | Cookie and IES atlas | S6, S7 | done |
| S12 | Baked GI: lightmaps, Shadowmask, Distance Shadowmask, probes, LPPV, APV, baking sky | S2, S6 | pending |
| S13 | Reflection probes in the clustered list; shared cube filter | S2, S7, S12 | pending |
| S14 | SSGI on the SSR trace/resolve/denoise core | S5 | pending |
| S15 | Editor parity: Shader Graph target, Wizard, Frame Settings, Light Explorer, inspectors and handles, Rendering Debugger panels, RenderGraph Viewer, material converter, menus | all | pending |
| S16 | Tests, user documentation, FeatureStatus, verification playbook | all | pending |
| S17 | Folder layout (`git mv` only) | all | pending |

## Interface contracts

Each stage fills in its section before its code lands.

### S11

- Authority: the cookie texture is the Unity Light's `cookie` (and `cookieSize2D` for directional lights); the IES
  profile is `InfinityAdditionalLightData.iesProfile` (Cube texture: every local light by direction; 2D texture: cone
  and pyramid spots, projected).
- `LightCookieAtlas` (one per pipeline): persistent RGBA8 atlas `cookieAtlasResolution` (power of two, full mip chain,
  0 disables), `AtlasQuadAllocator` buddy blocks (16..`cookieMaxSize`, aligned to their size so every mip stays in its
  block), keyed by (texture identity, layout) and re-uploaded when `Texture.updateCount` changes; LRU eviction of
  blocks not used this frame. Transaction as the cached shadow atlas (pending → produced on queue → commit /
  rollback).
- `LightContext.ResolveCookies` (after `Build`): `record.cookieIndex` → `FLightCookieGPU` (`// GPU-RECORD` ↔
  `Lighting/LightCookie.hlsl FLightCookie`: cookieRect, iesRect, layout, tiling) in `_LightCookieBuffer`
  (`SRV_LightCookies`).
- Content feature `EFrameFeature.LightCookies` → `LightCookiePass` (`lightCookieShader`, `Compute_LightCookie.compute`:
  `CookieCopy2D`, `CookieCubeToOctahedral`, `CookieDownsample` reading mip m−1 and writing mip m as two UAVs) imports
  the atlas as `LightCookieAtlas` and uploads queued textures once per frame.
- `FLightLoopInputs` carries the cookie buffer and atlas (`Read`, `Bind`, `BindGlobal`; white texture when absent).
  `LightCookieColor` applies to Deferred, Forward, fog and clouds; rect / disk cookies are emitter textures
  (`LightPlanarCookie`: specular at the representative point, blurred by roughness; diffuse from the last mip).
- Editor: `InfinityIESImporter` completes CoreRP's IES importer prefab (Infinity component, profile texture, candela
  from the file maximum when enabled) while an Infinity asset is active.

### S10

- `ContactShadowAllocation.Assign` (after the depth sort, before shadow allocation): every contact-enabled local light
  gets a bit in `record.misc.z` (−1 without one), at most 24, largest tile rectangle first. 24 instead of the planned
  32: the mask keeps the HDRP layout, bits 24..31 hold the occlusion strength, so consumers need no extra constants.
- Content feature: `EFrameFeature.ContactShadow` is requested after `LightContext.Build` when the Volume is active and
  the sun or a local light has Contact Shadows on.
- `ContactShadowPass` (`Compute_ContactShadow.compute`, kernel `ContactShadowCS`, internal resolution) writes
  `ContactShadowBuffer` (`ContactShadowSun`, R8, 1 = lit) and `ContactShadowMaskBuffer` (R32_UInt). Per pixel: one ray
  toward the sun, then one ray per light of the clustered list (`LightLoop.hlsl`) that owns a bit and reaches the
  pixel (`LocalLightWindow`); rays stop at the emitter (sphere radius); box spots trace along −forward. Hits compare
  linear view depths (`ContactShadow_DepthParams` = GPU m22, m23, ortho) with the Volume thickness; IGN jitter per
  frame.
- Consumers: `ContactShadowFromMask(light, mask)` (`LightRecord.hlsl`); `EvaluateLocal(..., contactMask)`; the sun's
  `EvaluateDirectional` receives the sun term, other directional lights 1. Deferred binds under
  `DEFERRED_CONTACT_SHADOW`; Forward reads `SRV_ContactShadowSun` / `SRV_ContactShadowMask` when
  `g_ContactShadowEnabled`. Volumetrics do not read contact shadows (they are a surface term).
- `FLightLoopInputs` binds `g_SunRecordIndex` for every light-loop consumer (it was only bound by the contact pass).
- `FContactShadowSettings` / `QualityPresets.ContactShadow` (numSteps, sunNumSteps); Volume `ContactShadow.Resolve()`
  adds `quality` and `SunNumSteps`.

### S9

- RP Asset: `cascadeCount` [1, 4] (default 4), `cascadeSplitRatios` (first `cascadeCount − 1` used),
  `cascadeBlendWidth` [0, 0.5], `shadowFilterQuality` (`EQualityLevel`) + `customShadowFilter`;
  `ResolveShadowFilter()` → `FShadowFilterSettings` (`QualityPresets.ShadowFilter`, pcf radius, PCSS blocker / filter
  samples and max penumbra, EVSM exponent, light-bleeding reduction, moment blur radius).
- `ShadowAllocator`: `ActiveCascadeCount`, `CascadeParams[4]` (texel world size, GPU m22, m23, blend width) → shader
  `_CascadeShadowParams[4]`; `_ShadowFilterParams` (pcf radius, blocker samples, filter samples, max penumbra).
  `LocalShadowFrustum(record, light, face, tile, guardTexels)` builds every local tile from the record flags: six cube
  faces (+X, −X, +Y, −Y, +Z, −Z) for point / sphere / line / tube; one tile for cone, pyramid (perspective with the
  pyramid tangents), box (orthographic) and rect / disk (perspective whose apex sits behind the emitter, 120° field of
  view). Frustums are widened by `filterGuardTexels` = pcf radius + 1. Culling uses the frustum's own planes.
- `FLocalShadowSliceGPU` (`// GPU-RECORD` ↔ `ShadowSampling.hlsl FLocalShadowSlice`): atlasRect, depthParams (GPU m22,
  m23, texel world size at unit depth, orthographic flag). `SRV_LocalShadowMatrices`, `SRV_LocalShadowRects` live in
  `ShadowSampling.hlsl`.
- `ShadowSampling.hlsl`: `SampleShadowHard`, `SampleShadowTent` (GatherRed + manual compare, bilinear tent weights; no
  comparison sampler), `SampleShadowPCSS` (Vogel disk blocker search + penumbra, light size from `sourceRadius` /
  source angle), `FilterShadow` (per-record `shadowFilter`), `SampleCascadeShadow(map, light, worldPos, viewDepth,
  normal)` blends into the next cascade over the blend width, `SampleLocalShadow`, `MixBakedAndRealtimeShadow`.
- Moments (S9b): content feature `EFrameFeature.ShadowMoments` (requested when the cascaded light or any valid local
  slice uses VSM / EVSM; a blocked feature throws at record). `ShadowMomentPass` (`shadowMomentShader`,
  `Compute_ShadowMoment.compute`: `MomentFromDepth`, `MomentBlurH`, `MomentBlurV`, one dispatch set per slice) writes
  `CascadeShadowMoments` / `LocalShadowMoments` (RGBA16F, half the depth atlas resolution, same UV layout).
  `ShadowMoments.hlsl`: moment depth (orthographic device depth, else linear depth × `momentParams.x` = 1 / far),
  VSM (d, d²) and EVSM (e+, e+², e−, e−²), Chebyshev with minimum deviation and light-bleeding reduction.
  `_ShadowMomentParams` = (EVSM exponent, bleeding reduction, minimum deviation, 0). `FLocalShadowSlice` gains
  `momentParams`.
- `FShadowInputs` (`Pass/ShadowInputs.cs`: `QueryShadowInputs`, `Read`, `Bind`, `BindGlobal`) is the one consumer
  contract for Deferred, Forward, fog and clouds; moment atlases bind `Texture2D.blackTexture` when absent.
- Cached shadows (S9c): `EShadowUpdateMode { EveryFrame, OnEnable, OnDemand }` on `InfinityAdditionalLightData`
  (`shadowUpdateMode`, `RequestShadowMapRendering()`), local lights only. `LocalShadowCache` (one per pipeline):
  persistent depth atlas `cachedShadowAtlasWidth` × `localShadowMapResolution` (same height so copy regions keep their
  row on every API), frame-atlas tile size, slots keyed by (light instance id, face), LRU eviction excluding tiles of
  the current frame; a light without a slot renders every frame. Content hash = shadow matrix, caster bias, tile size,
  culling mask, rendering layers; OnEnable re-renders on hash or enable change, OnDemand on request.
  Stale tiles draw into the cleared frame atlas; `CopyCachedLocalShadow` (transfer pass) saves them into the cache and
  loads current tiles into the frame atlas. Transaction: `Acquire` marks pending, the copy pass's queue observer marks
  produced, `CommitFrame` validates produced tiles, `RollbackPending` invalidates every tile written in a failed frame.
- Shadowmask casters (S9d): `CascadeShadowPassUtilityData.ShadowmaskExcludesStatic(light)` (baked Mixed + Shadowmask
  mixed mode + `QualitySettings.shadowmaskMode == Shadowmask`) → `ShadowDrawingSettings.objectsFilter = DynamicOnly`
  and `MeshView.excludeInstanceFlags = EMeshInstanceFlags.ShadowmaskStatic` (Static `MeshComponent` whose renderer has a
  lightmap). The flag mask is part of `MeshVisibilitySignature` and `MeshGpuVisibilityKey`; CPU culling
  (`MeshInstanceCullingJob`) and GPU `CullFrustum` (`_ViewExcludeFlags`) both apply it.
- Debug views (S9e): `EDebugView.CascadeShadowAtlas`, `LocalShadowAtlas`, `CachedShadowAtlas`, `ShadowMoments`
  (kernel `DebugViewShadow`, letterboxed, near = bright); a missing atlas shows the magenta Missing view.

### S8

- `Shaders/ShaderLibrary/Lighting/LightShapes.hlsl`: `LocalLightWindow` (range, spot, emitter side, barn doors),
  `EvaluateLocalLightShape` → `FLightShapeSample` (diffuse E / I; specular L, N.L, falloff, energy) and
  `SunDiskSpecularL`. Intensity convention: `radiance` is the on-axis intensity I; area sources emit I / A.
- `SurfaceLighting.hlsl`: `SpecularBRDF` (unclamped GGX × Smith × Schlick) + Lambert, diffuse and specular weights
  separate; `DefultLit` removed (it clamped the BRDF to 1).
- Volumetrics keep the punctual approximation (`LocalLightAttenuation`).
- Tests: `LightShapeGpuTests` (`Tests/Editor/Shaders/LightShapeContract.compute`).

### S7

- `LightPipeline/LightBinning.cs` (pure C#): `TileSize = 16`, `ZBinCount = 128` logarithmic bins
  (`ZBinParams` = count, scale, bias), bin encoding `min | max << 16` (empty = `0x0000FFFF`), `BuildZBins`,
  `TryComputeTileRect` (GPU projection with `renderIntoTexture = true`, one pixel margin for jitter).
- `LightContext.Build(..., screenSize)`: directional records first, local records sorted by view depth; uploads
  records, `_LightTileRectBuffer` (int4 per local light), `_LightZBinBuffer` (128 uint), `_EmptyLightTileMaskBuffer`.
  Local lights whose rectangle is off screen are dropped (`OffscreenCount`).
- `LightTileMaskPass` (async compute, kernel `BuildTileMask`, `lightTileMaskShader`) writes `_LightTileMaskBuffer`
  (tiles × ⌈N/32⌉ uint). `FLightLoopInputs` (`QueryLightLoop`, `Bind`, `BindGlobal`) is the consumer contract.
- `Lighting/LightLoop.hlsl`: `g_LightTileParams` (screen size, tiles per row, words per tile), `g_LightZBinParams`,
  `SRV_LightZBins`, `SRV_LightTileMasks`, `BeginLightLoop` / `BeginLightLoopUV` / `NextLight`.
- Removed: `ZBinningPass`, `Compute_ZBinningLightList`, `FLightBounds`, the empty tile/bin buffers, the ZBin overflow
  counter and its capture. Probes join the list as a second index space in S13.

### S6

- `LightPipeline/LightShape.cs`: `EInfinityLightType { Directional, Point, Spot, Rect, Disk }`, `ESpotShape { Cone,
  Pyramid, Box }`, `EShadowFilter { Hard, PCF, PCSS, VSM, EVSM }` (PCSS/VSM/EVSM sample as PCF until S9),
  `FLightShape` (Unreal form) and `LightShapeUtility` (`FromUnityLight`, `NativeType`, `ApplyToUnityLight`,
  `IsBakeable`, `IsValid`, `PyramidTangents`).
- `InfinityAdditionalLightData`: `m_Shape`, `m_ShapeAuthored`, `m_ShadowFilter`; `shape` returns the authored shape or
  the native-derived one; `SetShape` and the inspector write the native fields; `Reset` and the inspector adopt the
  native shape (`AdoptNativeShape`). Nothing writes on load.
- `FLightRecord` v2 (`// GPU-RECORD` ↔ `Shaders/ShaderLibrary/Lighting/LightRecord.hlsl`): radiance, positionRange,
  forward (w = cone cos outer), right (w = 1 / (cos inner − cos outer)), shape (radius, soft radius, length,
  tan sun half angle), area (rect size + barn doors / pyramid tangents / box size), weights (diffuse, specular,
  volumetric intensity, volumetric shadow), misc (shadow strength, tan soft sun half angle), shadowAtlasRect;
  lightLayer, flags, shadowMatrixIndex, shadowSliceCount, shadowFilter, visibleLightIndex, bakedOcclusionChannel,
  cookieIndex (−1 until S11). Flags: shadow, contact, volumetric, shadowmask, baked, directional, spot cone / pyramid /
  box, planar.
- `FLightRecordPack.TryPack` applies Maximum Distance fade on the CPU (per camera) and rejects invalid shapes;
  `LocalShadowSliceCount` replaces the light-type switch in `ShadowAllocator`.
- `InfinityLightmapperDelegate` is installed with the pipeline's global state (Editor only) and restored on Dispose.

### S5

- One compute pass `ComputeScreenSpaceReflection`, kernels in `Compute_ScreenSpaceReflection.compute`:
  `SSRClassify` (one group per 8x8 trace tile) → `SSRClearSkipped` / `SSRTraceHiZ` / `SSRTraceLinear` (indirect, one
  group per tile of the class) → `SSRResolve` → `SSRTemporal` → `SSRSpatial` (when `spatialPasses > 0`) → `SSRDebug`
  (only for the SSR debug views).
- Pass-local: `SSRTileArgs` (9 uints, `IndirectArguments`; triplets Hi-Z, linear, skip; cleared with
  `ComputeUtility.ClearIndirectDispatchArgs`), `SSRTileList` (3 × tile count, segment per class, `x | y << 16`),
  `SSRTileClass` (R32_UInt).
- Graph textures: `SSRRayHitBuffer` (trace resolution, RGBA32F: hit `(uv, raw depth, +pdf)`, miss
  `(octahedral dir, 0, -pdf)`, none 0), `SSRResolvedBuffer` / `SSRTemporalBuffer` / `SSRBuffer` (RGBA16F, rgb
  premultiplied by confidence in a), `SSRHitDistanceBuffer` (R16F), `SSRMetaBuffer` (RGBA16F: octahedral normal, raw
  depth, history age), `SSRDebugBuffer`. `SSRBuffer` is the spatial output, or the temporal texture when there is no
  spatial pass.
- History: `HistorySSRRadianceBuffer` ← Temporal, `HistorySSRMetaBuffer` ← Meta (`CopyHistorySSR`).
- Reads: Depth, HiZ, GBufferA/B/C (forward-route and invalid pixels are not reflective), OpaqueLightingPyramid, Motion,
  RuntimeTextures blue-noise Sobol textures. Matrices are the `FlipY` jittered set.
- Registry: SSR requires the eight kernels, `utilityShader.ClearIndirectArgs` and the blue-noise textures.
- Settings: `FSSRSettings` / `QualityPresets.SSR`; Volume `ScreenSpaceReflection.Resolve()`.

### S4

- `Runtime/RenderPipeline/Quality/`: `EQualityLevel { Low, Medium, High, Ultra, Custom }`, `QualityLevelParameter`, `QualityPresets.Select`.
- Per feature: `F<Feature>Settings` (quality fields + artistic fields) and a hardcoded `QualityPresets.<Feature>` table of four presets. Medium matches the 0.4.0 defaults.
- Volume components gain `quality` (default `Custom`, so existing profiles keep their values) and `Resolve()`. Artistic fields always come from the Volume.
- Wired in S4: GTAO, RTAO, volumetric fog, volumetric cloud. SSR (S5), contact shadows (S10) and SSGI (S14) adopt the same shape when rewritten.
- Editor: `InfinityQualityVolumeEditor` shows preset values read-only outside Custom and offers "Copy Preset To Custom".

### S3

- `IComputeCommands.DispatchComputeIndirect(shader, kernel, RGBufferRef args, uint argsOffset)`; the args buffer is created with `ComputeBufferType.IndirectArguments` and declared with `ReadBuffer`.
- `Shaders/Utility/Compute_Utility.compute` (`ClearBufferUInt`, `ClearIndirectArgs`) + `ComputeUtility` helper; resource `InfinityRenderPipelineRuntimeShaders.utilityShader`.
- `RGResourceKey` (`id` = `Shader.PropertyToID(name)`, implicit `int`) for every semantic graph resource in `InfinityShaderIDs`; RGScoper errors name the resource.
- `FeatureRegistry`: one descriptor per `EFrameFeature` (kernels, dependencies, temporal requirement, custom predicate). `ConfigureFrameFeatures` only requests; `Resolve` decides, requests dependencies of supported features, and records reasons (Rendering Debugger → Frame Features). `ConfigureContentFeatures` runs after `LightContext.Build` (first content feature: `LocalLightList`).

### S2

- `CameraRenderPolicy` (`Runtime/RenderPipeline/Camera/`): Full (Game, SceneView), Preview, Capture (Reflection). `temporal` gates jitter, TAA/SR and history features (SSR, SSGI, GTAO, volumetrics); `postProcessing` is false for Capture, whose Present writes linear into the float probe target.
- `FrameFeatureSet.EnsureRequiredProducers(requiresTemporalAntiAliasing)`; new `EFrameFeature.HalfResDownsample`.
- RP Asset: `cascadeSplitRatios` (validated at record), `maxShadowedLocalLights`; culling uses `shadowDistance`.
- `FLightRecordPack.TryMapUnityType`; unsupported shapes are skipped and counted (`LightContext.UnsupportedShapeCount`).
- `PassBinStore.GetBuildSerial` drives `MeshCandidateTableStore` invalidation; `MeshGpuVisibilityKey` is a structured key.
- `Shaders/ShaderLibrary/DebugViewModes.hlsl` mirrors `EDebugView`.

### S1

- Product and architecture documents state current code facts only. Process and evidence (model names, receipts,
  hashes, PIDs, cleanup logs) live in `Docs/History/`.
- `Docs/FeatureStatus.md` is the single place that states how verified a feature is.

## Verification at home

The user verifies in commit order with
[Docs/Verification-Playbook-0.5.0.md](Docs/Verification-Playbook-0.5.0.md) (written in S16; each stage appends its
steps as it lands).
