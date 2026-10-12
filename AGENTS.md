# InfinityRP Agent Guide

## 0.5.0 upgrade override (user approved, 2026-10-11)

The user approved the 0.5.0 upgrade and convergence plan (PLAN.md, stages S0–S17). It supersedes the C0–C7 rule "Do not create branches, commit, push": 0.5.0 work happens on branch `claude/infinityrp-0.5`, one or more commits per stage, each stage marked by an `SNN:` commit-subject prefix and pushed. Building or packaging Players is still out of scope. Without an Editor, every stage must pass the offline gate in `Tools/StaticCheck/` before the next stage starts, and image results stay `TODO(UNVERIFIED)` until the user verifies them.

This document defines coding conventions and collaboration rules for `com.infinity.render-pipeline` (InfinityRP). It states rules and current code facts only; verification state lives in `Docs/FeatureStatus.md`, and archived plans and receipts live in `Docs/History/`.

## Architecture principles

1. **Entity data belongs to the entity; draw descriptions only reference it.** One logical object owns one `TransformId` / bounds. Submesh, material, and pass only add `MeshDraw` / template references.
2. **Cache stable structure; regenerate view results.** Pass bins and their command table are rebuilt only on structural change. Visibility, LOD, sort order, and compaction are per request/view.
3. **One public Mesh Drawing API for CPU and GPU backends.** Callers use `MeshDrawRequest` / `RGDrawListRef`; backends stay internal.
4. **RenderGraph-native lifecycle.** `CreateDrawList` records only; structural pass culling runs first; only live DrawLists schedule work; cleanup is graph-owned.
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
- `MeshDrawCommandKey` uses full structured equality; hash is lookup acceleration only.
- Filter / Grouping / Sort are three separate semantics — never one hash for all.

## Mesh Drawing Pipeline touchpoints

| Module | Responsibility |
|--------|----------------|
| `MeshScene` / `MeshSceneUpdate` | SoA tables, typed IDs + generation, dirty-page bitmaps, undo transactions |
| `GpuScene` | Dirty-page GPU upload of transforms, layers, baked lighting, bounds, flags |
| `PassBinStore` / `MeshDrawCommandKey` | Per-pass filter, structural sort, grouping into command spans |
| `MeshVisibilityShare` | Intern CPU cull results by `MeshVisibilitySignature` |
| `MeshGpuVisibilityCache` | GPU `CullFrustum` result per view key |
| `MeshDrawCompiler` | `MeshPassExtractJob` (commands × visibility → draw commands) |
| `MeshCandidateTableStore` | Per-pass GPU command and candidate tables |
| `MeshDrawPipeline` / `MeshWorld` | Facades: extract / resolve / submit; per-scene ownership |
| `MeshDrawGPUBackend` | Per-DrawList GPU payloads, compaction, indirect args + Auto fallback |
| `RGDrawList*` | Graph registry; not an `ERGResourceType` |

### Mesh Drawing Pipeline conventions (required)

