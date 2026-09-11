# InfinityRP 0.4.0 local handoff

For a new local session on the Mac (Unity 6000.6 + InfinityExample). Do not treat this wave as verifier-passed.

## Snapshot

| Item | Value |
|---|---|
| Package repo | `com.infinity.render-pipeline` |
| Branch | `normalize/v0.4.0` |
| Last **committed** HEAD | `7af53a9` (0.3.0 rendering work) |
| 0.4.0 work | **uncommitted working tree on the cloud agent** — commit + push before a local clone can see it |
| Package target | `0.4.0` / Unity `6000.6` / CoreRP·SG·VFX `17.6` |
| Ledger | [PLAN.md](../PLAN.md) U00–U82 |
| Plan file (do not edit) | user-attached InfinityRP Normalization Plan |
| Research | [Docs/History/CoreRP-17.6-API-Research.md](History/CoreRP-17.6-API-Research.md) |
| API follow-up | IsActive via `IInfinityVolumeActivity`; UI overlay via `CreateUIOverlayRendererList`; GlobalSettings bind via `GetSettingsForRenderPipeline` + `SetRenderPipelineGlobalSettingsAsset` |

No Mac worker was connected. This cloud VM has no Unity and no InfinityExample. Nothing has been compiled, EditMode-run, or frame-captured.

## What the new session is

Local **compile → fix first error → U02 baseline → Grok 4.6 batch verify**. Not more architecture. Protected frame stays as-is unless a task declares a visual delta.

Roles (already approved): main agent = plan / generate / inspect UI; Grok 4.6 = search / review / batch verifier. If Grok is missing, search-only may fall back to GPT-5.6 Luna; **verification must not silently downgrade**.

## First 30 minutes on the Mac

1. Confirm one live Editor on InfinityExample. Read `<InfinityExample>/Library/EditorInstance.json`. No second Editor. No `-batchmode` against that project.
2. Get this working tree onto `InfinityExample/Packages/com.infinity.render-pipeline` (`normalize/v0.4.0` after commit/push, or copy the dirty tree).
3. `unity command recompile`. Diagnose only the **new** `Logs/Editor.log` window.
4. Fix the first real owner / order / lifetime / API-signature defect. Forbidden: `if (shader == null) return` in execute, dummy textures, disabling a designed path, `count = 0` dispatches.
5. Run CLI `infinity_normalization_baseline` (U02 harness). Then existing capture / EditMode XML. Intermediates: `<InfinityExample>/intermediate/<task>/<run>/`. Durable: `/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/normalization-<date>/`.
6. Grok 4.6 verifier per PLAN §6. FAIL stays on this branch. No ready PR until PASS.

Likely first compile hits (17.6 signatures still unverified):

- `EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<InfinityRenderPipeline>`
- `GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>`
- `ScriptableRenderContext.CreateUIOverlayRendererList`
- `IRenderPipelineResources` / `[ResourcePath]` / `isAvailableInPlayerBuild`
- `DebugUI.IntField` / `DebugUI.Value` getter types
- `VolumeProfile.TryGet(Type, out VolumeComponent)`
- `CameraMovement` namespace change (`InfinityTech.Component.Utility`) breaks Example scenes until scripts reconnect

## Code that is in the tree

**B0** Ledger rewrite; N00–N15 archived to `Docs/History/PLAN-2026-09-N00-N15.md`; AGENTS/DESIGN/runbook/README updated.

**B1** `InfinityRenderPipelineGlobalSettings` + RuntimeShaders/Textures/Materials + default Volume settings. RP Asset no longer holds 22 computes / `debugView` / default Volume. `renderScale` `[0.5,1]`, Game-only SR. Dispose restores `SupportedRenderingFeatures` / `Shader.globalRenderPipeline` / GraphicsSettings flags. Build preprocessor + shader/compute strippers.

**B2** `InfinityAdditionalCameraData` / `InfinityAdditionalLightData` **reuse old script GUIDs** (`e2b461597c1f2764ea25d546fd978935`, `c8dcc11eda3e9404e884a2332f3a4890`). Old `CameraComponent` / `LightComponent` deleted. SceneView Volume: unique active Game additional data, else `~0` + SceneView transform. Preview mask `0`. WorldView list removed. Stubs/wizards/`RenderGraphWindow`/Probe/Foliage deleted.

