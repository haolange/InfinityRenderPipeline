# InfinityRP 0.5 staged local verification — 2026-10-23

Original InfinityExample, Unity 6000.6.0f1, CoreRP/Shader Graph 17.6, macOS Metal, Apple M3 Max. Work remains on local main and is delivered only to `origin/claude/infinityrp-0.5`. Remote main is not a push target. Original historical failures, repaired snapshots and cumulative main results are distinct evidence.

The final main executes **1826 EditMode cases: 1773 Passed / 0 Failed / 53 existing Skipped**. All cases are discovered, started and finished, including two reload/resume boundaries. Native framework XML contains1793 results;33 actual checkpointed Skipped objects account for the documented gap. `callbacksComplete=true`, `frameworkComplete=false`; this is not1826Passed. Final remaining-category tests45/45 pass. Those45verified leaves then have only29Category attribute lines retired across9files; a subsequent cold compile is0/0 and all9classes pass64/64, with the exact45identity subset retained. No full-suite or rendering rerun is claimed after that metadata-only retirement. The original sixteen accepted S16 baseline files are unchanged; eight normal-frame comparisons pass on repaired S16, S17 historical and final main. Baselines are not overwritten to conceal failure.

## Stage receipts

P/F/S means Passed/Failed/Skipped. Counts below are the final historical repaired-stage results unless explicitly labelled package-only. Cumulative main failures were retained at their checkpoint and resolved in later owned stages; see linked reports for exact original and main counts, source overlays, native/callback reconciliation and evidence paths.

| Stage | Historical compile / tests | Actual behavior and picture boundary | Fix commit |
|---|---|---|---|
| S0 | Offline checker and10 self-tests; no Editor/image claim | Checks live assets and source-bound historical receipts | `6b57678` |
| [S1](S1-report.md) | Actual scripts load; package329/329. Fixed historical full Example rerun is missing; original1529=1271P/205F/53S retained | Camera auto-add/Undo, Film→ACES normal frames and native Debugger accepted. Environment recovery documented. Current-main full success does not retroactively supply missing S1 full history | `88933dc` |
| [S2](S2-report.md) | Compiles;1544=1491P/0F/53S | Camera/Preview/Reflection, light gates and Mesh GPU update; user confirmed native Albedo menu | `e4096e4` |
| [S3](S3-report.md) | Compiles;1549=1496P/0F/53S | Resource errors and feature state/recovery; existing resource bindings preserved | `2157d21` |
| [S4](S4-report.md) | Compiles;1556=1503P/0F/53S | Presets and Undo; cloud shell GPU3/3 and matched cloud appearance | `279c336` |
| [S5](S5-report.md) | Compiles;1567=1514P/0F/53S | SSR numeric, normal history/debug/dispatch; passive readback retained. No isolated GPU timing acceptance | `ecd3c7b`, cleanup `98012a1` |
| [S6](S6-report.md) | Compiles;1604=1551P/0F/53S | Native Type/SpotShape/baking menus and Undo/Redo, Disk circular handle; Rectangle native carrier retains authored Disk/Disc bake. Spot baked/realtime attenuation examined; unexplained original residuals retained | `5b34e14` |
| [S7](S7-report.md) | Compiles;1617=1564P/0F/53S | Metal loop state, tile projection, Forward/Deferred, multicamera and matched volumetric consumers accepted; exact compiler/driver lowering cause unproved | `f4bbc3a` |
| [S8](S8-report.md) | Compiles;1621=1568P/0F/53S | Production shape/energy GPU oracle, normal shape/weights controls and startup repair; RG16F residual investigation remains separate | `f033d5a` |
| [S9](S9-report.md) | Compiles;1687=1634P/0F/53S | Actual Metal shadow copy/casters/filter/cache failures fixed; bounded light-space matrix alias dependency explicitly replayed, not later GI features | `83d72d5` |
| [S10](S10-report.md) | Compiles;1689=1636P/0F/53S | Sun/local masks,24-light gate, Forward and normal contact controls | `05a1bf6` |
| [S11](S11-report.md) | Compiles;1705=1652P/0F/53S | Cookie/IES transactions, projections, source/reimport and normal consumer controls; valid whole-frame CPU/GPU3×300 before/after samples | `8f4b862` |
| [S12](S12-report.md) | Compiles;1720=1667P/0F/53S | Three-way shadow-matrix evidence plus actual Lightmap/Shadowmask/Terrain/Probes/LPPV/APV L1/L2 and sky bakes; scoped acceptance, no valid isolated GPU timing | `c35b019` |
| [S13](S13-report.md) | Compiles;1737=1684P/0F/53S | Probe atlas/filter/cache/priority/blending/Forward/multicamera controls; remaining picture defects explicitly retained | `64456f1` |
| [S14](S14-report.md) | Compiles;1762=1709P/0F/53S | SSGI red bounce/offscreen fallback/orbit/disocclusion/history and native dispatch; numeric residual can persist about14frames | `08b529d` |
| [S15](S15-report.md) | Compiles;1793=1740P/0F/53S | Actual Shader Graph generation, editor tools, converters, inspectors/debug views and real pass evidence; transient purple probe/black output and final Display differences unclosed | `1977445` |
| [S16](S16-report.md) | Compiles;1822=1769P/0F/53S | Eight normal baselines, real light-change Fail/restored Pass, missing/corrupt/commit-write/cancel/reload/dirty-recovery controls; original NaN and invalid request failures retained | `e01e082` |
| [S17](S17-report.md) | Compiles;1826=1773P/0F/53S |57actual moved GUID bindings,15default components, strict eight scene references; same S16 baseline8/8. Six seam paths and two creator persistence fixes;2fresh disk-import tests | Commit containing this report |

