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

## 15. Full rendering pipeline (locked order)

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
         → DisplayColorBuffer (display-linear) → Game-display UIOverlay → Present (transfer encoding)
```

Contracts that stay locked:

- Atmosphere lives only on `AtmosphericalProfile`. Volume does not override it.
- History lives only in `HistoryCache`. CopyHistory is Transfer `CopyTexture` after the producer.
- `DisplayColorBuffer` is display-linear and is the sole Present source. Final Present owns transfer encoding.
- Present stays Raster. Gizmo / WireOverlay / Present disable native RP for API reasons only.
- Not supported: hardware RT outside UnifiedRayTracing, DOF, motion blur, upscalers beyond fixed-ratio SR, XR, MSAA, dynamic resolution. Baked GI is completed in 0.5.0 (PLAN S12).

Image / Frame Debugger / GPU-trace verification state is tracked in [Docs/FeatureStatus.md](Docs/FeatureStatus.md).

## 16. Settings layers, Default Volume, Output authority, Gizmo-before-encode

Project-wide resources and the default Volume profile live on `InfinityRenderPipelineGlobalSettings` (`IRenderPipelineGraphicsSettings` / `IRenderPipelineResources` with `[ResourcePath]`). Per-quality authoring lives on `InfinityRenderPipelineAsset` (features, shadows, `renderScale`, output, optional `qualityVolumeProfile`, diffusion profiles, atmosphere). Per-scene art lives on Volume / Profile. Per-camera / per-light Infinity fields live on additional-data components.

Pipeline creation requires a complete default Volume registry. The constructor calls `VolumeManager.Initialize(defaultProfile, qualityProfile)` once; disposal deinitializes that manager and restores process-global graphics flags captured at construction. Required color/exposure consumers read the resolved camera stack. Optional features record when `IsActive()` is true. CombineLUT has no inactive-component constant fallback. Atmosphere remains separately owned by AtmosphericalProfile.

The Present output decision resolves the target format in this order (first hit wins; `GraphicsFormat.None` is not a hit):

1. `camera.targetTexture.graphicsFormat`
2. `camera.activeTexture.graphicsFormat`
3. Editor Game/Scene present target (`PlayModeView` / `SceneView` RT that Unity already allocated)
4. last successfully resolved format for that camera

All missing throws at record time. HDR `HDROutputSettings.graphicsFormat` and `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)` are not authorities.

Gizmo / WireOverlay record on linear `PostProcessBuffer` after post/DebugView and before final Present, so editor overlays are encoded with the scene. They keep `EnableNativeRenderPass(false)` because Unity forbids gizmos inside `BeginRenderPass`. Present remains the single transfer-encoding owner; `DisplayColorBuffer` remains the present source.

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
| Screen Space Overlay UI | `CreateUIOverlayRendererList` on display-linear color before Present, only for Game cameras without a target texture |
| Screen Space Camera / World Space UI, particles, lines | Translucent RendererList |

Adding a Camera or Light (GameObject menu, Add Component or `ObjectFactory`) and the Infinity inspectors add additional data with Undo. `GameObject > Create` default materials come from the RP Asset (`defaultMaterial`, `defaultParticleMaterial`, `defaultLineMaterial`, `defaultTerrainMaterial`, `default2DMaterial`).

Hardware ray tracing (`RayTracingShader`) is D3D12/console only. RTAO records a UnifiedRayTracing visibility pass (`VisibilityRTAO.urtshader` + `IRayTracingAccelStruct`) when the Volume is active and the context/accel/shader are ready. Otherwise it does not record and GTAO owns AO. `SystemInfo.supportsRayTracing` is not a Metal capability claim; Metal uses the URT compute backend. Player shader load and D3D12 hardware remain `TODO(UNVERIFIED)`.


## 19. Serialized-asset authority

- Atmosphere's sole physical/configuration owner is RP Asset → `AtmosphericalProfile` → `AtmosphereParameter.FromProfile`. Volume does not override it.
- Unity `Light` owns light physical values; `InfinityAdditionalLightData` owns Infinity-only fields (see AGENTS "Light data authority").
- A source schema migration is a targeted persistent-data transaction: immutable current-byte backups and intent precede writes; exact per-object allowed deltas and complete non-target/identity checks follow; a separate no-op verifies idempotence. Loading never migrates.
- Custom VFX Graph outputs are unsupported. Standard ParticleSystem rendering uses the translucent RendererList.

### 19.1 Player Volume component registry

CoreRP 17.6 enumerates Volume types by reflection in the Editor, but a Player derives its registry exclusively from the global default Profile. Infinity passes the GlobalSettings default profile (and the optional RP Asset quality profile) to `VolumeManager.Initialize(defaultProfile, qualityProfile)` once. That Profile contains the required color/exposure components and every consumed optional-feature type; pipeline creation rejects an incomplete registry. `Window > Infinity > Validate Default Volume Profile` only appends absent types and keeps existing components, identities and values.

### 19.2 Resolved default Volume values

Exposure, FilmTonemap and ColorGrading are required active components; their consumers read the resolved per-camera stack. CoreRP clears stack parameter override flags when applying default values; those flags describe scene overrides and never gate required exposure. Optional features record when `IsActive()` is true. CombineLUT has no separate inactive-component default table; exposure is a pre-LUT multiply. Tonemapping is None or ACES; `mode == None` skips the ACES curve.

### 19.3 Native SDR display transfer authority

A Player camera targeting a native Display need not expose a RenderTexture. Its sRGB conversion requirement comes from that display's `requiresSrgbBlitToBackbuffer`, not from a guessed default format. Output decisions keep `backbufferFormat = None` and `displayTransferAuthority = true` when only this capability is known. Linear projects use hardware conversion when the display supports it; otherwise Present performs LinearToSRGB. Camera RenderTextures and Editor surfaces keep their observed texture-format path.

## 20. Normal-frame capture ownership

`RenderCaptureService` owns one immutable request and session shared by Editor and Player. A matching camera warms on successful submitted frames. Capture records explicit RG Transfer reads and copies to session-owned imported staging whose descriptor is the actual source `TextureDescriptor`. Raw readbacks keep nonfinite values and fail validation. The normal camera target, DebugView and history are not changed.

Staging is reserved before recording and stays alive until both frame submission and the asynchronous readback callback. Cancellation/timeout stop new recording and drain outstanding work. Evidence I/O failure records the original error, fails the session and continues retirement. A read-only native Metal event in the Present raster pass records the actual color attachment format/dimensions.

The Editor Frame Debugger adapter is an independent, paused inspection after normal capture ends. It restores prior enabled/limit/pause state and removes callbacks even when capture or persistence fails.

Lighting capture is recorded after the final opaque lighting/atmosphere producer and before scene-color ownership transfer. Display and confidence are captured at their own later stage.

## 21. RenderGraph failure and consumer contracts

The graph owns an unnamed command buffer separate from enclosing frame/camera scopes. Execute propagates the original exception and stack; failed pass commands are abandoned without clearing enclosing scopes. Async temporary command buffers return in `finally`. Unreturned transient graph allocations move to a retirement queue during logical clear and become reusable only after the enclosing frame Submit.

Attachment Load is a read of prior contents. Loaded depth that later effects or capture consume must be stored. Forward explicitly reads GGX/SH and, when local lights exist, the tile range/list resources; it binds through the raster capability surface. Atmosphere and ZBin producers do not prebind outputs globally. Required local-light ZBin kernels fail at record when absent. The LightContext upload is a Transfer producer. Imported GraphicsBuffer resources share `RGBufferRef` dependency tracking with ComputeBuffer resources; native target/count/stride stay explicit and an imported GraphicsBuffer is never cloned as a ComputeBuffer descriptor. LightContext retires replaced buffers only after frame Submit.

Pass queue observers publish capture ownership and atmosphere production only after Unity accepts the pass command buffer. Abandoned recording never latches queued ownership. A graph orders its first async work after earlier graphics submissions and joins its last accepted async fence on exit, including after a later recording failure.

HistoryCache keeps separate committed and pending descriptors and swaps each descriptor with its own allocation. A failed pending resize cannot replace committed history. Shared atmosphere keys discard an earlier pending generation before recording a different key; failed cameras discard only unproduced shared reservations. Successful frame submission commits shared resources by accepted production and each camera by its own execution result. A failed camera or submission requires a history reset. Final queue failure still attempts Submit; physical retirement runs only after successful submission and a known async join.

## 22. Material routes

Infinity surface materials declare `_SurfaceRoute` and `_TranslucentStage`. `MaterialRouteUtility.Read` validates their exact integer values; fractional, nonfinite and out-of-range values fail rather than being rounded into another route. Subsurface scattering requires opaque Deferred routing. `MeshComponent` eligibility and the Editor share this reader. `MaterialRouteUtility.ApplyPassState` is the explicit Runtime/Editor operation after changing these properties; it derives pass enables and the opaque/transparent queue category. The ShaderGUI validates without rewriting serialized state and applies derived state only after a user edit or explicit shader assignment.

## 23. Rendering layers and native light ownership

`InfinityAdditionalLightData.shadowLayer` is a validated property over native `Light.renderingLayerMask`; there is no serialized duplicate. Native Light owns physical color/intensity/temperature, geometry and shadow mode.

Surface masks are encoded in GBufferC alpha as an eight-bit UNorm value. Unity shaders use native rendering-layer data. Infinity Mesh keeps instance records as authority; residency derives a transform-indexed uint layer buffer through the exclusive Transform/Instance owner map and uploads it with transform dirty data. CPU and GPU submissions bind the same buffer. Surface direct-light masks are independent of caster masks and camera visibility. Runtime high bits are rejected.

## 24. Editor interaction boundary

Automated Editor interaction uses the official Unity CLI connection with an Editor-only command adapter (`Editor/Cli`, compiled only when `INFINITY_UNITY_CLI` is defined by `com.unity.pipeline`). Rendering evidence stays owned by the RG capture modules. `com.unity.pipeline`'s `screenshot` rerenders into a substituted camera target and is not used for normal-frame, temporal or actual-window acceptance.
