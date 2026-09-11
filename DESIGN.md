# InfinityRP Mesh Drawing Pipeline — Design (as implemented)

> Active delivery is governed by PLAN.md U00–U82 (0.4.0 normalization). Historical N00–N15 receipts live in Docs/History. Main agent implements; Grok 4.6 verifies batches.

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
- **Shadow MeshDraw filter (Cascade + Local):** `layerMask = light.cullingMask`; `filterRenderingLayers = true`, with the validated native `Light.renderingLayerMask` as the caster mask. Unity shadow RendererLists enable `useRenderingLayerMaskTest` against the same native mask.
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

`ERenderingLayer : byte` with `[Flags]` (`Nothing` … `Everything = 0xFF`) is the shared Mesh/Light mask. `MeshFilterProgram` tests rendering layers only when `filterRenderingLayers` is enabled. The enabled filter rejects `(instance.mask & filter.mask) == 0`; camera visibility leaves this filter disabled, including for zero-layer surfaces.

| Pass family | `layerMask` | `renderingLayerMask` |
|-------------|-------------|----------------------|
| Depth / GBuffer / Forward / Motion | camera / pass default (`~0` open) | Layer filtering disabled; native RendererLists use an open mask |
| Cascade / Local shadow MeshDraw | `light.cullingMask` | Validated native `Light.renderingLayerMask`; layer filtering enabled |

`MeshComponent.renderingLayer` is flags, not an int layer index. Do not reintroduce `1 << renderLayer` indexing.

## 15. Full rendering pipeline (S7 / S8 locked order)

`InfinityRenderPipeline.RecordRG` records in these phases. Do not reorder without a new stage and a captured frame.

```text
Phase 0  CombineLUT + AtmosphericLUT          (zero RG-resource inputs; async-eligible)
Phase 1  Depth → DBuffer → GBuffer → Motion
Phase 2  HiZ + HalfResDownsample + ZBin       (async-eligible)
Phase 3  CascadeShadow + LocalShadow
Phase 4  reserved (VolFog moved to Phase 7)
Phase 5  GTAO → CopyHistoryOcclusion → ContactShadow
Phase 6  Deferred → Forward → SSS → AtmosphericSkyAndFog
         → OpaqueLightingPyramid → SSR/SSGI (+ history) → ScreenSpaceComposite
         → OpaqueSceneColor
Phase 7  TranslucentDepth → VolCloud (+ history) → VolFog (+ history)
         → FogComposite → FoggedSceneColor
         → T0 → ColorPyramid → T1 → T2
Phase 8  TAA or SuperResolution (+ history) → Post (Exposure → Bloom → CombineLUT
         → Vignette → FilmGrain) → DebugView → Gizmo/WireOverlay (linear PostProcessBuffer)
         → OutputTransform → DisplayColorBuffer → Present
```

Contracts that stay locked:

- Atmosphere lives only on `AtmosphericalProfile`. Volume does not override it.
- History lives only in `HistoryCache`. CopyHistory is Transfer `CopyTexture` after the producer.
- `DisplayColorBuffer` is the present source. OutputTransform is the single transfer-encoding owner.
- Present stays Raster. Gizmo / WireOverlay / Present disable native RP for API reasons only.
- Hardware RT, Baked GI, DOF, SR expansion, XR, MSAA, and dynamic resolution are out of this delivery.

Image / Frame Debugger / GPU-Trace results stay `TODO(UNVERIFIED)` until captured. See [Docs/FullRendering-Delivery-Report.md](Docs/FullRendering-Delivery-Report.md).

## 16. Settings layers, Default Volume, Output authority, Gizmo-before-encode

Project-wide resources and the default Volume profile live on `InfinityRenderPipelineGlobalSettings` (`IRenderPipelineGraphicsSettings` / `IRenderPipelineResources` with `[ResourcePath]`). Per-quality authoring lives on `InfinityRenderPipelineAsset` (features, shadows, `renderScale`, output, optional `qualityVolumeProfile`, diffusion profiles, atmosphere). Per-scene art lives on Volume / Profile. Per-camera / per-light Infinity fields live on additional-data components.

Pipeline creation requires a complete default Volume registry. The constructor calls `VolumeManager.Initialize(defaultProfile, qualityProfile)` once; disposal deinitializes that manager and restores process-global graphics flags captured at construction. Required color/exposure consumers read the resolved camera stack. Optional features record when `IsActive()` is true. CombineLUT has no inactive-component constant fallback. Atmosphere remains separately owned by AtmosphericalProfile.

OutputTransform resolves the backbuffer format in this order (first hit wins; `GraphicsFormat.None` is not a hit):

