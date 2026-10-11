# InfinityRP — Editor convergence execution ledger

User-approved C0–C7 replaces the prior normalization completion claims as the active execution scope. Previous ledger is preserved verbatim in Docs/History/PLAN-0.4.0-before-editor-convergence.md; its PASS records remain historical and do not pass these gates.

## Contract and roles

Main agent implements and performs self-tests and visual inspection. Independent GPT-6 Astra verifies candidates. Luna is read-only search/inventory only. No branches, commits, push, PR, Player builds, packaging or Player acceptance. All Editor quality gaps are in scope. Current user-approved contract takes priority over prior native-motion/two-pass/output-transform constraints.

Production targets: Unity native motion for native Renderers; retain only necessary custom MeshDrawing history; one-dispatch TAA+sharpen; linear DisplayColorBuffer with transfer encoding in final Present; functional SR/UI/Volume/Debugger; no legacy compatibility shims. No in-scope unknown is relabeled complete.

## Active stages

| Stage | Dependencies | Status | Required result |
|---|---|---|---|
| C0 | — | passed (Astra candidate 2) | Full current input snapshot; active Editor/dirty state; Spazon and all validation baselines; full effective Volume/LUT; source-bound evidence |
| C1 | C0 | implementing | Lossless configuration, explicit setup/reset, resources, lifecycle ownership and consumer-correct shader stripping |
| C2 | C1 | pending | Camera/Light/Material user flows, scoped auto-add, Undo/mixed/prefab/reload, canonical default object materials |
| C3 | C2 | pending | Complete Volume editors, real effective-state Debugger, all fields and producer-backed diagnostics |
| C4 | C3 | pending | Production per-camera render dimensions, fixed SR and complete TMP/Canvas fixture |
| C5 | C4 | pending | Native Renderer motion, view discontinuity handling, single-dispatch TAA and fused final output |
| C6 | C5 | pending | Editor lighting/shadow/SSR/SSGI/SSS/fog/cloud/translucent/particles and correct denoised URT |
| C7 | C6 | pending | Full regression, performance, one-hour stability, obsolete-path retirement, docs and disk cleanup |

## U-task closure map

