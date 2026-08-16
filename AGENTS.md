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

## Integrity / no paper-over

These rules exist because Console-clean patches have already hidden real ownership, ordering, and lifetime bugs. A silent fallback is not a fix.

1. **Do not disable a designed path to silence Console.** Forbidden patterns include `EnableAsyncCompute(false)` as a correctness switch, replacing `throw` with `return false`, dispatching with `count = 0` to look wired, and `if (shader == null) return` inside execute. If a feature is unimplemented, do not record the pass.
2. **Do not swallow invalid resources.** An invalid `RGTextureRef` / `RGBufferRef`, a `Query*` miss, or a failed Resolve must fail at record/setup. Forbidden: `Texture2D.blackTexture` fallbacks, lighting-as-history, binding an empty `RenderTargetIdentifier` to compute.
3. **Pass type is not a workaround knob.** Transfer = copy. Raster = draw. Compute = dispatch. Gizmo/WireOverlay and Present all stay Raster, because each of them ends in a draw. Never move Draw into Transfer to dodge an API, and never call `SetRenderTarget` inside a pass execute — attachments are declared at record time and bound by `RGBuilder`.
4. **History lives only in `HistoryCache`.** Cross-frame color/depth is `ImportTexture` plus a frame-end CopyHistory transfer. Never Query a same-frame scoper ID that has not been written yet and call it history.
5. **One semantic buffer, one owner.** Downstream reads the ID the last producer registered (`DisplayColorBuffer` for the frame's present source). Do not guess a sibling ID (`SuperResolutionBuffer` vs `AntiAliasingBuffer` vs `LightingBuffer`).
6. **Console wording is not the root cause.** Unity's `temporary render texture` message is the empty-identifier diagnostic. It does not mean RDG used `GetTemporaryRT`. Check handle validity, pass order, and who should `Register*` first.
7. **Ownership, order, and lifetime first; API migration second.** A workaround must be `// TODO: <root cause>` and cannot be the final design.
8. **RDG textures go through Create / Import / ResourcePool only.** No `GetTemporaryRT` on hot paths.
9. **Record-time gate vs execute-time silence.** Volume-missing or optional-feature-off may skip recording. Shader/kernel missing for an optional feature also skips recording. A required producer (Lighting, active TAA, Display) throws at record. Execute must not contain `if (shader == null) return`.
10. **Feature classes talk to command capability interfaces, never `CommandBuffer`.** `IComputeCommands` / `IRasterCommands` / `IRaytracingCommands` / `ITransferCommands` are the only command surfaces. RG encoders implement them. Outside RG, wrap a `CommandBuffer` with `CommandBufferCommands`. Do not add `implicit operator CommandBuffer` on encoders.
11. **One physical quantity, one authority.** Atmosphere defaults live on `AtmosphericalProfile`. `AtmosphericScattering` Volume parameters are overrides (`overrideState`) only. Do not keep a second independent default set. Geometric sizes are meters. Hillaire scatter/absorption coefficients are stored per kilometer and converted to per-meter at bind (`AtmosphereParameter.ScatterPerKmToPerMeter`). Do not mix the two units in the compute shader.
12. **`VolumeManager.GetComponent<T>()` is never a null check.** Unity always returns a default component. Optional Volume features (volumetric fog/cloud) record only when `active` and at least one parameter has `overrideState`.
13. **Dead code is deletable only when zero-referenced and already replaced by an equivalent RG path.** "Not wired yet" is not "obsolete".

## RenderGraph resource and pass shape

1. **Mip chains stay in one compute pass.** HiZ / ColorPyramid / bloom downsample are the same resource reading mip N-1 and writing mip N. RG tracks resources, not subresources; splitting per-mip into multiple passes creates false hazards and no extra parallelism. Loop dispatches inside one execute.
2. **LUT / froxel / cubemap generation is compute.** Do not introduce `Blit` / `SetRenderTarget` / `BuiltinRenderTextureType` to generate atmosphere tables. Cubemap faces are a `RWTexture2DArray`.
3. **Fallback raster depth flags follow `EDepthAccess`.** `ReadOnlyDepthStencil` is set only when the pass declared read-only depth without write.

## Known gaps (do not paper over)

- `SSRPass` records only the raytracing kernel. Spatial and temporal filter live in `ScreenSpaceReflectionGenerator` and are not yet an RG pass.
- `GTAOPass` does not dispatch the temporal kernel (`OcclusionTemporal`).
- RTAO has a Volume component and `.raytrace` shader but no RG pass.
- `ZBinningPass.HasZBinningLightList` returns a hardcoded `false`, so `ComputeZBinningLightList` never records. `LightContext` only uploads directional lights and exposes neither local light bounds nor a visible-light count, which is what the binning kernel needs.
- The `AtmosphereCubemap` kernel in `Compute_AtmosphericLUT.compute` has no consumer (there is no sky IBL / ambient probe path), so `AtmosphericLUTPass` deliberately does not create the cubemap texture or dispatch kernel 4. Kernel index 5 (`SunBuffer`) still assumes kernel 4 exists in the file.
- Directional light intensity has two conflicting consumers. `LightContext` packs `light.color * light.intensity` into `color`, so `color.rgb` already carries intensity and `color.a` ends up holding intensity as well. `Compute_DeferredShading` and `Compute_VolumetricFog` then compute `color.rgb * color.a` and apply intensity twice, while `InfinityLit.shader` / `InfinityLit-Instanced.shader` use `color.rgb` alone and `AtmosphericLUTPass` / `VolumetricCloudPass` recompute `sunLight.color * sunLight.intensity` on the CPU. Converging these changes scene exposure, so it must be its own change with its own frame capture.
- `Compute_CombineLUTs.compute` and `InfinityRenderPipelineAsset.combineLUTShader` exist but no RG pass produces a grading LUT, so `Compute_PostProcessing.compute` has no color grading stage.
- `PostProcessingPass` hardcodes bloom intensity / threshold, vignette, and film grain instead of reading a Volume component. There is also no exposure stage anywhere in the pipeline, so `PP_BloomThreshold` is compared against raw linear luminance; with the current light intensities a sunlit surface peaks near 0.3, which is why a conventional "above white" threshold of 1.0 produces no bloom at all.

## Render target and depth conventions

1. **`EnableNativeRenderPass(false)` is allowed only where a native-RP attachment is impossible.** Today that is Gizmo / WireOverlay (Unity forbids drawing gizmos inside `BeginRenderPass`) and Present (the backbuffer has no owning `RenderTexture`, so `AttachmentDescriptor.graphicsFormat` cannot be resolved). A new use needs the same kind of hard API reason, not convenience.
2. **Clear depth and sampled depth are different values.** `ClearRenderTarget(depth)` and `AttachmentDescriptor.clearDepth` are normalized by Unity: `1.0` always means far plane and the backend flips it on reversed-Z platforms. Use `GraphicsUtility.ClearDepthFar` there. Only shader-side comparisons against a sampled depth buffer use `GraphicsUtility.SampledFarDepth`. Feeding one into the other clears the depth buffer to the near plane and silently kills geometry in every raster pass.
3. **A color-only raster target binds color only.** Never pass an empty `RenderTargetIdentifier` as the depth target of a `RenderTargetBinding`; Unity reports it as `temporary render texture not found`. Use the color-only `SetRenderTarget` overload.
4. **The backbuffer enters RDG through `ImportBackbuffer`.** It is an imported resource and therefore skips Create/Release. `RTHandle.SetTexture` is CoreRP-internal, so rebinding reallocates when the identifier changes.

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

### Post-change self-check loop

Console-clean is not render-correct. A single depth-clear regression once held the Console at 0 errors while the Game view rendered nothing at all, because every raster pass had lost its geometry to a failed depth test.

1. Refresh through `Tools/RefreshUnityEditor.ps1`. Never launch a second Unity or a `-batchmode` run while `Library/EditorInstance.json` points at a live editor.
2. Diagnose only the new `Logs/Editor.log` window past the pre-refresh byte mark, and fix the first real owner / order / lifetime / contract defect rather than the loudest message.
3. Then capture the frame with `Tools/CaptureUnityWindow.ps1` and actually look at it. Any change touching clears, depth, attachments, pass order, or present is unverified until the image has been inspected.
4. Repeat until the new log window is free of InfinityRP errors **and** the captured frame is correct.
5. Only results confirmed by a captured frame may drop the `TODO(UNVERIFIED)` marker. Log-only checks keep it.
