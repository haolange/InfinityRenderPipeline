# InfinityRP 0.5 approved remaining-acceptance closure

2026-10-23–24, original InfinityExample, Unity6000.6.0f1 / CoreRP17.6 / Shader Graph17.6 / AppleM3Max / macOSMetal.
Baseline main1307adc; verified production602e6499337fe702f7d20e3c7ce4f0147c45b185.
The final delivery commit adds acceptance records only. Historical stage reports remain the source for S0–S17;
they were not rerun. See acceptance-scope.md for the approved S1 change and all53 externalSDK skip reasons.

| Required closure gate | Actual result / identity |
|---|---|
|Production repair|FixS15 602e649: active decals recover once into a replacement RenderContext; actual RenderDBuffer + three projectors restored. Focused5/5 and related8/8; off/restore and second camera finite/0pending; independent source/frame approval before commit/push.|
|Unity compilation|Current production assemblies loaded in three normal cold Editors; no compile block, shader/capture error or script diagnostic in successful current runs. Final625 cases executed against actual Infinity runtime/tests; final idle assembly MVIDs retained. Focused repair compile0errors/0warnings and StaticCheck0/0 retained separately.|
|Final Infinity-only EditMode|625 discovered/started/finished,625Passed/0Failed/0Skipped, no cancellation/reload; framework+callbackcomplete; run47f9e6ec-e3e8-421c-85d1-9d2ef0c7d9fd, PID39066. Both XMLs retained.|
|S1 revised scope|Historical12 related +329 package tests and actual Camera Undo/Redo, Film→ACES, Debugger evidence traced; current625/625 closes approved current-package gate. Historical repaired wholeExample full was waived, never relabelled run.|
|External SDK|53 historical skips remain skipped/outsideInfinity; no unrelated rerun or falsePassed.|
|Persistent picture disposition|See picture-report.md: finite-plane horizon/disabled planetary ground and marker shadow controls explain reported bands/dot. No arbitrary large triangle/blotch remains visible in reviewed normal fixtures. No atmosphere algorithm fix or universal image claim.|
|Probe/Display transient|Three independent Editor PIDs34863/39066/42623: newRP Game prioraccepted0, necessary Reflection6face preproduction explicit; frame2/index1/resettrue → frame122/index121/resetfalse. All3pairs normal/finite, Presenttrue/nulltarget,121successful/2captured,8buffer+2native retired/0pending each. Historical purple/black root cause unknown, currently not reproduced.|
|Eight normal image comparisons|AreaLights, LocalLights, Decal, Translucent, Shadows, Cookies, ReflectionProbes, ScreenSpace allPass. Original16S16baseline files byte-identical; record=false, thresholds unchanged. CaptureCompleted/present/finite/0pending individually checked. No new baseline version needed.|
|Necessary performance|Seven loads incl restored Decal drawing, before1307adc vs602e; each120accepted warmup +3×300unique validCPU/GPU. SSR/SSGI separate. Exact inputs, everygroup/invalidcount, median/P95 and originaldiagnostics retained. Independent recalculation approved; see performance-report.md.|
|Restore|Spazon9rootsclean, emptyselection, options3/backgroundfalse and prior EditorBuildSettingsdirty state retained;427source files byte-exact. Play/compile/bake/FrameDebugger false, tests+regressioninactive/capturefinished/0pending. final-restoration.json.|
|Cleanup / final branch delivery|Recorded separately in cleanup-receipt.json and PLAN.md after stopping the owned Editor and retiring only whitelisted task outputs.|

Initial failures are retained separately from passes: original eight comparison had Shadows7Pass/1Fail, with frozen
Time.frameCount despite submissions. Real advancing Play retry passes the same baseline/tolerances; final eight
passes again. Early lazy pipeline disposal captures are invalid controls, not accepted images. Decal replacement
missing-producer failure has actual before/fixed frames and a production fix. Early100ms background-cadence timing
is diagnostic only. The first640pilot serialization exception has no surviving original exception receipt; this
provenance gap is explicitly recorded and excluded from pass counts in performance-report.md.

Performance is composite wholeEditor using a fixed1280×720High RenderTexture, not finalbackbufferPresent/uGUI,
isolatedpass, Player or causal speedup. Restored Decal adds real work. Repeated probeCPU offset was investigated
with reversecode and samecode observation-state controls; matched observationCPUdiff0.0356ms/GPUdiff0.0257ms.
No stable degradation attributable to the patch was identified; no specificOS mechanism or strict equivalence
is claimed. Baked probe input is an actual durable128²nativeBC6H cubemap, not APV/lightmap/bakeprocess timing.

Independent verifiers approved the repair, picture controls,625tests,8regressions,3cold pairs and performance
statistics/archives within these boundaries. Existing optional feature limits, D3D12/Vulkan, Player, HDR,
APV/lightmap performance and unspecified historical transient causes retain their honest status. They are not new
mandatory gates under this approved closure. There is no remaining reproduced blocking failure in this scope.
Acceptance is complete within the approved scope after final independent review and verified cleanup. Recommend
Ready for review when this evidence batch is present on the PR branch; final delivery checks local/remote SHA
equality. This does not automatically convert or merge PR11.

Evidence: final-acceptance-evidence.tar.gz + member manifest retain both testXMLs, regression reports/raws/graphs,
three cold raw pairs and source/setup scripts. final-eight-preview.jpg is the inspected normal-frame grid.
Performance, picture and decal archives are separate to keep original failure/normal/pixel/numeric/timing evidence
distinct. Old baselines remain in the existingS16 archive, with baseline-unchanged.json proving byteidentity.

Cleanup completed after owned PID42623 normal exit/lifetime confirmation: exact755 task files retired,
104,843,792 payload bytes /287,047,680 allocated bytes (273.75MiB allocated). Task run directory and owned fixture
root/meta are verified absent. All427 original source paths still exist with their original hashes. Earlier consumed
raw/receipt retirements have separate exact inventories; these figures are only the final remaining set, not a
fabricated grand total. Permanent closure evidence is about30.3MiB payload /37.5MiB allocated. No Library, DataStore,
user log, source scene/configuration or other-session Editor was cleaned or controlled. See cleanup-receipt.json.
