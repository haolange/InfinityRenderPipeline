# InfinityRP — Design (as implemented)

This document describes the code as it is. Verification state is in [Docs/FeatureStatus.md](Docs/FeatureStatus.md);
the active plan is [PLAN.md](PLAN.md); archived delivery reports are in [Docs/History/](Docs/History).

Part I (sections 1–14) is the Mesh Drawing Pipeline. Part II (sections 15–24) is the frame and its ownership contracts.

## 1. Mesh Drawing Pipeline: problem solved

Infinity `MeshComponent` content does not go through Unity RendererLists. It is drawn from an authoritative SoA scene
with separate lifetimes for:

1. Logical instance + exclusive transform
2. Geometry section and material data (refcounted)
3. Per-pass bins and their grouped command table (rebuilt only on structural change)
4. Per-view visibility (CPU job or GPU `CullFrustum`)
5. Per-DrawList submission payload (CPU procedural or GPU indirect)

## 2. Data model

```text
MeshInstanceRecord ──TransformId──► TransformRecord   (1:1, exclusive)
        │
        └── MeshDraw[] ──► MeshSectionId + MaterialDataId (+ eligibility, queue, priority)

PassBin (per MeshPassId)  = filtered, structurally sorted MeshDraws
MeshPassCommand           = MeshDrawCommandKey + member span inside the bin
MeshDrawCommand           = mesh, section, material, count/offset, bakedTextureSet (per view, per list)
```

Typed IDs (`MeshInstanceId`, `TransformId`, `MeshDrawId`, `MeshSectionId`, `MaterialDataId`) carry
`Index + Generation`; generation 0 is invalid and stale handles are rejected.

**TransformId ownership is exclusive (1:1).** `MeshSceneUpdate.CreateInstance` and `MeshScene.AllocInstance` reject a
transform that already has an owner ("TransformId already owned"). There is no multi-instance transform sharing.
`MeshScene.MatrixDuplicateRatio` (`TransformCount / LogicalInstanceCount`) is 1 in steady state.

## 3. Module map

| Path (`Runtime/RendererCore/PrimitivePipeline/MeshPipeline/`) | Role |
|------|------|
| `MeshScene.cs`, `MeshSceneRecords.cs` | Authoritative SoA tables, generation free lists, transform ownership map, refcounted sections/materials, dirty-page bitmaps, revisions |
| `MeshSceneUpdate.cs` | Transaction with full undo log; `Dispose` without `Commit` rolls back |
| `GpuScene.cs` | GPU mirror of `MeshScene`: uploads dirty 64-slot page runs of transforms, rendering layers, baked lighting, bounds, instance→transform index and flags |
| `MeshWorld.cs` | Per-scene facade owning `MeshScene`, `GpuScene`, `MeshVisibilityShare`, `PassRegistry`, `MeshDrawPipeline`, `MeshGpuVisibilityCache` and per-view previous transforms |
| `PassRegistry.cs`, `MeshPassTypes.cs`, `MeshPassId.cs` | The nine `MeshPassId` definitions (filter program, eligibility, sort fields) |
| `PassBinStore.cs` | Per-pass bins: filter (eligibility, queue, liveness, motion), structural sort (GBuffer/Forward), grouping by `MeshDrawCommandKey` into `MeshPassCommand` spans |
| `MeshDrawCommandKey.cs` | Structured command identity (full `Equals`; hash is lookup acceleration only) |
| `MeshView.cs` | One observer (Main / SceneView / Preview / CascadeShadow / LocalShadow): view key, planes, layer masks |
| `MeshVisibility.cs`, `MeshVisibilityCache.cs` | CPU per-instance frustum/layer cull job; `MeshVisibilityShare` interns results by `MeshVisibilitySignature` |
| `MeshGpuVisibilityCache.cs` | One GPU visibility buffer per view key for the GPU `CullFrustum` path |
| `MeshDrawCompiler.cs` | `MeshPassExtractJob`: bin commands × visibility → draw commands and transform/slot index streams |
| `MeshCandidateTableStore.cs` | Per-pass GPU command table and candidate table (`uint2` instance slot, local command) |
| `MeshDrawPipeline.cs` | CPU Extract / Resolve / Submit and GPU prepare/submit delegation; frame-buffer retirement |
| `MeshDrawGPUBackend.cs` | Kernel resolution, backend policy, per-DrawList payload pool, ClearCounts/Compact/BuildIndirectArgs, `DrawMeshInstancedIndirect` |
| `MeshBakedLighting.cs` | Per-instance lightmap index/scale-offset and probe SH capture; per-draw lightmap keywords |
| `MeshPipelineDiagnostics.cs` | Counters (see §11) |
| `Runtime/RendererCore/RenderGraph/RGDrawList.cs` | Per-graph DrawList registry: declare, prepare, resolve, submit, release |
| `Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute` | `CullFrustum`, `ClearCounts`, `Compact`, `BuildIndirectArgs` |
| `Shaders/ShaderLibrary/GPUScene.hlsl` | `transformBuffer` / `previousTransformBuffer` / `instanceIndexBuffer` |

