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
11. **One physical quantity, one authority.** Atmosphere lives only on `AtmosphericalProfile`. There is no Volume override and no `AtmosphereParameter.Default()`. `FromProfile(null)` throws. `ThrowIfInvalid` rejects physical-range violations (thickness, Hillaire scatter/Mie/heights/ozone/sunAngle), not only zeros. Geometric sizes are meters. Hillaire scatter/absorption coefficients are stored per kilometer and converted to per-meter at bind (`AtmosphereParameter.ScatterPerKmToPerMeter`). Do not mix the two units in the compute shader. **Default Volume values come only from `InfinityDefaultVolumeProfileSettings` on `InfinityRenderPipelineGlobalSettings`. Quality-level Volume values come from the optional RP Asset `qualityVolumeProfile`. The constructor calls `VolumeManager.Initialize(defaultProfile, qualityProfile)` once; do not register the same profile again as a custom default. There is no second hardcoded default (no IdentityLut, no AtmosphereParameter.Default).** Atmosphere / DeferredShading / TAA are not Volume features.
12. **Validate the Player Volume type registry before recording.** CoreRP 17.6 Editor discovers types by reflection, but Player only registers components listed in the global default Profile. Missing types return null; do not rely on Editor behavior or substitute hardcoded values. The default Profile includes required color/exposure components and every consumed optional-feature type. Optional features record only when `VolumeComponent.IsActive()` is true (explicit `enable` or a documented intensity/mode threshold). `overrideState` marks a scene override; it is not a feature gate.
13. **Replaced rendering paths are deletable only after consumer closure and equivalent RG ownership are proved.** "Not wired yet" is not "obsolete". T02d separately audited and retired the nonfunctional GraphSRP binder/template stub after proving its unsupported capability and exact consumer closure; this is not permission to delete valid unintegrated hardware-RT assets.

## RenderGraph resource and pass shape

1. **Mip chains stay in one compute pass.** HiZ / ColorPyramid / bloom downsample are the same resource reading mip N-1 and writing mip N. RG tracks resources, not subresources; splitting per-mip into multiple passes creates false hazards and no extra parallelism. Loop dispatches inside one execute.
2. **LUT / froxel / cubemap generation is compute.** Do not introduce `Blit` / `SetRenderTarget` / `BuiltinRenderTextureType` to generate atmosphere tables. Cubemap faces are a `RWTexture2DArray`.
3. **Fallback raster depth flags follow `EDepthAccess`.** `ReadOnlyDepthStencil` is set only when the pass declared read-only depth without write.
4. **DebugView writes linear quantities only, immediately before Gizmo/WireOverlay and OutputTransform.** It overwrites `PostProcessBuffer`. Gizmo/WireOverlay then draw on that linear buffer; OutputTransform encodes them. DebugView is not a second encoding owner. `TAAConfidenceBuffer` is created only when `debugView != None` or an explicit normal-frame capture requests confidence for this frame. Capture must not change DebugView or history.

## Source schema and integration boundaries

- Native legacy fields and current effective values are different evidence. Before an explicit schema cleanup, identify the actual owner, bind source/meta and per-object/native field identities, and prove every non-approved field/reference unchanged. Preserve the currently running effective configuration; do not infer old physical units or material/light authority from discarded fields.
- An approved source schema change uses fresh durable backups and intent before targeted saves, exact allowed native deltas, stable GUID/local IDs and references, current scene/dirty preservation, second-save byte identity and a separate no-op. Preserve pending evidence and original exceptions; do not clear dirty flags or blindly restore source bytes. Loading/reload never authorizes migration.
- Completed preflight source/copy proofs may be reused only through immutable report/summary/artifact hashes, current input hashes and reviewed implementation equivalence. A failed original report remains failed; accepted replacement receipts are additional evidence. Explicitly retired source files and new diagnostic/docs inputs require exact delta receipts, never broad exclusions.
- Custom VFX Graph/GraphSRP outputs are **unsupported** in the current InfinityRP. The invalid binder/templates/includes were retired in T02d; do not restore an old HDRP identity, add a friend-assembly shim, or patch PackageCache to revive them. The package declares CoreRP/ShaderGraph/VFX 17.6; Example lock resolution is the current runtime fact. Standard Unity ParticleSystem rendering is owned by U41 / remaining N11 image quality; it is not proven by VFX retirement.
- Schema, compilation and image gates remain separate. The current Validation_Decal/LocalLights/Translucent captures show sky/dark-triangle/cube artifacts despite clean error windows; PLAN assigns these visual FAIL baselines to N07/N08/N11. Do not relabel these as correct frames or as schema-induced lighting changes without evidence.