1. `camera.targetTexture.graphicsFormat`
2. `camera.activeTexture.graphicsFormat`
3. Editor Game/Scene present target (`PlayModeView` / `SceneView` RT that Unity already allocated)
4. last successfully resolved format for that camera

All missing throws at record time. HDR `HDROutputSettings.graphicsFormat` and `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)` are not authorities.

Gizmo / WireOverlay record on linear `PostProcessBuffer` after post/DebugView and before OutputTransform, so editor overlays are encoded with the scene. They keep `EnableNativeRenderPass(false)` because Unity forbids gizmos inside `BeginRenderPass`. OutputTransform remains the single transfer-encoding owner; `DisplayColorBuffer` remains the present source.

## 17. DebugView, Rendering Debugger, and SceneView temporal gating

`EDebugView` writes a linear quantity into `PostProcessBuffer` in one compute pass immediately before Gizmo/WireOverlay. It is not a second encoding owner. The active view comes from `InfinityDebugDisplaySettings` (Rendering Debugger), not from a serialized RP Asset field. `TAAConfidenceBuffer` exists only when `debugView != None` or an explicit normal-frame capture requests confidence for this frame. Optional AO/SSR/SSGI views that were not recorded this frame use a dedicated Missing kernel (magenta), never an invalid RT bind.

SceneView uses the full pipeline. Volume selection on SceneView uses the unique active Game camera's additional-data mask/trigger when one exists; otherwise `~0` and the SceneView transform. Preview cameras use the default profile only. A new camera state or a skipped frame (`lastSeenFrame` gap > 1) sets `historyReset` and disables jitter that frame. SceneView states recycle after 120 unseen frames so docking the tab does not rebuild history every time. Game recycle stays 8. Preview temporal gating remains a documented independent defect.

## 18. User-facing integration

| Surface | Owner |
|---|---|
| Compute shaders, blit material, BestFit LUT | GlobalSettings `IRenderPipelineResources` |
| Default Volume profile | GlobalSettings `InfinityDefaultVolumeProfileSettings` |
| Quality Volume profile, SR `renderScale`, shadows, atmosphere, diffusion profiles | RP Asset |
| Camera Volume mask/trigger, SR override | `InfinityAdditionalCameraData` |
| Light layers, weights, volumetric, contact, distance | `InfinityAdditionalLightData` |
| DebugView / Volume dump / Mesh stats | Rendering Debugger |
| Screen Space Overlay UI | `DrawUIOverlay` after OutputTransform |
| Screen Space Camera / World Space UI, particles, lines | Translucent RendererList |

GameObject Camera/Light creation and the Infinity inspectors auto-add additional data with Undo. `GameObject > Create` default materials come from the RP Asset (`defaultMaterial`, `defaultParticleMaterial`, `defaultLineMaterial`, `defaultTerrainMaterial`, `default2DMaterial`).

Hardware ray tracing (`RayTracingShader`) is D3D12/console only. RTAO uses CoreRP `UnifiedRayTracing` (hardware on D3D12, compute BVH on Metal). `SystemInfo.supportsRayTracing` is not a Metal capability claim.


## Rendering integration and serialized-asset authority

The approved 0.4.0 normalization graph is authoritative in `PLAN.md` (U00–U82). Historical mesh/N00–N15 convergence does not imply complete render or platform acceptance. N01 applied package 0.3.0 and Infinity identities on Unity 6000.5.3f1/CoreRP 17.5.0 (historical). The active Editor compiled/reloaded the new assemblies. Seven binary assets received 22 explicit class-identifier updates with exact remaining native-data checks and a separate no-op. Final N01 type verification/independent acceptance passed. N02 subsequently executed 136 passing Editor tests; the first Player build failed on a missing Mac IL2CPP module, so Player acceptance remains open.

Atmosphere's sole physical/configuration owner is RP Asset → AtmosphericalProfile → AtmosphereParameter.FromProfile. T02c persisted the already effective configuration and removed abandoned serialized fields only after exact loaded-value, parameter-bit, native-delta and idempotence evidence. It did not re-tune the atmosphere or derive new Hillaire values from old fields. Unity Light remains the light-value owner; the Validation scene cleanup removed old LightComponent duplicates and editor show-state, preserving all current native Light fields and references. Layer-route behavior and material import/persistence still require U40 verification.

A source schema migration is a targeted persistent-data transaction: immutable current-byte backups and intent precede writes; exact per-object allowed deltas and complete non-target/identity checks follow; a separate no-op verifies persisted idempotence. Old failed evidence remains immutable. Composite acceptance may inherit unchanged parsed-source/copy proofs through exact hashes and reviewed reader equivalence, adding narrowly certified source replacements or explicit source deletions. It must not hide unknown changes or reclassify an unverified platform as passed. Three referenced BoxMatrix LightingSettings changes are approved (Hybrid/SRPBatcher MinBounces 1→2; MeshPipeline Direct/AO Gaussian 1/2→5/5; no Bake) and passed N01.a source migration/independent no-op under Terra verification.

