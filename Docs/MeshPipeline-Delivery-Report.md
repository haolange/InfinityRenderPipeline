# Mesh Drawing Pipeline — Delivery Report (Closure D / D1–D6)

Date: 2026-08-10
Package: `com.infinity.render-pipeline`
Target Editor (project): Unity **6000.0.26f1**
Agent environment: **no local Unity Editor** — GPU / multi-camera / Frame Debugger / EditMode Test Runner must be run on your machines (`TODO(UNVERIFIED)`).

## 1. Previous wave (convergence A–G)

Status values below remain historically **Done** for that wave, but acceptance is **superseded by closure gates C1–C11**, then further refined by **closure D (D1–D6)**.

| Slice (convergence plan) | Status | Notes |
|-------|--------|-------|
| A Scene txn + Transform validation + draw/section/material reclaim | Done (superseded by closure gates) | Full undo log; free list; refCount; unified `m_Scene` authority |
| B MeshSortPlan real 64-bit keys | Done (superseded by closure gates) | Direction / Distance / signed priority packing in Burst path |
| C Template cache closed loop | Done (superseded by closure gates) | Structured `MeshPassDrawCacheKey`; section/material revision; `passDrawId` groups builds |
| D RDG DrawList + visibility share ownership | Done (superseded by closure gates) | Share refcount; `ReleaseAll` cleans visibility/GPU payloads; no manual `cullingDatas.Release` |
| E True GPU Driven slice (per-payload) | Done (superseded by closure gates) | Cull/Compact/PrefixSum/Scatter/BuildArgs dispatched; overflow chunk or CpuDirect fallback |
| F Motion + CascadeShadow MeshDraw | Done (superseded by closure gates) | Instanced Motion/Shadow passes; `previousTransformBuffer`; cascade viewKeys |
| G Tests + docs sync | Done (superseded by closure gates) | EditMode cases below; Editor/GPU runs remain `TODO(UNVERIFIED)` |

## 2. Closure gates (C1–C11)

| Gate | Status | Notes |
|------|--------|-------|
| C1 Deferred section/material reclaim + state snapshot rollback | `code-converged` | Pending reclaim flush/discard; live-slot + dirty/free-list restore |
| C2 Instance-indexed GPU cull chain | `code-converged` | Bounds/visibility by instance; compact → transform; CPU indices stay transform |
| C3 Sort encode saturation + distance scale | `code-converged` | Explicit signed/unsigned saturate; `quantizeScale` on Distance fields |
| C4 GPU submit preflight + kernel safety | `code-converged` | `TryPlanBatches` before dispatch; `HasKernel` resolve; unconditional `SetShader` |
| C5 CPU Submit ownership + frame buffer release | `code-converged` | **superseded by D1 — frame-end retirement** |
| C6 MeshComponent sync + idempotent unregister | `code-converged` | Snapshot/diff, dirty drain, destroy-safe RemoveById (further refined by D2) |
| C7 Shared `ERenderingLayer` flags | `code-converged` | Mesh/Light 8-bit flags; camera Everything; shadows use `shadowLayer` (refined by D4) |
| C8 LocalShadow MeshDraw + Point 6-face | `code-converged` | Tile budget; dual MeshDraw+RendererList; `MakeLocalShadowViewKey` |
| C9 RDG DrawList generation + per-record visibility share | `code-converged` | Stale-ref reject; Release uses record-owned share (refined by D5 contextId / signature) |
| C10 Pass cache key + geometryRevision | `code-converged` | `staticFlags` in key; geometry bump → section.revision |
| C11 Docs sync | `code-converged` | DESIGN / Delivery / AGENTS / README aligned to C1–C10 (superseded by D6) |

## 2.1 Closure gates (D1–D6)