1. **`MeshSceneUpdate` must fully undo.** Every mutating op pushes an undo entry; `Dispose` without `Commit` rolls back via the undo log (live counts / object relations) and restores revision + dirty ranges. Free-list membership and highWater stay owned by Free*/Restore*/deferred reclaim — do not truncate free-lists on rollback. Do not add half-applied structural edits.
2. **Deferred section/material reclaim inside a transaction.** When `refCount` hits zero during `BeginUpdate`…`EndUpdate`, queue pending reclaim — do not free immediately. `Commit` flushes pending reclaim; `Rollback` discards pending lists after undo restores refCounts.
3. **`MeshDrawCommandKey` and `MeshVisibilitySignature` use structured `Equals`.** Hash codes are lookup acceleration only; never treat hash equality as key equality. (The GPU visibility cache still packs its view key into one `ulong`; see Known gaps.)
4. **Visibility ownership:** `MeshVisibilityShare` interns results; RDG `ReleaseAllDrawLists` (paired `Release`) owns handle lifetime. Do not leave TempJob cull arrays alive across frames.
5. **GPU payload is per-DrawList.** Rent/return payload buffers from the backend pool keyed to a DrawList lifetime.
6. **No process-global static indirect / args buffer** shared across concurrent DrawLists or cameras.
7. **GPU cull is always instance-indexed.** Bounds / visibility / candidate indices use instance slots; compacted indices written for shading are **transform** indices. CPU `MeshDrawList.instanceIndices` remain transform indices for procedural submit.
8. **Sort encoding must explicitly saturate** (signed and unsigned 16-bit segments). Never rely on silent truncation / wrap.
9. **`ERenderingLayer` is the shared Mesh/Light bit-flag mask** (`enum : byte` + `[Flags]`; `Everything = 0xFF`). Do not reintroduce an int-index `renderLayer` / `1 << layer` convention.
10. **Physical GPU resources retire only after frame-end `ScriptableRenderContext.Submit()`.** `ReleaseAll` / `ReleaseAllDrawLists` perform logical cleanup + `RetirePayload` (and CPU rented-buffer retire) only. Call `FlushRetiredPayloads` / `FlushRetiredBuffers` after Submit to drain retired queues. Never Release/Return a payload still referenced by in-flight GPU work.
11. **No mid-recording ComputeBuffer Release / EnsureCapacity rebuild.** Pre-record: `ComputePayloadBudget` then one `EnsureCapacity`. During CommandBuffer recording / submit: `RequireCapacity` check only — never grow or recreate buffers already bound to a live command buffer.
12. **`TransformId` ownership is exclusive (1:1).** One live transform maps to at most one `MeshInstance`. `CreateInstance` must reject an already-owned transform. Do not add refcount-free multi-instance transform sharing.
13. **`BufferDescriptor` / `TextureDescriptor`: field-level `Equals` is authoritative.** `GetHashCode` is lookup acceleration only (same principle as `MeshDrawCommandKey`).

Public RDG usage:

```csharp
RGDrawListRef draws = graph.CreateDrawList(meshView, MeshPassId.GBuffer);
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
13. **Replaced rendering paths are deletable only after consumer closure and equivalent RG ownership are proved.** "Not wired yet" is not "obsolete". Without an Editor, consumer closure is proved by the differential compile gate plus a repository-wide search (including `Editor/Cli`, docs and tests). Valid unintegrated hardware-RT assets are kept.

## RenderGraph resource and pass shape

1. **Mip chains stay in one compute pass.** HiZ / ColorPyramid / bloom downsample are the same resource reading mip N-1 and writing mip N. RG tracks resources, not subresources; splitting per-mip into multiple passes creates false hazards and no extra parallelism. Loop dispatches inside one execute.
2. **LUT / froxel / cubemap generation is compute.** Do not introduce `Blit` / `SetRenderTarget` / `BuiltinRenderTextureType` to generate atmosphere tables. Cubemap faces are a `RWTexture2DArray`.
3. **Fallback raster depth flags follow `EDepthAccess`.** `ReadOnlyDepthStencil` is set only when the pass declared read-only depth without write.
4. **DebugView writes linear quantities only, immediately before Gizmo/WireOverlay.** It overwrites `PostProcessBuffer`. Gizmo/WireOverlay then draw on that linear buffer, which becomes the display-linear `DisplayColorBuffer`; the final Present pass is the only transfer-encoding owner. DebugView is not a second encoding owner. `TAAConfidenceBuffer` is created only when `debugView != None` or an explicit normal-frame capture requests confidence for this frame. Capture must not change DebugView or history.

## 0.5.0 architecture rules

1. **Quality presets resolve before passes.** A Volume feature exposes `quality` (`EQualityLevel`: Low / Medium / High / Ultra / Custom) plus all of its low-level parameters. `Resolve()` returns a low-level settings struct (`F<Feature>Settings`): `Custom` reads the Volume parameters, every other level reads a hardcoded preset table in code (`Runtime/RenderPipeline/Quality/QualityPresets.<Feature>.cs`). Passes consume only the settings struct and never branch on `EQualityLevel`.
2. **Light data authority.** Unity `Light` owns color, intensity, temperature, range, spot angles, shadow mode/strength/bias/near plane, culling mask, rendering layers, bake type, bounce intensity and the cookie texture. `InfinityAdditionalLightData` owns the source shape in Unreal form (`sourceRadius`, `softSourceRadius`, `sourceLength`, `sourceWidth`/`sourceHeight`, barn doors, `sourceAngle`) and Infinity-only fields (shadow filter, update mode, contact shadow, volumetric, weights, IES, distance fade). The component writes the derived native fields (`type`, `shapeRadius`, `areaSize`, `shadowAngle`) only on user edits and API setters, never on load. A light without the component derives its shape from the native fields. The GPU record has no shape enum: shapes are parameter combinations.
3. **Persistent cross-frame caches are transactions.** Atlases that live across cameras and frames (reflection probes, cookies/IES, cached shadows, sky IBL) follow the `AtmosphereSharedCache` pattern: pending reservations, commit only on queue-accepted production (`MarkProduced`), `DiscardUnproducedPending` for failed cameras, physical retirement after Submit. Cross-frame keys use instance IDs, never per-frame record indices.
4. **Content-dependent gating happens after light/probe collection.** Feature configuration (Volume, asset, kernels, dependency closure) runs before culling; features whose recording depends on scene content (tile masks, moment shadows, probe/cookie atlas updates, local contact shadows) are decided after `LightContext.Build`.
5. **GPU records have one layout.** A C# struct uploaded to a shader carries a `// GPU-RECORD: <HlslStruct> <hlsl file>` marker; `Tools/StaticCheck/static_check.py` and an EditMode test compare field order and types with the HLSL struct.