- [ ] U00: reopened for full user-approved Editor acceptance; prior scoped gate: Ledger, archive, AGENTS/DESIGN/runbook agree with this wave
- [ ] U01: reopened for full user-approved Editor acceptance; prior scoped gate: CoreRP 17.6 research pack in Docs/History
- [ ] U02: reopened for full user-approved Editor acceptance; prior scoped gate: Baseline + Spazon Game frames + LUT hash; SceneView capture still blocked by historyReset
- [ ] U10: reopened for full user-approved Editor acceptance; prior scoped gate: GlobalSettings generic order + settingsList ownership; ResolveDefaultVolumeProfile throws when empty (no Resources.Load fallback)
- [ ] U11: reopened for full user-approved Editor acceptance; prior scoped gate: RP Asset schema + renderScale; Example second-save / no-op byte-identical
- [ ] U12: reopened for full user-approved Editor acceptance; prior scoped gate: Dispose restores graphics state; Infinity→Builtin→Infinity midPipe empty
- [ ] U13: reopened for full user-approved Editor acceptance; prior scoped gate: Editor/ShaderStripper IPreprocess* ; Player preprocess variant receipt (Mono)
- [ ] U20: reopened for full user-approved Editor acceptance; prior scoped gate: Additional camera data + InfinityCameraEditor
- [ ] U21: reopened for full user-approved Editor acceptance; prior scoped gate: Additional light data GUID c8dcc11eda3e9404e884a2332f3a4890; GameObject/Light Undo auto-add
- [ ] U22: reopened for full user-approved Editor acceptance; prior scoped gate: SceneView/Preview Volume rule in ResolveVolumeSelection
- [ ] U23: reopened for full user-approved Editor acceptance; prior scoped gate: RenderGraphView / PipelineAssetConfiguration / FoliagePipeline retired
- [ ] U30: reopened for full user-approved Editor acceptance; prior scoped gate: Pipeline filter, taxonomy, IsActive, enable, tonemap mode
- [ ] U31: reopened for full user-approved Editor acceptance; prior scoped gate: IsActive gating; VolumeHasOverrides deleted
- [ ] U32: reopened for full user-approved Editor acceptance; prior scoped gate: SSS quality = Volume numSamples only
- [ ] U33: reopened for full user-approved Editor acceptance; prior scoped gate: Dedicated VolumeComponentEditors for shipped components
- [ ] U34: reopened for full user-approved Editor acceptance; prior scoped gate: Validate & Complete + confirmed Reset; EnsureAsset deleted
- [ ] U35: reopened for full user-approved Editor acceptance; prior scoped gate: Debugger panels; DebugView reads runtime settings
- [ ] U40: reopened for full user-approved Editor acceptance; prior scoped gate: LitGUI blocks + YAML property rename; migrate menu retired in U80
- [ ] U41: reopened for full user-approved Editor acceptance; prior scoped gate: InfinityUnlit + T2 SRPDefaultUnlit/untagged
- [ ] U42: reopened for full user-approved Editor acceptance; prior scoped gate: DiffusionProfile index / unreferenced warning
- [ ] U50: reopened for full user-approved Editor acceptance; prior scoped gate: UIOverlay + TMP Validation_UI Game capture; Player/HDR UI TODO(UNVERIFIED)
- [ ] U60: reopened for full user-approved Editor acceptance; prior scoped gate: Custom motion retained; native A/B flag only. See Docs/History/U60-U61-Experiment.md
- [ ] U61: reopened for full user-approved Editor acceptance; prior scoped gate: Two-pass TAA retained; fusion flag reserved. See Docs/History/U60-U61-Experiment.md
- [ ] U70: reopened for full user-approved Editor acceptance; prior scoped gate: URT VisibilityRTAO + accel; Metal Compute frame captured; Player/D3D12 TODO(UNVERIFIED)
- [ ] U80: reopened for full user-approved Editor acceptance; prior scoped gate: One-off Migrate/Upgrade menus stripped; Window/Infinity validation + CLI remain
- [ ] U81: reopened for full user-approved Editor acceptance; prior scoped gate: package 0.4.0 / 6000.6 / 17.6; CHANGELOG + Documentation~ synced
- [ ] U82: reopened for full user-approved Editor acceptance; prior scoped gate: Editor vs U02 match; Player beauty/UI TODO(UNVERIFIED) (Burst fd rebuild + Metal MRT)

## Evidence and acceptance

Every candidate binds complete source/asset hashes, applicable test XML, actual new Editor.log delta, normal-frame images and independent Astra verdict. Test failure/skip, missing frame, or missing source identity blocks closure. Stable ROI changed-pixel ratio <0.5% at 8/255 unless explicitly justified; fused history bit-identical and output max error <=1/1024; three warmed 300-frame CPU/GPU samples, median/P95 regression <=5%, fused TAA faster; one-hour stability and steady resources. Never infer these from means, partial LUT keys, field existence or HEAD alone.

## Current attempt / compressed working state

- C0 input owner: intermediate/editor-convergence/c0-input. Verified preimage copies: 972 files, 25,197,833 bytes. Full package source hash inventory and new-log byte mark in manifest.json. Retained for C0 baseline and subsequent approved migration integrity checks; remove after final consumer C7.
- Editor PID 96357 is alive and CLI ready via authorized host execution. Sandboxed discovery falsely reported no instance. Initial loaded scene: Assets/Scene/Spazon/Scene_Spazon.unity, dirty=false; Play stopped. No restart needed.
- One read-only eval initially failed due missing return/semicolon; corrected query succeeded. This is an agent command diagnostic, not a source compile failure.
- C0 next: normal Game/SceneView baselines and full effective Volume/LUT capture; no rendering source changed yet. Capture failures remain recorded failed baselines, not runtime acceptance.
- Cleanup: no task data deleted yet. No capture/readback producer active at this checkpoint. Only the recorded task directory is owned; no shared/Library/user-log deletion.

### C0 independent verdict

GPT-6 Astra `/root/c0_astra_verifier`: candidate 2 baseline PASS. 972 preimages, 1391 candidate entries, 10 three-frame captures/60 buffers, 10 full LUTs and 42-field descriptors verified; 275 tests passed. Exact review in Docs/Editor-Convergence-C0.md. Existing visual failures remain assigned C4/C5/C6, no quality PASS. C1 unlocked.