## Known gaps (do not paper over)

S4–S8 closed the record paths below. Image / Frame Debugger / GPU-Trace quality stays `TODO(UNVERIFIED)` until a captured frame (and framedump where noted) is inspected. S9 is inspector / marker / leftover / docs only.

- **S4 closed:** HiZ 4-mip/batch, ColorPyramid 2-mip/batch, GTAO Trace → SpatialX → SpatialY → Temporal → BilateralUpsample. Temporal AO quality and upsample edges stay `TODO(UNVERIFIED)`. AO is applied once in DeferredShading on IBL; GTAO records only with Volume overrides.
- **S5 closed:** Atmosphere lives only on `AtmosphericalProfile`. Shared / View / IBL caches; SkyCubemap → L2 SH → GGX prefilter on miss. Cache-hit zero dispatch, SH energy, cubemap-array seam filtering, and still-frame IBL quality stay `TODO(UNVERIFIED)`.
- **S6 closed:** SSR/SSGI record RayMarch → Spatial → Temporal → Bilateral after OpaqueLightingPyramid, then Composite → OpaqueSceneColor. Temporal quality, history rejection, cubemap-less HiC mip cone, and still-frame / moving-camera framedump stay `TODO(UNVERIFIED)`.
- **S7 closed:** VolCloud / VolFog record in Phase 7 after T0 depth (CSM + ZBin + atmosphere LUTs + HistoryCache). FogComposite writes OpaqueSceneColor then MoveTexture to FoggedSceneColor. T0/T1/T2 record onto FoggedSceneColor + ReactiveMask + Motion (RendererList owner). TAA runs after T2. T0 glass / T1 refraction / T2 particle quality, fog/cloud temporal blend, and CSM/local-shadow in volume lighting stay `TODO(UNVERIFIED)` until Validation_Translucent / a framedump is captured.
- **S8 closed:** Phase 8 post is exposure multiply → Bloom (threshold on exposed scene-linear) → CombineLUT → Vignette → FilmGrain → DebugView (linear) → Gizmo/WireOverlay (linear) → OutputTransform → DisplayColorBuffer → Present. Volumes: Exposure / Bloom / Vignette / FilmGrain. Off defaults are Manual 0 EV and bloom/vignette/grain intensity 0. Auto exposure is a 256-bin 10–90 percentile histogram with HistoryCache EV. OutputTransform is the single transfer-encoding owner. Metal HDR display encode and non-Metal stay `TODO(UNVERIFIED)`. Gray-card Game sRGB [0.44,0.48] is still open (observed ~0.31).
- RTAO has a Volume component and `.raytrace` shader but no RG pass. RTAO/RTGI files stay; hardware RT is out of scope.
- ZBin records in Phase 2 when `LightContext.HasZBinningLightList()` (local light count > 0). Fog/cloud/deferred bind the live tile/zbin buffers, or LightContext empty buffers when no locals. Overlap vs shadow raster stays `TODO(UNVERIFIED)` without a GPU Trace.
- Directional light intensity has two conflicting consumers. `LightContext` packs `light.color * light.intensity` into `radiance.rgb` (and still writes intensity into `.a`). Fog/cloud now use `LightRadiance` (`.rgb` only). Remaining `color.rgb * color.a` consumers and CPU `sun.color * sun.intensity` paths must not be mixed in the same change. Converging these changes scene exposure, so it must be its own change with its own frame capture.
- CombineLUT is keyed (grading + film + output mode). Cache hit is zero dispatch; miss rebuilds the same frame. FinalCombine samples the 3D LUT with LinToLog after exposure+bloom. Neutral default-profile film/grade values produce a near-identity curve; there is no IdentityLut flag. LUT writes Rec.709 linear only — no sRGB/PQ/HLG. Metal HDR hardware capture and D3D12/Vulkan stay `TODO(UNVERIFIED)`.
- Several passes always record but emit no draws, so Frame Debugger may omit them: `RenderDBuffer` has no `DBufferPass` LightMode shader; `RenderLocalShadow` has no local shadow casters in the current scene; `CopyHistoryAntiAliasing` / `CopyHistoryDepth` / `CopyHistorySuperResolution` / `CopyHistoryOcclusion` / `CopyHistoryOcclusionDepth` / `CopyHistoryVolumetricFog` / `CopyHistoryVolumetricCloud` are Transfer `CopyTexture` events, not draws. `RenderCascadeShadow` still records and binds a depth target even when a cascade has 0 draws — if Game view shows cascade shadows while Frame Debugger omits the pass, that is a display-filter issue, not a skipped record. `TODO(UNVERIFIED)` until a captured Frame Debugger tree is inspected after this change.
- CombineLUT / AtmoLUT record in Phase 0 (zero RG-resource inputs). HiZ / HalfRes / AtmoLUT stay marked async. Whether they actually overlap is `TODO(UNVERIFIED)` without a GPU Trace.
- Native MotionPass preserves coverage for force-no-motion renderers and marks motion metadata invalid. Native previous rigid and skinned poses come from `NativeViewMotionHistory`, committed per view; `NativeMotion.hlsl` reads instanced previous matrices and the explicitly uploaded previous-vertex buffer. Original renderer and per-material PropertyBlocks are restored after recording.
- `Compute_ScreenSpaceReflection` unprojects HiZ with the global `Matrix_InvViewProj` (`renderIntoTexture = true` convention). Compute-side reconstruction uses `FlipY` (`renderIntoTexture = false`). That mismatch is a separate contract defect.
- `TAAJitter` still has SuperResolution consumers. TAA uses explicit current/previous framebuffer jitter UV and per-pixel sample age; do not remove the shared upload without checking SR consumers.
- TAA accumulation and color history are RGBA16F. `ComputeTemporalSharpen` is a separate output pass at strength 0.35; only unsharpened accumulation is copied into history. Sharpen rescales RGB by bounded luminance to preserve chromaticity.
- TAA geometry validation compares matched previous-surface depth from MotionMetadata against the previous jittered depth footprint. An interpolation footprint is a geometry witness, not a temporal sample-age multiplier. Wave acceptance remains in PLAN; numerical tests alone do not close motion quality.
- Preview cameras still lack temporal gating; independent defect, not in Spazon gate.

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
4. **Color history and depth history have distinct grids.** Resolved color uses `uv - motion`; raster depth uses `uv - motion + previousJitterUV - currentJitterUV`. MotionMetadata RGBA32F carries expected previous raw depth, current surface raw depth, validity and reserved data from one surface. TAA copies this current surface depth into R32F history. Invalid/offscreen/disoccluded history has zero weight; valid accumulation tracks sample age in RGBA16F alpha. Never compare current eye depth directly with previous-camera eye depth.
5. **Sharpening never feeds history.** Temporal blending is linear RGB, with reversible linear-luminance compression for neighborhood clipping. Output sharpening changes bounded luminance by scaling RGB together. Adding a constant to RGB via a YCoCg Y edit changes chromaticity and is not equivalent.
6. **SceneView history advances by accepted camera submissions.** A global frame gap alone does not reset or recycle a live SceneView. Cuts, projection/descriptor changes and failed submission reset history; new/reset frames use zero jitter. Per-view Mesh transform snapshots commit with camera matrices, validate scene/slot generation and upload through an explicit RG Transfer producer. Do not substitute another camera's most recent object pose. Native renderer-list draws use per-view rigid/deformed snapshots through instanced properties and an RG-owned vertex upload. Unity global previous poses are not a second temporal authority. Preview acceptance remains separate.

