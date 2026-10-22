# S12 verification report — 2026-10-22

Candidate: local main based on 8f4b86280b684f327a48467d2334f8bb8f6e4b3a; source identity in main-final-source-identity-v2.json. Original stage: ccc04407a3486054d27eebee5df00d6d592af587. Historical replay with accepted earlier repairs and scoped S12 delta: historical-s12-final-source-identity-v2.json (135 files). Unity 6000.6.0f1, CoreRP/ShaderGraph 17.6, OSXEditor Metal, Apple M3 Max. Original InfinityExample only; no Player or other Editor control.

## Compilation and tests

Original historical full: 1711 discovered/executed, 1649 Passed / 9 Failed / 53 Skipped. Original category:112 =111P/1F. Original missing APV default and APV activity/keyword query failures preserved. Repaired historical: category121/121; full1720 =1667P/0F/53Skipped. New nine tests and nine original failure resolutions independently reconciled; no removed UID. Final main full-v5:1762 discovered/started/finished =1705P/4F/53Skipped;3 domain reloads resumed, callbacks complete. Native XML omits33 real skipped tests; callback XML/checkpoint reconciles them. Results' UID/fullname/result/label/site multiset exactly equals full-v4. No all-main-tests pass claimed.

The four failures remain owned by later stages: ReflectionProbe record parser (S13), MaterialUpgrader exact color rounding and RenderGraphViewer expected exception (S15), retired DepthPass source path (S17). No tests skipped/disabled/deleted to clear S12. Tags removed only from executed BakedGI, BakingSky readback and ShaderStripping contracts. Actual Player stripping remains unverified.

Applicable offline gate: StaticCheck 0 errors/0 warnings; Terrain DXC126 compiled/0failed; diff whitespace clean. Unity cold imports/actual runtime frames are separate evidence.

## Original failures and fixes

- Missing Terrain basemap dependencies caused native bake exclusion (instance with no materials). BaseMapShader/BaseMapGenShader and generated albedo/Meta path now support the native bake and retain raster/shadow paths.
- Original Mesh registration ran before shared probe interpolation and performed single-instance sampling. Newly enabled instances now join the world lists, use the shared batch, then complete dirty registration/upload. Static samples also invalidate on tetrahedralizationCompleted, after the new topology exists; stale error reproduced before the completion hook.
- GBuffer numeric tests synchronously waited on all process GPU requests and stalled Metal full tests. Same assertions now yield their own asynchronous readbacks with callback lifetime and timeout retirement. Eight GPU cases include the three ValueSource silhouette cases; three additional screen-space cases bring the focused run to eleven.
- BakingSky scene-name ownership aliases reproduced. Outputs now use saved scene GUID, reject foreign/occupied outputs, save only owned assets, validate all six finite RGBAHalf faces and preserve object identity on resize. Original resize GUID loss was not reproduced; retention controls pass. Actual native startup rejection and completed bake are distinct receipts.
- Shader stripper accepts native/system passes and uses pipeline global APV keywords. Tests reflect active CoreRP sampling options rather than using them as a feature switch.
- Default Profile is an explicit current-schema save plus APV component completion, not append-only bytes. profile-native-schema-prior-audit.json binds original ad6e78f7… to fe022b4c… and enumerates34 native deltas while preserving IDs/references and prior effective components; cold load and second-save/no-op evidence pass.

## Rendering evidence

- Three matched original versions: pre0.5 c92be77, S11 endpoint, S12 endpoint. Actual custom/native caster draws are identified separately. Fixed-light local atlas changes201 texels when the observer moves on pre0.5/S11; S12 has3256 written texels and byte-identical atlas between poses. The defect predates0.5. Cascades fit camera and are not a cross-version pixel equality oracle. Six post-shadow GPU reads across two cameras/three distinct frames verify both matrix aliases restore to the camera matrices.
- Actual native lightmap/mixed-light mask bake,18 probes; matched eight jitter phases prove both modes' far receiving-floor equality and removal of baked shadow darkening at strength0. Native and explicit Mesh caster controls separately prove Shadowmask filtering vs DistanceShadowmask. Names containing Custom are not Mesh draw proof; initial mislabeled controls explicitly retracted.
- Terrain receives native baked lighting. Main and repaired historical near/far actual normal frames satisfy world/color oracle below.01; runtime holes remove all interior samples. Main actual Terrain on/off only changes casting: cascade index1 identical raster/culling matrices/splits/spheres/bias/camera with7385 depth texels changed. Cascades2/3 refit and are excluded. No new native FD draw identification claim. Meta hole extraction remains unverified.
- Actual atmosphere vs black-environment native bakes on identical white cube/plane/sun: eight matched jitter phases,141–165 shadow-floor pixels, blue fill exceeds black in every RGB sample. Repaired historical real rebake and eight frames independently confirm blue fill. Saved128² six-face array public sample preview has +Y blue sky,−Y near black, horizontal horizons; numeric centers are64² sample center2x2Mean. Asymmetric copy/orientation tests remain separate.
- Dynamic Mesh normal-frame left/right front surfaces:1225 pixels each, exact public SH.Evaluate converted half. Actual rebake/static and topology-changed CPU/GPU slot reads agree with public interpolation, and task positions restore exactly with no pending readback.
- LPPV1088 receiver pixels show spatial gradient (historical1087 distinct RGB); BlendProbes constant and Off zero. Native separate unbatched draw binds3D SH resources. APV real1107-probe bake, L1/L2/Off normal frames and runtime keyword/texture reads; Off legacy lighting disabled. Two-camera enable gating passes after one pending-operation boundary, not immediate physical unload or per-camera GPU constant proof.
- Final original Spazon main frames3875/3876/3877:27 successful frames,3captures,12 retired,0outstanding; all actual formats finite. Fresh log delta125bytes contains Curl error35 / UnityTls7 certificate validation failure, classified as a network TLS diagnostic; no Infinity rendering/compile/shader exception occurs in that bounded span. Original scene bytes/9roots/selection/quality/batching restore before normal Editor exit; protected settings/GlobalSettings hashes match after exit.

## Limits, storage and delivery

GPU timing:60warm-up+120attempts yielded0 valid GPU samples; Perf stays Open, no median/P95 or causal speedup. D3D12/Vulkan/Player/HDR unverified. Bounded fixture acceptance does not clear unrelated ThinGBuffer scene issues or prove full GI equivalence.

456 completed capture payloads retired after independent consumption:164804608logical bytes,184549376allocated bytes (retired manifest holds hashes); key images/numeric/metadata retained. Closed old main log41712585bytes compressed losslessly to470049bytes. Remaining bake/APV/source fixture assets have final complete-HEAD/regression consumers in S13–S17 and remain task-owned until final cleanup; no duplicate large backups or DataStore movement. Main final results, candidate manifest and fresh key capture remain for independent review, then compact/retire.

Fix S12 commit/push: pending final independent delivery manifest review. PR11 stays Draft while S13–S17 and final complete-HEAD/image-regression gates remain open.

Durable closure: `S12-closure/manifest.json`; original task-relative paths in compressed receipts identify the producer. Raw capture payloads are retired with hashes after independent consumption.