Terrain, foliage and water are separate systems. Shared seams: `EGeometrySourceKind` (`IndexedMesh`,
`SkinnedDeformed`, `Procedural`, `MeshletCluster`) and instance/transform identity suitable for RTAS sharing.

## 4. Frame flow

1. `MeshComponent` mutates the scene through `MeshSceneUpdate` (one transform, N draws) and commits.
2. `MeshWorld.UpdateResidency` → `GpuScene.Update` uploads dirty page runs.
3. `MeshWorld.BeginCamera` resets per-camera counters and GPU cull flags.
4. `PassBinStore` rebuilds a pass's bins only when `StructuralRevision` (or rollback `BinEpoch`) changed.
5. Per DrawList (`RGDrawList`): `PrepareLive` selects the backend.
   - **CPU:** `MeshVisibilityShare.Acquire` (CPU cull job), `MeshDrawPipeline.Extract` → `MeshPassExtractJob` → `DrawMeshInstancedProcedural` with `instanceIndexBuffer` = transform indices and `instanceIndexOffset` = command offset.
   - **GPU:** `RGBuilder.EnsureMeshGpuCull` dispatches `CullFrustum` once per view key; `EnsureResolved` builds the candidate table and rents/pre-sizes a payload; `ClearCounts` → `Compact` (instance slot → transform index) → `BuildIndirectArgs` → `DrawMeshInstancedIndirect`.
6. `RGDrawListContext.ReleaseAll` retires payloads and drops visibility references; `MeshWorld.FlushRetired` drains them after frame Submit.

Wired MeshDraw passes: Depth, GBuffer, Forward, Motion, Cascade Shadow, Local Shadow. Unity `MeshRenderer`s still use
RendererLists.

## 5. RenderGraph DrawList lifecycle

```text
Record:  DeclareDrawList + UseDrawList
Compile: CountPassReference → CullingUnusedPass → CompileDrawLists → UpdateResource
Execute: EnsureResolved(per consumer) → encoder.Draw
Finally: ReleaseAllDrawLists (logical: builds + RetirePayload + visibility Release)
Frame end: ScriptableRenderContext.Submit → FlushRetiredPayloads / FlushRetiredBuffers
```

A DrawList is not an `ERGResourceType`. Each `RGDrawListRef` carries a graph generation; stale refs are rejected.

### 5.1 Physical GPU resource lifetime

| Phase | API | What happens |
|-------|-----|----------------|
| Logical cleanup | `RGDrawListContext.ReleaseAll` | Releases NativeArray builds and visibility references and retires payloads into retired queues. No `ComputeBuffer.Release`. |
| Physical retirement | After `ScriptableRenderContext.Submit()` | `FlushRetiredPayloads` / `FlushRetiredBuffers` return payloads to the pool or dispose them. |

Before recording, the payload budget is sized once (`EnsureCapacity`); during recording only `RequireCapacity` checks
run. Buffers bound to a live command buffer are never grown or recreated.

## 6. Transactions and keys

- Every `MeshSceneUpdate` mutation pushes undo; `Dispose` without `Commit` rolls back atomically, restoring revisions
  and dirty pages. Section/material slots whose refcount reaches zero inside a transaction are reclaimed on commit and
  discarded on rollback.