## Unity / Shader

- Components: prefer `[ExecuteAlways]` + `AddComponentMenu("InfinityRenderer/...")`.
- Asset private serialization: `[SerializeField] private T m_*`.
- Shader path: `InfinityPipeline/...`; tag `RenderPipeline=InfinityRenderPipeline`.
- Compute files: `Compute_<Feature>.compute`.
- LightMode tags must match `InfinityPassIDs` (`DepthPass`, `GBufferPass`, …).
- GBuffer encode/decode symmetry (YCoCg checkerboard from `SV_POSITION.xy` and BestFit) is owned by `GBufferPack.hlsl`.

## Comments and hygiene

- Comments in English; TODO format: `// TODO: <action>`.
- Do not commit large blocks of commented-out dead code.
- Do not introduce spellings that diverge further; fix typos when touching a symbol.
- Assembly identities use `Unity.RenderPipelines.Infinity.*`; keep references and friend declarations consistent. Old identities are allowed only in temporary explicit migration tooling and immutable historical evidence, never as compatibility shims.

## Model / agent work split (approved 0.4.0 normalization)

- Main agent owns orchestration, planning, code generation, and UI/UX visual inspection.
- Grok 4.6 owns search, code review, and batch verification. If Grok 4.6 is unavailable, search-only falls back to GPT-5.6 Luna. Verification never silently downgrades.
- Every batch candidate requires a Grok 4.6 verifier PASS plus the main agent's applicable self-test/review and visual evidence. Failed/partial/missing evidence returns to the main agent; dependent batches remain locked. No retry limit.
- PLAN.md U00–U82 is the only active task ledger. N00–N15 and earlier waves are archived in `Docs/History/`.
- Editor loading/script reload must never migrate, rebuild, delete or save user assets. Migrations are explicit and targeted, with current-byte backups, integrity, reopen and no-op checks.
- Preflight never activates source scenes or instantiates prefabs. Use bounded cancellable inspection. Managed-reference missing-type APIs apply only to deduplicated supported MonoBehaviour/ScriptableObject hosts, not native objects/importers.

