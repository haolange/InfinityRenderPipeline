# InfinityRP — Design (as implemented)

This document describes the code as it is. Verification state is in [Docs/FeatureStatus.md](Docs/FeatureStatus.md);
active tasks are tracked in the conversation; archived delivery reports are in [Docs/History/](Docs/History).

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
| `MeshDrawPipeline.cs` | CPU Extract / Resolve and graph-owned prepare/submit delegation; frame-buffer retirement |
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
Record:  CreateDrawList + UseDrawList
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
Phase 2  HiZ + HalfResDownsample + LightTileMask (async-eligible)
Phase 3  CascadeShadow + LocalShadow
Phase 4  reserved (VolFog moved to Phase 7)
Phase 5  GTAO → CopyHistoryOcclusion → ContactShadow
Phase 6  Deferred → Forward → SSS → AtmosphericSkyAndFog
         → OpaqueLightingPyramid → SSR/SSGI (+ history) → ScreenSpaceComposite
         → OpaqueSceneColor
Phase 7  TranslucentDepth → VolCloud (+ history) → VolFog (+ history)
         → FogComposite → FoggedSceneColor
         → T0 → ColorPyramid → T1 → T2
Phase 8  TAA or SuperResolution (+ history) → Exposure / Bloom / CombineLUT
         → raster post-combine (grading, vignette, grain)
         → direct final output + hardware-sRGB UI, OR
           linear intermediate → DebugView → Gizmo/WireOverlay → UI → final output
