# Mesh preparation record order — 2026-11-11

Unity 6000.6.0f1, Apple M3 Max, macOS Metal. The owned Editor was `InfinityExample`, scene `Scene_Spazon`, Game camera `MainCamera`. No second Editor was opened and no scene was saved.

## Schedule

Recorded order is the execution order. The compiler no longer moves a pass after it is recorded. Pass culling, resource lifetime, aliasing, native render-pass merge and async cross-queue fences stay.

A nonempty mesh list records its upload, and for GPU indirect its compaction, after that wave's visibility is written and before the raster block that draws it. An empty list records no upload or compaction. The raster pass and native renderers still record.

Wave 1 is depth and shadows. When the previous Hi-Z history is valid, wave 2 is recovery plus the main-view lists, and it is recorded after `RasterDepth` and `ComputeEarlyFurthestHiZ`. Without that history the main view shares wave 1 and there is no second preparation block.

## Marker names

The outer samples are `ComputeMeshCulling`, `UploadMeshData` and `ComputeMeshCompaction`. A compute sample does not use a `Raster*` LightMode name. `Frustum`, `Batch`, `DrawListN` and `Final` are not marker names.

| Parent | Child | Meaning |
|---|---|---|
| `ComputeMeshCulling` | `DepthPrediction` | Main view, previous Hi-Z is valid. Occlusion prediction, not the shadow box test. |
| `ComputeMeshCulling` | `Shadows` | Shadow views only. Cascade and local slices are not split on this dispatch. |
| `ComputeMeshCulling` | `Depth` | No Hi-Z history, and this dispatch has the main view only. |
| `ComputeMeshCulling` | `DepthAndShadows` | No Hi-Z history, and this dispatch has the main view and shadows. |
| `ComputeMeshCulling` | `DepthRecovery` | Main view after `RasterDepth` and early Hi-Z. |
| `UploadMeshData` | consumer token | `CascadeShadow.SliceN`, `LocalShadow.SliceN`, `DepthPrediction`, `DepthRecovery`, `GBuffer`, `Motion`, `Forward`, or a translucent stage. |
| `ComputeMeshCompaction` | consumer, then `SliceN` for a shadow, then `InitializeIndirectArgs` and `Compact` | One list. `Slice` appears only under a shadow. |

CPU markers use the same consumer token: `MeshCPU.Schedule.<consumer>`, and the same form for Complete, Upload and Draw. Worker jobs stay Culling, Gather, Sort and Merge. Draw samples stay `RenderLoop.DrawMeshDirect` / `Indirect` and `Shadows.DrawMeshDirect` / `Indirect`.

On `Scene_Spazon` with a valid Hi-Z history, the first `ComputeMeshCulling` shows `DepthPrediction` then `Shadows`. That `Shadows` row is shadow views only. The main view is `DepthPrediction`.

## Payload

`MeshGPUDrawPayload` is a class. A new draw-list record leaves it null. Reading `commandCapacity` before the rent threw `NullReferenceException` from `PrepareMeshDrawList` on every Game view frame. `EnsureGpuPayload` rents when the payload is null or its capacity is 0. `PrepareMeshDrawList` and `PrepareLive` both call it.

## What was checked

- Static check: 0 error, 0 warning.
- `dotnet build` of the runtime, editor and test projects: 0 error. The test project still warns on the pre-existing unassigned `RenderGraphCompileTests.PassData` fields.
- After the owned Editor reloaded the payload fix, the next `MainCamera` frame logged Present and the log has no later `NullReferenceException`. The Game view shows the courtyard. The console row at 01:10:17 is the exception from the assembly that was still running during that compile.
- Marker and extension EditMode tests passed on the task mirror. Live GPU preparation did not run there. Metal batchmode residency failed with `No live rendering context was available`.

## Not claimed

Continuous play, 5/20/60 second memory, residency readback, Player, iOS, D3D12 and Vulkan were not run. `HierarchicalRay_HitIsOneActualSurfaceTexel(True)` was not run. Dispatch count is not a GPU-time result. The task mirror and `intermediate/mesh-prep-order/` were deleted.
