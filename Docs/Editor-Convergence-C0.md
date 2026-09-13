# C0 Editor convergence baseline — candidate 1

This is input/baseline evidence, not acceptance of rendering quality or implementation C1–C7. Independent GPT-6 Astra candidate 2 baseline verdict: PASS. No rendering source or user asset was edited for C0. Existing pending changes remain the candidate input.

## Identity and integrity

Editor: existing PID 96357, Unity 6000.6.0f1, Metal, Apple M3 Max. Original scene `Assets/Scene/Spazon/Scene_Spazon.unity`, not dirty, not playing. Scene restored without save. Source comparison against 972 preimages found only intentionally changed package AGENTS.md and PLAN.md. The previous ledger is archived verbatim in Docs/History/PLAN-0.4.0-before-editor-convergence.md. No branch, build, packaging, commit or push was performed.

All task-owned evidence below is relative to `/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/editor-convergence/`. `c0-input/manifest.json` binds original pending source, GUID/meta bytes and backups (25,197,833 bytes); `c0-integrity.json` records the exact two documentation deltas. `c0-candidate-hashes.json` contains 1,391 current package/Example Assets/ProjectSettings entries, SHA256 `0a992f346964691dce39e695cd8bf87779f0792968c59d831fd36f844048cfb5`. The candidate report itself was written after that inventory and is an explicit documentation-only addition.

## Captured baseline and primary visual review

Normal submitted frames, never Camera.Render: three DisplayColor and Lighting captures per camera, full frame, finite readbacks, outstanding count zero on completion. Game captures warm up 120 frames. Spazon SceneView capture warms up eight submissions, then captures three non-reset frames; this only proves this capture path, not intermittent temporal correctness.

| Scene | Evidence | Current visual result, not accepted quality |
|---|---|---|
| Spazon Game | c0-spazon/capture, window.png | Detailed architecture and animated objects visible. Protected baseline; no new quality claim. |
| Output | c0-Validation_Output/capture, window.png | FAIL: mostly blank gray area and a blue rectangular region; not usable gray-card acceptance. |
| LocalLights | c0-Validation_LocalLights/capture, window.png | FAIL: black horizon band; colored light pools and cubes visible, shadow correctness not proved. |
| Decal | c0-Validation_Decal/capture, window.png | FAIL: black horizon band; overlapping flat colored regions need producer/consumer review. |
| Translucent | c0-Validation_Translucent/capture, window.png | FAIL: black band and flat translucent shapes; no correct refraction/particle acceptance. |
| Temporal | c0-Validation_Temporal/capture, window.png | FAIL: black band; two cubes, one partially outside view; no motion acceptance from still images. |
| Volume CameraA and CameraB | c0-Validation_Volume/full-lut-CameraA/capture and full-lut-CameraB/capture, respective window.png | Two views captured separately. FAIL: black backgrounds; intended distinct grading/mixing is not established by this frame. |
| UI | c0-Validation_UI/capture, window.png | FAIL: magenta unreadable TMP blocks, incomplete fixture and black lower region. |
| Spazon SceneView | c0-sceneview/capture, window.png | FAIL: image occupies only upper-left part of available view. Capture succeeded; viewport correctness did not. |

Primary agent opened and inspected all listed actual window screenshots (Volume combined window shows both cameras). Raw capture formats must be read from each capture.json: Game DisplayColor is RGBA16F display-linear under hardware sRGB policy; SceneView DisplayColor is RGBA8 shader-encoded sRGB. These are not interchangeable. window.png is the actual Editor display evidence; a derived preview is not a substitute.

## Full effective Volume and LUT evidence

`c0-full-luts.json` indexes nine Game camera snapshots. Each `full-lut-<camera>/effective-state.json` enumerates actual per-camera effective Volume components and all reflected LUT key fields. `lut-source.json` records committed texture descriptor. Each complete LUT is 32×32×32 RGBA16F, 262,144 bytes, all 32 readback layers concatenated; accompanying SHA256 binds content. Spazon hash is `5167cd3d014b44f0bbe3a4c7e31a249160563eb0261ccfc6702dfb1808e1e28f`; eight validation-camera LUTs share `93e5489c517d7a492b01c504076937f1316d9e7f4e25f34c3e11d66d37ceaac3`. Equal LUT bytes are an observation, not proof that Volume mixing is correct.