## User-facing integration contracts

1. **Resources live only in GlobalSettings `IRenderPipelineResources` containers** with `[ResourcePath]`. The RP Asset carries no shader, blit-material, or LUT-texture references. Missing required resources fail at record/setup.
2. **Debug state is runtime.** Rendering Debugger (`InfinityDebugDisplaySettings`) owns DebugView and Volume/Lighting/Mesh/Temporal panels. Do not serialize `debugView` on the RP Asset.
3. **Additional data is the only per-Camera/Light Infinity storage.** `InfinityAdditionalCameraData` / `InfinityAdditionalLightData` auto-add with Undo from the Infinity inspector or GameObject menu. Do not expose a public `unityCamera` / `unityLight` field for the user to assign.
4. **uGUI is a first-class consumer.** `SupportedRenderingFeatures.active.rendersUIOverlay = true`. Screen Space Overlay uses `DrawUIOverlay` after OutputTransform. Screen Space Camera / World Space canvases use the translucent RendererList. Missing overlay support is a contract defect, not an optional skip.
5. **SSS quality lives only on Volume `numSamples`.** Diffusion profiles stay on the RP Asset. Volume does not override distance/albedo/maxRadius.
6. **Fixed-ratio SR:** RP Asset `renderScale` in [0.5, 1.0], default 1.0, Game cameras only when Super Resolution is enabled. One per-camera dimension descriptor owns internal vs display size.
7. **Dispose restores process-global state the pipeline set:** `SupportedRenderingFeatures.active`, `Shader.globalRenderPipeline`, and the `GraphicsSettings` flags captured at construction. Do not restore values that another pipeline now owns.
8. **No compatibility shims.** Replaced types (`CameraComponent`, `LightComponent`, resource fields on the RP Asset, `VolumeHasOverrides`) are deleted after consumer closure and an explicit Example-project migration. Do not add `FormerlySerializedAs`.

## Verification

- EditMode tests live under `Tests/Editor` (Test Framework dependency in `package.json`).
- Runtime Editor is not assumed available in every agent environment; mark unverified GPU/platform results as `TODO(UNVERIFIED)` in delivery notes.
- Steady-state invariant: `MeshScene.MatrixDuplicateRatio == 1.0`.

### Intermediate data location and mandatory cleanup