## Integration exceptions (explicit and bounded)

- **Shader Graph.** Shader Graph 17.6 declares `Target`/`SubTarget` internal (`// TODO: Public`) and grants access only to Unity assemblies. InfinityRP compiles its target into `Unity.ShaderGraph.Editor` through the single asmref at `Editor/ShaderGraph/`. Only code under that folder may use Shader Graph internals. It references Infinity only by strings (shader paths, include paths, Material GUI class names); `static_check.py` verifies those strings. Nothing in Runtime may depend on it. No other assembly may use asmref injection, reflection into Unity internals or friend-name spoofing.
- **Adaptive Probe Volumes.** CoreRP binds APV runtime resources with `ProbeReferenceVolume.BindAPVRuntimeResources(CommandBuffer, ...)` and `UpdateShaderVariablesProbeVolumes(CommandBuffer, ...)`. These are CoreRP-owned, persistent, read-only globals. They are bound in the per-camera setup block on the pipeline command buffer, before the RenderGraph records; no RG encoder exposes a `CommandBuffer` for them.

## Source schema and integration boundaries

- Native legacy fields and current effective values are different evidence. Before an explicit schema cleanup, identify the actual owner, bind source/meta and per-object/native field identities, and prove every non-approved field/reference unchanged. Preserve the currently running effective configuration; do not infer old physical units or material/light authority from discarded fields.
- An approved source schema change uses fresh durable backups and intent before targeted saves, exact allowed native deltas, stable GUID/local IDs and references, current scene/dirty preservation, second-save byte identity and a separate no-op. Preserve pending evidence and original exceptions; do not clear dirty flags or blindly restore source bytes. Loading/reload never authorizes migration.
- Renaming a serialized field (Volume parameter, component or asset field) resets user values because `FormerlySerializedAs` is not used. Prefer adding fields. Every unavoidable rename is listed in the verification playbook of the change.
- Custom VFX Graph outputs are **unsupported**. Do not restore an old HDRP identity, add a friend-assembly shim, or patch PackageCache to revive them. Standard Unity ParticleSystem rendering goes through the translucent RendererList.
- Schema, compilation and image gates remain separate. Passing a schema or compile check does not make a frame correct.