GraphSRP output integration is currently unsupported. The retired Infinity VFX binder was a nonfunctional stub relying on an inaccessible Unity-internal friend API, with no valid output data and broken template paths. T02d removed its 99 proven invalid binder/template/include files under exact backup/deletion review. The VFX 17.5 package dependency remains; ordinary ParticleSystem rendering remains within N11. No old-identity shim or PackageCache patch is part of this design, and usable hardware-RT assets are retained.

Current Validation scene images are explicit quality failures: sky black bands, large triangular dark regions and cube blotches are assigned to N07/N08/N11 for causal diagnosis and image/numeric verification. Passing source schema or C# compilation checks does not satisfy those rendering gates. See `Docs/FullRendering-Delivery-Report.md` for the root-inspected images and fresh log windows.


### Player Volume component registry (N02 startup correction)

CoreRP 17.6 uses reflection to enumerate Volume types in Editor, but Player derives its type registry exclusively from the global default Profile. Infinity passes the GlobalSettings default profile (and optional RP Asset quality profile) to `VolumeManager.Initialize(defaultProfile, qualityProfile)`. The default profile is registered once; there is no duplicate custom binding. That Profile contains the required color/exposure components and every consumed optional-feature type. Optional features use `IsActive()` rather than override flags. Pipeline creation rejects an incomplete registry. The lossless Validate tool only appends absent types and retains existing components and their identities/values.


### Native SDR display transfer authority (N02 candidate)

A Player camera targeting a native Display need not expose a RenderTexture. Its sRGB conversion requirement comes from that target display's `requiresSrgbBlitToBackbuffer`, not from a guessed default texture format. Output decisions retain `backbufferFormat=None` and `displayTransferAuthority=true` when only this transfer capability is known. Linear projects use hardware conversion when the display supports it, otherwise OutputTransform performs LinearToSRGB. Camera RenderTexture and Editor surfaces retain their observed texture-format path. Missing or invalid RG texture resources still fail; an unknown native display pixel format is tracked separately from a valid display transfer decision. Native Metal pixel-format capture passed N03; full output image contracts remain N05 work.

## Normal-frame capture ownership

`RenderCaptureService` owns one immutable request and session shared by Editor and Player. A matching camera warms on successful submitted frames. Capture records explicit RG Transfer source reads and copies to session-owned imported staging; the staging descriptor is the actual source `TextureDescriptor`, serialized without a second descriptor schema. Raw readbacks retain nonfinite values and fail validation. The normal camera target, DebugView and history are not changed.

Staging is reserved before recording. Once queued, it remains alive until both frame submission and the asynchronous readback terminal callback. Cancellation/timeout stop new recording and drain outstanding work. Evidence I/O failure records the original error, fails the session and continues retirement. A read-only native Metal event in the existing Present raster pass records its actual color attachment format/dimensions; its unmanaged payload follows the same submission/terminal ownership. This format observation is separate from the linear intermediate's bytes and display-transfer decision. N04 still owns full RenderGraph/Submit failure-transaction closure.

The Editor Frame Debugger adapter is an independent, paused inspection after normal capture ends. It restores prior enabled/limit/pause state and removes callbacks even when capture or persistence fails. Its actual event tree can be accepted without claiming unavailable per-event native detail fields. N03 independently passed Editor/Player capture and frame-tree gates. Later camera-selection and Lighting-stage refinements are verified with N04.b in PLAN.md.

Lighting capture is recorded after the final opaque lighting/atmosphere producer and before scene-color ownership transfer. Display and confidence are captured at their own later stage. Capture does not guess a replacement scoper name after MoveTexture; the source is selected while its semantic owner is authoritative.

## RenderGraph failure and consumer contracts (N04)

The graph owns an unnamed command buffer separate from enclosing frame/camera scopes. Execute propagates the original exception and stack; failed pass commands are abandoned without clearing enclosing scopes. Async temporary command buffers return in finally. Unreturned transient graph allocations move to a retirement queue during logical clear and become reusable only after the enclosing frame Submit. N04.a is independently accepted; shared-cache production and whole-frame Submit-failure closure remain N04.c work.

