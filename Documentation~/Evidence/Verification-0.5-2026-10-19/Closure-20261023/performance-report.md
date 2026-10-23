# Bounded Editor performance measurements

Unity6000.6.0f1 / AppleM3Max / macOS Metal. Production comparison:1307adc versus602e649. Local main was retained; only two production files and the repair-only test pair were temporarily overlaid while the owned Editor was closed. No user assets were rolled back.

Each required run has120 accepted Game warmup submissions and900 unique positive finite FrameTimingManager samples, grouped3×300. Inputs use1280×720, actual camera High overrides, fixed camera/light/exposure, renderScale1/SRoff, frozen marker. The normal Game camera submits to an owned memory RenderTexture (B8G8R8A8_SRGB/D32_SFloat_S8_UInt); this excludes final backbuffer Present and overlay UI. It is whole-Editor timing including the observer, UI and external scheduling, not isolated pass cost or Player performance.

| Workload | Before CPU median / P95 ms | After CPU median / P95 ms | Before GPU median / P95 ms | After GPU median / P95 ms |
|---|---:|---:|---:|---:|
|lights|8.315 / 9.139|8.320 / 9.431|10.054 / 11.875|6.086 / 11.526|
|shadows|8.311 / 9.307|8.307 / 9.349|8.571 / 17.979|7.150 / 14.721|
|ssr|8.325 / 8.668|8.317 / 9.399|9.515 / 20.747|9.693 / 17.287|
|ssgi|8.307 / 9.340|8.306 / 9.393|10.128 / 19.574|10.271 / 17.610|
|fog|8.321 / 8.913|8.308 / 9.402|10.431 / 20.399|6.523 / 15.557|
|probes|9.517 / 10.162|10.646 / 12.020|17.000 / 20.058|18.193 / 21.729|
|decal-replace|8.315 / 8.824|8.319 / 9.374|5.920 / 10.232|6.920 / 9.166|

Every original group, auxiliary CPU column, invalid count and all3×300 values remain in performance-summary.json / performance-groups.csv and the lossless archive. The early after-* runs with100ms CPU frame cadence are diagnostic only and not the code comparison; after-cold reruns match the foreground cold-start cadence. Before first-six input snapshots lack explicit focus fields; that absence is recorded rather than filled in later.

Decal replacement has unequal work by design: before has noDBuffer producer; after restores it and three actual projectors. Its observed full-frame change includes restored drawing and is not an equal-image speed comparison. For the other loads, scene/target/effectiveVolume/camera/RP identities match after normalizing temporary object IDs. No improvement percentage or isolated-pass saving is claimed.

Probes includes actual128² native BakedLeft cubemap input plus six-face EveryFrame sphere updates, filtering and sampling. It does not measure APV/lightmap/bake-process cost. An initial repeatable CPU offset required investigation. The reverse same-baseline control, then the identical baseline after the same native read-only window observation, gives:

| Probes observation | CPU median ms | Main thread median ms | Render thread median ms | GPU median / P95 ms |
|---|---:|---:|---:|---:|
|before-probes-v1|9.5169|4.6391|7.8343|17.0001 / 20.0585|
|after-cold-probes-v1|10.6458|5.0326|8.4837|18.1928 / 21.7286|
|after-cold-probes-repeat-v1|10.5361|4.9965|8.4500|17.3824 / 21.3842|
|before-reverse-probes-v1|9.4693|4.6592|7.7646|16.4417 / 19.7640|
|before-reverse-observed-probes-v1|10.5005|5.0082|8.3776|17.3567 / 21.3998|

The same-code observation-state control reproduces the earlier roughly1ms CPU shift. Matching that observation state brings baseline and after repeat close (CPU difference0.0356ms/GPU0.0257ms). No stable degradation attributable to the production patch was identified. This does not establish a particular OS mechanism or strict performance equivalence. Alloriginal, repeat and reverse groups are retained; none was replaced with its best sample. Independent verifier recalculated every median/P95, checked timestamps/fences/accepted submissions and producer/input identities.

Current submitted-sample completeness is accepted within these boundaries. Small SSR/SSGI median shifts and broad GPU group scatter do not establish a code regression; lower medians elsewhere do not establish acceleration. No other session was closed or controlled to improve results.

The initial pilot-lights-640-v1 failed result persistence because Unity Color serialization recursed into itself. No measurement.json was generated; the original error-status file was subsequently overwritten. This failure is known from earlier tool/dialog output, not an original exception receipt in the 87-member archive. It is excluded from passed measurements. perf-failure-state.json and perf-failure-identity.json are state/identity receipts, not the missing exception.