## Known gaps (do not paper over)

Per-feature verification state is in `Docs/FeatureStatus.md`. The technical defects below are known in code; each is owned by a PLAN.md stage. Do not hide them behind fallbacks.

- Mesh: pass `defaultSort` plans are not applied (bins sort only by `CompareStructural`). `PrepareIndirect` can call `TryEnsureCapacity` on the recording path; it is dormant because the budget matches the pre-record size.
- Validation scenes (Decal, LocalLights, Translucent) show sky black bands, large dark triangles and cube blotches. Their cause is undiagnosed.
- Light types without a realtime representation yet (Disc, Pyramid, Box, Tube) are skipped and counted in the Rendering Debugger (S6 implements them).
- Cascade count is fixed at 4 (S9).
- `ContactShadowCS` reads `g_LightRecordBuffer[0]` and the result applies only to directional lights in Deferred; Forward passes 1 (S10).
- Forward loops all local lights instead of the tile/z-bin lists; Deferred's bin filter is bypassed for empty bins; bins are linear in depth (S7).
- `maxDrawDistance` / `maxDrawDistanceFade` are uploaded but no shader reads them (S6).
- `TAAJitter` has SuperResolution consumers. TAA uses explicit current/previous framebuffer jitter UV and per-pixel sample age; do not remove the shared upload without checking SR consumers.
- TAA accumulation and color history are RGBA16F. `ComputeTemporalSharpen` is a separate output pass at strength 0.35; only unsharpened accumulation is copied into history. A single-dispatch TAA + sharpen is not implemented.
- Several passes always record but emit no draws, so Frame Debugger may omit them (`RenderDBuffer` without a DBufferPass shader, `RenderLocalShadow` without local casters, the `CopyHistory*` Transfer passes). `RenderCascadeShadow` records and binds its target even with 0 draws.
- HiZ / HalfRes / AtmosphereLUT are marked async; actual overlap is unmeasured.
- RTAO records only when UnifiedRayTracing `RayTracingContext` + `IRayTracingAccelStruct` + `VisibilityRTAO.urtshader` are ready and the accel has instances; otherwise GTAO owns AO and the Debugger shows the reason. Player shader load is unverified.
- CombineLUT is keyed (grading + ACES + output mode); cache hit is zero dispatch. The LUT writes Rec.709 linear only.

## Render target and depth conventions

