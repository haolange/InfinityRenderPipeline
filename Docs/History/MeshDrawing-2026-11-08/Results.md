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

- LOD: a temporary 3-level `MeshAsset` (screen sizes 0.45, 0.18, 0) registered 1 entity and 3 elements in Play after `NotifyGeometryChanged`. The Game view of that fixture stayed black, so holes, overlap, popping, and CPU/GPU/orthographic agreement are `TODO(UNVERIFIED)`.
- Overlapping translucent MeshComponents at T0/T1/T2: `TODO(UNVERIFIED)`.
- Edit-mode Scene view handle, parent drag, layer, undo/redo, and move to another loaded scene: `TODO(UNVERIFIED)`.
- D3D12, Vulkan, Windows: `TODO(UNVERIFIED)`.

Rerun on another API, from a checkout of this branch, against an Example that has already been migrated:

1. `Tools/RefreshUnityEditor.ps1` or `.sh` on that Editor.
2. `unity command infinity_tests_start <new absolute directory>`.
3. Play `Assets/Scene/Spazon/Scene_Spazon.unity`. For `CpuDirect` and `GpuIndirect`, capture a differing window pair and `infinity_capture_start` of `MainCamera` / `Scene_Spazon` / `DisplayColor`.
4. `infinity_runtime_probe_start` with `movingRatio` 1, `windowsSeconds` `[5, 20, 60]`, counts 0, 1000, 10000 and 50000, and `meshBackend` `CpuDirect` and `GpuIndirect`.
