# InfinityRP Full Rendering — Delivery Report

> Active execution: PLAN.md N00–N15. N00–N04 have independent scoped Terra PASS. N05 material/default/LUT and Editor post-effect child gates have passed; N05 Player and parent acceptance remain open. N06–N15 remain pending. Full rendering delivery is not accepted.

> Current acceptance is governed by `PLAN.md` (approved 2026-09-06) and the checkpoint below. The later S0–S9/R0–R6 rows are historical implementation claims, historical T/S/R records, not acceptance of N00–N15. Do not inherit their former gray-card or SkyView thresholds; use the separated output and atmosphere contracts in `PLAN.md`.

## Current checkpoint — 2026-09-07

The complete rendering delivery remains unaccepted. N01 migrated Infinity identities and verified all 21 live script/importer bindings. N02 established an actual macOS Player baseline; N03 independently accepted shared Editor/Player normal-frame GPU capture, terminal staging retirement, native Metal Present metadata and Editor frame-tree export. The N03 Player artifact is `player-build-20260906T1654560667240Z-61547db42ede464686aecf6b245ca5a2/macOS-ARM64-IL2CPP-N03.zip` under the durable validation root. Its real windows still show the non-intended emissive figure, noise and shadow artifacts, and its log retains 12 Metal shader entry-point failures assigned to N06/N11.

N04 passed integrated Editor and actual Player fault/recovery verification. Original exceptions, native light uploads, graph consumer dependencies, per-camera histories, shared cache publication and Submit/retirement behavior have independent receipts under `N04-20260906T170440Z`.

N05 corrected only Anim_MAT emission RGB8→0 with native copy/source/reload/second-save/no-op proofs; the figure is textured and normally shaded in actual Editor frames. Required color/exposure consumers now use resolved Volume values; the RP default is registered once. Production LUT/output GPU gates passed in RGBA16F/RGBA32F and five output policies, including boundary dimensions. FilmGrain now uses zero-mean signal-relative PCG modulation, and Bloom.scatter controls upsample mixing; Bloom off allocates no chain and uses a combine kernel without a Bloom binding. The latest numeric XML reports190/190. Actual post-effect Editor sessions cover six phases plus automatic completion, cancellation and timeout cleanup. The first additive-grain visual failure and later manual-wait timeout remain unchanged in evidence. The fresh N05 ARM64 Player export/native build/signature and six-phase runtime capture are complete; independent Player acceptance is pending in `player-build-20260907T1601535878500Z-2759c82ff89d47f1bce350b0a94c26fd/N05-player-candidate.json`. Main inspected its actual running/restored windows and raw-derived post-effect comparison. Original upstream normal/AO/SSR/SSGI variance and the static0.5% temporal gate remain unresolved; these passes do not certify whole-image quality.

Current N05 receipts live under `N05-20260906T193300Z`: `N05a-terra-verdict-01.json`, `N05b1-terra-verdict.json`, `N05b2-terra-verdict.json`, `N05c-editor-terra-verdict.json`. `N05c-editor-terra-supplemental-verdict.json` independently matched 10 source and 354 evidence hashes, parsed 190/190 passing tests, reran raw statistics, and confirmed Bloom graph presence only in enabled phases.

| Gate | Actual accepted result | Scope still open |
|---|---|---|
| T01 | Baseline, existing-work preservation and loading-time asset safety passed independent verification. | Later authorized deltas are tracked separately; the original bytes are not restoration targets. |
| T05a | 13 production SSR/SSGI/GTAO numerical/bounds kernels at three sizes; Terra checked 66 raw float buffers. | Full reprojection, temporal/synchronization and image quality gates in N09 remain pending. |
| T06a / T06a.1 | Actual 120-frame Tint A/B/A, source Tint 1→0 migration, injected-save recovery, separate no-op and a fresh persistent-source observation/cleanup passed. Root saw the broad pink stone cast disappear. | Grain, figure emission/clipping, full exposure/LUT/output and temporal quality remain unresolved. The first observer timeout is retained as a negative-case receipt. |
| T02a | Exact deletion of the proven empty StaticMeshAsset and its meta, including backups and live import, passed. | Does not accept full identity migration. |
| T02b | Spazon missing animation registration and ten Landscape missing PhysicsMaterial references repaired; native/effective/reference preservation, persisted reopen and separate no-ops passed. | General animation/physics/rendering correctness is not implied. |
| T02c | Atmosphere source schema persisted the already effective profile/FromProfile bit values, current RP reference, metadata and scene state; source second-save and separate no-op passed. | This was configuration preservation, not atmosphere quality tuning or N08 acceptance. |
| T02 Validation schema subgroup | Decal/LocalLights/Translucent removed only their exact certified legacy LightComponent fields; complete non-target native/UnityLight/reference preservation, second-save and separate no-op passed. | Actual image quality is FAIL below. |
| T02d | Exact retirement of 99 invalid VFX integration files/metas passed backup/deletion and current-Editor verification. | Custom GraphSRP output remains explicitly unsupported; standard ParticleSystem is still an N11 requirement. |
| N01.a BoxMatrix schema subgroup | Approved GI values and canonical source schema saved; second-save and independent no-op passed Terra. | Does not pass assembly migration, rendering or Player acceptance. |