1. **`EnableNativeRenderPass(false)` is allowed only where a native-RP attachment is impossible.** Today that is Gizmo / WireOverlay (Unity forbids drawing gizmos inside `BeginRenderPass`), Present (the backbuffer has no owning `RenderTexture`, so `AttachmentDescriptor.graphicsFormat` cannot be resolved), and UI Overlay (`CreateUIOverlayRendererList` / `DrawRendererList` is uGUI's overlay list; Unity does not expose that list as a native-RP attachment payload). A new use needs the same kind of hard API reason, not convenience. Overlay is not `DrawUIOverlay`.
2. **Clear depth and sampled depth are different values.** `ClearRenderTarget(depth)` and `AttachmentDescriptor.clearDepth` are normalized by Unity: `1.0` always means far plane and the backend flips it on reversed-Z platforms. Use `GraphicsUtility.ClearDepthFar` there. Only shader-side comparisons against a sampled depth buffer use `GraphicsUtility.SampledFarDepth`. Feeding one into the other clears the depth buffer to the near plane and silently kills geometry in every raster pass.
3. **A color-only raster target binds color only.** Never pass an empty `RenderTargetIdentifier` as the depth target of a `RenderTargetBinding`; Unity reports it as `temporary render texture not found`. Use the color-only `SetRenderTarget` overload.
4. **The backbuffer enters RDG through `ImportBackbuffer`.** It is an imported resource and therefore skips Create/Release. `RTHandle.SetTexture` is CoreRP-internal, so rebinding reallocates when the identifier changes.
5. **`HistoryCache` reallocates only via `TextureDescriptor.Equals`.** That field-level compare is the authority (same rule as `MeshDrawCommandKey`). Do not rebuild a second compare through `RenderTextureDescriptor`: the implicit conversion forced `depthStencilFormat = None` and `mipCount = -1`, so history textures could reallocate every frame, latch `resetHistory`, and silently kill TAA accumulation.

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
6. **SceneView history advances by accepted camera submissions.** A global frame gap alone does not reset or recycle a live SceneView. Cuts, projection/descriptor changes and failed submission reset history; new/reset frames use zero jitter. Per-view Mesh transform snapshots (`MeshViewMotionHistory`) commit with camera matrices, validate scene/slot generation and upload through an explicit RG Transfer producer. Do not substitute another camera's most recent object pose. Native renderer-list draws use Unity's own previous-frame data (`unity_MatrixPreviousM`, skinned previous positions); the pipeline keeps no second native pose history and sets no per-renderer PropertyBlocks.

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

## Roles

- An implementing agent owns planning, code and self-tests. An independent verifier reviews candidate batches; a failed, partial or missing verification returns the batch and locks dependent batches. Verification never silently downgrades. Model names are not part of this contract.
- PLAN.md is the only active task ledger. Earlier ledgers are archived in `Docs/History/`.
- Editor loading/script reload must never migrate, rebuild, delete or save user assets. Migrations are explicit and targeted, with current-byte backups, integrity, reopen and no-op checks.
- Preflight never activates source scenes or instantiates prefabs. Use bounded cancellable inspection.
- Without an Editor, a change passes the offline gate (`Tools/StaticCheck/`) and stays `TODO(UNVERIFIED)` for images.

## User-facing integration contracts

1. **Runtime resources live only in GlobalSettings `IRenderPipelineResources` containers** with `[ResourcePath]` (compute shaders, blit material, LUT textures). The RP Asset never references runtime shaders; its shader/material fields are optional overrides for the default materials Unity assigns to GameObject-menu objects. Missing required resources fail at record/setup. `ResolveDefaultVolumeProfile` throws when the GlobalSettings slot is empty; there is no `Resources.Load` fallback. Player shader stripping lives in `Editor/ShaderStripper/`.
2. **Debug state is runtime.** Rendering Debugger (`InfinityDebugDisplaySettings`) owns DebugView and its panels. Do not serialize `debugView` on the RP Asset. Do not add a Debugger control without a consumer.
3. **Additional data is the only per-Camera/Light Infinity storage.** `InfinityAdditionalCameraData` / `InfinityAdditionalLightData` are added with Undo by `ObjectFactory.componentWasAdded` and by the Infinity inspectors. Do not expose a public `unityCamera` / `unityLight` field for the user to assign.
4. **uGUI is a first-class consumer.** `SupportedRenderingFeatures.active.rendersUIOverlay = true`. Screen Space Overlay uses `CreateUIOverlayRendererList` + `DrawRendererList` on the display-linear `DisplayColorBuffer` before final Present, only for `CameraType.Game` with no target texture (`UIOverlayPass`). SceneView never draws the display overlay list. Screen Space Camera / World Space canvases use the translucent RendererList.
5. **SSS quality lives only on Volume `numSamples`.** Diffusion profiles stay on the RP Asset.
6. **Fixed-ratio SR:** RP Asset `renderScale` in [0.5, 1.0], default 1.0, Game cameras only when Super Resolution is enabled. One per-camera dimension descriptor owns internal vs display size. Explicit Unity-owned camera targets are imported by identifier with their descriptor; dimensions freeze after BeginCameraRendering callbacks.
7. **Dispose restores process-global state the pipeline set:** `SupportedRenderingFeatures.active`, `Shader.globalRenderPipeline`, and the `GraphicsSettings` flags captured at construction.
8. **No compatibility shims.** Replaced types are deleted after consumer closure and an explicit Example-project migration. Do not add `FormerlySerializedAs`.

## Verification

- EditMode tests live under `Tests/Editor` (Test Framework dependency in `package.json`).
- Runtime Editor is not assumed available in every agent environment; mark unverified GPU/platform results as `TODO(UNVERIFIED)` in delivery notes.
- Steady-state invariant: `MeshScene.MatrixDuplicateRatio == 1.0`.

### Intermediate data location and mandatory cleanup

1. **One location for task-generated intermediate data.** Use `<Unity project root>/intermediate/<task-id>/<run-id>/`. For the maintainer's workspace the root is the Example project's `intermediate/`, never the package repository root (`.gitignore` excludes `intermediate/`). This includes raw GPU/frame captures, repeated screenshots, test outputs, diagnostic log copies, temporary scripts, scratch projects and temporary archives. Configure every controllable producer explicitly; do not accumulate these files in `/tmp`, `/private/tmp`, the home directory or unrelated archive folders. If a tool forces another output location, promptly collect its output into the task directory and remove the redundant tool output once verified and no longer in use.
2. **Account for storage before and during capture.** Record the task's output paths, approximate size and owner. Check free space on the actual destination volume and estimate capture size from resolution, formats, buffers and frame count before a large run. Bound capture counts and avoid retaining duplicate raw buffers across repeated tests without a concrete validation need. An OS temporary directory is not an automatic cleanup policy.
3. **Clean after every completed task or subtask.** Stop producers, drain pending writes/readbacks, retain the required result summary, and delete that task's intermediate data as part of its completion. GPU/resource drain is not disk cleanup; verify both separately. Do not postpone cleanup until the user notices disk pressure.
4. **Carry-over requires a specific next consumer.** Intermediate data may remain between steps only when a named upcoming verification, review or repair needs it. Record the exact paths, reason and consuming step in the active task ledger. "May be useful later" or generic evidence preservation is not sufficient. Delete the retained data immediately after that consumer finishes.
5. **Final completion has no intermediate-data exemption.** Before declaring the overall task complete, delete all intermediate data owned by that task, including carry-over data, temporary copies and scratch outputs outside the canonical directory. Remove its empty task directory. If no other task owns data there, `intermediate/` must be empty or absent. A blocked or interrupted task must explicitly report any retained paths, sizes and next consumer; it must not be presented as completed.
6. **Evidence retention must not become hidden duplication.** Preserve original failure evidence unchanged while it is needed for diagnosis and acceptance. Keep concise final reports, test verdicts, relevant images and hashes as deliberate deliverables. When a separate user requirement explicitly requires a durable raw-evidence archive, verify that archive before deleting the intermediate originals; do not create an unsolicited permanent archive, or merely move temporary data elsewhere and call that cleanup. Archive verification alone does not complete local cleanup.
7. **Delete by task ownership, never by broad directory guesses.** Cleanup must not delete user source/assets, baked data, Unity's actual `Library` or engine-managed caches, user-owned logs, approved deliverables, or another active task's files. Use the recorded task paths, validate their resolved locations, and ensure no active producer or consumer still holds them. Do not recursively delete a shared `intermediate/` containing other tasks. Do not commit intermediate payloads to Git.
8. **Report cleanup as a delivery check.** Verify the task's intermediate paths are gone and check actual disk usage after cleanup. State the reclaimed space and any outstanding cleanup blocker in the final status. Do not claim "all cleaned" from zero outstanding GPU requests, successful archiving, or a delete command alone.

### Post-change self-check loop

Console-clean is not render-correct. A single depth-clear regression once held the Console at 0 errors while the Game view rendered nothing at all, because every raster pass had lost its geometry to a failed depth test.

1. Refresh through the explicitly targeted connected-Editor CLI command. If the connection is unavailable, diagnose it; do not silently fall back to UI-driving scripts. Never launch a second Unity or a `-batchmode` run against a project held by a live editor.
2. Diagnose only the new `Logs/Editor.log` window past the pre-refresh byte mark, and fix the first real owner / order / lifetime / contract defect rather than the loudest message.
3. Capture a normal frame through the validated noninterfering capture entrypoint, obtain actual window evidence where required, and inspect it. Official camera-rerender screenshots are not equivalent. Any change touching clears, depth, attachments, pass order, or present is unverified until the image has been inspected.
4. Repeat until the new log window is free of InfinityRP errors **and** the captured frame is correct.
5. Only results confirmed by a captured frame may drop the `TODO(UNVERIFIED)` marker. Log-only checks keep it.
6. **Editor Game view that is not redrawing produces identical consecutive screenshots.** "Two static frames match" is not a convergence proof by itself. Require a liveness gate first: Play mode, or at least one pair of captures that differ in the Game view region. Without that gate, report the capture as invalid and do not claim image results.

### RenderGraph failure and ownership contracts

- `RGBuilder.Execute` propagates the original failure; it never returns a soft false result. Graph commands are isolated from enclosing frame/camera profiling scopes. Unreturned owned graph allocations retire into pools only after Submit.
- Attachment Load declares a read. Preserve depth with Store when subsequent effects or diagnostics consume it.
- Atmosphere/ZBin outputs are explicit consumer inputs; producers do not prebind globals. Forward binds its declared GGX/SH/tile inputs through the raster command capability.
- Capture uses an explicitly selected active Game camera or requires one unambiguous active Game camera. No MainCamera-tag assumption.
- Capture Lighting before its ownership moves into scene-color stages. Do not query a retired semantic name at frame end or guess an alias.
- LightContext uploads are graph Transfer producers. All light-buffer consumers declare RGBufferRef inputs and bind through capabilities. Imported GraphicsBuffer targets are not ComputeBuffer descriptors; do not silently clone or cast them. Replaced LightContext buffers retire after Submit.
- History write reservations must not mutate committed descriptors/resources. Swap both on commit, and preserve old committed history on pending rollback. Shared-cache validity comes from queue-accepted producers, independent of camera success. Failed camera/frame transactions force history reset; frame cleanup preserves the first exception and still attempts Submit for earlier accepted work.

## Unity Editor interaction

- Automated Editor interaction uses the official Unity CLI connection to an explicitly selected project (`Editor/Cli`, compiled only with `INFINITY_UNITY_CLI`). Commands take explicit arguments, return status/evidence paths, and never save assets. Use `127.0.0.1` endpoints; keep bearer authentication and loopback enforcement; never print or archive credentials.
- Never use `unity run`, batch builds or a second Editor against a project already held by an Editor. Confirm the target project and PID before commands.
- `com.unity.pipeline` `screenshot` replaces `camera.targetTexture` and calls `Camera.Render`. It is not a normal-frame, history, TAA/SR or actual-window capture. Use the RG transfer capture contract and observe the real window.
- Long-running operations expose status and cancellation/drain; a timeout never authorizes repeated submission, resource release or Editor restart.


### Material routes

- Use `MaterialRouteUtility` for Infinity surface route validation and explicit pass-state updates. Do not reintroduce independent Editor rounding or route parsing.
- `ShaderGUI.ValidateMaterial` is read-only. Inspector redraw alone must not apply derived pass state or mark every referencing MeshComponent dirty.
- Runtime callers changing `_SurfaceRoute` or `_TranslucentStage` must call `MaterialRouteUtility.ApplyPassState` to apply Unity Renderer pass enables. Exact route values and Deferred-only SSS are validated before derived mutations.

### SceneView validation lifecycle

SceneView window creation, close, docking and restoration run only in the Editor update pump. The render callback may advance the bounded motion pose only. Closing a SceneView inside its own render/OnGUI stack destroys an active camera and render texture and can crash native outline drawing. Preserve tab ownership as well as position; setting `EditorWindow.position` can undock the view.