First attempts are retained as failures: initial top-level `lut.bin` files contain only layer 0 (8,192 bytes) and are invalid as full-LUT evidence; `c0-baselines.json`'s first Volume attempt expected one camera and failed because two exist. The corrected `full-lut-*` artifacts supplement rather than rewrite those original records.

## Tests, logs and open defects

`c0-tests/run.json` and `results.xml`: 275 passed, 0 failed, 0 skipped, 0 inconclusive. Existing Editor TestRunner, no Player build. This does not close visual/interaction/performance gates.

`c0-editor-log-delta.log` records project Logs/Editor.log from byte 141989; exact endpoint and hash in `c0-log-summary.json`. 80 shader-warning lines include Metal potentially uninitialized DeferredShading/lighting/shadow variables. They remain defects owned by C6, with final warning-free gate C7. Curl35 certificate, licensing404 and native TouchGestureResponder diagnostics are preserved separately; no claim that all Console entries are Infinity defects or that the log is clean.

## Retention and next consumers

Current task data approximately 1,303,739,735 bytes. C0 review consumes input, captures and corrected LUTs now. Baseline raw frames/parameters are needed by C1–C7 image regression and migration checks; preimages by targeted asset migration integrity and final comparison; selected final evidence/summary will be retained and raw intermediate data deleted after those consumers. Swift module cache and scratch capture scripts are owned by this task and must also be removed. No producer or outstanding readback remains after each completed capture. Disk cleanup has not passed and the overall task is not complete.

Next: independent Astra reviews baseline completeness, failure classification, identity and restoration. C1 remains locked until that review passes. No stage may convert the visual FAILs above into a PASS without the planned repair and actual verification.

## Candidate 2 corrections (preserve candidate 1 findings)

Independent Astra found that the CLI stringified `List<object>` in the separate effective-state export. The initial exports are invalid as full state evidence; capture.json already contains 14 actual effective Volume components, but does not replace the full LUT key. `c0-structured-v2.json` now indexes ten explicit-JSON exports (nine Game cameras and Spazon SceneView); `structured-v2-*/effective-state.json` contains actual arrays, all components and all descriptor fields (vector fields use full JSON x/y/z/w). Every matching committed LUT has all 32 layers, 262,144 bytes. A transient eval placeholder compilation error was corrected before these exports; it did not change Unity source.

`c0-final-editor-state.json` explicitly records restored Spazon, dirty=false, playing=false, same PID. The Volume screenshot asterisk was observed only in Play; no Save was invoked and byte comparison shows no persisted scene change. The existing initialization/auto-add behavior will be removed in C1/C2; its exact dirty trigger is not inferred from the asterisk alone.

The immutable input manifest's top-level `bytes` field incorrectly says 1036. The sum of the 972 per-file byte entries and verified preimage file lengths is 25,197,833; this correction supplements the original rather than changing it. The original source inventory remains valid except the explicitly declared report addition and its Unity-generated meta; no rendering source changed.

Independent verifier `/root/c0_astra_verifier` confirmed all current/preimage hashes, 60 buffer files, ten full state arrays (15 effective components, 42 LUT fields), complete LUTs, XML/log receipt and restored scene. C1 may unlock; visual/cleanup gates remain open. Verifier created no artifacts.

## Superseded-data cleanup receipt

After candidate 2 acceptance, independent Astra verified the exact 64-file `cleanup-c0-candidate.json` manifest (SHA256 `b1ba0838f2f920ffae311c3bbb9ad7a901b30685d9717aac038ded1df05d44d0`). Those v1 List exports, partial/duplicate LUTs, duplicate descriptors and hash sidecars were retired after all original/replacement hashes were rechecked. `cleanup-c0-superseded.json`: 64 removed, 2,421,310 bytes reclaimed, all named originals absent and all corrected v2 replacements present. No raw frame capture, window image, preimage, original failure index or v2 artifact was removed. Historical v1 paths above describe captured-and-retired evidence; the manifest maps each to its retained replacement. Overall C7 cleanup remains open.

The two completed invalid capture-producer scripts were separately retired after proving the retained v2 script has no dependency on them. cleanup-c0-producers.json binds their hashes and verifies absence; 13,195 additional bytes reclaimed, total 2,434,505 bytes. No baseline measurement removed.
