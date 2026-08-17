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
- `ZBinningPass.HasZBinningLightList` returns a hardcoded `false`, so `ComputeZBinningLightList` never records. The RecordRG slot is Phase 2 (after Depth, before shadows; async when the gate opens). `LightContext` only uploads directional lights and exposes neither local light bounds nor a visible-light count, which is what the binning kernel needs.
- The `AtmosphereCubemap` kernel in `Compute_AtmosphericLUT.compute` has no consumer (there is no sky IBL / ambient probe path), so `AtmosphericLUTPass` deliberately does not create the cubemap texture or dispatch kernel 4. Kernel index 5 (`SunBuffer`) still assumes kernel 4 exists in the file.
- Directional light intensity has two conflicting consumers. `LightContext` packs `light.color * light.intensity` into `color`, so `color.rgb` already carries intensity and `color.a` ends up holding intensity as well. `Compute_DeferredShading` and `Compute_VolumetricFog` then compute `color.rgb * color.a` and apply intensity twice, while `InfinityLit.shader` / `InfinityLit-Instanced.shader` use `color.rgb` alone and `AtmosphericLUTPass` / `VolumetricCloudPass` recompute `sunLight.color * sunLight.intensity` on the CPU. Converging these changes scene exposure, so it must be its own change with its own frame capture.
- `CombineLutPass` records and writes `CombineLookupTexture`, but nothing samples it. `Compute_PostProcessing.compute` FinalCombine has no color-grading stage.
- `PostProcessingPass` is split into `ComputeBloom` + `ComputePostCombine`, wrapped by `RGProfilingScope` (`CustomSamplerId.PostProcessing`). Bloom intensity / threshold, vignette, and film grain are still hardcoded instead of a Volume. There is no exposure stage, so `PP_BloomThreshold` is compared against raw linear luminance; with the current light intensities a sunlit surface peaks near 0.3, which is why a conventional "above white" threshold of 1.0 produces no bloom at all.
- Translucent slots: T0 (pre-fog) and T2 (post-fog) are RecordRG comments only. T1 is `RenderTranslucentDepth` + `RenderForwardTranslucent`, but no shader implements `TranslucentDepthPass` / `ForwardTranslucentPass`. `TranslucentDepthBuffer` is written and unread; `TranslucentLightingBuffer` is unused.
- Several passes always record but emit no draws, so Frame Debugger may omit them: `RenderDBuffer` has no `DBufferPass` LightMode shader; `RenderTranslucentDepth` / `RenderForwardTranslucent` have no matching LightMode; `RenderLocalShadow` has no local shadow casters in the current scene; `CopyHistoryAntiAliasing` / `CopyHistoryDepth` / `CopyHistorySuperResolution` / `CopyHistoryColorPyramid` are Transfer `CopyTexture` events, not draws. `RenderCascadeShadow` still records and binds a depth target even when a cascade has 0 draws — if Game view shows cascade shadows while Frame Debugger omits the pass, that is a display-filter issue, not a skipped record. `TODO(UNVERIFIED)` until a captured Frame Debugger tree is inspected after this change.
- CombineLUT / AtmoLUT record in Phase 0 (zero RG-resource inputs). VolCloud records in Phase 2 (reads Depth + TransmittanceLUT, not the shadow map). VolFog stays after CascadeShadow. HiZ / HalfRes / AtmoLUT / VolFog / VolCloud are marked async. Whether they actually overlap is `TODO(UNVERIFIED)` without a GPU Trace.
- `InfinityLit.shader` MotionPass discards when `unity_MotionVectorsParams.y == 0` and relies on CameraMotion (`stencil != 5`) to fill holes. Moving objects can leave coverage gaps.
- `Compute_ScreenSpaceReflection` unprojects HiZ with the global `Matrix_InvViewProj` (`renderIntoTexture = true` convention). Compute-side reconstruction uses `FlipY` (`renderIntoTexture = false`). That mismatch is a separate contract defect.
- `TAAJitter` is still uploaded globally but the active TAA kernel no longer samples with it. Keep the upload until SuperResolution decides whether it still needs the offset. `TAA_BlendParameter.x = 0.97` is a quality knob left unchanged after the jitter-free motion / direct-texel fix; whether it should drop is `TODO(UNVERIFIED)`.
- TAA sharpening lives in-kernel as a luma-only unsharp (`TAA_Sharpness = 0.35`). A proper post-TAA sharpening pass (RCAS-style) is not implemented; if more sharpness is needed, add it as its own PostProcessing stage instead of raising the in-kernel strength.
- TAA depth rejection uses relative linear-eye delta `smoothstep(0.02, 0.1)`. Thresholds are a quality knob; remaining ghosting at thin geometry after this pass is `TODO(UNVERIFIED)` until a moving-camera frame is inspected.