### C1 current checkpoint

- Implemented (not gate-accepted): pure GlobalSettings Require; explicit fresh creation; lossless missing-component completion; real settings/property editor; reset byte/value backups and Undo; resource preflight; constructor cleanup; immutable installed-global ownership; shader callback scope and compatible-pass preservation. Old Ensure/auto-load-create/force-override/unused-pass stripper owners removed.
- Verified current attempt: c1-tests-a 280/280 passed, 0 failed/skipped. c1-authoring/receipt.json proves fresh settings, 13 preserved components, one required addition, identities/reopen/second-save/no-op, original source bytes unchanged, temporary Assets fixture directory deleted. Later code review fixes require a new complete test run.
- Independent Astra pre-gate review found and main fixed: native VolumeProfileEditor implicit repair; partial graphics-state installation; graphics sentinel incorrectly controlling shared-resource cleanup; mutable installed quality/material/texture comparison; missing default-state refresh after Reset. Await renewed verification; no C1 PASS.
- c1-tests-b blocked before discovery by Spazon save prompt; dialog canceled without Save/Don't Save. Test cancel requested; no result XML/PASS. Read-only dirty-object query identified Directional Light (with space), GameObject/Transform/Light/AdditionalLightData, native local IDs 1873545565/68/67/66. User asked whether this is their change; pending. Preserve current dirty scene, do not clear flags or reload it. C1 live switch/full TestRunner gates depend on this clarification.
- New C1 source compilation briefly failed because test assigned read-only VolumeManager.globalDefaultProfile; replaced with public SetGlobalDefaultProfile and recompiled. Keep failed attempt in logs. New shader callback/lifecycle/editor tests are not yet signed off.
- C1 disk consumers: c1-authoring before/after copies and script -> C1 independent asset review, then cleanup; c1-tests-a/b -> C1 final report, then cleanup. C0 comparison/preimage consumers remain C1–C7. No unrelated data cleanup.

- C1 full current checkpoint and unresolved gates: Docs/Editor-Convergence-C1.md. Latest source compiles; final gate remains open. Native Graphics tab was present in page middle; earlier navigation inference of missing UI is retracted. Current custom drawer was visually reached.

- C0 superseded-data cleanup: exact 64-file manifest independently verified by Astra and hash-bound before deletion; 2,421,310 bytes reclaimed, originals absent/replacements present. c0-structured-v2 state/LUT, baseline raw frames/windows, preimages and original failure indexes remain for C1–C7 named consumers. See cleanup-c0-superseded.json; no overall cleanup PASS.

- c1-reset-a: isolated persisted Profile verification passed current-byte backup, 14 identities, effective Reset/Undo/Redo, source not implicitly saved, explicit save/reopen/second-save, fixture deletion, original asset and dirty scene preservation. Backup consumer is independent C1 asset review. Full suite still blocked; synchronous mode cannot bypass scene-save safely or cover UnityTests.

### Exact current intermediate consumers

All paths below are relative to `/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/editor-convergence/`; remove each after its listed final consumer, with the final report/hash receipt retained.

| Retained path | Named verification consumer |
|---|---|
| c0-input/preimage and manifest.json | C2/C3 explicit asset-delta preflight and C7 original-input/final-candidate integrity review |
| c0-spazon/capture and window.png | C1 Spazon unchanged-region comparison; C7 final protected-baseline comparison |
| c0-sceneview/capture and window.png | C4 viewport defect comparison and C5 intermittent-view temporal comparison |
| c0-Validation_Output/capture and window.png | C5 final output encoding and C6 gray-card comparison |
| c0-Validation_LocalLights, c0-Validation_Decal, c0-Validation_Translucent captures/windows | C6 respective lighting, decal and translucency repair comparisons |
| c0-Validation_Temporal/capture and window.png | C5 temporal history/fusion and C6 SSR/SSGI comparison |
| c0-Validation_Volume/full-lut-CameraA/capture, full-lut-CameraB/capture and windows | C3 actual per-camera Volume mixing comparison |
| c0-Validation_UI/capture and window.png | C4 TMP/Canvas/mask/viewport repair comparison |
| c0-*/structured-v2-* state, descriptor and full LUT | C1 value-preservation comparison; C3 complete effective Volume/LUT validation |
| c0 failure indexes, log/test/identity receipts and cleanup manifests | C7 final evidence audit; preserve selected summaries in final report before retiring raw copies |
| c0-structured-v2.py and swift-cache | Next C1 normal-frame/settings-window evidence collection after scene clarification |
| c1-authoring and c1-reset-a | Independent C1 persisted-profile/Undo/reset evidence review; then delete raw fixtures/backups/scripts |
| c1-tests-a and c1-tests-b | C1 final test review; retain 280-pass and canceled-before-discovery facts, then retire raw attempts |