The package now declares **0.3.0**, Unity `6000.5.3f1` and CoreRP/ShaderGraph/VFX `17.5.0`, with Infinity assembly identities. The existing Editor actually generated/reloaded Runtime, Editor and Tests DLLs after the six-file apply in `identity-apply-20260906T135910229917Z`. Seven binary assets received 22 explicit serialized class-identifier updates through Unity. Source and independent no-op receipts are `identity-native-apply-20260906T1412588224000Z-70d74450c2384ac0a5123c882e9e3483` and `identity-native-apply-20260906T1414242971790Z-d0e941a96fec435aa63952ced7598305`. An 88-native-file byte scan found no old assembly strings. N01 final live all-script type verification and independent Terra acceptance passed (`N01-20260906T134157Z/N01-final-verdict.json`). DLL creation alone is not a test-suite or Player PASS.

Ordinary ForceReserialize did not update class identifiers: that failed source attempt remains recorded. Explicit SerializedObject updates were then validated on a scene and Profile copy before source recovery. Preview scenes cannot be saved, so their failed copy attempt remains recorded; successful updates use temporary additive scenes and restore the previously loaded/active scene state. The source recovery accepts only exact original-native-equivalent or exactly migrated data, retains fresh bytes, and does not overwrite the failed receipt.

### Accepted source changes and evidence

Persistent T02 evidence lives in `/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation`; earlier `/private/tmp` and Unity temporary-path receipts are still retained and must be preserved durably before final delivery.

