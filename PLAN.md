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
| S5 | Stochastic SSR (classify, Hi-Z/linear trace, ray-reuse resolve, temporal, spatial) + shared denoise library | S3, S4 | in progress |
| S6 | Light data model v2 (UE-form shape data, implicit Unity Light sync, lightmapper delegate, record v2) | S2 | pending |
| S7 | Clustered light list v2 (CPU z-bins + GPU tile bitmasks) and one shared light loop | S6, S3 | pending |
| S8 | Area shapes: sphere, tube, line, rect, disk, sun disk (Karis representative point + Frostbite diffuse) | S7 | pending |
| S9 | Shadows: settings on the RP Asset, Gather-PCF, PCSS, VSM/EVSM moment atlas, cached shadows, area-light shadows, Shadowmask object filtering | S6, S7 | pending |
| S10 | Screen-space traced shadows (sun + up to 32 local lights) | S7 | pending |
| S11 | Cookie and IES atlas | S6, S7 | pending |
| S12 | Baked GI: lightmaps, Shadowmask, Distance Shadowmask, probes, LPPV, APV, baking sky | S2, S6 | pending |
| S13 | Reflection probes in the clustered list; shared cube filter | S2, S7, S12 | pending |
| S14 | SSGI on the SSR trace/resolve/denoise core | S5 | pending |
| S15 | Editor parity: Shader Graph target, Wizard, Frame Settings, Light Explorer, inspectors and handles, Rendering Debugger panels, RenderGraph Viewer, material converter, menus | all | pending |
| S16 | Tests, user documentation, FeatureStatus, verification playbook | all | pending |
| S17 | Folder layout (`git mv` only) | all | pending |

## Interface contracts

Each stage fills in its section before its code lands.

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
