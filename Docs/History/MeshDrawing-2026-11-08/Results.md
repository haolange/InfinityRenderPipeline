# Mesh drawing pipeline Editor results

2026-11-08. Unity 6000.6.0f1 (`f7f8ed4d1e24`). Metal. Apple M3 Max. macOS 27.2.

The user's Editor (PID 7038) kept `/Volumes/DataDisk/Projects/Unity/InfinityExample` on package `main` `94c1a49`. Verification used a separate Editor on an Example mirror whose package worktree moved from `a814a40` to `6703ab8` to `6c2d282`. DataDisk is ExFAT, so the mirror Library was a file copy, not an APFS clone. Copied AppleDouble sidecars were removed before the mirror Editor would start (`stoull: no conversion`). `com.unity.pipeline` `0.6.0-exp.1` was installed only in the mirror so the CLI server could listen.

## EditMode

`infinity_tests_start`, assembly `Unity.RenderPipelines.Infinity.Tests`.

| Checkout | Passed | Failed | Skipped |
|---|---:|---:|---:|
| `a814a40` | 834 | 13 | 1 |
| `6c2d282` before the draw-list test fix | 813 | 15 | 1 |
| `MeshDrawListTests` after `6509201` | 7 | 0 | 0 |

Baseline failures: missing `RGMeshCompile.cs` in `CompactContractTests`, `GpuSceneSubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs` (expected 4, was 0), ten `ShaderStrippingTests` unhandled logs `attempt to write a readonly database`, and `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)` (expected 1, was 0).

Candidate failures that remain after the draw-list rerun, without a second full-suite run: the same ten stripping logs, `MeshResidencySubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs` (same expected 4 / was 0), the same hierarchical-ray failure, and `MeshPassShaderUtilityTests.FindPassIndex_ShaderReplacement_ResolvesCurrentShaderIncludingMissingPass` (`Material.FindPass("RasterForwardPass")` was 0, `FindPassIndex` was -1).

The async-compute skip is the one skip. This GPU reports no async compute.

## Migration at `6703ab8`

Menu **Window → Infinity → Migrate MeshComponents to MeshAsset (0.5)**, once.

- 8024 components migrated.
- 4 MeshAssets created: Cube, Sphere, Capsule, Cylinder, each one level, `target` null.
- 0 rebuilt, 0 not rebuildable, 0 invalid, 0 not migrated. No `MeshAsset` in the Example had a non-null `target`. `Assets/Profile/StaticMeshAsset.asset` is not a `MeshAsset`.
- Loaded scenes after migration: 64024 `MeshComponent`s, 8024 with `asset`, 0 with a `meshAsset` or `movebility` property still on the type after `6c2d282`. 0 prefab-instance overrides of `asset`. The Example has no instance that overrides the legacy mesh to None or to a different mesh, so that case was not observed.
- Files written: the four assets (created) and `MeshGroup_64000-1.prefab`, `Scene_Spazon`, `BoxMatix_MeshPipeline`, `Scene_Landscape` (bytes changed). Existing `.meta` files of those four were not in the compare delta. New asset `.meta` files and `InfinityMeshAssets.meta` were added.
- Force-reserialize before the menu changed only those four files (binary size grew). That was reviewed as its own delta.
- Second save: all eight written files were byte-identical.
- Second menu: 0 migrated, 0 created, 0 rebuilt, four assets `bytes unchanged`. `AssetBaseline.py compare` against the pre-noop capture passed.

## Scene_Spazon images

MainCamera, Game, 1666×1754. Both backends, Play. Each window pair differed. The Game view shows the courtyard, columns, arches and banners on CPU at `a814a40` and on GPU at the candidate.

DisplayColor readback, `R16G16B16A16_SFloat`, compared without masking:

| Pair | SSIM | Changed fraction (luma step > 8/255) |
|---|---:|---:|
| CPU baseline vs CPU candidate | 0.9863 | 0.0066 |
| GPU baseline vs GPU candidate | 0.9852 | 0.0066 |

Gate is SSIM ≥ 0.99 and changed pixels ≤ 0.5% after excluding translucent MeshComponents that are now visible and LOD dither. These numbers are raw. The scene was not time-frozen, so flags and the character move between captures. The gate is not met.

## Probes

`infinity_runtime_probe_start`. 100% moving. Windows at 5, 20 and 60 s. Proxy `GC.Alloc` p50 is 0 in every window below. ResourcePool texture estimate is 608,287,712 bytes in every window. `actualBackend` matched the request.