- T01 baseline: `/private/tmp/InfinityRP-validation-20260905T160919Z-T01`; independent `terra_t01_verify_r2` and `terra_t01_review` accepted the original 187-input preservation/loading gate. T02a final verifier: `/private/tmp/terra_t02a_orphan_cleanup_verdict_20260906.md`.
- T05a raw GPU run: `/private/var/folders/n0/3gxz482n50317gkmxlm6s6z00000gn/T/InfinityRP-T05a-GPU-menu-20260905T1955232636400Z`; verifier `/private/tmp/InfinityRP-T05a-20260905T193602Z/terra_t05a_verify_20260905T1953Z/verdict.md`. This proves the bounded numeric contracts only.
- Tint source migration/recovery: `/private/var/folders/n0/3gxz482n50317gkmxlm6s6z00000gn/T/InfinityRP-T06a1-TintMigration-20260906T0744348091340Z`; separate no-op `InfinityRP-T06a1-TintMigration-20260906T0750258275990Z`; positive source observer `InfinityRP-T06a-TintAB-20260906T0820309184810Z` in that same temporary root. [Root-inspected persistent-source frame](/private/tmp/InfinityRP-T06a1-source-neutral-r2-20260906.png). `PostProcessProfile.asset` changed only validated Tint value 1→0, retaining override=true; hash is `d93726bda558bf06a289fa10363ed2f39eb7eaf551e5fd9bb99cce3e5e6f4d82`. Temperature, Film, BlueCorrection, ExpandGamut, exposure, lights/materials and the then-existing figure emission 8 were not adjusted in that scoped Tint test. The new approved N05 target is non-emissive.
- T02b Spazon repair/recovery: `known-scene-repair-20260906T0859179842870Z-c7b5e3fee498408e873a9b9e105ba83b`; no-op `known-scene-repair-20260906T0954066224490Z-ba0e039348f44bbb9ca26f8b8dc21029`. The first post-save dirty refusal is preserved; read-only loaded-object and full live-copy/native comparison protected globals before root reopened. Valid default/registered animation was retained. Root observed it in Play and captured [Spazon](/private/tmp/InfinityRP-T02b-Spazon-after-animation-repair.png). Final scene hash is `0cd8a3407a46022a1ca20b99970112110495bff5d3c4c87072e7852731683f96`.
- T02b Landscape repair: `known-scene-repair-20260906T1034386128850Z-ae1731bf84844ae88be1e50314145fa1`; no-op `known-scene-repair-20260906T1036013696710Z-8f197e1071af4e92bb7ac050ffde5203`. Ten exact missing PhysicsMaterial PPtrs became explicit null, preserving current default physics behavior. Save records dirty=true before and false afterward; both invocations passed 129 distinct Hash128 checks. Final scene hash is `b679a48f5baf5d34dfeb2caece4ffa1a7a8d8a8aba7e867598efd3cb9648977e`.
- T02c Atmosphere diagnosis: `atmosphere-effective-20260906T1055571538840Z-788d2d64ab0f4b88bb76e4a0073c9cb2`; source migration `atmosphere-schema-20260906T1106155259080Z-46d6d63b72084c41ae3640ebb67fbba8`; no-op `atmosphere-schema-20260906T1107428715720Z-24adb6c3174d42628d9d0499c9b880cb`. Source hash changed from `01dee56c77bad81614468a723d3e41297d84c0456a6e6bbc1ad49866fe313c2e` to `c39b95596d4f6c385e583c54c37fa28dc4865b4aa35201ef17a0c1dfc67daa04`; meta/GUID stayed unchanged. Thirteen unused old fields were removed, twenty-six current fields materialized, and ten shared native fields remained equal. Effective profile/FromProfile float bits, including LUT/quality and per-meter bindings, were preserved. [Before inspector](/private/tmp/InfinityRP-T02c-Atmosphere-live-inspector.png), [after inspector](/private/tmp/InfinityRP-T02c-Atmosphere-after-source-schema.png). No Reset, guessed old-unit conversion or compatibility mapping was applied.
- Validation schema batch: `scene-schema-Validation-20260906T1138230918360Z-5e347c19aedc48ca850415967d6254f7`; separate no-op `scene-schema-Validation-20260906T1140471446850Z-d5314609ca134d65984716ce101a7350`. Exact per-component removals were 17/28/7 fields. Decal includes unconsumed LightComponent lighting-value copies; Unity Light is the actual owner and its full native fields remain equal. Final scene hashes are Decal `e1cc420e904f46f64f281852368e632b66f1694d6a0db4ae1a859f1b5e702dd2`, LocalLights `477f8e1c69107cd9cabb032542477a72018574c00aecf2171be0c7230dc201e0`, Translucent `b95d6abd13f46862ddb01706a07ff879c19bb83b47d5d241f03d44451e105b2d`. Source metadata/GUIDs and already-loaded scene state stayed unchanged. The source run contains three certified replacement receipts and `schema-closure-partial.json`.
- T02d exact invalid-VFX deletion: `/private/tmp/InfinityRP-validation-20260905T164500Z-T02/prepared-vfx-retirement/apply-receipt.json`; 99 source/backup hashes checked, all listed paths absent, parent metas retained, pre-apply active log mark `7192762420`. Root relayed independent Terra final acceptance including active-Editor import. The old binder could not provide a usable GraphSRP output, depended on an internal friend API unavailable to Infinity.Editor and had invalid template paths. VFX 17.5 remains resolved and its dependency remains in the approved target manifest. Custom GraphSRP output is unsupported; standard ParticleSystem rendering and valid hardware-RT assets are retained in their agreed scope.

### Remaining schema/composite boundary

`serialization-20260906T111704174Z-0d3ff3eb9a2b43ec856a7dace9536b21` is an immutable `Completed, passed=false` report with six schema failures. Its prior 88 parsed source proofs and ten completed copy gates are reused through exact source/summary/artifact hashes; the later three Validation source replacements close those individual gates. That historical partial closure left three BoxMatrix gates and `identityApplyAllowed=false`; N01.a subsequently closed those gates and the separate current-input composite supported the six-file identity apply. Original failed reports are not rewritten, and known large-source/native work is not repeated wholesale.

All three Box scenes reference LightingSettings object 780319870. Hybrid/SRPBatcher canonicalization changes MinBounces 1→2; MeshPipeline changes GaussDirect/AO 1/2→5/5. Those are referenced-owner values, not discarded duplicate settings. Box sources now match the approved canonical outputs and passed Terra N01.a; the Box entrypoint uses its own frozen manifest, preserving the earlier Validation entrypoint and evidence.

