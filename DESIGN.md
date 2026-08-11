# InfinityRP Mesh Drawing Pipeline — Design (as implemented)

Status: **code-converged; runtime-unverified** (closure **D / D1–D6**). Editor / GPU / multi-camera / Frame Debugger runs remain `TODO(UNVERIFIED)`.
Wave C gate **C5** (“CPU Submit ownership + frame buffer release” / “physical resources closed”) is **withdrawn** and **superseded by D1** (frame-end retirement).
This does **not** include HZB occlusion, GPU radix sort, or a full GPU LOD pipeline.
Related research: external `MeshDrawingPipeline-Research-and-Design.zh-CN.md`.
Baseline notes: [Docs/MeshPipeline-Baseline.md](Docs/MeshPipeline-Baseline.md).
Delivery / verification: [Docs/MeshPipeline-Delivery-Report.md](Docs/MeshPipeline-Delivery-Report.md).

## 1. Problem solved

The previous MeshPipeline treated each submesh as a full element with a duplicated matrix, built pass work during RenderGraph record (before culling), never submitted draws, and used one hash for identity/equality/sort.

The new system separates six lifetimes:

1. Logical instance + transform
2. Geometry section
3. Material data
4. Pass-stable draw template (`MeshPassDraw`)
5. Per-view visibility / filter / sort results
6. Backend submission payload (CPU procedural or per-DrawList GPU buffers)

## 2. Data model

```text
MeshInstance ──TransformId──► TransformTable (current/previous)
     │
     └── DrawSpan ──► MeshDraw[] ──► MeshSectionId + MaterialDataId

MeshPassDraw (cached) = shaderPass + mesh + section + material + revisions
VisibleMeshDraw       = grouping/template + InstanceId + sortKey + transformIndex
MeshDrawList          = ordered commands + instance index buffer
```

Invariant: `TransformCount / LogicalInstanceCount == 1` in steady state (exposed as `MatrixDuplicateRatio`).

**TransformId ownership is exclusive (1:1).** Each live `TransformId` is owned by at most one `MeshInstance`. `CreateInstance` / `AllocInstance` rejects an already-owned transform (`ArgumentException`). There is no refcounted multi-instance transform sharing — do not describe shared transforms as a supported capability. Instance-indexed GPU cull remains compatible with this 1:1 model (instance slots map to their sole transform index).

Typed IDs carry `Index + Generation` to reject stale handles after remove/reuse.

`MeshSceneResidency` uploads `transformBuffer` and `previousTransformBuffer` (plus instance-indexed bounds centers/extents and `InstanceTransformIndexBuffer` for GPU cull staging).

## 3. Module map

| Path | Role |
|------|------|
| `Runtime/.../MeshPipeline/MeshScene*.cs` | Authoritative SoA scene + atomic undo transactions |
| `MeshSceneResidency.cs` | Dirty-range transform / previous / bounds upload |
| `MeshVisibility.cs` | Instance-granularity frustum cull |
| `MeshVisibilityCache.cs` | `MeshVisibilityShare` — signature intern (viewKey + sceneId + frustumHash + revision + policy) |
| `MeshDrawCompiler.cs` | Burst filter / 64-bit SortPlan keys / build |
| `MeshPassDrawCache.cs` | Template cache; structured `MeshPassDrawCacheKey` Equals |
| `MeshDrawPipeline.cs` | Schedule / Resolve / Submit facade |
| `MeshDrawGPUBackend.cs` | Per-payload GPU buffers + Auto fallback; Retire → Flush after Submit |
| `RenderGraph/RGDrawList.cs` | Logical DrawList registry; `ReleaseAll` = logical cleanup + Retire (visibility + payloads) |
| `Shaders/.../Compute_MeshDrawPipeline.compute` | Cull / compact / BuildIndirectArgs kernels |
| `Shaders/ShaderLibrary/GPUScene.hlsl` | `transformBuffer` / `previousTransformBuffer` / `instanceIndexBuffer` |
| `Runtime/RendererCore/RenderingLayer.cs` | Shared 8-bit `ERenderingLayer` flags for Mesh/Light |

