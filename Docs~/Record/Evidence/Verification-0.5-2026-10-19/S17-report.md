# S17 local validation receipt — 2026-10-23

Original endpoint: `eab18e5ff74b207e52186e5bfa3290fdbebb6efa`. Accepted predecessor main: `e01e08222c6018ed1d3b6b84fcffea4fdb7a7760`. Unity 6000.6.0f1 / CoreRP 17.6 / Shader Graph 17.6 / macOS Metal / Apple M3 Max, original InfinityExample. Fix S17 is the commit containing this report; no branch, Player build, force push or remote-main update is involved.

## Original failures and minimal repairs

The original S17 replay carries 212 exact accepted predecessor source files. Actual cold compilation succeeds. `PipelineSeamTests` executes 2 cases: 1 Passed, 1 Failed because it still reads the pre-move `Pass/DepthPass.cs`. The fix updates exactly six canonical path strings: Geometry Depth/GBuffer/Motion, Lighting Forward, Shadow Cascade/Local. Every file-existence and forbidden-token assertion remains active; there is no recursive search or fallback.

The original folder move preserves all 57 script/meta pairs byte-for-byte. Sixteen new folder metas and removal of `Runtime/PostProcess.meta` are separate events; this report does not claim every folder GUID survived. The original range also updates other test paths and docs, so it is not described as literally an entirely unmodified `git mv` diff.

Strict fixture reference auditing exposes an inherited creator defect: LocalLights and Translucent add Volume components only to the in-memory list. After reload, both profiles contain zero components. Original positive tests fail 2/2. Each generator now explicitly adds its authored components to the Profile asset, matching the existing shared fixture pattern. Feature enable values and parameters are unchanged.

An initial `IsSubAsset` assertion also fails after the fix. A separate tiny native control explicitly adds a persistent child and still returns false for IsSubAsset/IsMainAsset, both with and without hide flags. The corrected regression copies saved asset bytes without its meta to a fresh path/GUID, then verifies component count, types, parameters, persistent state, GUID/local ID and actual LoadAllAssetsAtPath membership. This is a stronger disk-persistence oracle. Prior failures remain in evidence; a compile-error attempt that ran an old assembly is not candidate acceptance. The current finally deletes only its own folder and destroys only explicitly owned, still nonpersistent components.

Two existing task-owned Profile assets are explicitly repaired after 11,356 bytes of current-byte backup. Local retains Fog/Cloud inactive, enable false; Translucent retains Fog density .04, height 8, distance 64, enable false. Parent GUID and local ID 11400000 remain unchanged. Second save and separate no-op are byte-identical. Other 98 fixture files, including all metas/scenes/materials, are unchanged. No user Profile is migrated on load.

## Actual execution and identities

| Candidate | Compile | Focused/category | Full Example EditMode | Normal-frame comparison |
|---|---|---|---|---|
| Original S17 + accepted predecessors, PID 35395 | 0 errors / 0 warnings | Seam 1P / 1F | Original failure stops progression | Not accepted |
| Main candidate, PID 38517 | 0 / 0 | Fixture save 8/8; category 47/47 | 1826 = 1773P / 0F / 53Skipped | 8/8 Pass |
| Repaired historical S17 + 213 exact sources, PID 44209 | 0 / 0 | Category 47/47 | 1826 = 1773P / 0F / 53Skipped | 8/8 Pass |
| Final main, PID 45993 | 0 / 0 | Remaining category 45/45 after retiring only the two newly verified leaves | 1826 = 1773P / 0F / 53Skipped | 8/8 Pass |

Each full run discovers, starts and finishes all 1826 cases, with two reload/resume boundaries. Native XML contains 1793 results; 33 actual nonempty checkpointed Skipped objects precisely reconstruct the callbacks. `callbacksComplete=true`, `frameworkComplete=false` is retained. This is not 1826 Passed. Compared with S16, no old UID disappears: only the old seam case changes Fail→Pass and two disk-import cases are added Passed. Repaired historical and final main stable UID/fullname/result/label/site sets match the candidate exactly. Independent verifier reviewed these reconciliations.