Current input protection must include the prior approved asset changes, the exact 99 VFX deletions, explicit new tooling/docs files and M_BoxC's still-unconfirmed material schema/pass-state change. Do not restore authorized deletions or later asset versions from the old full baseline. Earlier 184/187 or 181/187 unchanged counts belong to earlier checkpoints and are not current totals. M_BoxC's authored color/emission/roughness/metallic/shader stayed unchanged in the read-only comparison `/private/tmp/InfinityRP-MBoxC-semantic-compare-20260906T1850`; its schema/pass-state changes remain protected input, not an implementation success. N06 must reproduce the import/ValidateMaterial persistence cause and prove non-target assets are not saved incidentally.

### Actual visual FAIL baselines — current N07/N08/N11

Root captured and viewed all three frames below. Sky black bands, large triangular dark regions in LocalLights/Translucent and cube blotches remain visible. These are quality failures, not clean-frame acceptance. The native migration evidence proved only the declared legacy-field deletion and preserved current effective Light ownership; it does not attribute the rendering defects to that migration.

| Scene / actual capture | Screenshot bytes | Fresh Editor.log window | Result |
|---|---:|---|---|
| [Validation_Decal](/private/tmp/InfinityRP-T02-Validation-Decal-after-schema.png) | 659,144 | `7192791142`–`7192797416` | Visual FAIL baseline; sky/scene quality still unresolved. |
| [Validation_LocalLights](/private/tmp/InfinityRP-T02-Validation-LocalLights-after-schema.png) | 1,139,093 | `7192797416`–`7192803576` | Visual FAIL; large triangular dark regions and cube blotches. |
| [Validation_Translucent](/private/tmp/InfinityRP-T02-Validation-Translucent-after-schema.png) | 717,586 | `7192803576`–`7192809736` | Visual FAIL; large triangular dark regions/sky defects. |

Terra confirmed no new missing-reference or InfinityRP errors in these windows. `UIR System.Exception&` is a stack signature rather than a thrown exception; Curl 35 is external TLS noise. N07/N08/N11 must reproduce and resolve the images with applicable resource/numeric evidence. Clean logs and accepted schema migrations cannot pass the visual gates.

Earlier macOS Player build/run baselines are accepted in their explicit N02–N04 scopes; the current final rendering Player is not yet accepted. External D3D12/Vulkan/HDR hardware remains unverified. N14 must remove the completed machine-local migration/inspection scaffolding, or retain needed functionality through portable validation entrypoints with durable evidence. Preserve all original negative receipts and precise scope boundaries.

## N02 real execution checkpoint

The existing Editor executed 136 tests, all passed, zero skipped. XML: `editor-tests-20260906T1452022187360Z-947e3396c98348b88cd1468c0cef0581/results.xml` under the persistent validation root. Two earlier failed runs exposed test fixtures incorrectly owning the active VolumeManager; those original XML files remain archived. The corrected fixtures initialize only when necessary and deinitialize only their own initialization. Camera disposal also checks that the owned stack becomes invalid while the pre-existing main stack remains valid. N02.b independent Terra verification passed. A subsequent Volume-registry regression run has 137/137 passing tests (`editor-tests-20260906T1520001834830Z-470fa64118a64c3994389e0b81fbbd89`).

First Player attempt: `player-build-20260906T1452302778410Z-8e196540b8434292b341735984b8c855/build-report.json`, Failed (missing Mac IL2CPP module). The matching official module was installed through Unity Hub; the project backend remains IL2CPP. Unity then exported Xcode successfully and an ARM64 Player was compiled and launched, but the actual window was black. Original exception propagation exposed an empty Player Volume type registry caused by supplying the default Profile as the quality profile instead of the global profile. The correction and exact ten-component registry completion are now undergoing rebuilt Player verification. Metal fragment output-index errors remain unresolved; N02 is not accepted. Failed builds and Player logs remain under their original run IDs.

## Historical report retained below

Date: 2026-09-05
Package: `com.infinity.render-pipeline`
Verification platform this session: **Metal** (macOS, Unity 6000.5.3f1). D3D12 / Vulkan stay `TODO(UNVERIFIED)` until the recipes in §5 are run on those editors.

## 1. Stages (S0–S9 record paths)