## Render target and depth conventions

1. **`EnableNativeRenderPass(false)` is allowed only where a native-RP attachment is impossible.** Today that is Gizmo / WireOverlay (Unity forbids drawing gizmos inside `BeginRenderPass`) and Present (the backbuffer has no owning `RenderTexture`, so `AttachmentDescriptor.graphicsFormat` cannot be resolved). A new use needs the same kind of hard API reason, not convenience.
2. **Clear depth and sampled depth are different values.** `ClearRenderTarget(depth)` and `AttachmentDescriptor.clearDepth` are normalized by Unity: `1.0` always means far plane and the backend flips it on reversed-Z platforms. Use `GraphicsUtility.ClearDepthFar` there. Only shader-side comparisons against a sampled depth buffer use `GraphicsUtility.SampledFarDepth`. Feeding one into the other clears the depth buffer to the near plane and silently kills geometry in every raster pass.
3. **A color-only raster target binds color only.** Never pass an empty `RenderTargetIdentifier` as the depth target of a `RenderTargetBinding`; Unity reports it as `temporary render texture not found`. Use the color-only `SetRenderTarget` overload.
4. **The backbuffer enters RDG through `ImportBackbuffer`.** It is an imported resource and therefore skips Create/Release. `RTHandle.SetTexture` is CoreRP-internal, so rebinding reallocates when the identifier changes.
5. **`HistoryCache` reallocates only via `TextureDescriptor.Equals`.** That field-level compare is the authority (same rule as `GroupingKey` / `MeshPassDrawCacheKey`). Do not rebuild a second compare through `RenderTextureDescriptor`: the implicit conversion forced `depthStencilFormat = None` and `mipCount = -1`, so history textures could reallocate every frame, latch `resetHistory`, and silently kill TAA accumulation.

## Profiling conventions

1. **`CommandBufferPool.Get()` is always unnamed.** A named buffer creates an implicit Frame Debugger scope that closes on every `ExecuteCommandBuffer`, orphaning any `BeginSample` / `EndSample` that spans later executes. Camera and pass grouping come only from `ProfilingScope(cmd, sampler)`.
2. **An RG pass GPU name comes only from `pass.customSampler`.** Do not write `cmdBuffer.name = pass.name`. That duplicates the sampler and produces `RenderDepth > RenderDepth > draws`.
3. **A `ProfilingSampler` name must not match any `cmd.BeginSample(string)` argument.** Cross-pass groups use `RGProfilingScope` (internally `sampler.Begin` / `sampler.End`). String `BeginSample` is only for in-pass temporary markers that have no same-named sampler (`BloomDownsample`, `BloomUpsample`).
4. **`ProfilingScope(sampler)` without a CommandBuffer is a deliberate CPU-only track.** Do not add a cmdBuffer to it to "make GPU markers appear".

## Temporal / jitter conventions

1. **`UNITY_MATRIX_VP` is `Matrix_ViewJitterProj`.** Depth and GBuffer are rasterized in jitter space. Any matrix that reconstructs world/view position from `(screenUV, depth buffer)` must be the jittered inverse VP. On the compute side that is `matrix_*FlipYJitter*` — `FlipY` in this repo means `GL.GetGPUProjectionMatrix(..., renderIntoTexture = false)`.
2. **Motion vectors must be jitter-free.** `SV_POSITION` uses the jitter VP so coverage matches the depth buffer. The clip positions used to compute velocity use the non-jitter VP. Baking jitter into motion makes a static frame produce `j_prev - j_curr` every Halton step, which wobbles history lookups and bicubic-resamples the accumulation.
3. **TAA reads the current frame at the exact texel.** `screenUV = (id.xy + 0.5) * texelSize` with point-clamp. Do not bilinear-resample at `screenUV - TAAJitter` to "unjitter" the image; that throws away the subpixel sample TAA exists to accumulate.
4. **History confidence gates offscreen reprojection and depth disocclusion.** UV outside the viewport zeroes the blend weight. Linear-eye depth of the current sample vs HistoryDepth at `reprojUV` scales the weight down (`smoothstep(0.02, 0.1)` relative delta). HistoryDepth is Depth32, copied by Transfer `CopyTexture` after TAA.
5. **TAA sharpen is luma-only and neighborhood-clamped.** Sharpen only the Y channel of YCoCg and clamp back into the neighborhood min/max. Sharpening chroma reintroduces color fringing; skipping the clamp reintroduces ringing.

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
6. **Editor Game view that is not redrawing produces identical consecutive screenshots.** "Two static frames match" is not a convergence proof by itself. Require a liveness gate first: Play mode, or at least one pair of captures that differ in the Game view region. Without that gate, report the capture as invalid and do not claim image results.