Terrain / foliage / water remain separate systems. Shared seams already present:

- `EGeometrySourceKind` (`IndexedMesh`, `SkinnedDeformed`, `Procedural`, `MeshletCluster`)
- `MeshInstanceRecord.deformationDataId` for skinned/compute preprocess
- Instance/transform identity suitable for future RTAS sharing (`RayTraceEnvironment` stays independent)

## 4. CPU path

1. `MeshComponent` registers via `MeshSceneUpdate` (1 transform, N draws).
2. Per camera / cascade / local-shadow face: `MeshVisibilityShare.Acquire` → shared `MeshViewCullingResult`.
3. Pass records `DeclareDrawList(request, visibility, share)` only.
4. RDG compile culls unused passes, then `CompileDrawLists` schedules live work.
5. First consumer `EnsureResolved` completes jobs and builds `MeshDrawList`.
6. `RGRasterEncoder.Draw` → CPU `DrawMeshInstancedProcedural` or GPU indirect with:
   - `transformBuffer` / `previousTransformBuffer` = TransformTable
   - `instanceIndexBuffer` = TransformId indices (CPU path and GPU compacted output)
   - `instanceIndexOffset` = command run offset

Unity `DrawRendererList` remains for Unity-owned MeshRenderer content; Infinity `MeshComponent` draws through MeshDrawPipeline.

Wired MeshDraw paths: Depth, GBuffer, Forward, Motion, Cascade Shadow, Local Shadow.

## 5. RDG DrawList lifecycle

```text
Record:  DeclareDrawList + UseDrawList
Compile: CountPassReference → CullingUnusedPass → CompileDrawLists → UpdateResource
Execute: EnsureResolved(per consumer) → encoder.Draw
Finally: ReleaseAllDrawLists (logical: builds + RetirePayload + visibility Release)
Frame end: ScriptableRenderContext.Submit → FlushRetiredPayloads / FlushRetiredBuffers
```

DrawList is **not** an `ERGResourceType`. Physical buffers created by backends remain Buffer resources. Resource loops use `(int)ERGResourceType.Max` (Buffer / Texture / AccelerationStructure slots).
Each `RGDrawListRef` carries a graph generation; stale refs after Begin/Clear are rejected. Visibility share is owned per DrawList record.

## 5.1 Physical GPU resource lifetime

Two distinct phases — do not conflate them:

| Phase | API | What happens |
|-------|-----|----------------|
| **Logical cleanup** | `RGDrawListContext.ReleaseAll` | Releases NativeArray builds, visibility handles / share refs, and **retires** GPU payloads (`RetirePayload`) / CPU rented buffers into retired queues. Does **not** `ComputeBuffer.Release` or return payloads to the pool. |
| **Physical retirement** | After `ScriptableRenderContext.Submit()` | `MeshDrawGPUBackend.FlushRetiredPayloads` + per-pipeline `FlushRetiredBuffers` drain retired queues back to pools (or dispose). Safe only once GPU work for the frame has been submitted. |

Recording capacity rules:

1. **Before CommandBuffer recording:** `ComputePayloadBudget` sizes the worst-case command/instance needs across planned batches; call `EnsureCapacity` / `TryEnsureCapacity` **once** for that budget.
2. **During recording / submit:** only `RequireCapacity` (boolean check). **Never** mid-record `Release` / recreate / `EnsureCapacity` growth of ComputeBuffers already bound to a live CommandBuffer.

## 6. Transactions and cache keys

