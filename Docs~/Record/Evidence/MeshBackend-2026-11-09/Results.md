# Mesh backend profiling and optimization — 2026-11-09/10

Candidate: `7c50725` plus the pre-existing uncommitted owner-layout changes and this batch. Unity 6000.6.0f1, Apple M3 Max, macOS Metal; Game view 1666 × 1754. The original project's Editor was not used. The isolated project was `/Volumes/InfinityTask/Example`, backed by a task-owned APFS sparse image under `InfinityExample/intermediate/mesh-backend-optimization/apfs-run/`; it had its own Library. The bridge was bound to 127.0.0.1. The project path was checked against the running Editor PID and `Application.consoleLogPath`. Screenshots are actual Editor windows, not the CLI screenshot operation.

## Result and ownership

- GPU in-pass labels identify view, pass, visibility phase and draw-list index, plus cascade slice. CPU markers distinguish Schedule, Complete, Upload and Draw with view kind, slice and list metadata; the worker retains its Culling, Gather, Sort and Merge job names. [CPU Timeline](baseline-cpu-timeline.png); [labelled frame](b-frame-gpu-verified.json).
- The five lists in the original last compaction group were Depth Recovery, GBuffer, Motion, Forward and TranslucentDepth, each formerly `ClearCounts`, `Compact`, `BuildIndirectArgs` (15 dispatches). Empty Forward and TranslucentDepth lists are now eliminated for this scene; each remaining nonempty list uses `InitializeIndirectArgs` then `Compact`. This saves work rather than changing the number of raster draw commands for nonempty lists.
- On the matched scene-frame examples the ten original lists and 30 compaction dispatches ([B](b-frame-gpu-verified.json)) became four lists and 12 dispatches after empty-list elimination ([C](c-frame-gpu.json)), then four lists and eight dispatches ([E](e-frame-gpu.json)). The four empty cascade lists were removed in that scene. With 10,000 shadow casters, [a separate final capture](final-shadow-frame.json) shows all four cascade slices plus four main lists, each with two dispatches. A native Renderer on an otherwise empty mesh pass remained visible in [the window](c-native-forward.png).
- CPU Sort/Merge fusion was tried and reverted: 100-frame Complete p50/p95 changed from 0.276/0.844 to 0.327/0.773 ms and schedule p50/p95 from 0.059/0.088 to 0.057/0.089 ms; no consistent benefit beyond noise was demonstrated. Existing jobs and ordering remain.
- GPU initialization writes all indirect fields and zero instance counts; compaction atomically reserves each stable/fading output slot in that argument record. GPU readback tests compared parameters, entity/fade membership, zero visibility and command-slot reuse. Two real cameras each recorded four independent lists and eight dispatches in [the same captured frame](e-two-camera.json). The cancellation test covers two distinct payloads and an unexecuted graph retry; it does not prove partial-submit/in-flight cancellation or fence behavior.

## Bounded measurements

The probe used `Scene_Spazon`, `MainCamera`, fixed Game size, 32 warmup frames and identical request settings for each backend. Each row is a separate 0–5, 5–20 or 20–60 second interval; p50/p95 are whole pipeline `Render` wall time, not a GPU timestamp or a single Job. The 10,000 instances move, but their animation phase derives from Editor realtime, so iterations are not frame-identical. GC counts are process-wide cumulative Gen0 values, including explicit collections at window boundaries; live memory is after those collections. Raw probe files contain GC allocations, all generations, total allocated/reserved memory, driver and pool bytes, entity/view counts and actual backend. Screenshot labels show elapsed time from the probe request. The 5 and 20 second windows were captured after each completed interval. For 60 seconds a task-mirror-only diagnostic handshake captured the verified window after interval statistics and collection, but before probe contents were removed; the probe was restored after the run. Each receipt records the sampling time at that signal. Links are real windows.

