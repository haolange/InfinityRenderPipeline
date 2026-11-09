# Owner layout validation

The candidate was compiled and run in the task-owned Example mirror at
`/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/owner-layout/20261109/Example-owner-layout`.
The Editor was Unity 6000.6.0f1 on macOS Metal. The original Example project was
not opened. Runtime, Editor, CLI and Tests assemblies were loaded from the mirror.
The task mirror used a fresh Library and the loopback-only Pipeline bridge.

The pre-repair Editor entered Safe Mode with two C# syntax errors. Its Console
also reported readonly database errors. The [baseline window](baseline-safe-mode.png)
was recorded before the repair. After repair the Scene_Spazon Game view showed
the courtyard, lighting and meshes. Its Game region changed between the
[short](probe-0-5s.png) and [middle](probe-0-20s.png) captures. The [long-window
image](probe-0-60s.png) was captured near the end of sampling. The
[CPU Direct image](cpu-scene.png) was captured during its 10-second probe.
The [AO debug view](debug-ao.png) ran after repairing a Metal shader compile error.
The [BoxMatix scene](box-mesh.png) rendered with 8,008 mesh entities and 8,008
elements. Its [frame tree](frame-tree.json) has 85 events and includes Unity's
`(RP 0:0)` depth-draw labels. A Metal GPU capture was not performed.

The two runs below used Game rendering at 985 × 1330 pixels, the asset backend
and GPU Indirect. The three intervals ended at 5, 20 and 60 seconds after warmup.
Frame times are the Editor probe's measured frame scope in milliseconds. The
probe forces a collection at each boundary. Counts are cumulative within the
Editor process and memory values are bytes.

| Load | End | Frames | Frame p50 / p95 ms | Frame allocation p50 | GC 0 / 1 / 2 | Post-GC live | Allocated / reserved | Graphics driver | Texture / buffer pool |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Scene | 5 s | 50 | 11.282 / 13.432 | 70355 | 197 / 197 / 197 | 76390400 | 482471497 / 1309966336 | 898545294 | 372003728 / 42283884 |
| Scene | 20 s | 149 | 9.527 / 13.086 | 70355 | 199 / 199 / 199 | 76804096 | 482482542 / 1309966336 | 898545294 | 372003728 / 42283884 |
| Scene | 60 s | 401 | 10.904 / 13.575 | 70355 | 203 / 203 / 203 | 76050432 | 482511226 / 1309966336 | 898545678 | 372003728 / 42284268 |
| 1,000 moving | 5 s | 50 | 13.238 / 15.140 | 71349 | 205 / 205 / 205 | 76906496 | 485526116 / 1309966336 | 900026382 | 372003728 / 43010220 |
| 1,000 moving | 20 s | 150 | 12.771 / 14.908 | 71349 | 207 / 207 / 207 | 77398016 | 485532949 / 1309966336 | 900093198 | 372003728 / 43079852 |
| 1,000 moving | 60 s | 399 | 12.594 / 15.138 | 71349 | 211 / 211 / 211 | 79306752 | 485551929 / 1309966336 | 900093198 | 372003728 / 43079852 |

The moving-load [short](probe-1000-5s.png) and [middle](probe-1000-20s.png)
images were captured after their corresponding sample boundaries. The
[late image](probe-1000-60s.png) was captured while sampling
was still active near the end of the 60-second window. The detailed records
are [scene](probe-scene.json) and [moving load](probe-moving.json). Neither run showed an empty Game view. The moving-load post-GC live set rose
across these three samples. A [repeat](probe-moving-repeat.json) measured
77053952, 77045760 and 77025280 bytes at the same boundaries. The Editor sampled
at about 10 frames per second in both runs. This is a bounded Editor observation.

The static gate reported zero errors and warnings and its 24 regression tests
passed. The Infinity EditMode run reported 832 tests, 819 passed, 12 failed and
1 skipped. Ten failures observed `attempt to write a readonly database`. The
mesh queued-input test and the hierarchical-ray wall-hit GPU test failed again
when run individually. The same two tests produced the same failures on
`b12a1f5^` in the same mirror and Editor. The parent also emitted the database
error during startup. The database owner remains open. The failed CPU probe request
used `CpuDirect` rather than the actual `CPUDirect` enum and was rerun
successfully. Other graphics APIs, Player builds and iOS were not tested.
