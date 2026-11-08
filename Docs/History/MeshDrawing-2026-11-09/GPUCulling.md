# GPU culling and preparation naming, 2026-11-09

Mirror `/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/gpu-culling/20261109-0112/project`, Unity 6000.6.0f1, Metal, Apple M3 Max. The user's Editor was not used. Candidate `3406ae3`. Baseline `69a0465`.

## EditMode

`infinity_tests_start`, Infinity assemblies: 819 passed, 12 failed, 1 skipped. The failures are the ten `ShaderStrippingTests` readonly-database cases, `MeshResidencySubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs`, and `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`.

## Frame hierarchy, Scene_Spazon, MainCamera

`69a0465`, GPU indirect, one frame tree of 151 events. `ComputeMeshCulling` appears 6 times. `ComputeMeshCompaction` is parented under `RasterDepth`, `RasterGBuffer`, `RasterCascadeShadow`, `RasterDepthRecovery`, `RasterObjectMotion`, `RasterForward` and `RasterTranslucentDepth`.

Candidate, GPU indirect, after Hi-Z history exists, 148 events. `ComputeMeshCulling` appears 3 times. `RasterDepth` contains `RenderLoop.DrawMeshIndirect` and `RenderLoop.DrawSRPBatcher` only. Cascade compaction is `ComputeMeshCompaction/CascadeSliceN`, after the single frustum `ComputeMeshCulling` and before the cascade draws.

Candidate, play-mode counter `MeshWorldStats.gpuCullDispatches` on MainCamera: frame 0 is 1, frames 1–3 are 3. The history-reset compiled graph contains one live `ComputeMeshCulling`.

## DisplayColor

1666×1754, `R16G16B16A16_SFloat`, `lockHistoryReset`, jitter 0, camera position (8, −1.25, 0.6). `Rotate.speed` is 0 and `Mesh_Anim` does not play automatically. Changed pixels use the `Tools/FramePairDiff.py` rule, max channel delta above 8/255.

| Pair | SSIM | Changed pixels | Max channel delta |
|---|---:|---:|---:|
| CPU candidate vs `69a0465` | 1 | 0 | 0 |
| GPU candidate vs `69a0465` | 0.9998815 | 31 | 0.0527 |
| Two `69a0465` GPU frames | — | 31 | 0.0527 |

The 31 GPU pixels are the same set on both rows. No pixel differs from `69a0465` that does not also differ between two baseline frames.

## Probe

All moving, camera path radius 3, windows 5/20/60 s, shadows on. Proxy `GC.Alloc` p50 is 0 in every window. The texture-pool estimate is 608,287,712 bytes in every window. GPU timestamp queries were not available from this probe.

| Run | 60 s proxy p50 | 60 s proxy min | allocatedViewCount across windows |
|---|---:|---:|---|
| 10k CPU | 0.4276 ms | 0.2976 ms | 0 |
| 10k GPU | 0.3646 ms | 0.2861 ms | 4, 4, 4 |
| 50k CPU | 2.1747 ms | 1.7332 ms | 4, 4, 4 |
| 50k GPU | 2.1327 ms | 1.7705 ms | 4, 4, 4 |

Round-2 10k 60 s proxy p50 was 0.387 ms CPU and 0.382 ms GPU. The 10k buffer-pool estimate stays 109,569,508 bytes on CPU and 110,233,892 bytes on GPU. The 50k buffer estimate rises between the 5 s and 20 s windows and then levels off (CPU 117,832,228 → 130,890,052 → 133,290,820; GPU 124,908,004 → 137,965,828 → 137,966,212).
