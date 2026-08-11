# Mesh Drawing Pipeline — Phase 0 Baseline

## Pre-migration problems

1. **Matrix duplication**: `MeshBatchCollector` stored one `float4x4` per submesh (`MeshElement`). A logical object with N sections uploaded N identical matrices.
2. **Weak removal**: `RemoveMeshBatch` was empty; instance lifetime was effectively append-only.
3. **One-shot GPU upload**: `GPUScene.Update` uploaded matrices only once (`m_IsUpdate` latch), so later transform edits never reached the GPU.
4. **CPU draw path disabled**: `DrawMeshInstancedProcedural` and buffer binds in `MeshPassProcessor.DispatchDraw` were commented out.
5. **Per-element culling**: Frustum tests ran on duplicated element bounds instead of one bounds per logical instance.
6. **No typed identity**: Integer cache ids / array indices mixed transforms, draws, and materials without generation safety.

## Target invariants

| Invariant | Target |
|-----------|--------|
| `MatrixDuplicateRatio` (`TransformCount / max(1, LogicalInstanceCount)`) | **1.0** in steady state |
| Transform ownership | Exactly **one** `TransformId` per logical instance |
| Draw expansion | N submeshes → N `MeshDraw` records, shared transform index |
| Instance index buffer | Stores `TransformId.Index` (not per-draw duplicates) |
| Residency upload | Dirty-range upload every frame when transforms change |
| CPU backend | Real `CommandBuffer.DrawMeshInstancedProcedural` submission |
| Symbol hygiene | No residual `MeshElement` / `MeshBatchCollector` / `GPUScene` / `CullingDatas` / `MeshPassProcessor` (except this baseline doc) |

## Phase map

- **Phase 0**: Diagnostics counters + this baseline document.
- **Phase 1**: `MeshScene` SoA + generation ids + `MeshSceneUpdate` transaction + component registration.
- **Phase 2**: Visibility, pass filter/sort/build jobs, `MeshDrawPipeline` CPU submit, `MeshSceneResidency` dirty upload, shader binding rename (`transformBuffer` / `instanceIndexBuffer` / `instanceIndexOffset`).
- **Phase 3**: RDG logical `RGDrawList` — `DeclareDrawList` / `UseDrawList` / per-payload Complete; culled passes skip Schedule; `ERGResourceType.Max` array sizing.
- **Phase 4**: `MeshPassDrawCache` template cache (material revision keyed; transform/camera motion must not miss).
- **Phase 5**: Minimal GPU backend (`MeshDrawGPUBackend` + `Compute_MeshDrawPipeline.compute`) with Auto fallback.

## Platform fallback (Phase 5)

| Capability | Backend |
|------------|---------|
| No compute shaders | `CpuDirect` (`DrawMeshInstancedProcedural`) |
| Compute but no indirect instancing | `CpuDirect` procedural |
| Compute + indirect | `GpuIndirect` (compute fills args, `DrawMeshInstancedIndirect`) |

Never read back visible counts on the submitting frame. Overflow increments `GpuOverflowCount`.

## Diagnostic counters

`MeshPipelineDiagnostics` exposes:

- `RegisteredInstances`, `RegisteredDraws`, `TransformRecords`
- `MatrixDuplicateRatio`
- `TempAllocCount`, `CulledPassSkippedBuilds`
- `TemplateCacheHits`, `TemplateCacheMisses`
- `GpuOverflowCount`

Use `Snapshot()` / `Reset()` around frames or tests.

## Convergence fixes (post Phase 0–5 skeleton)

The Phase 0–5 map above describes the **skeleton delivery**. A later convergence pass closed engineering for the committed slices without claiming HZB occlusion, GPU radix sort, or full GPU LOD.

Authoritative post-convergence status and verification checklist:

- [DESIGN.md](../DESIGN.md) — status: engineering-closed for committed slices; explicit non-goals
- [MeshPipeline-Delivery-Report.md](MeshPipeline-Delivery-Report.md) — A–G Done table; GPU / multi-camera / Frame Debugger / EditMode runs marked `TODO(UNVERIFIED)`

Do not treat older “Phase 0–5 Done + GPU only BuildIndirectArgs” wording as the current contract; prefer DESIGN + Delivery Report.