**B3** All Volumes `[SupportedOnRenderPipeline]`. Optional features `enable` + `IInfinityVolumeActivity.IsActive()` (not `overrideState`). Film `mode` None/Film. SSS Volume = `numSamples` only. `VolumeHasOverrides` / `EnsureAsset` deleted. Dedicated Volume editors. Debugger panels (Rendering / Lighting / Mesh / Temporal). DebugView reads runtime settings only.

**B4** LitGUI blocks. Shader rename `_NomralTexture` → `_NormalTexture`, `_PixelDepthOffsetVaule` → `_PixelDepthOffset` + `Window/Infinity/Migrate/Lit Material Property Names`. `InfinityUnlit` + T2 `SRPDefaultUnlit` / untagged. DiffusionProfile index / missing-slot warnings.

**B5** `rendersUIOverlay`. Raster UIOverlay after OutputTransform on `DisplayColorBuffer`. `Window/Infinity/Create UI Fixture` → `Assets/Scene/Validation/Validation_UI.unity` (uGUI; add TMP on the Mac fixture if needed).

**B6** Custom `NativeViewMotionHistory` **kept**. `preferNativeMotionVectors` is A/B only. Two-pass TAA **kept**. `fuseTemporalSharpen` reserved, default false. Decision: [Docs/History/U60-U61-Experiment.md](History/U60-U61-Experiment.md).

**B7** `InfinityRayTracingEnvironment` reports UnifiedRayTracing backend. RTAO compute `RTAOTrace` writes `OcclusionBuffer` when Volume active **and** `enableRayTrace`. SSAO is the other AO owner. Old `.raytrace` deleted. RTAO Volume GUID reused (`3860ad677b4fe544fa9e0b844ea99d10`).

**B8** Menus under `Window/Infinity`. Completed one-off migration MenuItems stripped (classes remain). `package.json` 0.4.0. `CHANGELOG.md`. `Documentation~/`.

## Contracts the new session must not break

- Entity data on the entity; MeshScene / RG / ZBin stay Infinity-owned. Not an HDRP port.
- Default Volume = GlobalSettings. Quality Volume = optional RP Asset profile. `VolumeManager.Initialize(default, quality)`.
- Feature gate = `IInfinityVolumeActivity.IsActive()` / `GraphicsUtility.VolumeComponentActive`. `overrideState` is only a scene-override marker.
- Resources only on GlobalSettings `[ResourcePath]`. Missing required resource = record-time throw.
- No `FormerlySerializedAs`, no dual authorities, no silent fallbacks.
- Camera/Light additional-data GUIDs must stay so Example scenes reconnect.
- Protected frame: do not retune exposure / tonemap / lights to “prove” architecture.
- Example asset writes: backup, exact allowed delta, second-save identity, separate no-op.
- Intermediate cleanup is mandatory. Official `screenshot` is not normal-frame evidence.

## Still Mac-only

| ID | Why |
|---|---|
| U02 | Spazon + Validation_* frames, LUT hash, 264 EditMode XML, Editor.log mark |
| U11 | Example RP asset → GlobalSettings receipts |
| U12 | Quality switch + domain reload play |
| U13 | Player variant/size before/after |
| U20–U21 | Inspector screenshots; no `GetCommandBuffers` warning |
| U40 | Material byte-checked rename on Example materials |
| U50 | Game / SceneView / Player UI captures (SDR + HDR) |
| U60–U61 | Fixture A/B + GPU time; keep current decision unless data wins |
| U70 | Metal compute RTAO frame; D3D12 `UNVERIFIED (external)` |
| U82 | Final vs U02; then ready PR (not merge) |

## Suggested first prompt for the new local session

> Continue InfinityRP 0.4.0 on `normalize/v0.4.0`. Read `Docs/Handoff-v0.4.0.md` and `PLAN.md`. Do not edit the attached plan file. Confirm the live InfinityExample Editor via `Library/EditorInstance.json`. Recompile. Fix the first real compile/API defect. Then run U02 baseline + EditMode XML. Do not launch a second Editor or `-batchmode`. Mark Mac-only results `TODO(UNVERIFIED)` until a captured frame is inspected.

## Do not do in the local session

- Merge to `main`
- Atmosphere / light-unit retune
- Replace custom motion or fuse TAA without fixture data
- Restore `.raytrace`, `CameraComponent`, `LightComponent`, `VolumeHasOverrides`, or `EnsureAsset`
- Paper over with dummy textures or disabled passes