```

Contracts that stay locked:

- Atmosphere lives only on `AtmosphericalProfile`. Volume does not override it.
- History lives only in `HistoryCache`. CopyHistory is Transfer `CopyTexture` after the producer.
- `DisplayColorBuffer` exists only for a requested linear intermediate. Encoded targets never alias it; output conversion is resolved before recording.
- Final output stays Raster. Gizmo / WireOverlay / FinalOutput disable native RP for API reasons only.
- Not supported: hardware RT outside UnifiedRayTracing, DOF, motion blur, upscalers beyond fixed-ratio SR, XR, MSAA, dynamic resolution. Baked GI is completed in 0.5.0 (PLAN S12).

Image / Frame Debugger / GPU-trace verification state is tracked in [Docs/FeatureStatus.md](Docs/FeatureStatus.md).

## 16. Settings layers, Default Volume, Output authority, Gizmo-before-encode

Project-wide resources and the default Volume profile live on `InfinityRenderPipelineGlobalSettings` (`IRenderPipelineGraphicsSettings` / `IRenderPipelineResources` with `[ResourcePath]`). Per-quality authoring lives on `InfinityRenderPipelineAsset` (features, shadows, `renderScale`, output, optional `qualityVolumeProfile`, diffusion profiles, atmosphere). Per-scene art lives on Volume / Profile. Per-camera / per-light Infinity fields live on additional-data components.

Pipeline creation requires a complete default Volume registry. The constructor calls `VolumeManager.Initialize(defaultProfile, qualityProfile)` once; disposal deinitializes that manager and restores process-global graphics flags captured at construction. Required color/exposure consumers read the resolved camera stack. Optional features record when `IsActive()` is true. CombineLUT has no inactive-component constant fallback. Atmosphere remains separately owned by AtmosphericalProfile.

The final output decision resolves the target format in this order (first hit wins; `GraphicsFormat.None` is not a hit):

1. `camera.targetTexture.graphicsFormat`
2. `camera.activeTexture.graphicsFormat`
3. Editor Game/Scene present target (`PlayModeView` / `SceneView` RT that Unity already allocated)
4. last successfully resolved format for that camera

All missing throws at record time. HDR `HDROutputSettings.graphicsFormat` and `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)` are not authorities.

Gizmo / WireOverlay record on linear `PostProcessBuffer` after post/DebugView and before final output, so editor overlays are encoded with the scene. They keep `EnableNativeRenderPass(false)` because Unity forbids gizmos inside `BeginRenderPass`. Final output encodes the composed intermediate; direct SDR UI uses target-owned hardware sRGB blending.

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
| Screen Space Overlay UI | `CreateUIOverlayRendererList` on a hardware-sRGB final target or on a required linear intermediate, only for Game cameras without a target texture |
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

A Player camera targeting a native Display need not expose a RenderTexture. Its sRGB conversion requirement comes from that display's `requiresSrgbBlitToBackbuffer`, not from a guessed default format. Output decisions keep `backbufferFormat = None` and `displayTransferAuthority = true` when only this capability is known. Linear projects use hardware conversion when the display supports it; otherwise final output performs LinearToSRGB. Camera RenderTextures and Editor surfaces keep their observed texture-format path.

## 20. Normal-frame capture ownership

`RenderCaptureService` owns one immutable request and session shared by Editor and Player. A matching camera warms on successful submitted frames. Capture records explicit RG Transfer reads and copies to session-owned imported staging whose descriptor is the actual source `TextureDescriptor`. Raw readbacks keep nonfinite values and fail validation. The normal camera target, DebugView and history are not changed.

Staging is reserved before recording and stays alive until both frame submission and the asynchronous readback callback. Cancellation/timeout stop new recording and drain outstanding work. Evidence I/O failure records the original error, fails the session and continues retirement. A read-only native Metal event in the final output raster pass records the actual color attachment format/dimensions.

The Editor Frame Debugger adapter is an independent, paused inspection after normal capture ends. It restores prior enabled/limit/pause state and removes callbacks even when capture or persistence fails.

Lighting capture is recorded after the final opaque lighting/atmosphere producer and before scene-color ownership transfer. Display and confidence are captured at their own later stage.

## 21. RenderGraph failure and consumer contracts

The graph owns an unnamed command buffer separate from enclosing frame/camera scopes. Execute propagates the original exception and stack; failed pass commands are abandoned without clearing enclosing scopes. Async temporary command buffers return in `finally`. Unreturned transient graph allocations move to a retirement queue during logical clear and become reusable only after the enclosing frame Submit.

Attachment Load is a read of prior contents. Loaded depth that later effects or capture consume must be stored. Forward explicitly reads GGX/SH and the light-list buffers (z-bins and tile masks, or the one-word empty mask when no local light is visible); it binds through the raster capability surface. Atmosphere and light-list producers do not prebind outputs globally. A missing tile-mask kernel fails at record when local lights are visible. The LightContext upload is a Transfer producer. Imported GraphicsBuffer resources share `RGBufferRef` dependency tracking with ComputeBuffer resources; native target/count/stride stay explicit and an imported GraphicsBuffer is never cloned as a ComputeBuffer descriptor. LightContext retires replaced buffers only after frame Submit.

Pass queue observers publish capture ownership and atmosphere production only after Unity accepts the pass command buffer. Abandoned recording never latches queued ownership. A graph orders its first async work after earlier graphics submissions and joins its last accepted async fence on exit, including after a later recording failure.

HistoryCache keeps separate committed and pending descriptors and swaps each descriptor with its own allocation. A failed pending resize cannot replace committed history. Shared atmosphere keys discard an earlier pending generation before recording a different key; failed cameras discard only unproduced shared reservations. Successful frame submission commits shared resources by accepted production and each camera by its own execution result. A failed camera or submission requires a history reset. Final queue failure still attempts Submit; physical retirement runs only after successful submission and a known async join.

## 22. Material routes

Infinity surface materials declare `_SurfaceRoute` and `_TranslucentStage`. `MaterialRouteUtility.Read` validates their exact integer values; fractional, nonfinite and out-of-range values fail rather than being rounded into another route. Subsurface scattering requires opaque Deferred routing. `MeshComponent` eligibility and the Editor share this reader. `MaterialRouteUtility.ApplyPassState` is the explicit Runtime/Editor operation after changing these properties; it derives pass enables and the opaque/transparent queue category. The ShaderGUI validates without rewriting serialized state and applies derived state only after a user edit or explicit shader assignment.

## 23. Rendering layers and native light ownership

`InfinityAdditionalLightData.shadowLayer` is a validated property over native `Light.renderingLayerMask`; there is no serialized duplicate. Native Light owns physical color/intensity/temperature, geometry and shadow mode.

Surface masks are encoded in GBufferC alpha as an eight-bit UNorm value. Unity shaders use native rendering-layer data. Infinity Mesh keeps instance records as authority; residency derives a transform-indexed uint layer buffer through the exclusive Transform/Instance owner map and uploads it with transform dirty data. CPU and GPU submissions bind the same buffer. Surface direct-light masks are independent of caster masks and camera visibility. Runtime high bits are rejected.

## 24. Editor interaction boundary

Automated Editor interaction uses the official Unity CLI connection with an Editor-only command adapter (`Editor/Cli`, compiled only when `INFINITY_UNITY_CLI` is defined by `com.unity.pipeline`). Rendering evidence stays owned by the RG capture modules. `com.unity.pipeline`'s `screenshot` rerenders into a substituted camera target and is not used for normal-frame, temporal or actual-window acceptance.

## Clustered light list (0.5.0 S7)

`LightContext.Build` runs per camera after culling: collect visible lights (directional first), compute each local
light's conservative screen tile rectangle on the CPU (tangent-line sphere bounds; a sphere that reaches the near plane
covers every tile; spheres behind the camera or off screen are dropped), sort local lights by view depth, build 128
logarithmic depth bins holding the [min, max] sorted index range they overlap, then allocate shadows. The upload pass
imports the records, tile rectangles and z-bins. `LightTileMaskPass` (async compute, `Compute_LightTileMask`) writes
one 32-bit mask word per tile per 32 lights. `Shaders/ShaderLibrary/Lighting/LightLoop.hlsl` is the only light loop:
`BeginLightLoop(pixel, viewDepth)` + `NextLight` visit the tile mask bits inside the pixel's bin range. Deferred,
Forward (`SV_Position`), volumetric fog and clouds use it; tiles are not culled by opaque depth so transparents and
volumes share the list.


### Execution and shader pass naming

GPU work markers and recording methods use Raster for draw, Compute for dispatch,
Copy for resource copy, and Upload for CPU uploads. Unity ShadowCaster, Meta and
SRPDefaultUnlit are fixed engine entrypoints. Infinity custom LightModes and their
matching pass names use RasterXXXPass. Other owned raster shader names use Raster.
RTAO selects ComputeRayTracedOcclusion or RayTracingOcclusion from its owned URT
context backend; its RenderGraph pass type remains RayTracing. Pass names continue
to come from the existing ProfilingSampler. No old-name aliases are supported.

Shader integration migration: DepthPass, DBufferPass, GBufferPass, ShadowPass,
MotionPass, ForwardPass, TranslucentDepthPass and TranslucentT0/T1/T2Pass now use
an additional Raster prefix in LightMode, matching pass Name, and InfinityPassIDs.
Custom shaders must update both their tags and pass names; update any explicitly
stored disabled-pass names with the same mapping. Shader asset identities and
material properties remain unchanged. Present is replaced by RasterFinalOutput;
UpscaleDepth and CopyShadowDepthTile pass Names gain Raster. TerrainAdd retains its distinct name as
RasterTerrainAdd while its LightMode is RasterForwardPass.

## Output and temporal attachment convergence

`FOutputPlan` selects the target before post processing. Raster post-combine uses
`Shaders/ShaderLibrary/PostCombine.hlsl` for one grading/exposure/Bloom/vignette/grain
algorithm. With no display-linear consumer, this draw also performs output conversion
and writes the imported camera target. Hardware-sRGB SDR UI blends on that target.
HDR/non-hardware-sRGB UI, DebugView, requested linear captures and Editor overlays
retain a display-linear intermediate; an output-only draw encodes it after composition.
Encoded targets are never published as linear semantic buffers. Partial viewports Load
the destination; full target overwrites use DontCare. Post bypass aliases read-only
inputs, but acquires an independent output before an overlay writes to it.

ReactiveMask is an R8 transparent attachment only on TAA cameras. Its first color pass
uses Clear(0) even with zero draws; subsequent stages Load. Other temporal policies
omit this attachment and its shader output, compacting motion/metadata slots. The
FogComposite shader has no reactive-mask initialization role.

Editor overlays share one demand decision with display-depth upsampling. Equal-size
depth is referenced; scaled depth is point-upsampled only for a consumer. Cached
shadow tiles use one raster copy implementation in separate save/load passes; Unity
requires whole-resource CopyTexture for depth-only textures, so partial atlas copies
read exact integer texels and write SV_Depth without conversion.


## Texture views, bounded retention and fused temporal output

A camera's logical Internal/Display extents and physical capacities are separate.
TextureDescriptor.width/height remain logical; explicit sizing domains resolve
allocationWidth/Height from that camera's transactional TextureCapacityState.
The physical descriptor is the allocation/cache key, with field-level equality;
FTextureView supplies the active extent and normalized sampling transform.
Standalone fixed resources use Exact. No size-based inference classifies textures.
Shader Graph and hand shaders share screen texture helpers; material textures
retain their original sampling. Mip sampling maps and clamps each contributing
LOD independently, including fractional LOD and odd image sizes. Raw capture
staging stays logical-sized and copies only the valid rectangle.

The implementation borrows principles from local UE 5.4.4 SceneTextures.cpp and
RenderTargetPool.cpp, not its editor-only permanent Grow policy: 64-texel buckets,
12.5% growth, a three-accepted-request peak and 30-request/75%-area shrink hysteresis.
Per-camera state prevents previews from contaminating main-view capacity.
ResourcePool owns both graph and external transient caches. Its texture/buffer
retention watermarks are 400/64 MiB; idle age greater than two successful
submission epochs selects oldest entries above the watermark. Retired entries
are destroyed only after the CPU-queryable completion fence passes. Active
working sets may exceed a watermark; allocation failures are not hidden.
Diagnostics distinguish estimated active, idle, pending-retirement and peak bytes.

TAA solves an 8x8 tile plus a one-pixel halo in one compute dispatch. Shared RGB
is quantized to the original RGBA16F precision before luminance sharpening at
0.35. Unsharpened color/sample age and surface depth write pending HistoryCache
resources directly; sharpened output never feeds history. Reads retain the accepted
texture and its own previous view until commit. Capacity-only changes preserve
valid history; failed resize writers leave the accepted target untouched. Commit/rollback and
capture semantics remain graph-owned. The two-dispatch algorithm exists only in
a test fixture as an independent numeric reference.

## Explicit MeshDraw preparation

Structural culling identifies live consumers before DrawLists resolve. Compilation
then inserts UploadMeshDrawData and optional ComputeMeshDrawCompact producers
before the first consumer, with explicit visibility, metadata, transformed-index,
count and indirect-argument dependencies. Other consumers reuse their outputs.
CPU Direct records no Compact producer. Raster execution contains no implicit
upload/dispatch prelude. GPU batch metadata, indices and args use global disjoint
ranges, so preparing a later batch cannot overwrite an earlier one.
Neutral functional scopes contain prepare and Raster passes as peers. Samplers
remain the naming authority; internal markers distinguish RasterMeshDrawDirect,
RasterMeshDrawIndirect and RasterRendererList. Unity's native batching labels
and shadow slice context remain intact.
