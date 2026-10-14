# Unity development interaction

Status (2026-09-12): the live CLI connection, read-only eval and recompile/reconnect path are verified on Unity 6000.6.0f1. Normalization batches compile, test, and capture on the Mac worker. Full replacement of custom menu/tool entrypoints remains U80 work; connection readiness is not full rendering acceptance.

## Mac worker runbook (0.4.0)

Cloud agents do not have Unity. Compile, EditMode XML, Inspector screenshots, normal-frame capture, and Example-project migrations run on the owner's Mac after `cursor worker start`.

1. Confirm one Editor holds InfinityExample: `Library/EditorInstance.json` plus `unity command editor_status --project-path /Volumes/DataDisk/Projects/Unity/InfinityExample --format json`.
2. Pull the package branch into `InfinityExample/Packages/com.infinity.render-pipeline` (or the file-link the project already uses). Never open a second Editor or `-batchmode` against that project.
3. Refresh: `unity command recompile --project-path ...` then `recompile_status` until ready. Diagnose only the new `Logs/Editor.log` window.
4. Tests: `infinity_tests_start` / official test command with XML export. Do not invent a second runner.
5. Capture: `infinity_capture_start` over the existing RG session. Official `unity screenshot` is forbidden for beauty/TAA/SR/window acceptance.
6. Example asset migrations take an explicit manifest, write a fresh backup, then exact native delta + second-save + separate no-op receipts under `InfinityExample/intermediate/<task>/<run>/`.
7. Evidence for the verifier lives under `/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/normalization-<date>/`. Task intermediates are deleted at task close.
8. After a batch, the Grok 4.6 verifier worker reads candidate.json + evidence and writes `verdict.json`. It does not edit code.

## Initial preparation baseline (historical)

- Local official CLI: 1.0.0-beta.6 (`unity --version`).
- Example dependency added explicitly: com.unity.pipeline 0.6.0-exp.1. Only that dependency changed semantically; the package lock awaits actual Editor resolution.
- Official UPM tarball SHA-1: `9bb4172c603cda2626bacac4d4c64bdb8270a3e0` (verified against registry metadata).
- No running Editor found. The former Unity 6000.5.3f1 application and EditorInstance.json are absent. The user confirmed removal and a 6000.6.0f1 download in progress.
- Runtime rendering, tests and source assets were not changed by this preparation. No scripts have been deleted on an untested equivalence assumption.

## Single interaction contract

Use the official executable as the connection and transport. Pass `--project-path` explicitly for a connected Editor; select a development Player explicitly for runtime operations. Discover actual command schemas rather than assuming built-ins from another package version. No per-operation cold Editor launch or hidden UI fallback.

Project-specific commands belong in an Editor-only adapter assembly, isolated from the shipping Infinity Runtime. Share the same validation implementation rather than wrapping menu dispatch or duplicating a test/capture engine. Use typed explicit arguments, error propagation, a run identity, status and evidence directory. A queued operation is not a passed operation. Provide cancellation and drain for asynchronous suites.

Read-only queries return data without saving. Scene changes reject unsaved state rather than dismissing prompts or silently saving. Migrations take an explicit manifest path, perform fresh backup/native integrity checks and preserve no-op evidence. Save operations are not invoked during import/reload.

## Official screenshot is not normal-frame evidence

Verified source: `com.unity.pipeline 0.6.0-exp.1/Editor/Commands/ScreenshotCommand.cs`, method `RenderToPng`. It assigns `camera.targetTexture = rt`, calls `camera.Render()`, then reads pixels. Camera selection prefers Camera.main and otherwise the first camera. This violates InfinityRP's normal-frame and explicit camera-identity contracts.

Do not use `unity screenshot` or the package's `screenshot` command for beauty convergence, confidence, history, TAA/SR, or real-window acceptance. Retain the render-graph Transfer staging and Submit/readback retirement implementation. Real OS window imagery is separate evidence; any replacement window capture must prove target PID/window identity and noninterference before deleting current capture helpers. Linear intermediate buffers must never be labelled as actual encoded backbuffer readback.

## Operation ownership and retirement map

