# InfinityRP Agent Guide

This document defines coding conventions and collaboration rules for `com.infinity.render-pipeline` (InfinityRP).

## Architecture principles

1. **Entity data belongs to the entity; draw descriptions only reference it.** One logical object owns one `TransformId` / bounds. Submesh, material, and pass only add `MeshDraw` / template references.
2. **Cache stable templates; regenerate view results.** `MeshPassDraw` is cacheable. Visibility, LOD, sort order, and compaction are per request/view.
3. **One public Mesh Drawing API for CPU and GPU backends.** Callers use `MeshDrawRequest` / `RGDrawListRef`; backends stay internal.
4. **RenderGraph-native lifecycle.** `DeclareDrawList` records only; structural pass culling runs first; only live DrawLists schedule work; cleanup is graph-owned.
5. **No legacy / deprecated compatibility layers.** When a path is replaced, delete the old ownership path. Do not keep dual authorities.

## Naming

| Kind | Convention | Examples |
|------|------------|----------|
| Namespace | `InfinityTech.<Domain>[.<Sub>][.Editor]` | `InfinityTech.Rendering.MeshPipeline` |
| Enum | `E` prefix | `EPassType`, `EMeshBackendPolicy` |
| Geometry / handle struct | `F` prefix | `FBound`, `FBufferRef`, `FPlane` |
| RenderGraph type | `RG` prefix | `RGBuilder`, `RGDrawListRef` |
| Job | `*Job` suffix | `MeshInstanceCullingJob` |
| Private instance field | `m_` + PascalCase | `m_RGBuilder` |
| Static readonly | `s_` prefix | `s_Shader` |
| Shader property IDs | `ID_` or semantic names on `InfinityShaderIDs` | `TransformBuffer` |
| Shader bindings | `SRV_` / `UAV_` / `CBV_` (HLSL/C#) | `SRV_DepthTexture` |

## Organization

- Extend `InfinityRenderPipeline` only via `partial` files under `Runtime/RenderPipeline/Pass/`.
- Pass file pattern: `XxxPassData` struct + optional `XxxPassUtilityData` + pipeline method.
- Prefer `#region` in Editor inspectors; avoid in hot Runtime paths.
- One primary type per file unless Pass/Feature grouping clearly benefits.

## Performance code

- Hot paths: `struct` + Burst jobs; explicit `Allocator` (`Persistent` / `TempJob` / `Temp`).
- `unsafe` only for Native containers and culling pointer jobs (`allowUnsafeCode` is enabled).
- Do not put managed delegates into Burst jobs.
- `GroupingKey` uses full structured equality; hash is lookup acceleration only.
- Filter / Grouping / Sort are three separate semantics — never one hash for all.

## Mesh Drawing Pipeline touchpoints

| Module | Responsibility |
|--------|----------------|
| `MeshScene` | SoA tables, typed IDs + generation, transactions |
| `MeshSceneResidency` | Dirty-range GPU transform / previous / bounds upload |
| `MeshVisibility` | Per-instance frustum / flags |
| `MeshVisibilityShare` | Intern cull results by viewKey + sceneId + frustumHash + revision + policy |
| `MeshDrawCompiler` | Filter / 64-bit SortPlan / build jobs |
| `MeshPassDrawCache` | Stable pass templates + revision invalidation |
| `MeshDrawPipeline` | Schedule / Resolve / Submit facade |
| `MeshDrawGPUBackend` | Per-DrawList GPU payloads + Auto fallback |
| `RGDrawList*` | Graph registry; not an `ERGResourceType` |

### Mesh Drawing Pipeline conventions (required)

1. **`MeshSceneUpdate` must fully undo.** Every mutating op pushes an undo entry; `Dispose` without `Commit` rolls back via the undo log (live counts / object relations) and restores revision + dirty ranges. Free-list membership and highWater stay owned by Free*/Restore*/deferred reclaim — do not truncate free-lists on rollback. Do not add half-applied structural edits.
2. **Deferred section/material reclaim inside a transaction.** When `refCount` hits zero during `BeginUpdate`…`EndUpdate`, queue pending reclaim — do not free immediately. `Commit` flushes pending reclaim; `Rollback` discards pending lists after undo restores refCounts.
3. **`MeshPassDrawCacheKey` uses structured `Equals`.** Hash codes are lookup acceleration only; never treat hash equality as key equality (same rule as `GroupingKey`).
4. **Visibility ownership:** `MeshVisibilityShare` interns results; RDG `ReleaseAllDrawLists` (paired `Release`) owns handle lifetime. Do not leave TempJob cull arrays alive across frames.
5. **GPU payload is per-DrawList.** Rent/return payload buffers from the backend pool keyed to a DrawList lifetime.
6. **No process-global static indirect / args buffer** shared across concurrent DrawLists or cameras.
7. **GPU cull is always instance-indexed.** Bounds / visibility / candidate indices use instance slots; compacted indices written for shading are **transform** indices. CPU `MeshDrawList.instanceIndices` remain transform indices for procedural submit.
8. **Sort encoding must explicitly saturate** (signed and unsigned 16-bit segments). Never rely on silent truncation / wrap.
9. **`ERenderingLayer` is the shared Mesh/Light bit-flag mask** (`enum : byte` + `[Flags]`; `Everything = 0xFF`). Do not reintroduce an int-index `renderLayer` / `1 << layer` convention.
10. **Physical GPU resources retire only after frame-end `ScriptableRenderContext.Submit()`.** `ReleaseAll` / `ReleaseAllDrawLists` perform logical cleanup + `RetirePayload` (and CPU rented-buffer retire) only. Call `FlushRetiredPayloads` / `FlushRetiredBuffers` after Submit to drain retired queues. Never Release/Return a payload still referenced by in-flight GPU work.
11. **No mid-recording ComputeBuffer Release / EnsureCapacity rebuild.** Pre-record: `ComputePayloadBudget` then one `EnsureCapacity`. During CommandBuffer recording / submit: `RequireCapacity` check only — never grow or recreate buffers already bound to a live command buffer.
12. **`TransformId` ownership is exclusive (1:1).** One live transform maps to at most one `MeshInstance`. `CreateInstance` must reject an already-owned transform. Do not add refcount-free multi-instance transform sharing.
13. **`BufferDescriptor` / `TextureDescriptor`: field-level `Equals` is authoritative.** `GetHashCode` is lookup acceleration only (same principle as `GroupingKey` / `MeshPassDrawCacheKey`).

Public RDG usage:

```csharp
RGDrawListRef draws = graph.DeclareDrawList(pipeline, request, visibility, visibilityShare);
data.draws = pass.UseDrawList(draws);
// execute:
cmdEncoder.Draw(data.draws);
```

## Unity / Shader

- Components: prefer `[ExecuteAlways]` + `AddComponentMenu("InfinityRenderer/...")`.
- Asset private serialization: `[SerializeField] private T m_*`.
- Shader path: `InfinityPipeline/...`; tag `RenderPipeline=InfinityRenderPipeline`.
- Compute files: `Compute_<Feature>.compute`.
- LightMode tags must match `InfinityPassIDs` (`DepthPass`, `GBufferPass`, …).

## Comments and hygiene

- Comments in English; TODO format: `// TODO: <action>`.
- Do not commit large blocks of commented-out dead code.
- Do not introduce spellings that diverge further; fix typos when touching a symbol.
- asmdef display names currently still say `HighDefinition.*` (historical). Prefer `Infinity` when renaming is intentionally scheduled.

## Model / agent work split (project preference)

- Search / explore / inventory: Composer-class low-cost agents.
- Mid-leverage implementation: Grok-class agents.
- Planning, architecture decisions, and review: the user-selected primary model.

## Verification

- EditMode tests live under `Tests/Editor` (Test Framework dependency in `package.json`).
- Runtime Editor is not assumed available in every agent environment; mark unverified GPU/platform results as `TODO(UNVERIFIED)` in delivery notes.
- Steady-state invariant: `MeshScene.MatrixDuplicateRatio == 1.0`.