- `MeshDrawCommandKey` uses full structured `Equals`; `GetHashCode` is lookup acceleration only. `bakedTextureSet`
  is part of the key, so different lightmap texture sets never share a command.
- `MeshSortKey.PackSortKey` (four saturating 16-bit segments → 64-bit key) exists and is tested, but bins are sorted
  only by `CompareStructural`; pass `defaultSort` plans are not applied yet.

## 7. Visibility

- CPU intern key `MeshVisibilitySignature`: scene id, view key, frustum hash (planes quantized to 1e-3), scene
  visibility revision, policy id, subview index, layer mask, rendering-layer mask, layer-filter flag. Results are
  refcounted and released by the graph.
- `MakeCascadeViewKey(light, cascade)` and `MakeLocalShadowViewKey(light, face)` isolate shadow views.
- GPU visibility (`MeshGpuVisibilityCache`) is keyed by a packed `ulong` view key, one `CullFrustum` dispatch per key.

## 8. GPU backend

`EMeshBackendPolicy`: `Auto`, `CpuDirect`, `GpuIndirect`, `GpuIndirectFullPass` (no consumer). `Auto` resolves to
`GpuIndirect` when instancing, compute, the compute shader and its four kernels are available, otherwise `CpuDirect`.
Payloads are rented per DrawList from a pool; there is no process-global indirect buffer. No same-frame visible-count
readback; overflow falls back to `CpuDirect` and increments `GpuOverflowCount`. The compute shader resolves from
GlobalSettings (`InfinityRenderPipelineRuntimeShaders.meshDrawPipelineCS`).

Out of scope: HZB occlusion, GPU sort of draw keys, GPU LOD, meshlets.

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

RGDrawListRef draws = graph.CreateDrawList(meshView, MeshPassId.GBuffer);
data.draws = pass.UseDrawList(draws);
// execute:
cmdEncoder.Draw(data.draws);
```

## 10. Non-goals

HZB occlusion, GPU radix sort of draw keys, GPU LOD, meshlets, replacing RendererLists for Unity `MeshRenderer`s,
in-place mesh topology rewrite without a section revision (re-register the `MeshComponent` after such edits).

## 11. Diagnostics

`MeshPipelineDiagnostics` counters: instance/draw/transform counts, `MatrixDuplicateRatio`, `TempAllocCount`,
`CulledPassSkippedBuilds`, `GpuOverflowCount`, `LocalShadowBudgetDropped`, `VisibilityProductionsPerFrame`,
`PassBinRebuilds`, `CompactDispatchesPerFrame`, `GpuCullDispatchesPerFrame`, `CandidateUploadsBytes`,
`GpuSceneUploadedSlots`, `GpuSceneUploadedBytes`.

## 12. Local shadow MeshDraw

- Atlas from `localShadowMapResolution`; tile = resolution/4, budget = 16 tiles. Spot costs 1 tile, Point 6.
  Candidates are scored (strength / distance, Spot preferred on ties) and greedily accepted; drops increment
  `LocalShadowBudgetDropped`.
- Point lights record six face frusta and six `MakeLocalShadowViewKey(light, face)` visibility acquires.
- Shadow MeshDraw filter: `layerMask = light.cullingMask`, rendering-layer filtering enabled with the native
  `Light.renderingLayerMask`. Unity shadow RendererLists use `useRenderingLayerMaskTest` with the same mask.
- Each atlas slice draws Infinity `MeshComponent` content via `RGDrawListRef` and Unity renderers via
  `DrawRendererList`; the two paths cover different objects.

## 13. Instance-indexed GPU cull

| Stage | Index domain |
|-------|----------------|
| Bounds / visibility buffers | Instance slot |
| Candidate table | Instance slot + local command |
| Compacted indices / shader `instanceIndexBuffer` | Transform index (`TransformId.Index`) |
| CPU `instanceIndices` (procedural submit) | Transform index |

## 14. Rendering layer unification

`ERenderingLayer : byte` with `[Flags]` (`Nothing` … `Everything = 0xFF`) is the shared Mesh/Light mask.
`MeshFilterProgram` tests rendering layers only when `filterRenderingLayers` is enabled (shadow passes); camera
passes leave it disabled. `MeshComponent.renderingLayer` is flags, not an int layer index.

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
