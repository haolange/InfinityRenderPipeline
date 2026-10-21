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
- Image results stay `TODO(UNVERIFIED)` until the user captures them. New tests carry `[Category("Unverified_0_5")]`.
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
| S12 | Baked GI: lightmaps, Shadowmask, Distance Shadowmask, probes, LPPV, APV, baking sky | S2, S6 | done |
| S13 | Reflection probes in the clustered list; shared cube filter | S2, S7, S12 | done |
| S14 | SSGI on the SSR trace/resolve/denoise core | S5 | done |
| S15 | Editor parity: Shader Graph target, Wizard, Frame Settings, Light Explorer, inspectors and handles, Rendering Debugger panels, RenderGraph Viewer, material converter, menus | all | done |
| S16 | Tests, user documentation, FeatureStatus, verification playbook | all | done |
| S17 | Folder layout (`git mv` only) | all | done |

## Interface contracts

Each stage fills in its section before its code lands.

### S17

- `git mv` only (files and their `.meta`, GUIDs and namespaces unchanged; new folder `.meta` files only):
  `Runtime/RenderPipeline/Pass/` → Geometry (Depth, GBuffer, Motion, DBuffer, MeshGpuCull, HiZ, HalfResDownsample),
  Lighting (DeferredShading, Forward, LightTileMask, LightUpload, LightCookie, ReflectionProbe, Subsurface), Shadow
  (CascadeShadow, LocalShadow, ShadowCasterCulling, ShadowInputs, ShadowMoment, ContactShadow), ScreenSpace (SSR,
  SSGI, GTAO, RTAO, ScreenSpaceComposite, ScreenSpaceHistory, OpaqueLightingPyramid), Volumetric (AtmosphericLUT,
  AtmosphericSkyFog, VolumetricFog, VolumetricCloud, FogComposite), Translucent (Translucent, ColorPyramid), Temporal
  (AntiAliasing, SuperResolution), Post (PostProcessing, CombineLut), Output (Utility: display depth, wire overlay,
  gizmos, present; UIOverlay), Debug (DebugView, RenderCapture).
- `Runtime/PostProcess/` (every Volume component) → `Runtime/Volume/`: `IInfinityVolumeActivity` at the root,
  Lighting (SubsurfaceScattering), Shadow (ContactShadow), ScreenSpace (SSR, SSGI, SSAO, RTAO), Volumetric (fog,
  cloud), Post (Bloom, ColorGrading, Exposure, FilmGrain, FilmTonemap, Vignette). The namespace stays
  `InfinityTech.Rendering.PostProcess`, so serialized Volume profiles are unaffected.
- Source paths updated in `NativeViewMotionHistoryTests`, `ScreenSpaceDenoiseTests`, AGENTS and this ledger.

### S16

- Tests: `ShadowAllocator.SelectWithinBudget` (pure greedy budget, extracted unchanged) → `ShadowBudgetTests`;
  GPU contracts `CubeFilterGpuTests` (constant cube stays constant × intensity at roughness 0 / 0.5 / 1; slice f =
  cube face f) and `LightTileMaskGpuTests` (45 lights, 7 × 5 tiles, every word against the CPU reference);
  `ImageComparisonTests`.
- Image regression (`Editor/Tools/Validation`): `ImageComparison` decodes RGBA8 / RGBA16F / RGBA32F readbacks (alpha
  ignored, NaN / Inf = full-scale) → rmse, max error, outlier fraction over a per-pixel threshold.
  `ImageRegressionRunner` (Window/Infinity/Image Regression): per case open (or create) the fixture scene → Play mode →
  `RenderCaptureService` DisplayColor of camera "Camera" after the warm-up → leave Play mode → record into
  `ImageBaselines/<GraphicsDeviceType>/<case>.bin|.json` or compare (default rmse ≤ 0.01, outliers ≤ 0.5% at 4/255).
  State in `Library/InfinityImageRegression/state.json` survives the Play-mode domain reload; report.json per run.
- Documentation~: shadows, baked-gi, screen-space, quality, shader-graph, editor, upgrade-0.4-to-0.5; lights,
  pipeline-asset, cameras, materials, rendering-debugger updated. README feature list, AGENTS known gaps,
  FeatureStatus rows, CHANGELOG, playbook in stage order.

### S15

S15a — Shader Graph target (`Editor/ShaderGraph/`, compiled into `Unity.ShaderGraph.Editor` by
`Unity.RenderPipelines.Infinity.ShaderGraph.asmref`):

- `InfinityTarget` (active when the current pipeline asset's type is `InfinityTech.Rendering.Pipeline.InfinityRenderPipelineAsset`),
  sub-targets Lit, Unlit, Decal. Target settings: Surface Route, Translucent Stage, Blending Mode, Render Face, Alpha
  Clipping, Custom Editor GUI.
- Lit emits every LightMode of InfinityLit (ShadowPass, ShadowCaster, DepthPass, GBufferPass, ForwardPass, MotionPass,
  TranslucentDepthPass, TranslucentT0/T1/T2Pass, Meta) with InfinityLit's keywords (lightmap, shadowmask, APV, DBuffer,
  forward AO, fog, aerial perspective, refraction). Hidden UnityPerMaterial floats `_SurfaceRoute`, `_TranslucentStage`,
  `_RefractionStrength`, `_Subsurface`, `_SSSProfileIndex`, `_SSSThickness`: `MaterialRouteUtility` treats graph
  materials like InfinityLit. Material GUI `InfinityTech.Rendering.Editor.InfinityShaderGraphLitGUI`.
- Blocks: Base Color, Normal (Tangent Space), Metallic (= reflectance), Roughness and Specular Level
  (`InfinityBlockFields`, Unreal form), Emission, Alpha, Alpha Clip Threshold; Decal adds Occlusion. Unlit draws in T2
  only (as InfinityUnlit); Decal draws a DBufferPass through the decal box (uv0 = object xz + 0.5).
- Includes (`Editor/ShaderGraph/Includes`): `InfinitySGCore` (pass ids, globals, Shader Graph library) → pass pre-graph
  (`BakedLighting`, `SurfaceLighting`, `Translucent`) → `InfinitySGGraphFunctions` (Scene Color in T1, Scene Depth in
  DBuffer, Baked GI in GBuffer / Forward, Reflection Probe in Forward, then SG `Functions.hlsl`) → graph →
  `InfinitySGVaryings` → the pass fragment. Motion uses the world position and `unity_MatrixPreviousM` (rigid motion).
S15b — Render Pipeline Wizard (`Editor/Wizard`, Window/Infinity/Render Pipeline Wizard): `InfinityWizardChecks.All`
lists read-only checks with optional fixes. Project: linear color space, compute-capable first graphics API, Infinity
default pipeline, no foreign quality-level overrides, GlobalSettings registered and complete, default Volume profile
complete (`DefaultVolumeProfileFactory.CountMissing`), SRP Batcher. Loaded scenes: mixed lighting not Subtractive, baking
sky with Baked GI on, light shapes in sync with native lights (`LightShapeUtility.IsInSync`), tube / line lights
realtime. Window/Infinity/Append Missing Default Volume Components runs `ValidateAndComplete` on the default profile.

S15c — Frame Settings (`Runtime/RenderPipeline/Context/FrameSettings.cs`): `EFrameSetting` (Decals, Ambient Occlusion,
Ray-Traced AO, Contact Shadows, SSR, SSGI, Volumetric Fog, Volumetric Clouds, Reflection Probes, Light Cookies);
`FFrameSettings` stores a disabled mask (zero = all enabled). RP Asset `defaultFrameSettings` (cameras) and
`reflectionFrameSettings` (probe captures; AO, RTAO, contact shadows, SSR, SSGI off); `InfinityAdditionalCameraData`
`customFrameSettings` + `frameSettings` (per-setting overrides). `FrameSettingsUtility.Resolve` runs per camera before
`ConfigureFrameFeatures`; a disabled setting withholds its feature requests (RTAO falls back to GTAO; cookies / probes
resolve no atlas entries). Rendering Debugger → Infinity Rendering → Frame Settings shows each value and its source.