The S12 matrix report distinguishes source evidence and actual frames: the caster used Matrix_ViewFlipYProj while the old shadow code replaced only Matrix_ViewProj. Actual matched pre-0.5 c92be77 and S11 fixed-light local atlases change201texels when the observer moves; S12 writes3256texels and stays byte-identical between those poses. The defect predates0.5. Cascades fit the camera, so they are not a cross-version pixel-equality oracle. Six GPU reads across two cameras/three frames verify both aliases restore to camera matrices. The S9 replay explicitly carries only the minimal matrix alias prerequisite.

## Unresolved issues and Ready judgment

**PR #11 should remain Draft.** Final tests and eight fixed-input comparisons pass, but the original mandatory acceptance scope is not wholly closed:

- Known sky black bands, large dark regions/triangles and fixture blotches retain incomplete root causes. Same-baseline success proves consistency, not picture correctness.
- Historical first purple probe output, transient black captures, and some matched final Display residuals remain unexplained. Isolated producer/numeric controls are accepted only within their measured boundaries.
- S11 has valid fixed-input whole-frame CPU/GPU measurements: three groups of300valid frames before/after, with median/P95 recorded. This is not a causal cookie-pass speedup. Several other required major paths still lack valid GPU samples or comparable measurements; their Perf cells remain Open. No missing300-frame groups or GPU medians are fabricated.
- S1 fixed historical full Example acceptance is missing; its package329/329 and final-main full success are different evidence.
- Other APIs/platforms, Player builds and HDR were not exercised. Their status remains unchanged; no Player was built.

S1–S9 reports were generated locally but ignored by Git. This delivery explicitly promotes their nine reports and the member-hash-verified EarlyStages archive; it does not pretend they were tracked earlier. S6/S7 receipts retain original failures, final tests, exact native omissions, UI/schema/source evidence and key images while retiring consumed raw data. The active APFS/ExFAT Library arrangement is functioning cache state and is not disposable task scratch.

Final main restores the original clean Spazon scene,9roots, and normal-exits only its own Editor. Temporary Enter Play Mode options are restored to3 and background execution tofalse through public setters, then checked after native exit flush. ProjectSettings returns to the initial exact bytes. The initial manifest lists427inputs and329declaredbackups; all329declared backups existed and matched before retirement;98were not backed up by design. Before retirement,326of329backed-up inputs matched current source bytes; a separate final rehash confirms all427initial inputs remain present,424byte-identical. Remaining differences are the explicitly audited GS resource schema and Unity native EditorSettings/TimeManager format conversion; original bytes are retained in the small permanent source audit. No stale whole-file restore, user-source save or other Editor operation is performed.

[Final cleanup receipt](S17-closure/cleanup.json) records actual retired paths/counts/bytes and final source checks after all producers and consumers finish. [S17 manifest](S17-closure/manifest.json) reconstructs24actual frame readbacks losslessly from S16 baseline members and small deltas. [EarlyStages manifest](EarlyStages-closure/manifest.json) indexes1922explicit members, including54exact skip capsules;10full capsules include each missing33original Skipped objects. These are evidence artifacts, not additional live cache copies.

Cleanup is complete: the original Example intermediate directory and both explicit task asset/baseline roots are absent. The known asset and baseline payloads retired are5,051,032 and16,387,358bytes. Exact final aggregate disk-reclamation counters were lost when a recorder tried to reread12intentionally retired task bake files; the receipt labels earlier du observations and does not substitute them for an exact final total. The former18-item protected list comprised6user/settings sources plus12task bake/probe files, all protected until final acceptance. All427initial user-source paths were independently rechecked after cleanup.

The final marker-only recheck is also cleaned:141files including sidecars,1,068,805payload bytes and37,224,448allocated bytes retired after own53193normal exit. All427source hashes still match the recorded final state, and the entire Example intermediate directory is absent. Startup authentication logs are omitted from permanent archives; only their SHA/size/reason receipts remain.