| Backend and load | End (s) | Frames | Render p50 / p95 (ms) | Live (MiB) | Gen0 count | Window, request elapsed |
|---|---:|---:|---:|---:|---:|---|
| GPU Indirect, scene | 5 | 312 | 2.766 / 3.101 | 56.24 | 337 | [8.69 s](gpu-scene-5.png) |
| GPU Indirect, scene | 20 | 936 | 2.787 / 3.121 | 56.30 | 345 | [21.34 s](gpu-scene-20.png) |
| GPU Indirect, scene | 60 | 2493 | 2.766 / 3.154 | 56.29 | 362 | [60.86 s](gpu-scene-60.png) |
| CPU Direct, scene | 5 | 327 | 2.478 / 2.786 | 56.36 | 369 | [8.69 s](cpu-scene-5.png) |
| CPU Direct, scene | 20 | 982 | 2.495 / 2.833 | 56.52 | 376 | [21.38 s](cpu-scene-20.png) |
| CPU Direct, scene | 60 | 2614 | 2.497 / 2.795 | 56.80 | 393 | [60.86 s](cpu-scene-60.png) |
| GPU Indirect, 10,000 moving | 5 | 319 | 6.575 / 7.219 | 62.46 | 401 | [8.76 s](gpu-10k-5.png) |
| GPU Indirect, 10,000 moving | 20 | 962 | 6.621 / 7.138 | 62.62 | 408 | [23.86 s](gpu-10k-20.png) |
| GPU Indirect, 10,000 moving | 60 | 2565 | 6.603 / 7.103 | 62.95 | 424 | [60.99 s](gpu-10k-60.png) |
| CPU Direct, 10,000 moving | 5 | 331 | 6.847 / 7.688 | 64.53 | 430 | [8.72 s](cpu-10k-5.png) |
| CPU Direct, 10,000 moving | 20 | 1010 | 6.709 / 7.314 | 64.59 | 437 | [23.86 s](cpu-10k-20.png) |
| CPU Direct, 10,000 moving | 60 | 2700 | 6.708 / 7.303 | 64.40 | 453 | [60.96 s](cpu-10k-60.png) |

Raw measurements: [GPU scene](gpu-scene-probe.json), [CPU scene](cpu-scene-probe.json), [GPU 10k](gpu-10k-probe.json), [CPU 10k](cpu-10k-probe.json). The screenshot pairs in the 10k runs have different Game pixels, so the Game view was advancing. Memory remained bounded over these intervals; this is not proof against longer-term leaks.

The earlier same-request 10k baseline p50 values were GPU 7.253/7.010/7.040 ms and CPU 7.042/6.911/6.917 ms ([GPU baseline](baseline-gpuindirect-10k-probe.json), [CPU baseline](baseline-cpudirect-10k-probe.json)). After many tests, CPU 10k ran at 8.761/8.850/8.806 ms, repeated at 8.739/8.831/8.814 ms ([first](final-cpudirect-10000-probe.json), [repeat](repeat-cpudirect-10000-probe.json)); GPU was 7.581/7.616/7.610 ms ([data](final-gpuindirect-10000-probe.json)). After restarting this task's Editor, both are near the earlier baseline as in the table. The prior slowdown's cause remains unknown; changing realtime phase and accumulated Editor state confound the comparison. No GPU-time or sustained speedup claim is made.

`BoxMatix_MeshPipeline` was played with 8,008 entities/elements on each backend: [CPU window](final-box-cpu.png), [GPU window](final-box-gpu.png). The GPU recorded four compaction lists/eight dispatches; CPU recorded no GPU mesh compaction. The native scene and stress windows showed lit geometry and no new InfinityRP exception during the measured periods. The original source scenes were not saved.

## Validation and limits

Unity compile and domain reload completed and the candidate Runtime, Editor, CLI and Tests assemblies were loaded. The final Infinity EditMode run was 835 passed, 2 failed, 1 skipped ([XML](editmode-results.xml)). Both failures also occurred on the parent baseline in the same Editor: `MeshResidencySubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs` and `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`; the skipped test required async compute. Those failures remain open, and the former makes the partial-submit lifetime boundary especially important to retest. Targeted mesh lifetime tests passed 21/21, mesh draw-list tests 7/7, render-graph failure tests 6/6 and LOD tests 8/8. Static checker: 0 errors, 0 warnings; static-check tests: 24/24.

TODO(UNVERIFIED): No isolated GPU timing, GPU capture/native Metal render-pass verification, Player build, iOS/Designed for iPad, Windows, D3D or Vulkan run was performed. Transparency ordering was covered by draw-list tests rather than a dedicated final-window visual fixture; partial submitted GPU work/cancellation is not closed by the unexecuted-graph test. Do not infer other-platform atomic/indirect-buffer behavior from macOS Metal.