- `MeshSceneUpdate`: every mutating op pushes undo; `Dispose` without `Commit` rolls back atomically.
- **Deferred reclaim:** during a transaction, section/material slots whose `refCount` hits zero are queued (`m_Pending*Reclaims`), not freed immediately. `Commit` / `EndUpdate(commit: true)` flushes pending reclaim; `Rollback` / `EndUpdate(commit: false)` discards the pending lists after undo has restored refCounts.
- **Rollback restore:** revisions + dirty ranges are restored from the begin-update state snapshot; free-list membership and live counts are owned by the undo log + flush/discard path (no half-applied structural edits).
- `MeshSortKey.PackSortKey`: up to four 16-bit segments → 64-bit lexicographic key; Descending inverts the segment. Signed/unsigned encodes **explicitly saturate** (no silent truncation).
- `MeshPassDrawCacheKey`: full structured `Equals` is authoritative (includes `staticFlags`); `GetHashCode` is lookup acceleration only. `geometryRevision` changes bump `section.revision` and invalidate templates.
- Transform / camera motion → **no** template cache miss; material/section/pass/revision/`staticFlags` change → miss.
- `MeshPassDrawCache.Enabled = false` → lookups return `MeshPassDrawId.Invalid`; build jobs fall back to `MeshGroupingKey` (image-equivalent). Invalidated entries are **tombstoned** and free-slots are reused with bumped generation.
- Counters: `TemplateCacheHits` / `TemplateCacheMisses`.

## 7. Visibility share

- Intern key is `MeshVisibilitySignature`: **`viewKey + sceneId + frustumHash + VisibilityRevision + policyId`** → one cull result, ref-counted.
- Same viewKey with a different frustum hash does **not** share.
- `MakeCascadeViewKey(light, cascade)` isolates shadow cascades from the main camera frustum.
- `MakeLocalShadowViewKey(light, face)` isolates Spot (face 0) and Point (faces 0–5) local-shadow views (`PolicyLocalShadow`).
- Depth / GBuffer / Forward / Motion share main-camera visibility when signature matches; cascade and local shadows acquire distinct keys.
- Ownership: per-record Share + RDG `ReleaseAllDrawLists` (paired `Release`); no leaked TempJob arrays across frames.

## 8. GPU backend

`EMeshBackendPolicy.Auto` selects:

| Capability | Path |
|------------|------|
| No compute or no CS asset | `CpuDirect` |
| Compute without usable indirect | `CpuDirect` |
| Compute + instancing | `GpuIndirect` |

Committed GPU slice: CPU still produces filtered/sorted commands and candidate **instance** indices; compute kernels cull/compact (emitting **transform** indices) and `BuildIndirectArgs` fills args; submit uses `DrawMeshInstancedIndirect` with **per-DrawList** payload buffers (pooled, never a process-global static indirect buffer).
`ComputePayloadBudget` + one pre-record `EnsureCapacity`; recording uses `RequireCapacity` only (see §5.1). `TryPlanBatches` preflights payload caps before any dispatch; kernel resolve uses `HasKernel`.
No same-frame visible-count readback. Overflow increments `GpuOverflowCount`.

Assign `InfinityRenderPipelineAsset.meshDrawPipelineCS` to `Compute_MeshDrawPipeline.compute`.

Explicitly **out of this closed slice**: HZB occlusion, GPU radix sort of draw keys, complete GPU-resident LOD selection.

## 9. Public API sketch

```csharp
using (MeshSceneUpdate update = scene.BeginUpdate())
{
    TransformId xf = update.CreateTransform(localToWorld);
    MeshInstanceId inst = update.CreateInstance(xf, bounds, layerMask, ...);
    for (int s = 0; s < subMeshCount; ++s)
        update.CreateDraw(inst, meshId, s, materialId, eligibility, queue, priority);
    update.Commit();
}

var request = new MeshDrawRequest {
    filter = BuiltinMeshesPasses.GBuffer.defaultFilter,
    sort = BuiltinMeshesPasses.GBuffer.defaultSort,
    backendPolicy = EMeshBackendPolicy.Auto,
    shaderPassIndex = BuiltinMeshesPasses.GBuffer.shaderPassIndex
};
RGDrawListRef draws = graph.DeclareDrawList(pipeline, request, visibility, visibilityShare);
```