Proxy p50 at 60 s, milliseconds:

| Count | Baseline CPU | Baseline GPU | Candidate CPU | Candidate GPU |
|---:|---:|---:|---:|---:|
| 0 | 0.002 | 0.003 | 0.003 | 0.003 |
| 1,000 | 0.810 | 0.743 | 0.073 | 0.069 |
| 10,000 | 6.682 | 6.498 | 0.387 | 0.382 |
| 50,000 | — | — | 2.099 | 2.122 |

10k all-moving proxy p50 is under 2 ms on the candidate. 50k is just above 2 ms; that target was stated for 10k.

Managed heap, 20 s to 60 s:

| Run | 20 s | 60 s | Delta |
|---|---:|---:|---:|
| Candidate CPU 10k | 190,103,552 | 187,465,728 | down |
| Candidate GPU 10k | 221,954,048 | 207,405,056 | down |
| Candidate CPU 50k | 245,334,016 | 247,955,456 | +2,621,440 (2.5 MiB) |
| Candidate GPU 50k | 244,678,656 | 244,678,656 | 0 |

The 1 MiB heap gate holds at 0, 1k and 10k. It does not hold for CPU 50k. A continuously moving camera was not a separate probe. Object motion at a fixed camera did not grow the texture-pool estimate.

## Not run

Round 1 left the following open. Round 2, in the next section, closed the first three on this Metal Editor. D3D12, Vulkan and Windows stay `TODO(UNVERIFIED)`.

- LOD: a temporary 3-level `MeshAsset` (screen sizes 0.45, 0.18, 0) registered 1 entity and 3 elements in Play after `NotifyGeometryChanged`. The Game view of that fixture stayed black, so holes, overlap, popping, and CPU/GPU/orthographic agreement are `TODO(UNVERIFIED)`.
- Overlapping translucent MeshComponents at T0/T1/T2: `TODO(UNVERIFIED)`.
- Edit-mode Scene view handle, parent drag, layer, undo/redo, and move to another loaded scene: `TODO(UNVERIFIED)`.
- D3D12, Vulkan, Windows: `TODO(UNVERIFIED)`.

## Round 2

Same Editor, same mirror, package `1a0cff8` then `d1ffaae` and `7397b12`. The user's Editor stayed PID 7038 on `main`.

### EditMode

Full `infinity_tests_start` on this checkout: 816 passed, 12 failed, 1 skipped. `FindPassIndex_ShaderReplacement_ResolvesCurrentShaderIncludingMissingPass` passed. The 12 failures are the ten `ShaderStrippingTests` readonly-database logs, `MeshResidencySubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs`, and `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`.

The same three filters at `a814a40`: `ShaderStrippingTests` 6 passed / 10 failed, `GpuSceneSubmissionTests` 3 passed / 1 failed (`FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs`), `VisualContractGpuTests` 11 passed / 1 failed (`HierarchicalRay_HitIsOneActualSurfaceTexel(True)`). No failure was new.

### Moving camera and CPU 50k

`d1ffaae` records `allocatedViewCount` and an optional camera path. `7397b12` pumps the player loop, because a backgrounded Editor otherwise records zero Game-camera frames. Scene_Spazon, 100% of the spawned objects moving. Proxy `GC.Alloc` p50 is 0 in every window. ResourcePool texture estimate stays 608,287,712. Buffer estimates stay constant inside a run. `actualBackend` matches the request.

| Run | Window | Frames | Proxy p50 | Heap | Views | Graphics |
|---|---:|---:|---:|---:|---:|---:|
| CPU 10k, camera path | 5 s | 49 | 0.558 ms | 252,428,288 | 7 | 1,488,575,810 |
| CPU 10k, camera path | 20 s | 151 | 0.561 ms | 160,219,136 | 7 | 1,488,575,810 |
| CPU 10k, camera path | 60 s | 400 | 0.508 ms | 160,219,136 | 7 | 1,488,473,650 |
| GPU 10k, camera path | 5 s | 49 | 0.489 ms | 162,693,120 | 8 | 1,489,898,402 |
| GPU 10k, camera path | 20 s | 151 | 0.504 ms | 163,610,624 | 8 | 1,489,898,402 |
| GPU 10k, camera path | 60 s | 400 | 0.491 ms | 163,971,072 | 8 | 1,489,828,050 |
| CPU 50k, camera fixed | 20 s | 200 | 1.876 ms | 198,311,936 | 8 | 1,550,152,786 |
| CPU 50k, camera fixed | 60 s | 399 | 1.887 ms | 201,015,296 | 8 | 1,550,152,786 |
| CPU 50k, camera fixed | 180 s | 1200 | 1.882 ms | 202,670,080 | 8 | 1,550,152,786 |