Old c0-capture.py and c0-full-luts.py were deleted after verifying the corrected v2 exporter is standalone. Exact hashes in cleanup-c0-producers.json; 13,195 additional bytes reclaimed. Combined C0 superseded cleanup: 2,434,505 bytes. Use corrected explicit-JSON exporter for new evidence.

- Final waiting checkpoint: c1-editor-checkpoint.json confirms Spazon remains dirty with the same four Directional Light objects, Play stopped, native TestRunner job inactive, capture Idle and outstanding readbacks 0. Latest compilation completed with no compiler errors; no preexisting asset/meta byte changes detected. Awaiting the user's ownership clarification before any scene-save/reload-dependent acceptance.

## SceneView specialization (approved priority override)

User approved implementing the SceneView plan ahead of remaining C2/C3 work, after necessary C1 verification. All original C0-C7 goals remain open unless separately accepted. Current live Spazon is clean; prior dirty-scene blocker is resolved. Owner: intermediate/editor-convergence/sceneview-20260912, initial budget 2 GiB. Stage SV0 input/C1 tests in progress; SV1 dimensions/viewport; SV2 linear target + fused Present and targeted resource retirement; SV3 real frame/interaction/performance and Astra verification; SV4 cleanup. No Player build, branch, commit or push.

### SV0 prerequisite checkpoint — 2026-09-12

- Initial live Scene_Spazon was clean. Full EditMode candidate discovered 291: 290 passed, 1 failed, 0 skipped. Failure was the foreign-pipeline shader test destroying a transient Shader and receiving `attempt to write a readonly database`; immutable fixture now replaces the transient Shader lifecycle. This failed receipt remains a failure.
- A rerun encountered the modified-scene prompt and was cancelled without saving/discarding; subsequent reload records InterruptedByAssemblyReload, not PASS. Live state has the original `DirectionalLight` clean and an additional dirty `Directional Light` (Light local ID 1411122354). Ownership question sent to the user; no scene save/reopen/delete is authorized by that unanswered question.
- Removed Light factory hierarchy/object-change scans; only ObjectFactory-created Light is targeted. Corrected duplicate Undo rollback in default-profile tests, including no retry after a rollback exception and finally cleanup. Independent Astra source review supports the normal-path changes but does not close C1; source review identified Inspector OnEnable and global Undo/state boundaries requiring runtime checks.
- Compilation passed after the final rollback-exception refinement; `git diff --check` passed. SV1–SV4 remain pending; no SceneView visual/size or output-fusion completion is claimed.
- Owned run: `intermediate/editor-convergence/sceneview-20260912/`, 2 GiB budget. Failed test originals and input identity remain consumers of SV0 repair; do not delete before evidence closure. Checkpoint allocated size: 2,359,296 bytes; destination volume available: 3,565,158,400,000 bytes. No final cleanup claimed.

### SV0–SV2 execution checkpoint — 2026-09-12, continued request