| Gate | Status | Notes |
|------|--------|-------|
| D1 Physical GPU/CPU resource frame-end retirement | `code-converged` | `ReleaseAll` = logical cleanup + `RetirePayload`; `ComputePayloadBudget` + one pre-record `EnsureCapacity`; recording uses `RequireCapacity` only; flush after `ScriptableRenderContext.Submit` |
| D2 MeshComponent full state machine | `code-converged` | Snapshot includes movebility + materialRenderQueues; structural vs UpdateBounds; world-list migrate / OnStateTypeChange |
| D3 TransformId exclusive 1:1 ownership | `code-converged` | `m_TransformOwners`; `CreateInstance` rejects already-owned transform; remove/rollback restore owner |
| D4 Shadow mask + 8-bit layer contract | `code-converged` | `layerMask = light.cullingMask`; `renderingLayerMask = light.shadowLayer`; Point nearPlane = `light.shadowNearPlane`; `ERenderingLayer : byte` + `[Flags]`; filter default `Everything` |
| D5 Identity semantics + dead-path cleanup | `code-converged` | Remove RestoreState/TruncateFreeList; `RGDrawListRef` context identity; visibility signature = viewKey+sceneId+frustumHash+revision+policy; pass cache `Enabled=false` → Invalid id + grouping fallback; tombstone free-slot reuse |
| D6 Tests + docs sync | `code-converged` | EditMode coverage for D1–D5; DESIGN / Delivery / AGENTS / README aligned |

Honest non-goals still open:

- HZB occlusion — **not implemented**
- GPU radix / full GPU sort of sort keys — **not implemented**
- Complete GPU LOD pipeline — **not implemented**
- Runtime in-place mesh topology rewrite without `geometryRevision` — **unsupported** (re-register `MeshComponent`)
- Multi-instance shared `TransformId` — **unsupported** (exclusive ownership)

## 3. What you must do locally

### 3.1 Editor (Windows / primary box) — required

1. Open `InfinityExample` in Unity **6000.0.26f1** (or compatible Unity 6).
2. Wait for script compile; resolve `com.unity.test-framework` if prompted.
3. Select Infinity RP Asset → assign **Mesh Draw Pipeline CS** =
   `Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute`
4. Ensure Infinity-path objects use `Mesh Component` + materials with InfinityLit **Instanced** bindings (`transformBuffer`, `previousTransformBuffer`, `instanceIndexBuffer`, `instanceIndexOffset`).
5. Enter Play Mode / Scene View; confirm opaque geometry (Depth/GBuffer/Forward).
6. Window → General → Test Runner → EditMode → run `InfinityTech.Rendering.MeshPipeline.Tests`.
7. Frame Debugger: verify Motion MeshDraw + Cascade Shadow MeshDraw + LocalShadow MeshDraw paths (and GPU `BuildIndirectArgs` when on D3D12/Vulkan with CS assigned).

**Expected EditMode tests** (method names from `Tests/Editor/MeshSceneTests.cs`)

- `ThreeSubmeshObject_SharesOneTransform`
- `CreateInstance_RejectsTransformAlreadyOwned`
- `RemoveInstance_Rollback_RestoresTransformOwner`
- `Rollback_CreateTransformInstanceDraw_RestoresCountsAndRevisions`
- `Rollback_AtomicallyRestoresCountsAndRevisions`
- `CreateInstance_RejectsDeadTransform`
- `CreateInstance_RejectsStaleOrInvalidTransform`
- `RemoveInstance_RejectsStaleGenerationOnReuse`
- `RemoveInstance_Rollback_RestoresDrawsAndSharedResources`
- `SetMaterial_UniqueOldMaterial_Rollback_KeepsAliveRefCount`
- `SetMaterial_SameId_NewRenderQueue_UpdatesDrawQueueAndMaterialRevision`
- `Rollback_RestoresRevisionsCountsAndDirtyRanges`
- `DrawFreeList_ReusesSlotWithBumpedGeneration`
- `SortKey_DescendingFlipsOrderRelativeToAscending`
- `SortPlan_DescendingChangesOrderRelativeToAscending`
- `SortKey_SignedPriority_MonotonicForNegZeroPosMinMax`
- `SortKey_Distance_DoesNotSaturateAtGivenScale`
- `GroupingKey_HashCollision_DoesNotMergeUnequalKeys`
- `PassDrawCache_StructuredKey_CollisionDoesNotOverwrite`
- `PassDrawCache_StructuredKey_EqualsAuthorityUnderHashCollision`
- `PassDrawCache_HitsOnSecondLookup_AndIgnoresTransformMotionInKey` (includes `staticFlags` miss)
- `PassDrawCache_Disabled_ReturnsInvalid`
- `CreateDraw_StaticFlags_StoredOnRecord`
- `MeshVisibilityShare_DifferentFrustumHash_DoesNotShare`
- `Section_GeometryRevisionChange_BumpsSectionRevision`
- `MeshVisibilityShare_SameKeySharesOneResult_DifferentCascadeIsolated` (includes LocalShadow viewKey / `PolicyLocalShadow`)
- `SelectPolicy_FallsBackWithoutComputeShader`
- `TryPlanBatches_FailsWhenSingleCommandExceedsMaxInstances`
- `TryPlanBatches_SplitsWhenTotalExceedsPayloadCaps`
- `TryPlanBatches_SingleBatchWhenWithinCaps`
- `ComputePayloadBudget_TakesMaxAcrossSplitBatches`
- `ComputePayloadBudget_UsesBoundsWhenLargerThanCandidates`
- `ComputePayloadBudget_FailsWhenSingleCommandExceedsMaxInstances`
- `RetirePayload_StaysOutOfPoolUntilFlush`
- `MeshPassBuildJob_WritesTransformAndInstanceSlotIndices`