Native Unity resolves all 57 moved GUIDs, 14 package Volume classes and 15 default Profile components including Core APV. All eight owned scenes are clean and have zero Missing Script; expected Profile counts are Local 2, Translucent 1, Shadows 1 and ScreenSpace 3. Scenes without Volume retain none. Original GraphLighting setup is restored after each probe.

## Same accepted baseline and image limits

The original 16 S16 baseline files remain byte-identical in all runs. No baseline is re-recorded. Each capture has 91 advancing successful frames, one normal Game-frame readback, finite data, GPU/native retirement 1/1 and outstanding 0. No Camera.Render substitution is used.

Final main five cases are byte-exact: LocalLights, Decal, Shadows, Cookies and ReflectionProbes. AreaLights RMSE is 1.168405106e-6, Translucent 2.7858609997e-7 and ScreenSpace 8.8705706958e-7. All eight have zero outliers. Historical six are byte-exact; Area RMSE 2.342434734e-6, ScreenSpace 7.913511871e-7. Small residuals are reported without assigning an unsupported cause.

Actual normal-frame images were inspected. Existing black bands/dark floor regions remain Open; accepted baseline consistency does not make those defects correct. S12 baked GI/APV/LPPV and S15 generated Shader Graph evidence remain separate from these eight defaults. Zero or unavailable GPU counters do not meet performance acceptance. S11 separately has valid whole-frame CPU/GPU three-by-300 before/after samples; those measurements do not prove isolated cookie-pass cost or satisfy unmeasured paths. Player, HDR, D3D12 and Vulkan remain unverified. PR #11 must remain Draft while these required image/performance boundaries are unresolved.

## Evidence and delivery correction

[S17 closure manifest](S17-closure/manifest.json) indexes original failures, exact source identities, tests, native references, frame reconstruction and restoration. [Early-stage closure manifest](EarlyStages-closure/manifest.json) promotes S1–S9 reports and narrowly indexed evidence that existed locally but had been excluded by the Documentation~ ignore rule. It preserves S6/S7 original/final tests, actual missing skipped objects, native UI, source/schema/restore receipts and key images; consumed raw/log files are retired by exact SHA. The corrected early archive has 1922 members and 54 skip capsules; its ten full-run capsules retain 42 actual nonempty Skipped objects, including each run's 33 native omissions. The first empty-capsule candidate was rejected and superseded before any original was deleted.

Final cleanup and asset/settings preservation results are recorded only after they occur in the closure cleanup receipt. No Library, DataStore, user log, real database or other session's Editor is a cleanup target.

Final cleanup completed after all consumers: the task asset root (417 real files / 5,051,032 payload bytes), named live baseline directory (16 files / 16,387,358 payload bytes) and entire verification intermediate directory are absent. Baselines and fixture sources remain reconstructible in permanent archives. Rehashing all 427 initial source inputs confirms all exist, 424 byte-identical and only the three documented GS/native-format changes. The former protected18 set contained6user/settings sources and12task bake/probe artifacts; the latter were intentionally retired after acceptance. A post-delete recorder initially misclassified those task artifacts, so exact final aggregate byte counters were not persisted. The cleanup receipt records this limitation and earlier du observations separately; it does not invent reclaimed-byte totals.

Final verification-marker closure: the remaining29Category attributes across9test files correspond to45actually Passed leaves in the archived final-category/full runs. Only those attribute lines were removed; all bodies, arguments and assertions remain byte-identical. Own cold Editor53193 compiles0/0 and re-executes all9classes:64/64Passed, native/callback complete, preserving every old45exact fullname. The first RenderCapture launch was refused during UTF post-test scene restoration; the later clean retry passed without clearing dirty flags. No full-suite, rendering or bake rerun is claimed after this metadata-only retirement. Spazon9clean/options3/backgroundfalse/captureIdle0/testIdle restored; normal exit observed. [Marker retirement receipt](S17-closure/marker-retirement.json) includes exact source hashes, identities and75member archive.

Final marker-round cleanup is verified:141files including sidecars,1,068,805payload bytes/37,224,448allocated bytes removed; entire intermediate absent and427source hashes unchanged. The sanitized S17 archive is1,391,176bytes and the marker archive46,103bytes. Five startup logs were omitted before any commit/push; source SHA/size/reason receipts preserve identity without authentication payloads. Independent review revalidated all member hashes,24lossless frame reconstructions and64native/callback results.