Attachment Load is a read of prior contents. Loaded depth that later effects or capture consume must be stored. Forward explicitly reads GGX/SH and, when local lights exist, Tile range/list resources; it binds through the raster capability surface. Atmosphere and ZBin producers no longer prebind their outputs globally. Required local-light ZBin kernels fail at record when absent. N04.d records the LightContext upload as a Transfer producer. Imported GraphicsBuffer resources share RGBufferRef dependency tracking with ComputeBuffer resources; native target/count/stride remain explicit and an imported GraphicsBuffer cannot be cloned as a ComputeBuffer descriptor. Capability bindings resolve the actual native resource without a second handle system. LightContext retains ownership and retires replaced buffers only after frame Submit. Forward (including Terrain) binds its own light records and counts. Explicit overflow capture copies the ZBin output in a dependent Transfer pass into session-owned staging; nonzero raw counters fail the capture. No diagnostic staging exists without a request. N04.d acceptance and full failed-command/Submit coupling are tracked separately in PLAN.md.

Pass queue observers publish capture ownership and atmosphere production only after Unity accepts the pass command buffer. Abandoned recording never latches queued ownership. A graph orders its first async work after earlier graphics submissions and joins its last accepted async fence on exit, including a later recording failure. Resource last-use returns occur after queue acceptance. N04.c.1 covers this queue boundary; whole-frame Submit failure, shared-key replacement and camera transactions remain c.2/c.3.

HistoryCache keeps separate committed and pending descriptors and swaps each descriptor with its own allocation. A failed pending resize cannot replace committed history. Shared atmosphere keys discard an earlier pending generation before recording a different key; failed cameras discard only unproduced shared reservations. Successful frame submission commits shared resources by accepted production and each camera by its own execution result. A failed camera or submission requires a history reset and clears temporal-valid counters. Final queue failure still attempts Submit; physical retirement runs only after successful submission and a known async join. Uncertain capture queue receipts retain ownership and drain readbacks on the exceptional post-Submit path. End-context and command-buffer cleanup preserve the original exception. c.2 unit/live baseline acceptance and c.3 integrated fault recovery remain separate gates.

### Resolved default Volume values

The GlobalSettings default profile is registered as the VolumeManager global default profile, including the complete Player type registry. Exposure, FilmTonemap and ColorGrading are required active components. Their consumers read the resolved per-camera stack values. CoreRP clears stack parameter override flags when applying default values; those flags describe scene overrides and must not gate required exposure. Optional features record when `IsActive()` is true. CombineLUT has no separate inactive-component default table; exposure remains a pre-LUT multiply. FilmTonemap `mode == None` skips the film curve.

### Explicit material route updates

Infinity surface materials declare `_SurfaceRoute` and `_TranslucentStage` on their shader. `MaterialRouteUtility.Read` validates their exact integer values; fractional, nonfinite and out-of-range values fail rather than being rounded into another route. Subsurface scattering requires opaque Deferred routing. `MeshComponent` eligibility and the Editor share this reader. `MaterialRouteUtility.ApplyPassState` is the explicit Runtime/Editor operation after changing these properties; it derives pass enables and the opaque/transparent queue category. The ShaderGUI validates without rewriting serialized state and applies derived state only after a user edit or explicit shader assignment. Direct property writes alone do not apply the derived Unity Renderer pass state; runtime callers must invoke the explicit update. The two inspected asset pass-state migrations have independent acceptance receipts in PLAN. Rendered route parity remains an N06 gate.

### N06 layer and native light ownership

`InfinityAdditionalLightData.shadowLayer` is a validated property over native `Light.renderingLayerMask`; there is no serialized duplicate. Native Light owns physical color/intensity/temperature, geometry and shadow mode. Removed IES/Cookie, per-light indirect, PCSS and duplicate configuration fields have no compatibility storage. The nine explicit scene retirements preserve the previously effective native values and all non-target data; PLAN links their independent receipts.

Surface masks are encoded in GBufferC alpha as an eight-bit UNorm value. Unity shaders use native rendering-layer data. Infinity Mesh retains instance records as authority; residency derives a transform-indexed uint layer buffer through the existing exclusive Transform/Instance owner map and uploads it with transform dirty ranges. CPU and GPU submissions bind the same buffer. Surface direct-light masks are independent of caster masks and camera visibility. Runtime high bits are rejected; Everything normalization belongs only to an explicit asset migration. These paths compile and have unit/GPU packing evidence; full rendered parity is still pending.

## Development interaction boundary

The approved N03.I replacement uses official Unity CLI as the sole automated Editor transport, with an Editor-only project command adapter and unchanged ownership of rendering evidence in RG/session modules. The bridge is an Example development dependency, not an Infinity Runtime requirement. Official com.unity.pipeline 0.6.0-exp.1 screenshot rerenders into a substituted camera target and is not usable for normal-frame temporal or actual-window acceptance. See Docs/Unity-Development-Interaction.md for the verified source finding and retirement gates. The basic CLI connection, read-only eval and compile/reconnect path are now verified on Unity 6000.6.0f1 using the outer Example embedded bridge with an explicit IPv4 loopback fix. Full command-surface migration and rendering compatibility remain separate pending gates.