| Stage | Status | What closed | Evidence |
|-------|--------|-------------|----------|
| S0 Tooling | completed | Refresh / Capture / Validation menus | `Logs/baseline/s0-*` |
| S1 Camera / Volume / History | completed | Frame state, HistoryCache field Equals | `Logs/baseline/s1-*`, volume dump |
| S2 Material / GBuffer / Decal | completed | Crytek GBuffer + DBuffer record | `Logs/baseline/s2-decal-play.png` |
| S3 Lights / Shadows | completed | Unity Light authority; CSM + local atlas | `Logs/baseline/s3-local-play.png` |
| S4 Pyramids / GTAO | completed | HiZ 4-mip/batch, ColorPyramid 2-mip/batch, GTAO chain | `Logs/baseline/s4-spazon-play.png` |
| S5 Atmosphere / IBL | completed | Profile-only; Shared / View / IBL caches | `Logs/baseline/s5-spazon-play.png` |
| S6 SSR / SSGI | completed | RayMarch → Spatial → Temporal → Bilateral + Composite | `Logs/baseline/s6-*` |
| S7 Volume / Translucent | completed | Phase 7 Fog/Cloud + FogComposite + T0/T1/T2 | `Logs/baseline/s7-translucent-play.png` |
| S8 Exposure / Output | completed | Exposure → Bloom → LUT → Vignette → Grain → OutputTransform | `Logs/baseline/s8-output-play.png` |
| S9 Inspector / Cleanup / Docs | completed | SessionState inspectors, leftover delete, docs | this report |

Image / Frame Debugger / GPU-Trace quality for S4–S8 stays `TODO(UNVERIFIED)` where noted in AGENTS.md.

## 2. Spazon convergence (R0–R6)

| Row | Status | Metal evidence | Open gate |
|-----|--------|----------------|-----------|
| R0 DebugView | completed | 10 views + `Logs/debug/Scene_Spazon-stats.json`. Validation_Output Albedo mean ≈ 0.180 | Marker ROI oversized |
| R1 Default Volume / Output | completed | IdentityLut deleted. `volumeProfile` → `SetCustomDefaultProfiles`. Gizmo before OutputTransform. Format `B8G8R8A8_SRGB` / Linear / HardwareSRGB | Gray-card Game sRGB ≈ 0.31 vs gate [0.44, 0.48]; linear dump 0.078 |
| R2 Atmosphere | completed | Physical `ThrowIfInvalid`. Earth fixture `deltaUvDaylight=0.007`, L0 r/b=0.55 | zenith/horizon 0.15 (gate [0.2,0.8]); sun/sky 3.9 (SkyView has no solar disc) |
| R3 GBuffer | completed | Metal raster→compute 5/5. Albedo DebugView is beige stone + authored banners | Stone ROI \|Co\|≈0.07 vs gate 0.05 (warm albedo) |
| R4 Screen-space denoise | completed | SpatialRadius, miss-fill, TAA-style temporal, AO owner=Deferred IBL, GTAO Volume gate, NumRays=2 | Play inter-frame SSR 0.09 (dump cycles TAA kernel) |
| R5 TAA / SceneView | completed | 3×3 HistoryDepth reject, 8-frame ramp, gap reset + jitter=0, SceneView linger 120. TAAConfidence mean 0.975 | `camera.Render()` Game dumps reset history (FramePairDiff 62% invalid). Scene drag SSIM unverified |
| R6 Docs | completed | AGENTS / DESIGN / PLAN / this report. Luna: zero dual-authority P0 | `TemporalAntiAliasingGenerator` still live jitter+dispatch |

Phase 8 after R5:

```text
TAA(+Confidence when DebugView≠None) → Exposure → Bloom → CombineLUT(stack)
→ Vignette/Grain → DebugView(linear) → Gizmo/WireOverlay(linear)
→ OutputTransform(unique encode, authority chain) → Present
```

## 3. Locked RecordRG order

```text
0  CombineLUT, AtmosphericLUT
1  Depth, DBuffer, GBuffer, Motion
2  HiZ, HalfRes, ZBin
3  CascadeShadow, LocalShadow
5  GTAO (Volume override only), CopyHistoryOcclusion, ContactShadow
6  Deferred, Forward, SSS, AtmosphericSkyAndFog, OpaqueLightingPyramid,
   SSR, SSGI, ScreenSpaceComposite, OpaqueSceneColor
7  TranslucentDepth, VolCloud, VolFog, FogComposite, FoggedSceneColor,
   T0, ColorPyramid, T1, T2
8  TAA or SuperResolution, Post, DebugView, Gizmo/WireOverlay,
   OutputTransform, DisplayColorBuffer, Present
```

## 4. Historical observations and current evidence

Earlier S-series observations are historical, not acceptance of the N00–N15 delivery. The old Earth SkyView sun/sky ratios, unconstrained gray-card range and DebugView captures are not valid acceptance gates. Their original receipts remain in the evidence archive; the perturbing capture commands have been retired.

