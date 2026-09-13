# InfinityRP 0.4.0 — Normalization ledger

Approved: 2026-09-12. Active delivery is this ledger only. Historical N00–N15 receipts are archived verbatim in [Docs/History/PLAN-2026-09-N00-N15.md](Docs/History/PLAN-2026-09-N00-N15.md). Old T/S/R IDs remain historical evidence.

Status: pending → implementing → verifying → passed. Failed candidates return to fixing. A Grok 4.6 verifier PASS closes a batch. External machine/display gates stay `UNVERIFIED (external condition)`.

## Ownership

- Main agent: orchestrator, planner, code generator, UI/UX visual inspection.
- Grok 4.6: search, code review, batch verification. If Grok 4.6 is unavailable, search-only falls back to GPT-5.6 Luna; verification never silently downgrades.
- Execution: code on `normalize/bX-<topic>` branches; compile, EditMode, CLI capture, and Example-project migrations run on the Mac worker (`cursor worker start`) against InfinityExample. No second Editor. No `-batchmode` against a live project.

## Remap from N00–N15

| Historical | Status at archive | Normalization owner | Still a rendering-quality follow-up |
|---|---|---|---|
| N00–N05 | passed (scoped) | archived | no |
| N06 route / layers / light schema | implementing | B4 (U40) + existing receipts | rendered route parity remains a frame gate |
| N07 lights / triangular artifacts | pending | not this wave | yes |
| N08 atmosphere / black bands | pending | not this wave | yes |
| N09 SSR temporal | pending | not this wave | yes |
| N10 SSS recomposition | pending | B3 U32 quality authority only | SSS image quality remains N10 |
| N11 fog / translucent / ParticleSystem | pending | B5 U50 UI; ParticleSystem in U41 | remaining translucent quality remains N11 |
| N12 SR dimensions | pending | B1 U11 `renderScale` + per-camera descriptor | TAA/Preview continuity still a frame gate |
| N13 stress / performance | pending | not this wave | yes |
| N14 cleanup | pending | B8 U80 / U23 | leftover machine-path retirement |
| N15 final package | pending | B8 U82 for this wave only | full N15 still requires N07–N13 |

## Active tasks

| Task | Batch | Dependencies | Status | Gate |
|---|---|---|---|---|
| U00 | B0 | — | passed | Ledger, archive, AGENTS/DESIGN/runbook agree with this wave |
| U01 | B0 | — | passed | CoreRP 17.6 research pack in Docs/History |
| U02 | B0 | U00 | passed | Baseline + Spazon Game frames + LUT hash; SceneView capture still blocked by historyReset |
| U10 | B1 | U00,U01 | passed | GlobalSettings generic order + settingsList ownership; ResolveDefaultVolumeProfile throws when empty (no Resources.Load fallback) |
| U11 | B1 | U10 | passed | RP Asset schema + renderScale; Example second-save / no-op byte-identical |
| U12 | B1 | U10 | passed | Dispose restores graphics state; Infinity→Builtin→Infinity midPipe empty |
| U13 | B1 | U10,U11 | passed | Editor/ShaderStripper IPreprocess* ; Player preprocess variant receipt (Mono) |
| U20 | B2 | U10 | passed | Additional camera data + InfinityCameraEditor |
| U21 | B2 | U10 | passed | Additional light data GUID c8dcc11eda3e9404e884a2332f3a4890; GameObject/Light Undo auto-add |
| U22 | B2 | U20 | passed | SceneView/Preview Volume rule in ResolveVolumeSelection |
| U23 | B2 | U20,U21 | passed | RenderGraphView / PipelineAssetConfiguration / FoliagePipeline retired |
| U30 | B3 | U10 | passed | Pipeline filter, taxonomy, IsActive, enable, tonemap mode |
| U31 | B3 | U30 | passed | IsActive gating; VolumeHasOverrides deleted |
| U32 | B3 | U30 | passed | SSS quality = Volume numSamples only |
| U33 | B3 | U30 | passed | Dedicated VolumeComponentEditors for shipped components |
| U34 | B3 | U10,U30 | passed | Validate & Complete + confirmed Reset; EnsureAsset deleted |
| U35 | B3 | U11,U30 | passed | Debugger panels; DebugView reads runtime settings |
| U40 | B4 | U30 | passed | LitGUI blocks + YAML property rename; migrate menu retired in U80 |
| U41 | B4 | U11 | passed | InfinityUnlit + T2 SRPDefaultUnlit/untagged |
| U42 | B4 | U11 | passed | DiffusionProfile index / unreferenced warning |
| U50 | B5 | U20,U41 | passed | UIOverlay + TMP Validation_UI Game capture; Player/HDR UI TODO(UNVERIFIED) |
| U60 | B6 | U02 | passed | Custom motion retained; native A/B flag only. See Docs/History/U60-U61-Experiment.md |
| U61 | B6 | U02 | passed | Two-pass TAA retained; fusion flag reserved. See Docs/History/U60-U61-Experiment.md |
| U70 | B7 | U35 | passed | URT VisibilityRTAO + accel; Metal Compute frame captured; Player/D3D12 TODO(UNVERIFIED) |
| U80 | B8 | U23,U34 | passed | One-off Migrate/Upgrade menus stripped; Window/Infinity validation + CLI remain |
| U81 | B8 | U10 | passed | package 0.4.0 / 6000.6 / 17.6; CHANGELOG + Documentation~ synced |
| U82 | B8 | U00–U81 | passed | Editor vs U02 match; Player beauty/UI TODO(UNVERIFIED) (Burst fd rebuild + Metal MRT) |

## Batch verification

Verifier = Grok 4.6 worker on the Mac. Inputs: branch SHA, candidate.json, evidence run dir. The verifier does not edit code.

Common gates: fresh recompile with no new InfinityRP errors; EditMode XML 0 failed / 0 skipped; three normal-frame captures vs U02 (static ROI < 0.5% at 8/255 unless the task declared an intentional delta); finite raw GPU; asset backup / exact delta / second-save / no-op when Example assets change; Undo / multi-select / prefab / reload; no legacy/shim/`FormerlySerializedAs`; docs updated in the same PR.

FAIL returns to the main agent on the same branch. PR is ready only after PASS.

## Non-goals of this wave

No HDRP RenderGraph port. No MSAA, DOF, XR, dynamic resolution, or APV. No atmosphere or light-unit retuning. No compatibility shims. Protected-frame changes require declared before/after images.

## Evidence root

`<Unity project root>/intermediate/<task-id>/<run-id>/` for task intermediates (deleted at task close). Durable receipts: `/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation/normalization-<date>/`.