| Existing implementation | Destination / deletion gate |
|---|---|
| RefreshUnityEditor.sh / .ps1 | Official connected-Editor refresh; delete after actual compile/reload recovery evidence |
| CaptureSceneView.sh | Retire UI navigation and first-window fallback; actual window capture must identify its target |
| CaptureUnityWindow.sh / .ps1 and UnityWindowId.swift | Replace with the verified noninterfering window-capture capability; official screenshot is not equivalent |
| InfinityValidationMenus scene/play/refresh entrypoints | Official commands; delete redundant agent-facing control facade |
| Normal frame capture | Retired menu facade after live typed CLI capture/cancel/drain and raw-stage verification. Use `infinity_capture_start/status/cancel` over the existing session engine. |
| PostEffectValidationMenus / RenderFaultValidationMenus | Pending their scoped typed-command cutover; never an automatic CLI fallback. |
| ValidationTestRunner | Preserve real XML/result lifecycle until the chosen official test command proves equivalent targeting/export/cancellation; one accepted test runner only |
| FrameDebuggerCapture | Keep Editor-specific frame-tree exporter behind a typed command; official CLI transport does not replace frame-tree extraction |
| LightSchemaRetirement file dialog | Explicit manifest argument; keep native backup and no-op semantics |
| Completed one-off identity/tint/emission/schema tools | Archive original evidence and close all code references before retirement; pending full migration consumers must be handled explicitly |
| AssetBaseline / raw GPU and image analysis | Keep pure evidence algorithms; they are not Unity transport compatibility |
| PrepareMacPlayerBuild / BuildMacPlayer | Keep only native export/build responsibilities not supplied by the tested official flow; never create a second Editor |
| Native Metal display probe | Preserve actual display measurement; separate from CLI communication |

## Acceptance sequence

1. Confirm installed 6000.6.0f1 executable, project/PID and bridge resolution. Record dependency changes without assuming CoreRP version equivalence.
2. Query state and discover commands. Prove that queries cause zero source asset writes and wrong project selection fails clearly.
3. Run one batch refresh and the actual Infinity tests with readable XML and exact counts. Test asynchronous result polling and cancellation.
4. Run normal beauty/confidence capture after at least 120 successful frames; verify finite raw data, target identity, unchanged capture target/history behavior and zero outstanding staging. Inspect actual window image independently.
5. Obtain a nonempty target-camera Editor frame tree. Build/run a development Player using the same evidence contract; keep shipping Runtime independent of CLI service requirements.
6. Submit a complete candidate to Terra. Delete replaced scripts/facades only after equivalent behavior and consumer closure pass, then repeat affected checks. Remove outdated active documentation references; retain historical failure receipts.

Sources: [Unity CLI official integration reference](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/references/integration-advanced.md), [official UPM metadata](https://packages.unity.com/com.unity.pipeline). Installed package source takes precedence over assumptions about a newer release.

## Verified local connection repair

The original bridge failed with `No available ports in range 7800-7849`. A same-Editor HttpListener probe proved `127.0.0.1` succeeds and the wildcard `+` prefix fails with SocketException: requested address not valid. The upstream port probe catches every exception and therefore misreports this as port exhaustion.

The Example project now owns an embedded `Packages/com.unity.pipeline` based on official 0.6.0-exp.1. The sole code correction is `BasePipelineServer.AddLoopbackPrefixes`: `http://127.0.0.1:{port}/` instead of `http://+:{port}/`. The original PackageCache is untouched. Token, Origin and request-loopback guards remain intact; the socket itself is now loopback-only. The embedded package's Documentation~/infinity-local-fix.md records provenance and the supported numeric IPv4 endpoint. Do not discard the embedded package or blindly upgrade over this fix.

Actual checks: editor_status ready for InfinityExample/6000.6.0f1; read-only eval returns its Assets path and camera count; CLI recompile followed by recompile_status completed with no errors; subsequent eval succeeds after temporary probe source removal and domain reload. lsof shows only127.0.0.1:7800; unauthenticated status returns401. Evidence: sibling InfinityRP-Validation/cli-listener-fix-20260909. No tokens are archived.

Use the following commands from a shell with the official `unity` executable on PATH:

```sh
unity command editor_status --project-path /Volumes/DataDisk/Projects/Unity/InfinityExample --format json
unity command recompile --project-path /Volumes/DataDisk/Projects/Unity/InfinityExample --format json
unity command recompile_status --project-path /Volumes/DataDisk/Projects/Unity/InfinityExample --format json
```

For another checkout, replace the explicit project path. Poll completion rather than resubmitting recompile. During reload, a temporary disconnect is expected; only a subsequent ready state and successful command prove reconnection. No second Editor is needed. The embedded bridge lives in the outer Example project, so distributing InfinityRP alone does not distribute this development dependency.