The accepted N02 early baseline and N03 capture evidence are indexed in PLAN.md. N02 proves a built, running macOS Player, while shader and image failures remain open. N03's Editor normal-frame capture reads DisplayColor, Lighting and genuine TAA confidence without changing the target, DebugView or history. Its native Metal probe observes the actual Present attachment separately from the linear intermediate readback. N03 independently passed its capture gate; N04 refinements and later image gates are tracked separately.

## 5. Platform reproduction and acceptance

Windows/D3D12, Windows/Vulkan, Linux/Vulkan and HDR hardware are each **UNVERIFIED (external conditions)**. Use Unity 6000.5.3f1, package 0.3.0 and the same fixed scenes, material/Profile hashes and camera settings. Record each platform independently; no Metal result implies another platform passes.

| Item | Current entry / procedure | Acceptance |
|------|---------------------------|------------|
| Compilation and tests | Refresh the existing Editor; `Infinity/Validation/Tests/Run EditMode With XML` | Save the new log byte window and actual XML discovery/execution/pass/fail/skip counts. Compilation alone is insufficient. |
| Normal-frame GPU capture | In an already running Game camera, targeted Unity CLI `infinity_capture_start --requestPath <json>` with `infinity_capture_status` and `infinity_capture_cancel`; Player reads an explicit JSON request through `-infinityCaptureRequest` | Unique run directory, immutable fixture/ROI, source descriptors and producer/queue/frame metadata, original raw bytes, zero NaN/Inf and terminal resource retirement. The macOS Player entry passed N03; other platforms remain externally unverified. |
| Static temporal quality | Fixed static fixture, FilmGrain disabled, proven liveness; warm up at least 120 successful frames then capture at least three normal beauty frames | Declare measurement color space and ROI beforehand. Per-channel change threshold 8/255; changed-pixel fraction below 0.5%. Stable valid confidence above 0.9; occlusion/reset must reject then recover. Constant fabricated confidence is invalid. |
| Motion and transparency | Dedicated moving-object, disocclusion and transparent fixtures | Inspect coverage, rejection and recovery separately; do not apply the static difference gate to moving regions. N11/N12 fixtures remain pending. |
| Output encoding | Known-linear OETF tests, actual target-format/transfer metadata, plus actual Game/Player window | Only a known linear 0.18 input is expected to encode to sRGB approximately 0.461356. Film/LUT/exposure are separate tests. HardwareSRGB intermediate bytes are pre-encoding linear data, not native backbuffer readback. |
| Atmosphere | N08 fixed Earth reference fixture and independent numerical reference, still pending | Validate units, LUT/SH/IBL, sun direction, cache invalidation and actual sky. Do not derive a sun/sky gate from a LUT without the sun disk or impose an unsupported horizon ratio. |
| Editor frame tree | In Play mode after GPU capture finishes, `Infinity/Validation/Dump Frame Debugger` | Nonempty identified Game-camera event tree; unavailable native event details remain explicitly unavailable. Player does not use this Editor evidence. |
| Depth and boundary sizes | Fixed targets at 1×1, 17×13, 1919×1079 and normal size | No out-of-bounds/nonfinite data, correct geometry/depth and history behavior. Capture must not switch targets or issue an extra camera render. |
| Performance | Three consecutive 300-frame steady-state windows after warmup | CPU/GPU p50/p95, memory, draw/dispatch, cache/pool/retirement; no sustained growth. Missing GPU timing/trace stays unavailable, with no inferred overlap benefit. |
| HDR | Actual capable display and matching output mode, independently captured | Record device capability, format and output encoding; inspect real window and numerical transfer contract. No hardware pass without the equipment. |

Final builds, complete scene matrix and per-platform reproduction bundles remain N15 deliverables. The active status and verdicts are solely in PLAN.md.

## 6. Legacy

Kept on purpose:

- `TemporalAntiAliasingGenerator` — live Halton jitter + TAA dispatch. Split later; do not delete.
- RTAO / RTGI files — no RG pass; hardware RT out of scope.

Removed earlier (S9): SSR/SSGI/GTAO/SVGF Generator classes, DummyShaders, ComputeCompress.

## 7. Excluded

| Item | Why |
|------|-----|
| Hardware RT | Files stay; no RG pass |
| Baked GI / DOF / XR / MSAA / dynamic resolution | Not in RecordRG |
| Super-resolution quality | Asset flag + pass exist; not a closed gate |
| Preview camera temporal gating | Documented independent defect |