View count is constant after the first window. The 10k heap from 20 s to 60 s is 0 on CPU and +360,448 bytes on GPU, under 1 MiB. Graphics memory is flat or slightly down. CPU 50k heap grows +2,703,360 bytes from 20 s to 60 s and +1,654,784 bytes from 60 s to 180 s. The rate falls, and it has not stopped at 180 s. Proxy allocation stays 0. The render-scope `GC.Alloc` sample count is 680 on every CPU window and 883 on every GPU window, the same at 10k and 50k, so it is not the 50k set. No mesh-proxy owner was found, and no fallback was added.

### Frozen Scene_Spazon

Time scale 0, the one legacy `Animation` disabled, MainCamera 1666×1754, `DisplayColor`. Baseline at `a814a40` before migration. Candidate after the same migration counts as round 1 (8024 migrated, 4 created).

| Pair | SSIM | Changed fraction |
|---|---:|---:|
| Frozen baseline vs frozen candidate | 0.9758 | 0.0203 |
| Two candidate frames in one Play session | 0.9855 | 0.0073 |

The gate is SSIM ≥ 0.99 and changed pixels ≤ 0.5%. Two frames of the same candidate miss it. The difference is high-frequency edges, consistent with Halton jitter advancing between captures. The cross-checkout gap is larger than that same-session gap. It was not masked into a pass.

### LOD

Mirror-only scene. One `MeshAsset`, screen sizes 0.60, 0.25, 0.08, `fadeWidth` 0.3, cube / sphere / cylinder with red / green / blue `InfinityLit-Instanced`. A directional light, a white ground, and a native cube. The native cube stayed black on `InfinityLit-Instanced` and drew, with a hard shadow, after the material used `InfinityPipeline/InfinityLit`.

Perspective, both backends: about 1.3 m and 4.2 m is the red cube, 6.2 m through 9.2 m is the green sphere, 12 m dithers green into the blue cylinder, 40 m is gone. Orthographic size 0.7 and 2.2 are red, 7 is blue, 25 is gone. CPU and GPU center colors match at the shared distances. The raised object’s shadow is a cube shadow, then a round shadow under the green sphere, then a longer shadow under the blue cylinder. The CPU and GPU frames at the sphere distance differ by mean absolute color 3.5e-5. The cross-fade is a stipple on one silhouette.

### Translucency

Six `MeshComponent` cubes, alpha 1, stages 1/2/3. Same priority: the near cyan covers the far red (T0), and the near orange covers the far blue (T2), with a sliver of the far color still visible. T1 puts priority 10 on the farther green and priority 0 on the nearer magenta; the green covers the magenta. That is priority, then farthest, as `MeshDrawItemComparer` specifies.

### Edit mode

Scene view, not Play. Undo-recorded moves of the static object and of its parent take the red mesh out of the framed view (red pixels 17883 to 0). Undo restores it (17820). Redo leaves it out. Setting layer 8 and clearing that bit in `Tools.visibleLayers` removes it. Play mode then moves the transform and creates and destroys another object without `NotifyEntityChanged`: red pixels stay 185762 then 185775. The explicit notify drops them to 0.

Moving the root into a second loaded scene does not change the draw. `EditorSceneManager.GetSceneCullingMask` is `16140901064495857664` for both scenes, and the Scene view still has about 17857 red pixels. The pipeline intersects that mask with the view. Two main-stage scenes reporting one mask do not change visibility.

## Rerun

Rerun on another API, from a checkout of this branch, against an Example that has already been migrated:

1. `Tools/RefreshUnityEditor.ps1` or `.sh` on that Editor.
2. `unity command infinity_tests_start <new absolute directory>`.
3. Play `Assets/Scene/Spazon/Scene_Spazon.unity`. For `CpuDirect` and `GpuIndirect`, capture a differing window pair and `infinity_capture_start` of `MainCamera` / `Scene_Spazon` / `DisplayColor`.
4. `infinity_runtime_probe_start` with `movingRatio` 1, `windowsSeconds` `[5, 20, 60]`, counts 0, 1000, 10000 and 50000, and `meshBackend` `CpuDirect` and `GpuIndirect`.