## 10. Explicit non-goals (this delivery)

- HZB occlusion
- GPU radix / full GPU sort of MeshSortPlan keys
- Full GPU LOD pipeline
- Nanite-like meshlets
- Replacing Unity RendererList for builtin MeshRenderers
- Completing AccelerationStructure RDG physical path beyond enum slot sizing
- **Runtime in-place mesh topology rewrite** without a `geometryRevision` notification — unsupported. Mutating vertex/index topology under a live registration leaves the scene/section cache stale; re-register the `MeshComponent` (or bump geometry through the supported AllocOrUpdateSection path) after such edits.

## 11. Diagnostics

`MeshPipelineDiagnostics`: instance/draw/transform counts, `MatrixDuplicateRatio`, temp allocs, culled-skip builds, template hits/misses, GPU overflow, `LocalShadowBudgetDropped`.

## 12. LocalShadow MeshDraw

Local shadows for Spot / Point lights use the same MeshDraw + RDG DrawList path as cascade shadows:

- **Tile budget:** atlas resolution from `localShadowMapResolution`; `tileResolution = resolution/4`, `tilesPerRow = resolution/tileResolution`, budget = `tilesPerRow²`. Spot costs 1 tile; Point costs 6. Candidates are scored (shadow strength / distance, Spot preferred on ties) and greedily accepted; over-budget lights increment `LocalShadowBudgetDropped` and are skipped.
- **Point 6-face:** each accepted Point light records six face frusta / view-projection matrices and six `MakeLocalShadowViewKey(light, face)` visibility acquires (`PolicyLocalShadow`). Spot uses a single face/key. Point face matrix construction uses `light.shadowNearPlane` as nearPlane.
- **Shadow MeshDraw filter (Cascade + Local):** `layerMask = light.cullingMask`; `renderingLayerMask = light.shadowLayer` from `LightComponent` when present, else `ERenderingLayer.Everything`.
- **Dual path:** each atlas slice draws Infinity `MeshComponent` content via `RGDrawListRef` **and** Unity-owned renderers via `DrawRendererList`. The two paths cover different object ownership — not dual authority for the same Infinity mesh.

## 13. Instance-indexed GPU cull

GPU cull separates lookup identity from shading matrix identity:

| Stage | Index domain |
|-------|----------------|
| Bounds / visibility buffers | **Instance** index (`InstanceHighWater`) |
| CandidateIndices (CPU → GPU) | **Instance** index |
| CompactedIndices / shader `instanceIndexBuffer` | **Transform** index (`TransformId.Index`) |
| CPU `MeshDrawList.instanceIndices` (procedural submit) | **Transform** index |

`MeshSceneResidency` uploads instance-indexed bounds + `InstanceTransformIndexBuffer`; Compact maps a visible instance slot to its transform index for matrix fetch. Do not treat candidate and compacted streams as the same semantic.

## 14. Rendering layer unification

`ERenderingLayer : byte` with `[Flags]` (`Nothing` … `Everything = 0xFF`) is the shared Mesh/Light mask. `MeshFilterProgram` defaults `renderingLayerMask` to `Everything`. Filter reject rule: `(instance.mask & filter.mask) == 0`.

| Pass family | `layerMask` | `renderingLayerMask` |
|-------------|-------------|----------------------|
| Depth / GBuffer / Forward / Motion | camera / pass default (`~0` open) | `ERenderingLayer.Everything` |
| Cascade / Local shadow MeshDraw | `light.cullingMask` | `light.shadowLayer` (`LightComponent`) when present; else `Everything` |

`MeshComponent.renderingLayer` is flags, not an int layer index. Do not reintroduce `1 << renderLayer` indexing.