S15d — Light Explorer (`InfinityLightingExplorerExtension`, `[SupportedOnRenderPipeline]`): Unity's Lights, Reflection
Probes, Light Probes and Static Emissives tabs plus Infinity Lights (shape read-only, shadow filter / update mode,
contact shadows, volumetric, max distance, weights, light layers), Infinity Probes (influence, sphere radius) and Volumes
(global, priority, weight, blend distance, profile). Lighting window → Environment (`InfinityLightingEnvironmentSection`):
atmosphere profile of the RP Asset, baking sky status / command, baked ambient and reflection intensity.

S15e — Inspectors and handles: the Light inspector has foldout sections (General, Shape, Emission, Baking, Cookie & IES,
Shadows, Volumetric, Layers & Distance) and an Additional Properties toggle; Scene view handles edit range, outer angle
(cone / pyramid outline), source radius, tube length, rect size and disk radius through `SetShape` with Undo on both
objects. Infinity reflection probe data draws a sphere-radius handle. Decal projectors: box handle on the transform,
UV scale / bias and start / end angle fade (`DecalProjection.hlsl`, per-renderer property block, depth-derived normal).
`[HelpURL]` on the RP Asset, GlobalSettings and the Infinity components (`InfinityDocumentation.BaseUrl`).

S15f — Rendering Debugger panels and Scene view Draw Modes: `DebugViewCatalog` assigns every `EDebugView` to one panel
(Material, Lighting, Screen Space, Rendering); each panel's view field and the master Debug View drive the one
`InfinityDebugDisplaySettings.debugView`. New views (appended, DEBUGVIEW_* 19–29): Metallic, Specular, ShadingModel,
RenderingLayers (GBuffer), LightComplexity / ReflectionProbeComplexity (kernel `DebugViewLightLoop`: the clustered
loop's visited count at the render pixel, heat ramp to `complexityRange`), BakedDiffuse / IndirectDiffuse /
IndirectSpecular (deferred outputs), ReflectionProbeAtlas (kernel `DebugViewProbeAtlas`: six faces of one probe from
`LightContext.ReflectionProbeFirstSlice`), ContactShadows (sun trace). Volume panel = CoreRP `DebugDisplaySettingsVolume`
widgets in its own "Volume" panel; Infinity Timings toggles `ProfilingSampler.enableRecording` for every
`CustomSamplerId` and lists GPU / CPU ms. Per camera: `CameraFrameState.debugView =
InfinityDebugDisplaySettings.ResolveDebugView(camera)` (Draw Mode override by camera entity id, else the debugger;
None for probe captures and previews); SSR and TAA diagnostics read it. `Editor/Debug/InfinitySceneViewDrawModes`
registers `SceneView.AddCameraMode(DisplayName, "Infinity <Panel>")` and syncs in `SceneView.beforeSceneGui`.

S15g — RenderGraph Viewer: `RGDebugSession` (Runtime/RendererCore/RenderGraph/RGDebug.cs) is the hook; while
`captureEnabled` (the viewer window is open) `RGBuilder.Execute` publishes `CaptureDebugSnapshot()` right after
`CompilePass`, keyed by `RGBuilder.debugLabel` (the camera name). `RGGraphSnapshot`: passes in record order (type,
queue, culled, cullable, wait / signal pass, reads, writes, allocates, releases) and every resource (buffers, textures,
acceleration structures; name, imported, size / format, first / last live use, allocating / releasing pass).
Window/Analysis/Infinity Render Graph Viewer draws passes × resources with the pool lifetime band, filters (culled,
imported, unused, name), pause, copy as text and a details pane.

S15h — Material conversion and create menus: `InfinityMaterialUpgraders` (CoreRP `MaterialUpgrader` subclasses
registered through `IMaterialUpgradersProvider` with `[SupportedOnRenderPipeline]`): Built-in Standard / Specular /
Legacy / Unlit, URP Lit / Complex Lit / Simple Lit / Unlit, HDRP Lit / LitTessellation / Unlit → InfinityLit /
InfinityUnlit (roughness = 1 − smoothness, metallic → `_Reflectance`, base map tiling x → `_BaseColorTile`, emission
color when enabled, transparent → T0, finalizer applies the base map keyword and `MaterialRouteUtility.ApplyPassState`).
Window/Infinity/Material Conversion Wizard lists convertible materials (project or selection) with the dropped inputs;
the Render Pipeline Wizard gains "Scene materials use Infinity shaders". GameObject/Light/Infinity creates every shape;
GameObject/Rendering/Infinity creates volumes, a decal projector (shared `Assets/InfinityDefaultDecal.mat`), box /
sphere reflection probes and a light probe group; Assets/Create/Rendering/Infinity holds the pipeline asset, profiles,
mesh asset, Global Settings and a Volume profile with the default components; Assets/Create/Shader Graph/Infinity
creates Lit / Unlit / Decal graphs.

S15i — Validation fixtures (`ValidationFixture` builder; Window/Infinity/Create … Fixture, scenes under
`Assets/Scene/Validation`): Shadows (one spot per filter, PCSS sphere light, OnDemand cached spot, PCSS sun, contact
shadow pebbles with the ContactShadow Volume), Cookies (spot / pyramid / tiled sun checker, cube cookie, IES rings
through CoreRP's importer), Reflection Probes (baked + realtime box probes by importance, sphere-influence probe,
roughness ramp), Baked Lighting (static room, Shadowmask sun, baked bulb, emissive panel, probe grid, LPPV, baked
reflection probe, LightingSettings asset), Screen Space (mirror / rough strips, SSR + SSGI High, GTAO).

- Offline checks: `Tools/StaticCheck/shadergraph_check.py` compiles an emulated generated shader for every pass and keyword
  variant; `static_check.py sgstrings` resolves include / template paths, type names and LightMode constants.

### S14

- Shared trace core: `RayTracing/ScreenSpaceRayTrace/ScreenSpaceTraceTiles.hlsl` (tile packing, half-resolution quad
  rotation, ray record encoding, Vogel-disk ray reuse offsets, cone mip) with `ScreenSpaceTraceUtility` (C#: tile size,
  trace size, tile count). SSR and SSGI both use it, together with `SSRTRayCast.hlsl` (Hi-Z walk) and
  `ScreenSpaceDenoise/{TemporalAccumulation,AtrousFilter}.hlsl`.
- One compute pass `ComputeScreenSpaceIndirect`, kernels in `Compute_ScreenSpaceIndirectDiffuse.compute`:
  `SSGIClassify` (8x8 trace tiles: trace when a valid deferred pixel is inside, else skip) → `SSGIClearSkipped` /
  `SSGITrace` (indirect per class; cosine-distributed direction, pdf NoL / pi, Hi-Z walk) → `SSGIResolve` (ratio
  estimator with f = NoL / pi over `resolveSamples` reused rays, cone mip of the lighting pyramid) → `SSGITemporal`
  (surface reprojection, variance clip, history age) → `SSGISpatial` (when `spatialPasses > 0`).
- Pass-local: `SSGITileArgs` (6 uints; triplets trace, skip), `SSGITileList` (2 × tile count).
- Graph textures: `SSGIRayHitBuffer` (trace resolution, RGBA32F, same record as SSR), `SSGIResolvedBuffer` /
  `SSGITemporalBuffer` / `SSGIBuffer` (RGBA16F: Lambert-convolved radiance (irradiance / pi) premultiplied by
  confidence, a = confidence), `SSGIMetaBuffer` (octahedral normal, raw depth, history age). `SSGIBuffer` is the spatial
  output, or the temporal texture without a spatial pass.
- History: `HistorySSGIRadianceBuffer` ← Temporal, `HistorySSGIMetaBuffer` ← Meta (`CopyHistorySSGI`, through the
  same `CopyHistoryRadianceMeta` as SSR).
- Composite unchanged: `lighting - IndirectDiffuse * a + albedo * rgb`; a miss keeps the baked / probe / sky diffuse.
- Registry: SSGI requires the six kernels, `utilityShader.ClearIndirectArgs` and the blue-noise textures; depends on
  HiZ and OpaqueLightingPyramid.
- Settings: `FSSGISettings` / `QualityPresets.SSGI`; Volume `ScreenSpaceIndirectDiffuse.Resolve()`.

### S13

- Authority: Unity `ReflectionProbe` / `VisibleReflectionProbe` (mode, bounds, blend distance, box projection,
  importance, intensity via `hdrData.x`). `InfinityAdditionalReflectionProbeData` adds only the sphere influence
  (`influence`, `sphereRadius`). Probes are axis aligned: `SupportedRenderingFeatures.reflectionProbeModes = None`,
  `reflectionProbesBlendDistance = true`.
- `FReflectionProbeRecord` (`LightContext.cs` GPU-RECORD ↔ `Lighting/ReflectionProbes.hlsl`, 7 × float4):
  `worldToBox0..2`, `extents` (influence half extents incl. blend; w = blend), `capture` (box-space capture position;
  w = box projection), `projection` (projection half extents), `parameters` (intensity, first atlas slice, mip count
  - 1, sphere flag).
- List order is the blending priority (`ReflectionProbePriority`: importance descending, then volume ascending); a probe
  without a free atlas slot this frame is skipped (`maxReflectionProbes` slots shared by the frame's cameras). Probes are the clustered list's second index space: own
  log z-bins (`_ProbeZBinBuffer`, same `LightBinning.BuildZBins`), tile rectangles (`_ProbeTileRectBuffer`) and GPU tile
  bitmasks (`_ProbeTileMaskBuffer`, `RecordTileMaskPass` shared with the light list).
- `ReflectionProbeAtlas` (one per pipeline): persistent RGBA16F Texture2DArray, `reflectionProbeResolution`² (power of
  two, full mip chain), 6 slices per slot, `maxReflectionProbes` slots (0 disables). Keyed by (probe identity, texture
  identity); filtered when new or when `Texture.updateCount` changes; realtime probes once per frame. LRU slot reuse
  excluding probes used this frame. Transaction as the cookie atlas (pending → produced on queue → commit / rollback).
- `Compute_CubeFilter.compute` (`FilterCubeToArray`, `FilterArrayToArray`) is the only GGX prefilter: the sky
  (`ComputeAtmosphericLUT`) and the probe atlas (`ComputeReflectionProbes`) both use it; mip m = roughness
  m / (mipCount - 1).
- Passes: `ComputeReflectionProbes` (feature `ReflectionProbes`, requested when a probe reaches the screen) writes the
  atlas (`InfinityShaderIDs.ReflectionProbeAtlas`) and `ComputeProbeTileMask` writes the probe tile masks.
  `FLightLoopInputs` carries both to Deferred and Forward; without probes it binds a 1×1 empty atlas and count 0.
- Shading: `BlendReflectionProbesWithSky` accumulates probes front to back (influence × remaining weight, stopping at
  1) and gives the remainder to the sky prefilter; the result replaces the sky term before `EnvBRDFApprox` in
  Deferred and Forward (`UAV_IndirectSpecular` / `indirectSpecular`), so SSR still replaces it by confidence.
  Translucent T0 has no specular environment term and is unchanged.

### S12

- Baked diffuse priority (`Lightmap.hlsl SampleBakedLighting`, `MeshBakedLighting.hlsl SampleMeshBakedLighting`):
  lightmap → Adaptive Probe Volumes (`PROBE_VOLUMES_L1 / L2` and `_EnableProbeVolumes`) → Light Probe Proxy Volume
  (`unity_ProbeVolumeParams.x == 1`, RendererList draws only) → light probe SH → sky SH (deferred / forward, alpha 0).
  Shadowmask / probe occlusion come from the same source. GBuffer and Forward renderer lists request
  `LightProbeProxyVolume | OcclusionProbeProxyVolume`; `SupportedRenderingFeatures.lightProbeProxyVolumes = true`.
- APV: `ProbeVolumeIntegration` (Runtime/RenderPipeline/Lighting) initializes `ProbeReferenceVolume` with the pipeline
  and, per camera before culling, `SetEnableStateFromSRP`, `PerformPendingOperations`, `UpdateCellStreaming` (not
  Reflection / Preview), `BindAPVRuntimeResources`, `RenderDebug`, `UpdateShaderVariablesProbeVolumes`, keywords.
  RP Asset implements `IProbeVolumeEnabledRenderPipeline` (`lightProbeSystem`, SH bands, budgets, streaming,
  scenarios); `overridesLightProbeSystem` / `skyOcclusion` follow it. `ProbeVolumesOptions` joins the default Volume
  registry. The stripper drops `PROBE_VOLUMES_L1 / L2` variants no Infinity asset of the build uses.
- InfinityLit-Instanced gains `ShadowCaster` and `Meta` (its own UnityPerMaterial layout). TerrainLit gains lightmaps
  (`LIGHTMAP_ON`, `DIRLIGHTMAP_COMBINED`, `SHADOWS_SHADOWMASK`, APV) writing the baked GBuffer targets (add passes
  write 0), `ShadowCaster` and `Meta` (splat-mixed albedo).
- MeshScene probes: `RenderContext.InterpolateMeshProbes` gathers every MeshComponent whose probe anchor moved or
  whose probe data changed (`LightProbes.lightProbesUpdated` / `needsRetetrahedralization` bump
  `MeshBakedLighting.ProbeVersion`) and interpolates them in one `CalculateInterpolatedLightAndOcclusionProbes` call.
- Baking sky: Editor `InfinityBakingSky` (Window/Infinity/Lighting/Use Atmosphere As Baking Sky) saves the atmosphere
  cubemap faces (`InfinityRenderPipeline.TryGetAtmosphereCubemap`) as a Texture2DArray asset and sets the scene skybox
  to `Hidden/InfinityPipeline/BakingSky` (same face layout as the pipeline); `Lightmapping.bakeStarted` refreshes the
  faces of scenes that use it.
- Shadow casters: both shadow passes set `Matrix_ViewFlipYProj` (what every caster shader projects with) as well as
  `Matrix_ViewProj` to the shadow matrix, and restore the camera's values.

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
- `FShadowInputs` (`Pass/Shadow/ShadowInputs.cs`: `QueryShadowInputs`, `Read`, `Bind`, `BindGlobal`) is the one consumer
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

### Local staged verification — 2026-10-20

- Frozen original range: c92be7707f45a1789688e5cc16e0845f183b2910..eab18e5ff74b207e52186e5bfa3290fdbebb6efa,
  39 commits / 18 endpoints. The endpoint table and receipts are in
  `Documentation~/Evidence/Verification-0.5-2026-10-19/S1-report.md` and `S1-results.json`.
  Work remains on local main; accepted fixes target only origin/claude/infinityrp-0.5. No new branch or Player build.
- S0 bounded tool contract is accepted in 6b57678: 10/10 offline controls and main static 0 errors / 0 warnings.
  The later S0 Film-reference checkout has a separate static FAIL24 (17 directory/meta states, 7 historical links);
  its reference images do not replace that failure or re-accept the original stage.
- S1 replay 2276094 plus the minimal repair passed shader callbacks 12/12 and Infinity EditMode 329/329.
  Final full Example callbacks: 1535 discovered/started/finished, 1482 Passed, 0 Failed, 53 original Skipped,
  root Skipped:Ignored. Unchanged framework XML has1502 leaves; separate callback-derived XML restores only33
  actual checkpointed skipped leaves omitted by UTF1.8.0 after reload. No1535/1535 PASS claim is made.
- Shared-runner source, real reload/immutability/negative controls and shader callback fixtures received independent
  review. SDK-local corrections preserve original assertions: IPv4 loopback endpoint, config Save cache invalidation,
  explicit UI material with bounded normal repaint, and exact owned-object cleanup across reload/Undo. The ownership
  control preserves an unregistered same-name sentinel and unrelated Undo; final cleanup leaves original9 Spazon roots.
  SDK patches/hashes remain separate from package commits; no external package production-rendering path was changed.
- S1 Camera auto-add/Undo/Redo, Film→ACES and Debugger behavior received independent acceptance. The same640×360
  normal Game-frame AntiAliasing and DisplayColor buffers are byte-identical between S0 Film and S1 ACES;
  mode None retains the input and changes the output. Effective parameters, native Inspector and all retained
  Debugger panels were checked. The S1 native screenshot contains an earlier operator command error and is not
  evidence of a globally clean Console. Returning to S1 matched all5 candidate source hashes, compiled normally,
  passed static0/0 and restored original SceneSetup/selection. Main compilation succeeds; main shader integration failed13=4Passed/9Failed and stays explicitly unaccepted.
- Original Example remains in place. An approved lightweight APFS Library entrance links large ExFAT caches.
  A failed98.22MiB SourceAssetDB/ArtifactDB control was rolled back. The useful DataStore-root control copied only
  uds.db9,797,632bytes and linked262 original entries; large bins stayed on ExFAT. Multiple real runs/cold starts
  and normal exits now work. This does not establish the precise Unity native root cause. Keep this active cache
  layout; do not treat its live database/new bins as disposable intermediate data.
- Original427-input audit:425 byte-identical, two native settings-format changes recorded. The external suite's
  enterPlayModeOptions3→1 was restored only at that field and independently reopened/no-op checked. No TimeManager
  value was assigned and no old binary settings were blindly restored. User edits stay outside repair commits.
- Owned intermediate root: InfinityExample/intermediate/verification-0.5/20261020-run02; budget4GiB, last allocated
  size about1.33GiB. Required preimages, failed controls, final XML, raw Film buffers and concise reports remain for
  independent review/final audit. Avoid more cache copies or whole-tree hashes; retire completed consumers' redundant
  scratch data after their evidence is collected. Final cleanup is not yet complete.
- S2 original exact-category run discovered4 tests and failed all4 as Invalid: NUnit rejects the hyphen in
  Unverified-0.5 before executing their bodies. A literal-only correction to Unverified_0_5 preserves the same4
  identities in S2 and all93 pending annotations across30 main test files. No assertions or test bodies change;
  FeatureStatus stays Open. Main's earlier missing utilityShader/
  atmosphere-kernel errors remain observed with unconfirmed cause: ResourcePath targets exist. The initial S17
  missing-path inference was incorrect and withdrawn.
- Independent verification accepts the bounded historical S1 checkpoint and allows S2 historical replay after its
  Fix S1 commit. The cumulative main integration failures remain blockers for final delivery, owned by the relevant
  later stages. Three-way S12 shadow evidence, S16 regression positive/negative
  controls, S17 baseline reuse, final HEAD acceptance and valid GPU timing are unexecuted. FeatureStatus and
  Pending markers remain set (canonical category `Unverified_0_5`). PR #11 remains Draft; the overall task is not complete.
- S2 fix-forward candidate repairs Preview jitter and full-width custom Mesh scene eligibility through CPU/GPU,
  shadow views and cache keys. Historical replay passed camera10/10, Mesh5/5 and pending-category4/4;
  final all-Example run was1544=1491Passed/0Failed/53Skipped. Independent review matched all1544 identities
  and the33 checkpointed skipped leaves omitted by raw framework XML after two reloads. Native Preview/Reflection
  and static additive-scene controls were inspected; Debug View's ten modes and enabled AO/SSR/SSGI ran normally.
  On2026-10-20 the user confirmed the native menu works and selected Albedo. This is user-manual evidence;
  a later Editor restart cleared the model, so no post-restart Albedo readback is claimed.
- S2 shadow-distance128/8 controls changed cascade distances/culling spheres; invalid ratios failed one frame
  with the expected exception and recovered on seven following frames, preserving original RP JSON/dirty state.
  Native Inspector inspection exposed nested Foldout Header errors around Diffusion Profiles. The repaired helper
  uses a styled foldout inside a vertical layout group; its expanded array and collapse/reopen controls passed.
  The later main Light inspector now pairs the whole Shape group inside its mixed-type condition. Its production
  OnGUI was exercised in a task native window using transient PreviewScene lights: mixed hides Shape, Point and
  Directional show their own fields, with no layout errors. Normal Inspector selection of PreviewScene objects
  did not display them, and an additive-scene control was rejected by Unity before mutation because the user's
  resumed scene is untitled. Neither setup failure is reported as a production Inspector failure.
- User resumed with a different Editor PID, clean untitled scene and Play options1. Preserve that new effective
  context rather than restoring old Spazon/options3. Original RP source asset/meta bytes remain unchanged;
  cross-stage JSON differs because main has additional serialized fields. Main's default-profile
  completeness failure is still observed and blocks cumulative rendering acceptance; no silent fallback or asset
  migration was used. The bounded S2 final source/changed-contract review is accepted below; no marker removal is claimed.
- Historical S2 gates accepted by the independent verifier on2026-10-20 (Unity6000.6.0f1/macOS Metal):
  compile, exact-stage category4/4, camera10/10, Mesh5/5, allExample1544=1491Passed/0Failed/53Skipped,
  the recorded normal-frame/behavior controls and user-manual popup evidence. A final control on the exact same
  S2 candidate called production TryMapUnityType for nine inputs: supported types map correctly; Disc/Pyramid/
  Box/Tube/undefined return unsupported. Native culling excluded Disc, so counter1 execution is not claimed.
  Main changed-contract tests25/25 and the production Light Inspector host control passed. The cumulative main
  default-profile/APV failure remains a final-HEAD blocker, outside this bounded historical-stage acceptance.
- S2 deliberate deliverables are under Documentation~/Evidence/Verification-0.5-2026-10-19/S2 (28 key files,
  compact identities/receipts, original33skip leaves and cleanup receipt), alongside S2-report.md/S2-results.json.
  With producers stopped,631 consumed intermediate files were retired:418560→7680KiB, releasing410880KiB.
  Only run02/S2/final-replay-candidate3.patch, its identity, contract-fixture.cs, and gtao-off-mesh-red/
  {capture.json,Lighting-65-449.bin,DisplayColor-65-449.bin} remain for S3 replay construction and image comparison.
  Delete those six inputs immediately after S3 acceptance. The owned Assets/InfinityRPVerification_0_5_20261020_run02/S2
  fixture has the same S3 consumer. Run02/preimage and input-manifest.json remain for the final source audit;
  they are not a new backup. S3 may start after this Fix S2 batch is committed and pushed. Pending markers and
  FeatureStatus remain unchanged until the final verification update; the overall task and cleanup are incomplete.

The user verifies in commit order with
[Docs/Verification-Playbook-0.5.0.md](Docs/Verification-Playbook-0.5.0.md) (one section per stage, in stage order).

- S3 original8032704 plus accepted S1/S2 repairs compiled and passed FeatureRegistry5/5. The first all-Example run
  discovered/started/finished1549:1495Passed/1Failed/53Skipped; the sole failure was resource preflight with a null
  Utility slot. A full-main explicit assignment initially passed current-schema JSON/Undo/second-save/cold-read
  checks but dropped historical zBinningShader, which main no longer declares. That preservation conclusion was
  withdrawn; the second full run failed the same preflight on zBinning. After normal Editor exit, exact file/meta
  guards allowed only that task-owned save operation to be reverted to the current-byte preimage cabd1631.
- The accepted S3 replay explicitly binds Utility in memory without dirtying/saving the original asset. Original
  zBinning and all other references remain intact. Resource preflight passed1/1; allExample1549 callbacks were
  1496Passed/0Failed/53Skipped after two reload/resumes. Raw framework1516=1496Passed/0Failed/20Skipped;
  the separate derived XML recovers only33 actual checkpointed skipped leaves. Stable identities and the53skip
  set were independently matched; no full1549PASS or persistent resource upgrade is claimed.
- S3 actual GPU controls cover utility clears, sentinel bounds, both argument-buffer types, offset12 and zero
  dispatch. A normal-frame FGraphics task also executed two real graph compute encoders with declared reads/writes
  and two terminal Transfer readbacks, then disposed688bytes of control resources. An earlier null-sampler harness
  failed before graph Execute; its tiny unsubmitted native resources reached their cleanup boundary at normal
  Editor exit. It is retained as a failed verification setup, not a production path failure or successful release.
- SSR Off/Records/Blocked/recovery each have32 distinct normal frames and actual Debugger images; all11 captured
  frames/22buffers completed and retired. Matching S2/S3 view/projection/current+previous jitter gives byte-identical
  Lighting. DisplayColor is visually equivalent in this fixture but not exact:2794 changed pixels, RGB mean absolute
  difference1.2100914e-5/max0.4486694 (6pixels over0.1). No cause for those few differences is established. Old SSR
  uninitialized-variable warnings remain a later S5 shader-quality item, not an accepted algorithm/performance result.
- Final32 normal accepted frames produced0 new errors/exceptions in the exact new log interval. Temporary SSR and
  Utility references restored the complete original JSON. Exiting Play restored the user's clean untitled two-root
  scene, root GameObject/Transform snapshots, backgroundfalse, options1 and selectionnull. Normal native Quit ended
  PID16585; source GS/meta match original bytes, and all13 separately protected small inputs remain unchanged.
  Independent verification accepts this bounded historical S3 checkpoint; main's persistent resource/profile
  integration and all later-stage/final gates remain open. Source/history/schema and rendering gates stay separate.
- S3 exact documentation/report candidate439faef9 received independent acceptance. Deliberate reports, original/final
  XML, key native images and compact receipts live under Documentation~/Evidence/Verification-0.5-2026-10-19/S3.
  With producers stopped,407 consumed files were retired, including S2's six inputs and owned fixture: combined
  allocation332800→768KiB, releasing332032KiB. Raw paths in statistics are historical source identities, not retained
  openable payloads. Only run02/S3/original-failure-replay.patch and final-source-identity.json remain for S4 replay
  construction; retire them immediately after that patch is frozen. Existing run02/preimage remains solely for final
  source audit, without the duplicate S3 GS preimage. Main's durable resource/profile configuration, S4–S17,
  final HEAD, valid GPU timing and overall cleanup remain incomplete. PR11 stays Draft.


### S4 local Editor validation (active, 2026-10-20)

- Historical f70837766999 plus accepted S1/S2 fixes compiled. QualityPresetTests4/4; original full Example1553
  identities=1500Passed/0Failed/53Skipped. After the cloud repair, final full1556=1503Passed/0Failed/53Skipped,
  reload/resume2/2. Independent review checked discovery, callbacks and all stable leaf identities. Native XML omits33
  actual pre-reload skipped leaves; callback XML preserves their original checkpoint results. Roots stay Skipped:Ignored;
  frameworkComplete=false and callbacksComplete=true are separate facts, not an all-PASS result.
- Four production Volume inspectors passed bounded Custom/preset preservation, editable artistic fields, native Copy
  Preset To Custom button clicks and exact Undo/Redo. Initial scratch-host NotEditable was removed before effective tests.
- Original cloud five-level normal frames failed: foreground bright speckles and missing sky clouds. Native Frame Debugger
  identified ComputeVolumetricCloud > VolCloud_Trace > Compute_VolumetricCloud. The fix uses Profile planet radius,
  unclamped world-to-planet height, ordered outer-minus-inner intervals, planet/opaque clipping and shared light-ray geometry.
  New GPU tests3/3 cover two planet radii, independent double boundary classification and exact clear versus distant depth.
- Repaired production five-level frames and valid foreground/inside-layer near/far wall controls completed and retired.
  Invalid initial material and stale-bounds wall captures are recorded separately and excluded from acceptance.
- Actual S3/S4 Custom A/B uses the same cloud geometry fix on both sides to isolate presets: all11 pairs of buffers
  across GTAO/RTAO/fog/cloud are byte-identical. Each capture has128 accepted warmup frames, phase1 modulo16, equal camera
  matrices/current+previous jitter and equal low-level Volume JSON after excluding quality. All22 buffers were independently
  read, then retired (7948800 logical bytes; allocation386048->370688KiB). Fixed simulation time limits this evidence.
- New CloudShell Metal potentially-uninitialized warnings are preserved, not attributed to old SSR warnings; no matching
  unassigned source path or runtime failure has been established. Full shader/artistic quality and performance are not passed.
- Editors26152/37499/44517 exited normally. The latest reference restored clean original roots/transforms, background=false,
  Play options1 and full original GS JSON without saving; GS/meta bytes stayed identical. Its33 source files and21 main-owned
  fixture files passed guards before main checkout. The first session initial-bind receipt was overwritten by a later session;
  the remaining original context restoration and source/log identities are retained without relabeling the later receipt.
- main now contains the exact helper/tests and cloud fix, adapting only LightRecord directional detection; cloud capture maps
  to the real graph output. Final main DXC3kernels, static_check0/0 and diff check pass. Main Unity compile passed;
  main targeted results: Cloud3/3 and quality-name-filter10/10. The unchanged default Profile still blocks main rendering.
  Dedicated grazing/far-plane and real Point positive/zero controls now pass independent review; independent review accepts Fix S4. S5 unlocks after its commit/push.
- S1 compacted31 payloads;2037 consumed files retired (1189888->1024KiB). Only its guarded startup helper, one4096-byte
  specimen and quarantine parent remain for final environment audit. S3's two carry inputs were consumed into S4 replay,
  then deleted; S3 intermediate is absent. Active Library caches were untouched.
- Current carry-over consumer: S5 historical replay construction uses only run02/S4/s4-final-replay.patch and
  s4-final-replay-identity.json. All S4 runtime, main targeted and report reviews are complete.
  Budget512MiB logical/max20 retained320x180 normal frames; consumed raw captures are deleted after exact stats/hash review.
  Root preimage/input-manifest remain solely for final protected-source audit. Overall validation/cleanup incomplete; PR11 Draft.
- Supplement Editor51277 restored original configuration and exited normally; all38 historical source identities and21
  main fixture identities passed before main return. Production grazing center segments37842..130660m and243446..336264m
  lie beyond far100m with clear depth. Real Point positive/zero controls change cloud radiance, while Lighting is identical;
  distinct jitter/sample phases prevent treating them as a quantitative energy-only image equality claim.
- Deliberate S4 evidence is27 payloads (8294192 bytes) plus one8123118-byte index under Documentation~/Evidence/
  Verification-0.5-2026-10-19/S4; original/final XML, checkpoints, key native images, capture identities and failures remain.
  After raw review48 more buffers/15206400 logical bytes retired (420352->389632KiB), in addition to earlier raw retirements.
  After final report acceptance,607 consumed scratch files/60170712 logical bytes were retired; allocation391168->768KiB.
  Only the exact replay patch/identity (149447 bytes) remain for S5; cleanup.json lists identities. Active caches were untouched.
- User clarified session scope: manage only this task's original Example Editor. Other sessions' Unity processes are neither
  operated on nor waited for; asset-import workers are not additional Editor sessions for this task.

### S5 local Editor validation (2026-10-21)

- Historical b8a582d287fa plus accepted predecessors compiles. StochasticSSRNumeric5/5 and quality9/9;
  original full Example1566=1513Passed/0Failed/53Skipped. Diagnostic replay1567=1514/0/53, reload/resume2/2.
  Independent audit matches every discovered/started/finished identity, unchanged53 skips and only one new UInt32 test.
  Native XML1534 lacks33 actual checkpointed skips; callbacks preserve them. Roots stay Skipped:Ignored;
  frameworkComplete=false and callbacksComplete=true are separate facts, not an all-PASS root.
- Passive capture exposes SSRMeta and requested R32_UInt tile class with graph-owned Transfer lifetime, preserving the
  ordinary pass-local path, normal-beauty guard, Debug View and history. UInt32 decoding preserves uint.MaxValue and ROI.
  Main compile, SSR numeric5/5 and configured capture13/13 passed. Original default-Profile and then cubeFilter-resource
  failures are retained; explicit memory-only configuration enables these tests without claiming a durable asset upgrade.
- Four main normal A/B runs use matched matrices, jitter and phase129 mod256. Stable Lighting, RayHit, Resolve,
  Temporal, SSR and Meta are byte-identical with/without requested classification. Display has tiny repeat variation;
  its all-output bit identity and cause are not claimed. R32 tile values {0:144,1:16,2:80} decode correctly.
- Original Example normal frames: four native SSR DebugViews plus Custom Frame Debugger8 events (Classify2, Trace3,
  Resolve1, Temporal1, Spatial1). Compute details were blank. Only actual viewport pixels count; native menu selection
  itself was driven by public runtime control. No inputs were sent to another session's Editor.
- Independent physical-floor ROI512 pixels grows age1/5/9/13/17. Four quad phases give zero mismatches only for their
  correct2x2 offsets. Dynamic cube actual Depth change exposes629 floor pixels: all restart at1 with Temporal=Resolved,
  then628 advance2/3; one edge remains1. Unoccluded control stays64, camera historyReset=false. Initial Static motion
  harness was invalid and is recorded separately. Vector serialization/cleanup failures were fixed in scratch harness.
- Small0/4/8/12deg orbit and four qualities show moving, clear mirrors and blurred rough receivers without an observed
  firefly burst. Old-reflection ROI562 pixels retains45/30/25 red Temporal-Resolved differences>.01 over three frames;
  no obvious whole-object tail was observed, but zero ghosting is not claimed. Legacy black sky horizon remains outside
  this SSR-specific repair. No general image, motion or platform quality acceptance is inferred.
- Two Confidence controls independently reconstruct31800 mirror pixels each: errors<.000488 at tolerance.001,
  36 edge-fade pixels,163 near MaxDistance and4 valid sampled backfaces per case with zero alpha. Four actual trace
  sizes160x90/160x90/320x180/320x180; rough receiver measured115/255. Ultra SSR=Temporal bytewise, other qualities differ.
  Medium/Ultra CPU marker positive/negative and output route prove spatial gating; GPU marker counts were all0,
  so they are not GPU execution telemetry or an Ultra native Frame Debugger screenshot.
- Public FrameTimingManager supplies four quality sets, each128 warmup + three300 unique valid CPU/GPU timings at
  320x180. Whole-Editor frame medians/P95 are retained; GPU medians are not monotonic. No SSR-pass cost, general
  speedup, isolated GPU load or pre-0.5 performance baseline is claimed.
- Editors56088/59923/64172/70233/70619 have exited. Final owned70619 capture/probe drain0, clean untitled roots/transforms,
  backgroundfalse/options1 and full original GS JSON restored. All15 small protected inputs,42 historical source and21
  main-owned fixture identities pass. A reused initial-bind filename lost the64172 receipt; the70619 receipt is explicitly
  separate and never relabeled as the earlier one. Direct public Exit after clean guards ended the unfocused Editor
  when a scheduled delayCall did not exit; the expected disconnected CLI reply is not a failed runtime gate.
- Static_check0/0 and diff check pass. Main production/test code is the same previously Unity-tested candidate;
  final documentation/report review and commit/push are the remaining S5 unlock conditions. FeatureStatus and pending
  test markers await final staged acceptance; durable Profile/resources, S6-S17 and final HEAD remain open. PR11 Draft.
- Deliberate report/evidence: Documentation~/Evidence/Verification-0.5-2026-10-19/S5-report.md and S5/.
  Current S5 intermediates feed only final evidence review/cleanup and S6 replay construction. The sole post-cleanup
  carry is s5-final-replay.patch plus s5-final-replay-identity.json; delete after S6 replay is frozen. Active Library/DataStore
  are untouched. Root preimage/input-manifest remains solely for final protected-source audit; overall cleanup incomplete.
- Independent final report/source/identity review accepted S5. Completed final cleanup of780 remaining scratch
  files/40361661 logical bytes, allocation237056->768KiB; two S6 replay inputs
  (160905 bytes) remain. AppleDouble handling caused the first cleanup precheck to stop and the next to stop
  after deleting budget.json/its sidecar; the final receipt distinguishes that tiny earlier deletion from these measured
  remaining files. Source commit ecd3c7b preceded cleanup because later shell commands ran after the first Python failure;
  this follow-up records actual cleanup without rewriting history. Push/remote/clean checks unlock S6.

### S6 local Editor validation (active, 2026-10-21)

- S5 source ecd3c7b plus cleanup ledger98012a125f3e53a8d53119dfcca844fb0d76c404 pushed to the explicit PR ref;
  remote main remains c92be7707f45a1789688e5cc16e0845f183b2910; clean package worktree verified before S6.
- S6 original834f41de69b2 plus accepted predecessor replay compiles on owned original Example80423.
  Shape14/14, layout2/2, record7/7. Full1585=1531Passed/1Failed/53Skipped;2 reloads/resumes.
  The failure is the known cold-start null Utility Shader; original XML remains unchanged. No later stage is unlocked.
- Task-owned preview light and public delegate sentinel prove legacy Inspector adoption has no Undo and prior custom
  Lightmapper delegate is lost. Main candidate adds adoption Undo and an independently owned, idempotent delegate chain,
  retaining external replacements and live owners across out-of-order release. No user light or source asset was saved.
- Installed6000.6 public Extract vs realtime pack finds gray0.5 vs0.214041144 and colored/temperature mismatches.
  Candidate linearizes authored Unity color before CCT/intensity. The old raw-color test assertion is corrected because
  it disagrees with the installed Unity lightmapper contract; an independent public Extract oracle covers both CCT states.
- Original80423 exited normally before main checkout. Main82740 compile0errors; lifecycle11/11, record9/9;
  Inspector shape regression15/15; public radiance probe matches all6 white/gray/color+CCT controls. Unpinned transient Profile was unloaded during test setup,
  causing6 prerequisite failures in each of two early main lifecycle runs; explicit HideAndDontSave fixes the harness
  lifetime and the next run11/11 passes. The original failures remain. Missing resource references are supplemented
  in memory using Unity object validity, not CLR object-null; no durable GlobalSettings upgrade is claimed.
- All-assembly authentication tests temporarily left CLI bearer discovery stale; connection later recovered before
  any import/log delta. A reversible own-source pulse was restored exactly and is not credited as the cause.
- S6 evidence and bounded scratch: original Example intermediate/verification-0.5/20261020-run02/S6 (512MiB cap,
  max20 retained320x180 normal frames). Producers: owned Editor, XML runner and explicit probes. Next consumers:
  historical fixed replay, shape/culling/normal-frame/Undo/bake controls and independent S6 acceptance. S5 carry consumed
  into complete S6 replay and its directory removed. Root preimage/manifest remains only for final source audit.
- Historical fixed replay84585: full1593=1540Passed/0Failed/53Skipped, callbacks complete,2 reloads/resumes.
  Stage shape15/15, record9/9, layout2/2, lifecycle11/11. A canceled pre-discovery start is explicitly not a pass.
  Native SaveModifiedScenes modal was canceled by the user; the stale zero-discovery adapter was retired without
  saving/discarding a scene. Candidate test runner now rejects dirty scenes before Execute, and the adoption test
  owns an additive scene so its objects/Undo do not touch the framework bootstrap.
- Fifteen320x180 normal Game frames each capture Lighting/Depth/DisplayColor: completed,1 captured frame,
  zero outstanding, all raw buffers retired/no NaN/Inf. Production records are matched to metadata producerFrame;
  no Camera.Render or extra diagnostic Cull is used for these captures. Receiver Depth reconstructs z=3 exactly
  in the interior. Rect back is off-equivalent; fade16/18/20m packs radiance30/15/no record and culled count0/0/1.
  Directional remains present beyond its local-distance threshold. These are behavior observations, not S6 acceptance.
- Disk is a blocking original S6 failure: both normal Disk frames have zero records. Separate public API controls
  prove native Disc assignment changes bake mode; restoring Realtime on a bare Disc stays Realtime for6 frames,
  yet public native culling excludes it. forceVisible and bounding-sphere override do not restore production records.
  Do not substitute Rectangle/native identity or render Baked lights as realtime. Candidate preserves the caller's
  bake mode around shape edits; explicit Disk candidate collection and downstream identity closure remain in progress.
- Fixture scene/resources actually unloaded; Play stopped; temporary full GS JSON restored byte-identically, GS clean;
  owned84585 exited normally before main checkout. Historical source replay was removed only after every file hash
  matched its fixed-replay identity. The saved main fix was reapplied. A transient candidate edit missed bakeType's
  declaration during startup (3 CS0103 errors); declaration fixed, static0/0. Failed startup94284 lacked a CLI close
  channel and was terminated by official unity close for this exact project; retry94686 uses original Library.
- Further actual controls reject native-Disc mode preservation alone: both RecordObjects and CompleteObjectUndo
  change Realtime/Mixed to Baked on Redo. Current main shape18=16Passed/2Failed/0Skipped, failures retained.
  Adoption test now uses DontSave objects moved to owned PreviewScene and passes with original dirty state preserved.
  CompleteObjectUndo production experiment was reverted because it did not fix the defect.
- Independent review accepts explicit authored Disk->native Rectangle as the next candidate after these failures;
  automatic approval review rejects that API/native/gizmo semantic adjustment pending explicit human authorization.
  The asynchronous authorization question is pending; carrier code was not applied. NativeType(Disk) remains Disc.
  Bare native Disc is not silently normalized; no Disk-only scan or native identity substitution was added.
- BakeHarness exists only as task scratch/dry-run compilation; Setup and Bake never executed and no bake asset folder
  exists. Review still requires full restoration, cancellation/readback exception safety and cumulative map budget.
  Current owned94686 exited normally after idle/clean guards and byte-identical GS restoration (dirty=false).
  Fifteen protected source inputs remain unchanged. No S6 commit/push; PR11 Draft; S7-S17 remain locked.
- Current retained S6 evidence/scratch116285676 logical bytes (~111MiB),958 files, under512MiB budget; exact next
  consumer is approved Disk fix, historical replay and independent S6 frame/bake acceptance. Completed discovery
  scripts/sidecars (3821bytes) deleted; no database/cache copies or migration. See S6/continuation-checkpoint.json.
- 2026-10-21 user explicitly authorizes authored Disk native Rectangle carrier, retained Disk/Disc baking and full
  actual verification before commit/push. This supersedes the pending approval blocker above. Main candidate now
  uses one canonical NativeType(Disk)=Rectangle mapping with equal-area native dimensions. Explicit mode-only
  edits establish the carrier before applying the selected Light-owned mode; loading/inspector adoption does not.
  Additional data remains shape authority; the real lightmapper delegate still must emit Disc/radius/mode. No Disk
  candidate scan, surrogate Light object, global Undo hook or second bake-mode storage is introduced.
- 2026-10-21 S6 carrier historical replay (834f41de69b2 + accepted predecessors + main candidate patch
  716db7a5) compiles; shape23/23, record9/9, layout2/2 and lifecycle11/11 pass. Full Example1601 discovered,
  started and finished =1548Passed/0Failed/53Skipped, callbacks complete,2 reloads/resumes.
- Disk Realtime/Mixed/Baked save/reopen checks retain Disk Additional data, Rectangle native carrier, native mode,
  stable scene/object GUID/local IDs and byte-identical second/no-op saves. Normal Point/off/Disk-front/Disk-back
  frames use an isolated free Layer30 because the original task anchor owns Layer31. All12 raw buffers retire,
  are finite and match actual producer frames; Disk front/back both produce native visible record0, flags516.
  Independently reconstructed common receiver interior38532pixels has Disk-front Lighting RGB mean increment
  0.03155720 over off; Disk-back mean difference -0.0000009904/max0.0000152588. Full-image edge differences remain
  reported (max0.00747681); jitter differs, so there is no bitwise-equivalence claim.
- Actual GPU bakes and five normal Game frame captures cover Disk, sun0/2 degrees and Spot Baked/Mixed. Each accepted
  normal frame has at least33 distinct camera frames before its producer, finite retired buffers and restored Play
  settings/native light JSON. The first frame1 capture is retained as an invalid progression control. Initial direct
  lightmap readbacks failed with streamed mip2 residency; only task-generated map importers were subsequently made
  non-streaming for native256x256 half-float analysis. This does not fix or validate production texture streaming.
- Independent sun source-angle control accepts all9 sampled shadow edges widening:10-90percent width median
  0.0802809967m at0degrees ->0.1483922484m at2degrees, identical geometry/camera/atlas ST. Disk baked data is consumed
  in normal BakedDiffuse/Lighting/Display, with isBaked=true and no duplicate realtime light record. No absolute
  radiometric calibration is inferred from those captures.
- S6 remains locked on a newly measured Cone attenuation discrepancy. The original production SpotWindow GPU probe
  fails4 transition points against CoreRP smooth attenuation (20degrees0.6201871 vs0.384545326). The baked radial data
  independently fits the squared window; the initial total-Lighting Spot comparison also includes GGX and cannot be
  used as a diffuse-only acceptance test. Current main candidate squares only the Cone window and adds actual GPU
  packed-record/transform/Point/Pyramid/Box regressions. Reverification and isolated diffuse normal frames are pending.
- The current complete-main full suite has14 later-stage failures (S12 defaults/stripper fixtures,S13 layout parser,
  S15 material numeric/viewer/context lifecycle fixtures,S17 moved-path test), separately classified from S6 tests.
  They remain blockers for final HEAD and must be fixed at their corresponding replay stages; main full is not green.
- Owned Editor9252 exited normally after capture/fixture drain, clean FilmParity restoration and exact original GS JSON
  restoration. All70 replay source hashes matched before targeted removal; main98012a1 and its frozen12-file candidate
  were restored. Both bake asset folders were deleted after restore; original FilmParity/meta hashes remain unchanged.
  Four closed diagnostic logs were losslessly compressed from~92MiB to~1.2MiB, with original hashes retained. No data
  store/cache copies or migration. No S6 commit/push yet; independent final acceptance and S7-S17 remain locked.

- Main angular candidate actual Metal GPU tests:3 discovered/started/finished,3Passed/0Failed/0Skipped, callbacks complete.
  Initial new-test Component namespace ambiguity caused2CS0118; explicitly qualified UnityEngine.Component. A CLI
  up-to-date response did not prove the refreshed test assembly was loaded (TypeLoad failure); public targeted import
  plus CompilationPipeline.RequestScriptCompilation then loaded and executed the new tests. Neither failed start counts
  as a test pass. DXC67/67 across the oracle, Deferred, Fog, Cloud and InfinityLit; static0/0. Owned18151 exited normally
  with original GS JSON equal/dirtyfalse and no active tests/Play before historical S6 replay.
- Cleanup of consumed task-only AppleDouble metadata reduced measured file allocation to~285MiB (real evidence files
  unchanged by size/mtime checks). The first cleanup journal write was interrupted by ExFAT removing ._Packages while
  its real compiler directory was removed; exact removed-sidecar list was not retained. Compiler include cleanup now
  uses explicit symlink/metadata removal and incremental receipts. No symlink targets, DataStore or user assets removed.

- Historical squared-Cone candidate (original834f41de69b2 +75-file replay, main patch SHA005764ac63d3018a)
  actually passes angular GPU3/3, Shape23/23, Record9/9, Layout2/2 and Lifecycle11/11. Full1604 discovered,
  started and finished =1551Passed/0Failed/53Skipped; stable-ID callbacks/checkpoint complete,2 reload/resumes.
  Native XML contains1571 leaves;33 earlier skipped callbacks are retained in checkpoint. XML overall is
  Skipped:Ignored, not an all-tests-Passed or native-frameworkComplete claim. Independent verifier checked all IDs.
- Two specular-isolated Spot Baked/Mixed captures with caster have identical material JSON and Specular0,
  roughness1/metallic0 GBuffer. Unoccluded ROI8434 correlated world samples: Mixed/Baked median1.002581,
  absolute relative error P95=6.36075percent. Caster shadows visibly differ; root cause is not yet proved.
- NoCaster control omits only the task caster/black material, keeping light/camera/receiver/bake settings.
  Both actual bakes and14 raw normal-frame buffers complete/finite/retired,33 advancing frames before producer34.
  Independent verifier accepts inner plateau and outer support: expected radii0.705308/2.309401m;
  normalized inner r<0.6m is Baked0.999802..1.000709 /Mixed0.999633..1.000643. Squared-window error over
  0.8<r<2.1m max0.00115808/0.00055021; full sampled radii including inner corner max0.01001784/0.00481878.
  Geometry-normalized radial values prove shape only. Unnormalized36372 correlated world samples have
  Mixed/Baked median1.00246870, P05/P950.93791721/1.07204548, absolute relative error P95=8.201891percent.
  Residual is under investigation; no exact radiometric equivalence or post-hoc tolerance is claimed.
- Both Spot task asset folders were restored/deleted with FilmParity/meta unchanged, no pending readback/bake.
  Owned20790 exited normally with original GS JSON equal/dirtyfalse, clean scene and inactive tests/Play.
  All75 replay file hashes matched before targeted restoration; local main98012a1 and17-file candidate match
  frozen main patch. Fresh static_check:0errors/0warnings. No other session Editor was operated.
- Native Inspector interaction evidence remains unavailable: Computer Use app-name/bundle/exact-install-path
  binding selects another project's window; no input sent there. Official current6.6 CLI exposes no Inspector
  window capture/input command. S6 is not accepted/committed/pushed; S7-S17 stay locked. GPU performance
  has no S6 valid comparable before/after sample set and remains Open.

- Independent fixed-scale Lambert analysis closes S6 Spot intensity scale without relaxing the original8.201891percent
  spatial-error result: realtime actual/theory P05/median/P95=0.998554/0.999309/0.999931 (absrelP95=0.14465percent),
  native baked atlas integral/theory=0.998071; normal Mixed/Baked integrated ratio=1.00197687 at radius2.65m.
  Native atlas centroid(71.50999,71.52485) versus original ST world-origin index(72,72) corresponds to world offset
  (-0.020417,-0.019798)m, close to independently computed half-texel0.020833m. Actual BakedDiffuse agrees with native
  map sampled using unmodified ST (absrelP95~0.052percent). Diagnostic centroid-only shift gives baked/theory
  absrelP95~0.3181percent, with no intensity fit. Production UV was not changed; upstream registration root cause and
  with-caster shadow difference remain unproved and retained for S12. Source uses Lambert, not commented-out Burley.
- Native UI retry: local main own25925 opened; only task unsaved additive scene/light created, source FilmParity hash
  unchanged/clean. Exact install-path CUA still returns another project's window, no input sent there. One consolidated
  actual-UI evidence request is pending; no source asset save or S6 acceptance inferred from elapsed time.

- User rejects another manual UI handoff and directs reopening the original Example directly into their scene.
  Previous own25925 was already normally exited (log UDS shutdown, PID absent). Official CLI-openfile launches
  own32233 directly into Assets/Scene/Spazon/Scene_Spazon.unity; no fixture loaded. CUA now binds correct Example.
- Real Spazon startup exposes packaged Profile missing ProbeVolumesOptions. Explicit Append menu adds it and targeted
  save passes unchanged14 current component JSON/local IDs and second-save byte identity. Binary native audit is more
  precise:15->16objects, new APV3810187332190063959, old component IDs/order preserved;34 semantic differences include
  current non-overriding S4/S5/S10/S14 defaults,14 retired SSR/SSGI parameters (all overridefalse), and component count.
  All other native fields and normalized reference identities unchanged. This is a separate later-stage main asset
  candidate, excluded from Fix S6; original Profile/meta preimage retained for early replay. No APV bake acceptance.
- Current Example GS lacks eight newer ResourcePath fields in native source. Explicit assignment/save populates them
  while retiring S7 zBinningShader. Current-JSON eight-reference audit alone was insufficient; read-only v23 mhtt native
  type-tree parsing proves exactly9 native differences and all other fields/managed IDs/types/PPtr identities unchanged.
  Old zBinning and new lightTileMask share GUID0a57574d950fac64c9465adeb0908015/pathID7200000; source replacement is
  e6155f32fbb6, no current consumer of old field. Independent verifier reproduced and accepts this bounded migration.
  GS meta byte-identical, original cabd1631 preimage retained; current GSf22a4f11 remains outside package fix commits.
- Spazon actual Game now renders. One disabled DontSave task light in the same scene (no test scene) confirms real
  Source Radius .025->.85, keyboard Undo .025 and Redo .85, with full native/Additional JSON receipts independently
  accepted. Type popup lists all5 shapes but AX/coordinate/key/continuous mouse/drag input does not produce selection;
  no product defect inferred or Inspector workaround added. Type/SpotShape/Mode popup/gizmo gate remains Open.
- Own task light removed, original9 roots/source scene+meta SHA checked, same Spazon reopened without saving to restore
  only known task dirty state. Independent accepts single-scene clean=true/owned-light-absent; final-state confirms
  no Play/compile/bake and25 assigned shader slots. Fresh log observation after retained failure history:562bytes,
  zero Exceptions/Shader errors/C#errors. Restored actual Game/empty Console screenshot retained. No other Editor operated.
  Current main source plus separate later asset candidate remain uncommitted; no S6 push or stage unlock inferred.

- S6 native Inspector menu blocker closed: moving the owned Example Inspector to the center made native AX menu items actionable. Actual Type choices cover all five types; native records match, Pyramid/Box apply, Disk .025/.85 radius and Realtime/Mixed/Baked retain Rectangle carrier. Full JSON equality verifies single-step Type/radius/mode Undo/Redo. Actual native window shows circular Infinity Disk handle. Legacy Disc setup-only follow-up is not counted as an additional native-menu pass. This round task light was removed, same original Spazon reopened clean after root/hash checks; scene/meta unchanged. No other Editor was controlled. Final candidate review pending; later Profile/GS candidates excluded.
- Independent verifier accepts final S6 batch (manifest ac6307cf0a410e57d5d2c7fd820959ed755904a807d49942d5a37e6db42d5f10): all17 files match reviewed identity, production/test hashes unchanged; static0/0 and diff clean. Historical full remains1551 Passed/0 Failed/53 Skipped, not1604 Passed. This closes S6 repair/historical validation only; later main failures, performance and final HEAD remain open. Scoped Fix S06 commit/push and remote identity precede S7 replay.

- S6 closure committed/pushed as5b34e14 to origin/claude/infinityrp-0.5; remote main remains unchanged.
- S7 originale6155f32 + accepted predecessors reproduced invalid NUnit category and missing local Deferred light.
  Caller-owned out-state restores actual Metal loop indices; CPU tile projection now matches sampled-depth consumers.
  Historical final full1617 =1564Passed/0Failed/53Skipped; callbacks/checkpoint complete, native XML1584=1564/0/20,
 33 earlier Skipped leaves independently reconciled. FrameworkComplete=false remains explicit.
- Ten no-diagnostic normal cases cover above/below, Deferred/Forward, inside/behind/offscreen and far views.
  Multi-camera local-list0->1/1->0 transitions independently accepted: positive-red>.001 full-image energy
  relative difference1.745163e-6, known-jitter-corrected centroid residual<.00017pixel; empty inner ROImax3.814697e-6,
  full imagemax.003479004 remains separately reported.
- S6/S7 matched fog8phases accepts Lightingmax3.05176e-5/SceneColormax1.22070e-4. Cloud16 actual global phases
  with exact jittered matrices/reset, source profile and CPU light records have byte-identical full RGBA Cloud
  and SceneColor. Earlier saturated/unmatched-phase controls are not accepted.
- Public-menu/CUA native Frame Debugger primary screenshot8ad2c3aa91f8515956cd6ba8fe5db69f4f6c36bbb4ef04d37e0684f19a50fe6b
  shows original Spazon MainCamera153events and one actual Compute_LightTileMask leaf. Payload panel is blank;
  no kernel/group counts claimed. Failed internal-reflection exporter is historical diagnostic only and will not be retried.
- Original Spazon remained unsaved; task objects restored, owned Editor exited. Main S7 candidate integration pending.
  Static0errors/0warnings; full offline DXC312targets/2 unchanged GTAO dynamic-unroll failures, not an all-shader pass.
  Later APV Profile candidate remains separate; S8-S17 not unlocked until S7 final batch accepted and pushed.
- Main S7 integration: actual Unity compilation0errors/0warnings; focused25/25passed. Full1689=1622Passed/14Failed/53Skipped,
  same14failure and53skip identities as S6 main-carrier-full;13newcasesallpassed(8DeferredGPU+2projection+3priorAngularGPU).
  Raw1656 plus33real skipped checkpoints reconcile;callbacksComplete=true/frameworkComplete=false,rootFailed(Child).
  Main ten normal cases accepted by numeric gate and direct contact-sheet inspection; no diagnostic readers.
  Clustered-list FeatureStatus and only14actuallyexecuted test cases updated for Unity6000.6.0f1/macOSMetal.
  Full main is not green; later-stage failure ownership and final HEAD acceptance remain Open.
- Main timing availability control:60warmup+120attempts gives0valid GPU samples (GPUtime/count0); CPU counts2
  are not a comparable single-camera timing set. Perf remains Open, no median/P95 or improvement claim. The initial
  unpumped task probe hit CoreRP ProfilerRecorder.GetSample(0) with no samples; stopped/restored, retained as failed
  instrumentation control, not a renderer failure or accepted performance run.
- Final main restoration: original Spazon9roots/source clean, noS7objects, inactive tests/Play/compile/timing recording,
  completed capture/outstanding0. Independent final20-file S7 review precedes commit; later Profile excluded.