## 8. Archive (example project, not in package git)

Copy under `InfinityExample/Logs/baseline/`:

- `t0-*` DebugView + liveness
- `t1-output-game.png`
- `t2-spazon-game.png`, `Logs/debug/atmosphere-earth-skyview-stats.json`
- `Logs/debug/Scene_Spazon-Albedo.png` (T3)
- `t4-a/`, `t4-b/`, `t4-spazon-play.png`
- `t5-play-a.png`, `t5-play-b.png`, `t5-play-diff.json`

## N04.d candidate evidence (not parent acceptance)

LightContext native buffer uploads and consumer inputs now participate in RG dependencies; buffer growth retires old allocations after Submit. Forward binds light records/counts itself. Optional ZBin counter capture uses an ordered Transfer copy to session-owned staging. The immediate empty diagnostic callback and unused LastZBinOverflow property were removed. Fresh Editor XML reports 155 passed, zero failed/skipped. LocalLights captured 123 successful frames, three samples with zero overflow and zero outstanding staging. Root inspected N04d-local-lights.png; band/triangle defects remain failed visual baselines. Evidence: N04-20260906T170440Z/N04d-candidate-01.json; Terra pending. Full Submit-failure transaction and final Player rebuild remain later gates.

## N04.c transaction candidates

c.1 independently passed queue-acceptance production/capture ownership and abandoned recording cleanup (`N04c1-terra-verdict-01.json`). c.2 implements separate pending/committed descriptors, per-camera commit/reset, accepted shared production, final Submit attempts and exception-preserving cleanup. Its final Editor suite has 173 passing tests, including eight camera/Submit/shared-producer combinations and failed texture/buffer resize preservation. A new normal LocalLights capture completed with zero outstanding staging and zero raw overflow. c.2 Terra is pending; c.3 must still perform actual Editor/Player fault/recovery runs. N04 remains incomplete, and current visual artifacts are not accepted.

## N04 integrated real fault/recovery run

The corrected Editor dual-view suite passed independent verification. A fresh macOS ARM64 Player was exported, compiled, signed, launched (PID 98798), and ran the same five faults: first/second camera before queue, after accepted graphics/async queue, and after native Submit. Both platforms recover through 120 successful warmup frames and three normal beauty captures per case. Player analysis independently checks 49 finite raw buffers and distinct camera matrices, with no outstanding captures after cleanup. Original exceptions and uncommitted history on failed frames are recorded. The Player ZIP is `player-build-20260906T1908391572710Z-df0a01059ccf4bbb8551ba21dc0d0da9/InfinityRP-N04-FaultPlayer-arm64.zip` (SHA-256 `67c576ef4566b0bd6fb0a3798256f0d093100b9f46cdc5ac550dafde8c15b672`). This is a fault-validation artifact, not the final rendering delivery. Player/integrated Terra verdict is pending.

Known band/triangle/cube errors remain N07/N08. Twelve pre-existing Metal shader-entry failures remain N06/N11. The five InjectedFault log entries are intentional test inputs, not normal-run errors. A historical reload missing-Behaviour warning remains unattributed despite clean loaded-object snapshots; revisit its context if reproduced during N14/N15. No hardware device-loss or external-platform result is claimed.

## Verifier model provenance correction

On the current continuation, a historical verifier receipt declared GPT-6 Astra and the former Player verifier confirmed GPT-6/Codex despite its Terra task name. Those receipts remain unchanged as evidence but do not satisfy the required actual GPT-5.6 Terra model gate. New agents explicitly configured as `gpt-5.6-terra` are rechecking N00–N04 and N05 completed candidates. The PLAN table records this verification state; do not interpret earlier PASS wording as completed model-specific acceptance until supplemental receipts pass.

## Unity CLI interaction preparation — 2026-09-09

User authorized replacing UI-driven automation and selected Unity 6000.6.0f1, currently being downloaded after removal of the old Editor. Official CLI 1.0.0-beta.6 is installed; Example manifest now pins com.unity.pipeline 0.6.0-exp.1, with an exact dependency-only change and pre-change manifest/lock backups. No Editor connection, new-version compile or rendering acceptance is claimed. The source-audited official screenshot command rerenders the camera and is excluded from normal-frame evidence. N03.I tracks the command adapter, actual verification and retirement of superseded scripts; full interaction refactor remains incomplete until those gates pass.

## Deprecated Jobs removal — 2026-09-09

