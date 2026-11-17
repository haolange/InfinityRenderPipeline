# CPU time

This page explains the CPU and allocation changes measured on 2026-11-17. The rules live in [ARCHITECTURE.md](../../../ARCHITECTURE.md). The numbers live in [Feature status](FeatureStatus.md). If a figure here and a figure there disagree, Feature status is the record.

## In one pass

Two changes account for the speedup on a scene whose meshes and lights are not moving.

1. Shadow casters are answered once, then kept. Moving the camera does not make the pipeline walk every entity again.
2. A moving camera no longer allocates a new atmosphere queue observer every frame. Steady play stops allocating inside `Render`.

Nothing was rewritten as a Burst job. The loops that remain run only when the cached answer is no longer valid.

## What used to happen every frame

`ProcessLight` asked three questions on the main thread, for every camera:

- Which entities can cast a shadow. That walk is one pass over the shadow-pass members.
- What bounds those entities have under this light's masks. That walk is another pass over the same list.
- Whether a local light's shadow frustum contains any of them. That walk is local lights, times faces, times entities.

The answers do not change when only the camera moves. The code still recomputed them.

Separately, a moving camera misses the atmosphere view lookup almost every frame. Recording that pass constructed a new `AtmosphereProduction` object. The profiler showed that as a `GC.Alloc` under `RecordCamera`, about one allocation per frame.

## What happens now

### Caster list

`ShadowCasterBounds` keeps the entity index list. It rebuilds the list only when `MeshWorld.structuralRevision` changes. [ARCHITECTURE](../../../ARCHITECTURE.md) defines that revision and who reads it.

Same world, same revision: `Collect` returns. It does not scan entities.

### Bounds and local lights

A stored bounds answer is reused when all of these still match:

- the structural revision
- `transformRevision` (poses and bounds)
- the light's culling mask and rendering mask
- the camera's scene mask

A stored local-light answer also checks the light's pose, range, spot angle, and the shadow tile size. A light that moves is recalculated. A light that stays put is not.

The recalculation is the same managed loop as before. These scenes did not show that loop as a cost, so it was not moved to a Burst job.

### Atmosphere observer

The pipeline owns one `AtmosphereProduction`. Each record writes that object's fields and hands the same object to the pass. The first record allocates it. Later records do not.

## What did not change

- Mesh column upload. On Matrix and Spazon the transform-version and upload markers ran 0 times. A static world already skips that work. No Burst job and no merged upload were added.
- Light packing, tile rectangles, depth sort, and z-bins. With one Game camera they run once per frame and are small. They stay managed.
- Cascade fitting. Temporal anti-aliasing changes the camera projection every frame, so a cache of the last fit never hits. A version that compared the matrices anyway made `Shadow.FitCascade` slower and was removed.
- Render-graph recording arrays, and CPU instance buffers sized by member count. Their allocations were not on the captured stacks, so their keys stayed exact.

## How to read the profiler

Three names, three jobs:

| Name | What it times |
|---|---|
| Pass `ProfilingSampler`, shown as the pass name on the GPU | The pass while it executes |
| `Record.<method>`, `ProcessLight.<stage>`, `Light.<stage>`, `Shadow.<stage>`, `Mesh.<stage>` | CPU only. Record time is not the same marker as execute time |
| `RG.Compile.<stage>`, `RG.Execute.<stage>` | CPU while the graph compiles and runs |

`Inl_RecordRG`, `Inl_ExecuteRG`, and `Inl_ProcessLight` are the parent ranges. On the measured scenes each ran once per frame, which matches one Game camera.

## Measured shape

Release, Play mode, Game view only, one Game camera. Warm up 300 frames, then average 300. Matrix moves the camera on a 40 m circle. Spazon leaves the camera still.

A larger caster set saves more, because the skipped work grows with entity count and with local lights times faces. The measured times are in Feature status.

Moving Matrix, both mesh backends, and still Spazon stop allocating inside `Render` after the observer reuse. Before that change the moving camera allocated while recording the atmosphere observer. The window counts are in Feature status.