- SV0 necessary C1 test-pollution gate: independently accepted by GPT-6 Astra for the bounded prerequisite. Fresh full EditMode XML has 291 test cases, all Passed, zero failed/skipped/inconclusive. Receipt `sceneview-20260912/c1-tests-clean/results.xml` SHA-256 `ad40eec0d060c565d729645fb12c773f629c87d51c5a849b551f89d1c44c3495`. This does not close all C1. Original failures remain unchanged.
- Unsaved scene preserved with SaveScene(saveAsCopy=true) at `sceneview-20260912/preserved-unsaved-Spazon.unity`, 115856 bytes, SHA-256 `42a91b6af9f43c4d0120b5195f1e10f63bd261b0778574f39f9d5d7207f924de`. Preview reopen compared 163 objects' transforms/components and matched. Disk original reopened without saving it; after full tests Spazon stayed clean with only original DirectionalLight. The snapshot remains protected because extra light ownership is not established.
- SV1 candidate implemented: existing per-camera dimensions integrated into internal/display consumers and history reset; Scene/Preview bypass Game SR; SR enabled distinct from downscaled; internal-grid jitter and SR input sampling; record-time Present viewport/UV; raster viewport/scissor reset; output-sized editor overlay depth and display ScreenParams. Runtime/visual acceptance remains pending.
- SV2 candidate implemented: display-linear source published directly, output transfer in new Present Raster shader; SceneView/Preview/RT storage semantics; native SceneView post-process gate; independent DebugView with correctly mapped source texels and independent UAV output; post bypass copies into its own color resource; capture includes dimensions/viewport/color encoding metadata. Old output compute pass/kernel/resource field remain temporarily uncalled, pending replacement verification and authorized retirement. Auto-review rejected premature deletion; no deletion was performed.
- Independent Astra found and main fixed three integration gaps: mismatched SR overlay color/depth, DebugView internal input indexing, and DebugView being hidden with post effects. Follow-up identified SRV/UAV alias on post bypass; DebugView now allocates a separate output. No actual-frame PASS issued.
- Performance: 900 CPU samples after 120 warmups in `performance-before.csv`; GPU values are zero and invalid for GPU gates. CoreRP sampler probe threw on an empty GPU recorder sample; the exact diagnostic callback was removed, recording stopped, and failures retained in log. No performance PASS.
- Editor infrastructure: old PID 96357 accumulated 1146 numeric descriptors including 986 databases, dominated by repeatedly opened ShaderCache.db; Bee failed to register fd 1149. CLI recompile_status reported completed despite stale loaded RenderPresent signature, so those responses were explicitly invalidated as live-code proof. Normal exit followed clean scene/view-state checkpoint. Original project restarted as PID 94446, no Library deletion or project migration.
- New Editor compiled the candidate but then stalled during early SceneView native RendererList CPU scheduling. `editor-restart-sample.txt` has 1977/1977 main-thread samples in PrepareDrawRenderersCommand/PrepareScriptableDrawRenderersJob/ujob scheduling; not a proven GPU readback wait. CLI main-thread and CUA connections time out. Auto-review rejected SIGTERM due possible non-scene unsaved state; user confirmation requested, not received at this checkpoint. Process was not forcibly stopped.
- Isolated Csc checks use only the actual Editor response files and write to the owned run; they do not run tests, validate shaders, or constitute Player builds. Runtime/Editor/Tests first check passed; subsequent source refinement receives a separate receipt. Compiler-observed obsolete VolumeComponentEditor attribute and FindObjectsSortMode call replaced with current APIs.
- SV3 live visual/resize/SR/camera/encoding/interaction/performance gates and SV4 retirement/migration/no-op/cleanup are NOT COMPLETE. Current source is a candidate, not an accepted rendering fix. Whole C0–C7 remains open.

- Latest static checkpoint: `sceneview-20260912/editor-static-compile-04/receipt.json`, Runtime/Editor/Tests each exit 0 with empty diagnostics; `candidate-source-04.json` binds 892 owned Runtime/Editor/Shaders/Tests files. This includes independent DebugView output, independent post-bypass copy, snapshot-based editor post/lighting state, HDR device probing skipped for texture targets, and current assembly enumeration API. No full Unity test run or shader/frame acceptance for this candidate yet.
- GlobalSettings source and meta still exactly match migration-before hashes (`3ef43263a8cc1d3c94b739d7aceddde51ec8ce573c06c8bb9349411e03213c42`, `09677976e5af1df19fa63616630ee2236159ea8f543cae07c74e93dbe12bbf38`); resource field retirement has not occurred. `git diff --check` passes. PID 94446 remains the blocked original-path restart instance. Approval request to force-stop remains pending; no forced termination or second Editor performed.

### SV startup-crash follow-up — 2026-09-12 22:09 onward