Removed com.unity.jobs from the Example and Infinity manifests and removed its empty Unity.Jobs assembly reference. Actual job namespaces/implementations remain provided by Unity and Collections. UPM regenerated the lock without Jobs or a referencing dependency. Corrected the Unity 6.6 CS0619 object-ID conversion in migration inspection to use ulong and EntityId.FromULong without narrowing. Unity 6000.6.0f1 recompiled and passed the actual216/216 Editor suite; Package Manager no longer shows the deprecated Jobs warning. Evidence: jobs-retirement-20260909. This does not validate all rendering features or rerun asset migrations on6.6. The official CLI server separately fails to bind ports7800–7849 and N03.I remains incomplete.

## CLI listener recovery — 2026-09-09

The previously open port-bind failure is now corrected in the outer Example embedded bridge: numeric IPv4 loopback replaces the failing wildcard prefix. Actual same-Editor probe, ready status, read-only eval and successful recompile/reconnect evidence are archived in cli-listener-fix-20260909. Authentication still rejects missing tokens with401 and the listener binds only127.0.0.1. No second Editor, production render change, Cache patch or retained probe script is involved. Full custom validation-command migration remains N03.I.


## 2026-09-10 visual repair wave — Editor-only acceptance

CSM, HiZ/SSR/SSGI/GTAO and baked/mixed-lighting repairs passed the finite visual-wave gates under user-selected Astra low independent review. Final Editor XML241/241; original scene/camera/11maps restored;0outstanding,MatrixDuplicateRatio1. See sibling `InfinityRP-Validation/visual-repair-20260909/DELIVERY.md` and `astra-independent-verdict.json`. No macOS build or Player run was performed. Unrelated N00–N15 and untested platform gates remain unchanged. Original failed receipts are preserved; no compatibility path or hit-record compression was added.


## 2026-09-10 TAA candidate03 — rendering accepted, crash attribution open

Game and SceneView now use matched per-view motion and previous-depth metadata, unsharpened RGBA16F accumulation/history, and a separate bounded luminance sharpen output. Native rigid/deformed snapshots and Mesh snapshots commit with accepted camera submissions. Consumer-audited replaced paths were removed; no history compression or compatibility shim was introduced.

The final candidate03 Editor run passed264/264 tests. Current normal-frame evidence includes both seven-phase camera trajectories,62 exact color/depth history pairs, two projection fixtures, five CPU/GPU lighting scenarios,108 finite screen-space outputs,33 HiZ mip CPU comparisons and3,938,612 coherent SSR hits. Root and Astra low inspected enlarged pole/sky and stop-sequence evidence. Source differs from candidate02 only by a hash-proved line-ending correction. Evidence: sibling `InfinityRP-Validation/TAA-20260910-052410/final-candidate03`, with independent review03 and failure archives retained separately.

Both required idle trajectories completed without recurrence in the same Editor (1801.362s startup;1801.550s after normal rendering/capture/Play). Nevertheless, an earlier candidate repeated the original SourceAssetDB idle crash. Its cause remains UNKNOWN; quiet observations do not prove a fix. Independent overall two-plan acceptance and the completion-conditioned main upload remain blocked on that distinction.

A separate UDS startup `stoull` parser fault was reproduced in the same-version empty project using a4KB AppleDouble sidecar. Quarantining only that sidecar recovered original-project startup while preserving the actual461,996,998-byte data file hash. This does not attribute the idle crash. No macOS build, engine upgrade, Library deletion, asset save/migration or rebake was performed. Existing N11 custom Mesh transparent submission and other untested scopes are not claimed as passing draws.


## 2026-09-10 main submission — current local change list

This submission records the existing local rendering fixes, CLI validation tools, tests, Unity6.6 importer metadata and development instructions at the user's explicit request. It is not a declaration that both repair plans are fully closed. Earlier runs recorded241/241 visual-wave and264/264 final TAA Editor tests; this submission does not claim a new test run. Historical evidence locations referenced above are provenance records, not a guarantee that temporary or external raw files remain available.

The original-path startup was temporarily recovered by removing a backed-up AppleDouble companion, but the Editor immediately generated a new ._UDSData_5.bin. Durable startup repair is therefore still open. The separate idle SourceAssetDB/MDB_BAD_RSLOT crash remains unattributed. No project relocation, installed-Editor binary/signature modification, engine upgrade or macOS Player build is part of this commit. Custom Mesh transparent submission remains the pre-existing N11 gap.

The Example project's embedded Unity CLI transport is outside this package repository; this commit includes the package-side adapters and documentation, not that external dependency. Intermediate captures, crash logs and local credentials are not part of the Git submission.