1. **One location for task-generated intermediate data.** Use `<Unity project root>/intermediate/<task-id>/<run-id>/`. For this workspace, the root is `/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/`, not the package repository root. This includes raw GPU/frame captures, repeated screenshots, test outputs, diagnostic log copies, temporary scripts, scratch projects and temporary archives. Configure every controllable producer explicitly; do not accumulate these files in `/tmp`, `/private/tmp`, the home directory or unrelated archive folders. If a tool forces another output location, promptly collect its output into the task directory and remove the redundant tool output once verified and no longer in use.
2. **Account for storage before and during capture.** Record the task's output paths, approximate size and owner. Check free space on the actual destination volume and estimate capture size from resolution, formats, buffers and frame count before a large run. Bound capture counts and avoid retaining duplicate raw buffers across repeated tests without a concrete validation need. An OS temporary directory is not an automatic cleanup policy.
3. **Clean after every completed task or subtask.** Stop producers, drain pending writes/readbacks, retain the required result summary, and delete that task's intermediate data as part of its completion. GPU/resource drain is not disk cleanup; verify both separately. Do not postpone cleanup until the user notices disk pressure.
4. **Carry-over requires a specific next consumer.** Intermediate data may remain between steps only when a named upcoming verification, review or repair needs it. Record the exact paths, reason and consuming step in the active task ledger. "May be useful later" or generic evidence preservation is not sufficient. Delete the retained data immediately after that consumer finishes.
5. **Final completion has no intermediate-data exemption.** Before declaring the overall task complete, delete all intermediate data owned by that task, including carry-over data, temporary copies and scratch outputs outside the canonical directory. Remove its empty task directory. If no other task owns data there, `intermediate/` must be empty or absent. A blocked or interrupted task must explicitly report any retained paths, sizes and next consumer; it must not be presented as completed.
6. **Evidence retention must not become hidden duplication.** Preserve original failure evidence unchanged while it is needed for diagnosis and acceptance. Keep concise final reports, test verdicts, relevant images and hashes as deliberate deliverables. When a separate user requirement explicitly requires a durable raw-evidence archive, verify that archive before deleting the intermediate originals; do not create an unsolicited permanent archive, or merely move temporary data elsewhere and call that cleanup. Archive verification alone does not complete local cleanup.
7. **Delete by task ownership, never by broad directory guesses.** Cleanup must not delete user source/assets, baked data, Unity's actual `Library` or engine-managed caches, user-owned logs, approved deliverables, or another active task's files. Use the recorded task paths, validate their resolved locations, and ensure no active producer or consumer still holds them. Do not recursively delete a shared `intermediate/` containing other tasks. Do not commit intermediate payloads to Git.
8. **Report cleanup as a delivery check.** Verify the task's intermediate paths are gone and check actual disk usage after cleanup. State the reclaimed space and any outstanding cleanup blocker in the final status. Do not claim "all cleaned" from zero outstanding GPU requests, successful archiving, or a delete command alone.

### Post-change self-check loop

Console-clean is not render-correct. A single depth-clear regression once held the Console at 0 errors while the Game view rendered nothing at all, because every raster pass had lost its geometry to a failed depth test.

1. Refresh through the explicitly targeted connected-Editor CLI command after N03.I cutover verification. If the connection is unavailable, diagnose it; do not silently invoke retired UI-driving scripts. Never launch a second Unity or a `-batchmode` run against a project held by a live editor.
2. Diagnose only the new `Logs/Editor.log` window past the pre-refresh byte mark, and fix the first real owner / order / lifetime / contract defect rather than the loudest message.
3. Capture a normal frame through the validated noninterfering capture entrypoint, obtain actual window evidence where required, and inspect it. Official camera-rerender screenshots are not equivalent. Any change touching clears, depth, attachments, pass order, or present is unverified until the image has been inspected.
4. Repeat until the new log window is free of InfinityRP errors **and** the captured frame is correct.
5. Only results confirmed by a captured frame may drop the `TODO(UNVERIFIED)` marker. Log-only checks keep it.
6. **Editor Game view that is not redrawing produces identical consecutive screenshots.** "Two static frames match" is not a convergence proof by itself. Require a liveness gate first: Play mode, or at least one pair of captures that differ in the Game view region. Without that gate, report the capture as invalid and do not claim image results.

### N04 graph and validation ownership

- `RGBuilder.Execute` propagates the original failure; it no longer returns a soft false result. Graph commands are isolated from enclosing frame/camera profiling scopes. Unreturned owned graph allocations retire into pools only after Submit.
- Attachment Load declares a read. Preserve depth with Store when subsequent effects or diagnostics consume it.
- Atmosphere/ZBin outputs are explicit consumer inputs; producer global prebindings are removed. Forward binds its declared GGX/SH/tile inputs through the raster command capability.
- Capture uses an explicitly selected active Game camera or requires one unambiguous active Game camera. No MainCamera-tag assumption and no substring match between Camera and SceneCamera.
- Capture Lighting before its ownership moves into scene-color stages. Do not query a retired semantic name at frame end or guess an alias.
- N04 RG/transaction gate passed independent Editor and actual Player fault/recovery verification. Refer to PLAN for immutable receipts; this does not close image-quality or later feature gates.