- User reported crashes. PID 94446 no longer existed; no SIGTERM was executed. New Hub Editor PIDs 3064/3297 aborted within approximately 0.84/0.76 seconds, SIGABRT, with `stoull: no conversion` in startup log. Separate from previously sampled native RendererList scheduling stall.
- Confirmed `Library/DataStore/._UDSData_9.bin` and `._UDSData_10.bin` were 4096-byte AppleDouble files (magic 00051607, com.apple.provenance); moved only those sidecars reversibly to `sceneview-20260912/startup-crash-2209`, preserving crash reports and receipt. Original UDSData_9/10 SHA-256 verified unchanged before/after. No Library deletion.
- Original-path recovery PID 4123 passed startup, initially responded to eval, loaded the three-argument Present implementation and clean Spazon. The sidecars reappeared with the same hashes, so this does not resolve recurrence. Subsequent CUA and main-thread eval timed out again; a fresh two-second thread sample is preserved. No rendering/stability PASS. The previous forced-termination request for PID94446 is obsolete; no authorization inferred for force-stopping PID4123.


## SceneView upper-left display correction — 2026-09-13 candidate

Scope: approved SceneView-size specialization of C1/C4/C5, preserving current colors and post. C0–C7 remains open. Main owns implementation/live visual inspection; independent GPT-6 Astra (`sceneview_prerequisite_review`) owns verification. No build/package/branch/commit/push.

Owned temporary root: `intermediate/editor-convergence/sceneview-size-20260912-2315`, initial 1 GiB budget; producers: normal-frame readbacks, bounded diagnostic callbacks, screenshots, EditMode results and CPU measurements. Cleanup remains pending until selected evidence and final verification are retained. Older `sceneview-20260912`/C0 inputs have separate historical consumers and are not blanket deletion targets.

- [x] Failure localization: actual PID 20238 connected after normal Editor recovery. Failed normal capture `capture-152029495` completed 3/3. Source, actual SceneView target and viewport were all 1638x1768, scale/bias (1,1,0,0); a full actual target readback ruled out a quarter-sized source. Explicit target import alone did not fix the window.
- [x] Root fix: `RenderUIOverlay` now runs only for Game cameras with no targetTexture, matching HDRP's main-game-view overlay ownership. SceneView was incorrectly invoking this native display-overlay list. Camera/World Canvas remain in T2. The exact native GUI/projection state altered by the invalid call is not asserted.
- [x] Main visual: real docked, maximized and resized floating windows now fill their drawable regions. Two simultaneous SceneViews used independent native targets (1638x1768 and 1588x1108). `capture-153403382` completed 3/3 after successful Submit; Present recorded, no SceneView UIOverlay and no independent OutputTransform compute.
- [x] Protected image: all-image encoded differences above 8/255 are 0.11433%, 0.12269%, 0.11098%, below 0.5%. Exposure/Tonemap/Volume authoring parameters unchanged.
- [x] Explicit Unity-owned target imports retain actual dimensions/format; identifier wrapper ownership test passed. Dimensions freeze after BeginCameraRendering callbacks; Present uses record-time viewport/source/UV metadata.
- [x] Controlled retirement: old OutputTransform pass/kernel/marker/resource field/meta removed after replacement evidence and independent Astra permission to retire. Fresh source-byte backup and full semantic/stable-reference snapshots precede the explicit GlobalSettings save. Only outputTransformShader and its reference removed. Reopen, second save and separate no-op bytes equal. Asset before `3ef43263a8cc1d3c94b739d7aceddde51ec8ce573c06c8bb9349411e03213c42`; after `cabd163193f185a27d3564ea735abcbaee81de103a49fdbf7dcbcb818be7ab3f`; meta unchanged `09677976e5af1df19fa63616630ee2236159ea8f543cae07c74e93dbe12bbf38`. Astra independently checked structural/reference identity; intermediate byte observations remain explicitly the main-agent receipts.
- [x] Actual GPU gray-card test: linear FP16 ~0.18; UNorm shader encoding and sRGB hardware encoding in [0.44,0.48]. Old compute test coverage migrated to real Present for 1x1, 17x13 and 1919x1079, five policies and sentinel padding; targeted 3/3 passed.
- [ ] Final full EditMode: earlier 299/300 failed obsolete UNorm-intermediate assertion, then 298/301 failed three retired-kernel callers. These failures remain preserved; fixes passed targeted tests, final XML pending.
- [ ] Interaction/camera-isolation matrix: Game SR 0.5/0.75/1 and Camera Off normal captures completed; final consolidated evidence, Preview and interaction cases pending.
- [ ] Performance: candidate CPU sampling in progress. Metal GPU recorder valid but sample Count=0 even with profiling and bounded repaint; no GPU PASS. Available Xcode templates lack Metal trace and `xctrace list instruments` is empty. No sample is converted to a zero-time result.
- [ ] Final unique-source/asset identity, independent Astra final verdict and owned-intermediate cleanup.