EditMode Test Runner execution in this agent environment: `TODO(UNVERIFIED)`.

### 3.2 Platform matrix

| Platform | Graphics API | Expected MeshDraw path | Verification steps | Status |
|----------|--------------|------------------------|--------------------|--------|
| Windows Editor/Standalone | D3D11 | Usually `CpuDirect` (indirect/compute limits vary) | Play + Frame Debugger: `DrawMeshInstancedProcedural` | `TODO(UNVERIFIED)` |
| Windows Editor/Standalone | D3D12 | `GpuIndirect` when CS assigned + instancing | Frame Debugger: compute args + `DrawMeshInstancedIndirect`; check Motion + Cascade Shadow + **LocalShadow (Spot 1-tile / Point 6-face atlas slices)** draws | `TODO(UNVERIFIED)` |
| Windows | Vulkan | Same as D3D12 intent | Same; Motion/Cascade/LocalShadow MeshDraw present | `TODO(UNVERIFIED)` |
| macOS | Metal | Prefer Auto; may fallback CPU | Play + Metal capture; Motion/Cascade/LocalShadow | `TODO(UNVERIFIED)` |
| Android | Vulkan / GLES | Often CPU fallback on GLES | Smoke scene; structured-buffer / pink-material watch | `TODO(UNVERIFIED)` |
| iOS | Metal | Auto → CPU or indirect per device | Smoke scene; Motion/Cascade/LocalShadow | `TODO(UNVERIFIED)` |
| Multi-camera | Any | Shared main visibility per camera viewKey; cascades + local-shadow faces isolated | Two cameras + cascaded light + Spot/Point local shadows | `TODO(UNVERIFIED)` |
| Multi-camera + GpuIndirect | D3D12 / Vulkan (preferred) | Per-DrawList payloads; frame-end Retire → Flush after Submit | Two+ cameras with Auto/`GpuIndirect`; RenderDoc: confirm **no cross-camera reference to an already-released ComputeBuffer** (payload lifetime / retirement) | `TODO(UNVERIFIED)` |
| LocalShadow 6-face | D3D12 / Vulkan (preferred) | MeshDraw + RendererList per atlas face | Place Point light with shadows; Frame Debugger: 6 face viewports/tiles, MeshDraw draws on Infinity meshes, RendererList on Unity renderers; over-budget → `LocalShadowBudgetDropped` | `TODO(UNVERIFIED)` |
| Consoles | N/A in this package CI | Later port | — | Out of scope |

Fallback contract:

- No compute **or** CS asset null → CPU procedural
- Compute but indirect/instancing unusable → CPU procedural
- Compute + instancing → GPU indirect args (per-DrawList payload)