- LightContext uploads are graph Transfer producers. All light-buffer consumers declare RGBufferRef inputs and bind through capabilities. Imported GraphicsBuffer targets are not ComputeBuffer descriptors; do not silently clone or cast them. Replaced LightContext buffers retire after Submit. Overflow readback is requested only through dependent session-owned capture staging, never through an immediate callback on the production buffer.

- History write reservations must not mutate committed descriptors/resources. Swap both on commit, and preserve old committed history on pending rollback. Shared-cache validity comes from queue-accepted producers, independent of camera success. Failed camera/frame transactions force history reset; frame cleanup must preserve the first exception and still attempt Submit for earlier accepted work.

### Material route implementation checkpoint

- Use `MaterialRouteUtility` for Infinity surface route validation and explicit pass-state updates. Do not reintroduce independent Editor rounding or route parsing.
- `ShaderGUI.ValidateMaterial` is read-only. Inspector redraw alone must not apply derived pass state or mark every referencing MeshComponent dirty.
- Runtime callers changing `_SurfaceRoute` or `_TranslucentStage` must call `MaterialRouteUtility.ApplyPassState` to apply Unity Renderer pass enables. Exact route values and Deferred-only SSS are validated before derived mutations. Full asset migration and draw-parity acceptance remain tracked in U40.
- Agent model provenance comes from session `turn_context.model` matched to the agent path, not generic inherited role text or the model's own self-description.


## Unity interaction replacement (N03.I)

- The approved destination is one official Unity CLI connection to the explicitly selected Example project. Do not maintain automatic CLI -> script -> UI fallback execution. Computer Use is for actual visual inspection or necessary native UI only.
- N03.I in PLAN is the cutover authority. Until live replacement verification passes, old scripts are pending retirement, not an endorsed fallback. Do not claim CLI readiness from manifest installation alone.
- CLI commands must reuse one validation implementation, take explicit arguments and return status/evidence paths. Separate command acceptance from test/render completion. Queries never save assets; migrations remain explicit transactions with backups and exact native delta/no-op checks.
- Never use `unity run`, batch builds or another Editor to operate a project already held by an Editor. Confirm the target project and PID before commands; do not infer absence solely from a failed connection.
- Official com.unity.pipeline 0.6.0-exp.1 `screenshot` replaces camera.targetTexture and calls Camera.Render. It is prohibited for normal beauty, history, TAA/SR and actual-window acceptance. Use the existing RG transfer/capture ownership contract and independently observe the real window.
- Long-running operations expose status and cancellation/drain; a timeout never authorizes repeated submission, resource release or automatic Editor restart.
- The user selected Unity 6000.6.0f1 on 2026-09-09; the Editor is now installed and the216-test suite and basic CLI connection have passed scoped checks. Full CoreRP/ShaderGraph/VFX rendering compatibility must be verified before updating supported-version declarations. Earlier 6000.5.3f1 receipts remain historical scoped evidence.


### Verified CLI connection on Unity 6.6

- InfinityExample now uses its own embedded com.unity.pipeline0.6.0-exp.1 with an explicit IPv4 loopback listener correction. Preserve the canonical outer-project package and its local-fix provenance; do not patch Library/PackageCache or add another transport fallback.
- CLI editor_status, read-only eval, recompile and post-reload reconnection have actual evidence in cli-listener-fix-20260909. Use these verified commands for routine state/compile interaction now. N03.I still owns replacement of custom validation entrypoints; no claim that all legacy automation is retired.
- Commands always specify the target project. Use127.0.0.1 endpoints from official discovery. Keep bearer authentication, Origin and loopback enforcement; never print/archive credentials.
- Official screenshot remains unsuitable for normal-frame evidence. Connection acceptance does not change capture or visual acceptance rules.

### SceneView validation lifecycle

SceneView window creation, close, docking and restoration run only in the Editor update pump. The render callback may advance the bounded motion pose only. Closing a SceneView inside its own render/OnGUI stack destroys an active camera and render texture and can crash native outline drawing. Preserve tab ownership as well as position; setting EditorWindow.position can undock the view. The original idle AssetDatabase crash and the owned SceneView-close crash are separate investigations.