This is an implemented and visually verified size correction, not a complete specialization or full-convergence PASS while any remaining gate is open.


### SceneView final functional receipt and native recovery blocker — 2026-09-13 02:xx

- Independent Astra verified the full 1318-file candidate manifest `f0c799741015ccad0507907b7ff9caea0dcb0912e9dce7a62669730dc038e423`, 301/301 final Infinity EditMode XML (0 failed/skipped), actual final paired window, all three paired raw differences (0.11004895%, 0.17306725%, 0.14730744%), explicit target ownership and resource migration. Functional correction and resource retirement PASS; not full specialization PASS.
- The exact old Overlay gate was reproduced visibly in the bounded CPU counterfactual; current production gate is restored. CPU 3x300 narrow-delta median no regression, P95 maximum +0.6573%.
- GPU discovery correction: sandboxed Xcode enumeration was incomplete; elevated read-only enumeration exposed Metal tools. Native 2-second Metal Application + GPU segments succeeded. Candidate collected 919 qualified native frames; all segments replayed through one strengthened parser and first 900 selected in segment/start-time order, 3x300. This is not an uninterrupted 900-frame window. Native interval unions/channel unions/span and all exclusions are retained. No before/after GPU PASS exists.
- Long/native-finalization failures were cancelled by scoped sampler limits and their partial traces removed. They are not performance evidence. Task budget revision to 4 GiB was recorded; the anomalous 65-second run exceeded it during native finalization, was cancelled and its ~15.6 GB partial trace deleted. Subsequent failed segments are separately recorded/cleaned. User Library/logs were not swept.
- GPU control transition hit native `AssetDatabase.Refresh -> ShaderGraphImporter.GetIcon -> MatchAsset`, with `LMDB MDB_BAD_RSLOT` and SIGTRAP, in PID 20238. This is separate from the earlier `stoull` UDS startup signature. Before-transition scene dirty=false; no signal was sent to that Editor. The finally path restored candidate source; post-crash full hash recheck found 0/1318 mismatches.
- Normal original-path recovery launched PID 58885. CLI/UI timed out; 2168/2168 sampled main-thread observations are in `SearchIndexArtifactGeneration -> ImportScheduler -> ujob_allocate_sync_job`. Current unsaved state cannot be queried. No force-termination authorization has been received; do not signal this Editor without user confirmation.
- Selected report: `Docs/Editor-Convergence-SceneView-Size.md`; selected evidence: `Documentation~/Evidence/SceneView-Size-2026-09-13/` (ignored by Unity importer). Old failed/unpaired receipts remain represented, not rewritten into PASS.
- Remaining: approved force-end decision if necessary, original-path Editor recovery, loaded-candidate check, GPU control/relative gate, final live-file cleanup. The current Editor owns task-local recovery log handles; the task root cannot yet be declared absent. C0–C7 remains open.

- SceneView size partial cleanup 2026-09-13: 删除已完成中间数据 4026 文件 / 722,318,080 逻辑字节；分配大小 1,719,402,496 字节。保留 pending-gpu-native（GPU 对照/原始输入审查消费者）和 PID 58885 持有的两个恢复日志；专项目录仍存在，清理 gate 未关闭。回执：Documentation~/Evidence/SceneView-Size-2026-09-13/partial-cleanup-receipt.json。


用户要求全量中间清理后的现状核验：项目 intermediate/ 已不存在。该目录在本次只读盘点与删除前置校验之间消失，删除脚本在 assertion 处停止，未执行删除；不能归因于本 agent，不能报告本次回收字节。此前 pending-gpu-native 及两个恢复日志保留状态已失效，GPU 对照仍为 NOT PASS。PID 58885 已不存在，本次未发送结束信号，未启动任何采样。详见 Documentation~/Evidence/SceneView-Size-2026-09-13/user-requested-cleanup.json。