Never depend on same-frame GPU readback of visible counts.

### 3.3 When you return to continue platform work

Bring: Unity version, Graphics API, Frame Debugger / RenderDoc capture, whether `meshDrawPipelineCS` is assigned, `MeshPipelineDiagnostics` snapshot (`MatrixDuplicateRatio`, `GpuOverflowCount`, `LocalShadowBudgetDropped`, template hits/misses), and shader errors from console.

## 4. Architecture fit

- Namespace / `E*` / `F*` / `RG*` / `m_*` / Burst jobs preserved.
- Passes remain `partial InfinityRenderPipeline` files.
- Unity `RendererList` kept for Unity-owned renderers; Infinity mesh path is MeshDrawPipeline only — no dual-authority adapter (LocalShadow intentionally dual-paths **different** object sets).
- RDG resource loops sized with `ERGResourceType.Max`.
- Visibility owned by per-record `MeshVisibilityShare` + RDG release; GPU payloads are per-DrawList with frame-end physical flush.
- Rendering layers are shared `ERenderingLayer : byte` flags (not int indices).
- TransformId is exclusive 1:1 with MeshInstance.

## 5. Known gaps / honest labels

| Item | Label |
|------|-------|
| HZB occlusion | Not implemented |
| GPU radix / full GPU sort | Not implemented |
| Full GPU LOD | Not implemented |
| Runtime mesh topology rewrite (no `geometryRevision`) | Unsupported — re-register `MeshComponent` |
| Multi-instance shared TransformId | Unsupported — exclusive ownership |
| Skinned deformation compute | Seam only (`EGeometrySourceKind.SkinnedDeformed`, `deformationDataId`) |
| Terrain/grass RTAS sharing | Seam only; systems remain separate |
| Image A/B vs Unity RendererList | `TODO(UNVERIFIED)` — needs Editor |
| Player build with MeshComponent (`ExecuteAlways`) | `TODO(UNVERIFIED)` |
| Multi-camera / Frame Debugger / EditMode run | `TODO(UNVERIFIED)` |
| Multi-camera + GpuIndirect buffer lifetime (RenderDoc) | `TODO(UNVERIFIED)` |
| LocalShadow Spot/Point 6-face runtime | `code-converged`; platform verify `TODO(UNVERIFIED)` |
| `InfinityLit.shader` non-instanced path | Unchanged; instanced shader is the MeshDraw binding target |

## 6. Key files

**Core:** `Runtime/RendererCore/PrimitivePipeline/MeshPipeline/*`, `RGDrawList.cs`, `RenderingLayer.cs`, `LocalShadowPass.cs`, `CascadeShadowPass.cs`, `Compute_MeshDrawPipeline.compute`, `Tests/Editor/*`, `AGENTS.md`, `DESIGN.md`, `Docs/*`.

**Removed authority (historical):** `MeshBatch.cs`, `MeshBatchCollector.cs`, `GPUScene.cs` (C# class), `MeshPassProcessor.cs`, `MeshPipelineJob.cs`, old element collector path. Binding HLSL `GPUScene.hlsl` remains as the transform buffer include name.

**Integrated:** `MeshComponent`, `LightComponent` (`shadowLayer`), `RenderContext`, `InfinityRenderPipeline`, Depth/GBuffer/Forward/Motion/CascadeShadow/LocalShadow passes, `PipelineIDs`, `GPUScene.hlsl`, `InfinityLit-Instanced.shader`, `InfinityRenderPipelineAsset` (`meshDrawPipelineCS`).

## 7. Suggested next sessions (user-driven)

1. Editor smoke + Test Runner (§3.1).
2. D3D12 Frame Debugger: GPU path + Motion + Cascade Shadow + LocalShadow 6-face.
3. Multi-camera + GpuIndirect RenderDoc: no cross-camera use-after-retire of ComputeBuffers.
4. Optional later: HZB occlusion, GPU sort, fuller GPU LOD — only if product priority demands it.
5. Wire skinned `deformationDataId` + RTAS shared identity when terrain/foliage systems upgrade.
