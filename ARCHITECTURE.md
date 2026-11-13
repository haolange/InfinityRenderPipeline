# Infinity Render Pipeline — Architecture

This document describes the implementation in this package. Its **Contracts** subsections are normative: they are the invariants each subsystem keeps. When the implementation changes, revise the affected statement. Do not append iteration notes. Principles, design rules, conventions and the work method are in [AGENTS.md](AGENTS.md). Authoring is in [Docs~/Manual/index.md](Docs~/Manual/index.md). Verification state is in [Docs~/Project/Status/FeatureStatus.md](Docs~/Project/Status/FeatureStatus.md). Archived plans and receipts under [Docs~/Record/History/](Docs~/Record/History) are evidence, not a specification.

## 1. System

InfinityRP is a Scriptable Render Pipeline. `InfinityRenderPipeline` derives from `RenderPipeline` and is split across partial files under `Runtime/Pipeline/`. A camera frame collects Unity culling results, builds an Infinity light context, records a render graph, executes that graph, and submits the scriptable render context.

Two geometry paths share the frame:

- Native renderers are submitted with Unity `CullingResults`, `RendererList` and `ShadowRendererList`. Lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion data stay on those renderer lists. Camera renderer lists are built only by `CameraRendererLists` from `RendererListParams`; the asset's instancing flag sets `DrawingSettings.enableInstancing`; Unity 6000.6 removed dynamic batching from `DrawingSettings`. A renderer list is created after culling only when a live pass consumes it, and submitting a list this graph did not create for the pass throws.
- `MeshComponent` instances are entities of `MeshWorld` and are submitted through `RGMeshDrawListHandle`. A mesh component does not discover or modify a `Renderer`.

The package owns the render graph, the physical resource pool, image history, the mesh world and its GPU entity columns, light records, shadow allocation and the atmosphere profile. Unity owns the device, command submission, fences, native culling and the Volume manager.

### Contracts

- Hot paths are structs and Burst jobs with an explicit `Allocator` (`Persistent`, `TempJob` or `Temp`). `unsafe` is limited to native containers and culling jobs. Burst jobs do not capture managed delegates.
- `MeshDrawCommandKey`, `MeshCullingContext`, `BufferDescriptor` and `TextureDescriptor` use field-level `Equals`; `GetHashCode` only accelerates lookup. Filter, grouping and sort are three operations, not one hash. Sort keys saturate signed and unsigned 16-bit segments before packing; silent truncation is not saturation.
- With fixed object counts after warmup, normal proxy update, graph record and execution allocate no managed memory, for stationary and moving objects. Growth, registration and requested diagnostics are separate from that path. A resource the compiled shader variant does not sample is not bound. Reuse does not shorten recording, submission, cancellation or completion ownership.

## 2. Layout

| Path | Assembly | Owns |
|---|---|---|
| `Runtime/` | `Unity.RenderPipelines.Infinity.Runtime` | Pipeline, passes, mesh and light pipelines, volumes, components |
| `Editor/` | `Unity.RenderPipelines.Infinity.Editor` | Inspectors, menus, Shader Graph target, stripper, validation |
| `Editor/Cli/` | `Unity.RenderPipelines.Infinity.Cli.Editor` | Command adapter, compiled only with `INFINITY_UNITY_CLI` |
| `Runtime/Validation/` | `Unity.RenderPipelines.Infinity.Validation` | Validation scenarios, compiled only for Editor and Development builds |
| `Editor/ShaderGraph/` | Compiled into `Unity.ShaderGraph.Editor` by one asmref | Shader Graph target only. It refers to Infinity by strings |
| `Shaders/` | `Unity.RenderPipelines.Infinity.Shaders` | HLSL, surface shaders, compute shaders |
| `Tests/Editor/` | `Unity.RenderPipelines.Infinity.Tests` | EditMode tests |
| `Tools/` | None | Offline checks, texture-view generation, refresh and capture scripts |

Runtime code is grouped by owner: Core, Scene, Mesh, Terrain, Material, Lighting, Atmosphere, RayTracing, RenderGraph, GPUResource, Volume and Pipeline in the runtime assembly, and the validation scenarios in their own assembly. Dependencies point down: Core holds only neutral types (geometry, device conventions, entity identity, documentation links), GPUResource depends on Core, RenderGraph on GPUResource and Core, material routing (`InfinityTech.Rendering.MaterialPipeline`) on neither, and the mesh mechanism (world, pass caches, backends, culling contexts, draw lists) on those layers but not on the pipeline; mesh shader IDs live in `MeshShaderIDs`. A shader property name has one C# ID: a pass binding lives in its pass `UtilityData`, a name several passes bind lives in `PassShaderIDs`, and camera matrices in `CameraUniform`. Material routing notifies mesh components through an event, not a call. `static_check.py` enforces these directions. `Runtime/Pipeline/Pass/` groups the partial pipeline by domain: Geometry, Lighting, Shadow, ScreenSpace, Volumetric, Translucent, Temporal, Post, Output, Debug. Volume components live in `Runtime/Volume/` under `InfinityTech.Rendering.Volume`. The atmosphere profile lives in `Runtime/Atmosphere/` under `InfinityTech.Rendering.Atmosphere` and is not a Volume component. `AtmosphereParameter` and `AtmosphereCache` live beside it and stay in `InfinityTech.Rendering.Pipeline`. Shaders use `Shaders/Library`, `Shaders/Surface`, `Shaders/Utility`, `Shaders/Mesh`, `Shaders/RayTracing` and `Shaders/Pass` with the same pass domains. A shader's `InfinityPipeline/` name does not follow its file path.

## 3. Configuration

| Concern | Owner |
|---|---|
| Runtime compute shaders, blit material, BestFit LUT | `InfinityRenderPipelineGlobalSettings` containers with `[ResourcePath]` |
| Default Volume profile | `InfinityDefaultVolumeProfileSettings` on Global Settings |
| Quality Volume profile, render scale, shadows, atmosphere, diffusion profiles, output mode, frame settings, mesh backend | `InfinityRenderPipelineAsset` |
| Per-camera Volume mask and trigger, super-resolution override, frame-setting overrides | `InfinityAdditionalCameraData` |
| Analytic light shape and Infinity-only light fields | `InfinityAdditionalLightData` |
| Physical light color, intensity, temperature, range, spot angles, shadows, cookie, culling mask, rendering-layer mask, bake type | Unity `Light` |
| Feature quality parameters | Volume component, resolved through `QualityPresets` unless the level is Custom |
| Debug view | `InfinityDebugDisplaySettings` at runtime. It is not serialized on the pipeline asset |

The pipeline constructor calls `VolumeManager.Initialize(defaultProfile, qualityProfile)` once. Disposal deinitializes that manager and restores `SupportedRenderingFeatures`, `Shader.globalRenderPipeline` and the `GraphicsSettings` flags captured at construction. A second registration of the same profile is not performed. There is no second hardcoded default profile.

The Editor discovers Volume types by reflection. A Player builds its registry from the components present on the global default profile. Pipeline creation rejects an incomplete registry. Optional features record when `VolumeComponent.IsActive()` is true. `overrideState` marks a scene override. It does not enable a feature.

`EQualityLevel` is Low, Medium, High, Ultra or Custom. `Resolve()` returns a settings struct. Custom reads the Volume parameters. Every other level reads the preset table in `Runtime/Pipeline/Quality/`. Passes consume the struct and do not branch on the quality level.

### Contracts

- Default Volume values come only from `InfinityDefaultVolumeProfileSettings`; quality-level values only from the optional asset `qualityVolumeProfile`. A missing Player component type returns null; no hardcoded value replaces it. The default profile includes the required color and exposure components and every consumed optional type.
- Passes read blended components from the camera's `VolumeSnapshot`, captured once after `VolumeManager.Update`; a component missing from the stack throws there, not in a pass. The atmosphere profile is resolved and validated once per camera into `CameraSetup.atmosphere`.
- Required exposure reads the resolved camera stack; `overrideState` does not gate it. Exposure is a multiply before the LUT. Tonemap is None or ACES. Combine LUT has no inactive-component constant fallback.
- A Volume feature exposes `quality` plus its low-level parameters; `Resolve()` returns `F<Feature>Settings`. Atmosphere, deferred shading and temporal anti-aliasing are not Volume features.
- Runtime resources live only in Global Settings `IRenderPipelineResources` containers with `[ResourcePath]`. The pipeline asset never references runtime shaders; its material fields are optional overrides for objects created from the GameObject menu. A missing required resource fails at record or setup. `ResolveDefaultVolumeProfile` throws when the Global Settings slot is empty. There is no `Resources.Load`. Capture diagnostics shaders live in the optional `InfinityRenderPipelineCaptureShaders` container (pipeline construction requires the other four; Unity does not add a container type to an existing asset, so the Wizard's Global settings Fix adds it explicitly, marked dirty and not saved); `CaptureShadersStripper` removes it from players built without Development, and a capture request then fails while the pipeline runs. Player shader stripping lives in `Editor/Material/`.
- Debug state is runtime and owned by `InfinityDebugDisplaySettings`; `debugView` is not serialized on the pipeline asset. A debugger control exists only with a consumer.
- The pipeline reads its asset only through `InfinityRenderPipelineAsset.Settings`, one snapshot taken at construction and normalized once (clamps, cascade ratios, resolved shadow filter). An Inspector edit recreates the pipeline through `RenderPipelineAsset.OnValidate`. Runtime code never writes the asset; Editor tools edit it through `SerializedProperty`. Static renderer poses refresh once per pipeline instance (in the Editor only when `updateProxy` is set).

## 4. Camera frame

`Render(ScriptableRenderContext, List<Camera>)` is the frame entry; `RenderCamera` runs one camera in four stages. `PrepareCamera` returns a `CameraSetup` holding every decision that does not need culling: render policy, frame settings, dimensions, temporal resolve and history reset, the volume update and its `VolumeSnapshot`, the validated `AtmosphereParameter`, camera uniforms, debug view, output transform, and the requested features with their support context. `CullCamera` runs Unity camera setup and culling, mesh and terrain LOD, `LightContext.Build` and atlas resolution, and returns a `CameraScene`. `RecordCamera` records the pass list below from a `CameraRecordContext` built from both. `m_RGBuilder.Execute` runs it. `EPipelineProfileId.RecordRG` and `ExecuteRG` are profiler labels around the last two stages.

`ConfigureFrameFeatures` runs inside `PrepareCamera` from the asset, the camera and the active volumes. `ConfigureContentFeatures` runs after `LightContext.Build` and decides tile masks, moment shadows, atlas updates and contact shadows from the collected lights.

Recording order, with the gate that skips a step:

1. **Frame constants.** `UploadLightData`, `ComputeLightCookies`, `ComputeReflectionProbes`, `ComputeCombineLuts`, `ComputeAtmosphericLUT`, `ComputeTemporalColorPyramid`. Cookies, probes and the color pyramid record only when their feature is enabled. The atmosphere lookup requires `atmosphericalProfile`.
2. **Geometry.** `RegisterMeshHistoryOccluder` when temporal rendering, Hi-Z, GPU indirect drawing, live instances and a valid Hi-Z history are all present. Then the depth and shadow lists' producers. With the history occluder: `RasterDepth`, `ComputeEarlyFurthestHiZ` as the new occluder, the remainder depth list and the other lists' producers (every one of them tested against the early pyramid), then `RasterDepthRecovery`; without it every list's producer precedes `RasterDepth`. Optional `RasterDBuffer` when the world contains decals, then `RasterGBuffer`, `RasterMotion`.
3. **Depth-derived work.** `ComputeHiZ`, `ComputeHalfResDownsample`, `ComputeLightTileMask` when a local light list exists, ray-traced or ground-truth occlusion, `ComputeContactShadow`. When the device supports async compute, screen-space reflection and screen-space indirect diffuse record here.
4. **Shadows.** `CullShadowCasterSplits` (the camera's allocated cascade and local splits, sorted by light, in one Unity `CullShadowCasters` call), `RasterCascadeShadow`, `RasterLocalShadow`, `ComputeShadowMoments` when a moment filter is in use.
5. **Lighting.** `ComputeDeferredShading`, optional deferred-lighting capture, `RasterForward`. Without async compute, reflection and indirect diffuse record here. Then `ComputeScreenSpaceComposite`, `ResolveOpaqueSceneColor`, `CopyHistorySSR`, `CopyHistorySSGI`, `ComputeBurleySubsurface`, `ComputeAtmosphericSkyAndFog`, optional lighting capture.
6. **Translucency.** `RasterTranslucentDepth`, volumetric cloud and its history copy, volumetric fog and its history copy, `ComputeFogComposite`, `ResolveFoggedSceneColor`, translucent T0, `ComputeColorPyramid`, translucent T1, translucent T2.
7. **Temporal resolve and output.** `ComputeSuperResolution` or `ComputeAntiAliasing` by the camera's `ETemporalResolve`; preview cameras have none. Then `RecordCameraOutput`.

Draw-list release is paired with execution. A camera exception rolls the frame state back, discards unproduced shared reservations, and preserves the first exception. Queue-accepted work is still submitted. Physical retirement runs after a successful submit and a known completion fence.

Async eligibility is a queue assignment. It is not a measurement of overlap. The current Metal validation host reports no async compute support. Early occlusion recovery and late shading, subsurface, temporal resolve and post stay on the graphics queue because their inputs are the surrounding raster work.

### Contracts

- Compute kernels (index and thread-group size), compute local keywords and blit shader pass indices are resolved once into `PipelineKernels` and `BlitShaderPasses` when `InfinityRenderPipelineResources` loads, and again in the first frame after an Editor import of a shader or compute shader. `FeatureRegistry` evaluates each feature's kernels and textures at that moment; per camera it adds only temporal, diffusion-profile and RTAO support. A kernel no feature gates, and every blit pass, is required: resolution throws.
- Volume, asset and dependency configuration run before culling. Recording that depends on scene content is decided after `LightContext.Build`.
- Depth, GBuffer, DeferredShading and Display are `FeatureRegistry.RequiredProducers`: every camera requests them, resources that cannot support one fail when resources resolve, and a camera that did not produce one, or its temporal resolve, throws after recording. A camera that records motion declares `DepthTextureMode.MotionVectors` once; the pipeline does not rewrite the flag every frame.
- A pass decides whether an optional input exists from the feature set (or the producer's own allocator state, as for shadow moments) and reads it with `QueryTexture`, which throws when the producer did not register it. It never infers a producer from whether a resource happens to be registered; `TryQuery` remains only for debug views and role adoption.
- Per-camera inputs reach a pass only through `CameraRecordContext`, taken by `in` and never stored; no pipeline field holds the active camera. World data comes from the pipeline-wide `renderContext` field, never from a parameter of the same name.
- Camera matrices live in the `ViewUniform` constant buffer (`Shaders/Library/ViewUniform.hlsl`, GPU-RECORD `ViewUniform`), which declares only matrices some shader reads, each under the name of its exact variant. `CameraFrameState` owns one `ViewUniformBuffer` per camera; `CameraUniform.SetUniformData` writes it once before culling and binds it globally, so raster and compute passes, async ones included, read the same values and no pass copies a camera matrix. The graph joins the async queue at its end, so a later camera's upload never overtakes a reader. Shadow casters project with `_ShadowCasterViewProj`, which the shadow passes set per cascade or local face; they never touch camera constants. The UnifiedRayTracing occlusion shader receives its matrix through the backend's binding calls.
- A missing volume or a disabled optional feature skips recording; so does an optional feature whose kernel is missing, with its reason in the Rendering Debugger. A pass that records trusts the tables: it neither looks a kernel up by name nor checks it again. Lighting, active temporal anti-aliasing and display throw at record when their contract is broken. Execute never contains `if (shader == null) return`.
- History copies of previous-color effects sit after the independent raster window. A graphics transfer immediately after an async producer joins that producer.

## 5. Render graph

`RGBuilder` records passes, textures, buffers and renderer lists for one camera graph. A handle carries the generation of its slot and is valid exactly when that generation is nonzero; every handle-taking accessor resolves through one checked lookup, so a handle from another graph is rejected. Two handles name the same resource when index, type and generation match. Work the core does not model attaches as one `IRGGraphExtension` per builder. The extension sees the recorded passes once per compile and clears its state whenever the recorded graph is dropped, with or without execution. It does not add or reorder passes. A pass stores the extension's ids in `extensionUses`, and pass handles and raster encoders carry their builder's extension, so a draw list from another builder is never consumed or submitted. The render graph depends on neither the pipeline nor the mesh pipeline: it executes on a `ScriptableRenderContext`, and mesh draw lists are its extension, in `Runtime/Mesh/Graph/`. Recorded order is the schedule.

Compile runs structural culling once. Culling propagates across texture and buffer reads. A pass that writes an imported resource, or that calls `EnablePassCulling(false)`, stays live. Mesh draw-list producers are already in the recorded sequence, before their raster block. The compiler then builds native render-pass groups and submission segments. A segment is queued as a unit; a new segment starts at a queue change, a wait, or a create whose physical descriptor matches an allocation released earlier in the open segment, because the pool would hand that allocation over before its last use is queued. A pass-local resource's owner is recorded once per compile. Resource checks compare the handle generation with the live slot and format the API name only when the check fails.

A submission segment is a run of passes on one queue. It breaks when the queue changes, when a pass waits on a cross-queue fence, or when a create (at its group-hoisted position) matches the physical descriptor of an allocation released earlier in the segment. One segment is one `ExecuteCommandBuffer` or `ExecuteCommandBufferAsync`. Adjacent raster passes merge into one native render pass when size, sample count and attachment limits allow and no pass samples an attachment written inside the group. Unity's attachment limit is eight, including depth. A non-imported attachment whose last use is a store becomes `DontCare`. On Metal, an attachment that never leaves that render pass can be memoryless, except a color UAV.

Unity exposes no GPU heap. Overlap is descriptor-level reuse by `ResourcePool`; the graph keeps no slot plan of its own, and both report sizes with the descriptors' one byte estimate. Repeated imports of one physical texture or buffer share one graph identity. Conflicting logical views of one texture fail at record time.

Pass types are fixed by the work. Transfer copies. Raster draws. Compute dispatches. Ray tracing is the render-graph type of the ray-traced occlusion pass. Gizmos, wire overlay, final output and UI overlay stay raster. `SetRenderTarget` is not called inside a pass execute. Attachments are declared at record time.

`EnableNativeRenderPass(false)` is used only where a native render-pass attachment cannot represent the target: gizmos and wire overlay, because Unity forbids gizmos inside `BeginRenderPass`; final output, because the backbuffer has no owning `RenderTexture` from which to resolve `AttachmentDescriptor.graphicsFormat`; and UI overlay, because Unity does not expose the overlay renderer list as a native render-pass payload.

`RGBuilder.Execute` propagates the original exception. Graph commands are isolated from the enclosing camera profiler scope. Unreturned graph allocations return to the pool only after submit. `RGProfilingScope` is a pass range: on one segment the parent sampler begins on the first live pass and ends on the last. A segment split closes it and opens it again. It does not insert a pass. Pass GPU names come from `pass.customSampler`.

### Contracts

- A designed path is not disabled to quiet the Console: no `EnableAsyncCompute(false)` as a correctness switch, no `throw` replaced by `return false`, no dispatch of count 0 that only looks wired. An unimplemented feature is not recorded.
- An invalid `RGTextureHandle` or `RGBufferHandle`, a failed query or a failed resolve fails at record or setup. `Texture2D.blackTexture`, a lighting buffer used as history and an empty `RenderTargetIdentifier` are never bound. Unity's "temporary render texture" message means an empty identifier, not a `GetTemporaryRT` call: check handle validity, pass order and which producer must register the ID.
- Graph textures are created, imported or taken from `ResourcePool`; hot paths do not call `GetTemporaryRT`. Mip chains stay in one compute pass: the graph tracks resources, not subresources.
- Every buffer in the pool, the graph and the caches is a `GraphicsBuffer`; `BufferDescriptor.target` is its `GraphicsBuffer.Target` (default `Structured`; indirect arguments declare `IndirectArguments | Structured`). An external `GraphicsBuffer` import builds its descriptor from the buffer's count, stride and target and lands in the same `RGBuffer.resource` as a pooled one.
- The scoper belongs to the graph builder and clears with the recorded graph. Downstream reads the semantic ID the last producer registered; a role such as `ResolvedSceneColor` is republished by each producer in order (fog composite, then super resolution or temporal anti-aliasing) and post processing reads only the role. `DisplayColorBuffer` is the present source when a linear intermediate exists; consumers do not guess among `SuperResolutionBuffer`, `AntiAliasingBuffer` and `LightingBuffer`.
- Feature code uses `IComputeCommands`, `IRasterCommands`, `IRaytracingCommands` and `ITransferCommands`, implemented by graph encoders. Each encoder keeps its pass type's restrictions and forwards to one `RGCommandCore` that holds the command buffer and the graph's own `RGResourceFactory`; there is no global current factory and no wrapper outside the graph. Graph handles are bound only through an encoder, which resolves them through that factory, so a handle from another graph is rejected where it is bound. `RGTextureHandle` and `RGBufferHandle` have no implicit conversion to a Unity resource, and encoders define no `implicit operator CommandBuffer`. Encoders take shader property IDs, never names, and no `params` overloads; the unified ray-tracing acceleration structure, `SetRayTracingShaderPass` and `DispatchRays` keep a name because Unity has no ID variant. A compute keyword is set through `SetKeyword(ComputeShader, in LocalKeyword, bool)` on the encoder, not on the shader.
- `ReadOnlyDepthStencil` is set only when the pass declared read-only depth and does not write it. A color-only target uses the color-only `SetRenderTarget` overload; an empty depth identifier is never put in a `RenderTargetBinding`. Attachment Load is a read. Depth that a later effect or capture consumes is stored.
- `CommandBufferPool.Get()` is unnamed. Camera and pass groups use `ProfilingScope(cmd, sampler)`. A `ProfilingSampler` name never matches a `cmd.BeginSample(string)` argument. In-pass markers use a cached `ProfilingSampler` of that name (`BloomDownsample`, `BloomUpsample`, `InitializeIndirectArgs`, `Compact`). `ProfilingScope(sampler)` without a command buffer is a CPU-only track.
- RAW, WAR and WAW hazards compile on the effective queue, gated by device async-compute and fence support. An async-queue fence is never queried as a CPU completion signal on a Metal device that does not support it.
- Native submit, queueing the final fence and accepting the frame transaction are three results. A fence that was never queued acknowledges no work.

## 6. Resources and history

`ResourcePool` owns physical textures and buffers for the graph and for other clients; nothing else in GPUResource or RenderGraph allocates GPU memory. Every acquisition returns a handle carrying its lease (acquisition generation), and a return through a handle of an earlier acquisition throws. The pipeline requires graphics fences at construction. `BeginFrame` opens a recording epoch. Release keeps recording references and queued GPU use; the release overloads without a reuse fence rely on that tracking alone. Same-queue reuse follows queue order. Opposite-queue reuse waits on a queued dependency. CPU `SetData` requires no recording or pending use and a CPU-queryable completion, or a separate writable allocation. A CPU frame transition is not permission to write.

Retention watermarks are 400 MiB for textures and 64 MiB for buffers. Resources idle for more than two successful submission epochs are evicted oldest first above those watermarks. The watermarks are not a working-set limit. Destruction waits for a CPU-queryable completion fence and does not block the CPU. Size counters are format estimates, not driver residency.

`HistoryCache` is the only image-history store. It borrows its committed and pending targets from `ResourcePool` and returns replaced or discarded ones with the frame's reuse fence; its owner disposes it before the pool. Readers import the accepted history through `HistoryCache.Read(id, descriptor, resetHistory)`, the one validity rule: same logical layout, committed at least once, not discarded by the camera's reset; a feature that can skip frames also requires its accepted flag. Writers import a pending target with the same descriptor. A producer may write the pending target directly, as temporal anti-aliasing and super resolution do, or copy into it with an explicit CopyHistory transfer. Pending history is accepted only after the camera submission succeeds. A failed camera or a failed frame resets history. Every owner of speculative GPU state is an `IFrameTransaction`: the atmosphere shared cache, local shadow cache, cookie atlas and reflection probe atlas are registered once on the pipeline; each camera's history cache, atmosphere view cache and combine LUT cache are registered on its `CameraFrameState`. `Render` drives them in one order: `BeginFrame`, `CameraFailed(queueUncertain)` for a throwing camera, `CommitFrame` or `RollbackPending` by the frame's submission, `FlushRetired` with the reuse fence. Each owner states its own camera-failure policy: camera-owned caches discard all pending writes, the shared atmosphere cache drops unproduced pending targets, in-place atlases keep pending state after a queue failure so completion invalidates it, and the local shadow cache relies on commit to invalidate unqueued tiles. Committed and pending acquisitions swap with their descriptors. A failed pending resize does not replace committed history.

Logical width and height are the pixel coordinates and the dispatch bounds. Allocation width and height are the physical compatibility key. `TextureDescriptor.Equals` compares the full logical descriptor. Physical compatibility is a separate field-level compare. History is not rebuilt through `RenderTextureDescriptor`, because that conversion drops the depth-stencil format and the mip count.

Each camera has its own `TextureCapacityState` for internal and display extents. Capacity aligns to 64 texels, grows by at least 12.5% when exceeded, and shrinks after 30 accepted requests whose three-request peak is below 75% of the capacity area. Orientation changes reset that history. Failed submissions do not advance it. A capacity-only change does not invalidate history. Preview and capture cameras do not grow another view. Fixed lookup tables, atlases and external targets use exact allocation.

Screen-texture bindings carry logical and physical metadata. Binding a graph texture to a property in the generated view registry writes that texture's descriptor view, mip-scaled for a mip binding, to the property's `_InfinityView` companion; a graph texture without a logical size is rejected there. A texture outside the graph has no descriptor, so the encoder rejects it for a registered property unless the caller states its view with `TextureView.Whole` or an explicit `TextureView`. Normalized sampling uses `TextureView.hlsl`. Integer loads use logical pixels. Material texture sampling is unchanged. Binding registries are generated with `python3 -B Tools/TextureViews.py --write`.

`MeshWorldGPU.transformVersions` is the exception to image-history ownership. It holds immutable matrix and generation buffers per entity slot. A camera keeps accepted and pending version references. A revision advances only when matrices or slot membership change. Dirty 64-slot pages upload once per version, and a compute producer copies the unchanged elements. Shader motion checks world identity, snapshot range and slot generation.

Clear depth and sampled depth are different values. `ClearRenderTarget` and `AttachmentDescriptor.clearDepth` take `GraphicsConventions.ClearDepthFar`, which is normalized so that 1 means the far plane. Shader comparisons against a sampled depth buffer use `GraphicsConventions.SampledFarDepth`.

### Contracts

- Image history lives only in `HistoryCache`. A same-frame scoper ID that has not been written is not history. Shared-cache validity comes from queue-accepted production, independent of camera success.
- `ReleaseBuffer` and `ReleaseTexture` keep recording references and queued use. Cancelled recordings release only their recording references. Partially queued or uncertain submissions stay protected. Pool return is not proof of safe CPU reuse.
- Camera framebuffer descriptors declare Internal or Display sizing.
- Screen-binding registries are generated only by `python3 -B Tools/TextureViews.py --write`, never by loading assets; the offline gate checks them.

## 7. Mesh drawing pipeline

`MeshComponent` content is not submitted through a renderer list. Layers, from stable to per request:

1. **World**: slot tables for entities, elements, sections and materials, owned by `MeshWorld`, one per `RenderContext`.
2. **Pass**: one `MeshPassCache` per interned `MeshPassDesc`: which elements belong to the pass and which cached command each draws with.
3. **Culling context**: what a list culls against (planes, layer, scene and rendering-layer masks, LOD projection). Visibility is evaluated inside each list's producer; there is no per-view visibility product.
4. **Draw list**: one culling context and one pass: a gather job that tests each element, then sort and merge, on the CPU backend; one producer kernel that tests and compacts each element on the GPU backend.
5. **Graph**: the builder's `MeshGraphExtension` records each nonempty list's producer where the list is created and submits the list from its raster consumer.

```text
MeshEntity  (slot = transform slot)  desc, LOD chain, element chain   transforms[], bounds[] parallel columns
  └─ MeshElement (one submesh of one LOD)  section ─► MeshSection (mesh, submesh, refCount)
                                           material ─► MeshMaterial (id, queue, route, stage, shader, refCount)
MeshPassCache   candidates[element] = (entity, command | lod << 24)   commands[] = key (section, material), refCount, stateKey
MeshDrawListBuild  items (key, command, entity, fade) ─sort─► draws (command, fading, offset, count)
                                                                    + instanceToEntity[] + instanceFade[]
```

Index spaces are entity, element, command, draw and instance; arrays are named `<from>To<to>`. Ids store an index and a generation. Live generations are odd, dead ones even. A killed slot returns to its free list only when the transaction commits, so an id handed out by a rolled-back transaction never matches a later slot.

| Type | File | Responsibility |
|---|---|---|
| `MeshWorld` | `Runtime/Mesh/World/MeshWorld*.cs` | Tables, revisions, dirty pages, pose writes, dynamic poses, interned pass caches, backends, residency |
| `MeshWorldUpdate` | `World/MeshWorldUpdate.cs` | Structural transaction lease and per-table undo logs |
| `SlotTable<T>` | `World/SlotTable.cs` | Fixed-index storage with generations and commit-time reclaim |
| `MeshWorldGPU` | `World/MeshWorldGPU.cs` | Entity columns (bounds, flags, masks, rendering layers, LOD chains) by dirty 64-slot pages |
| `MeshTransformVersionCache` | `World/MeshTransformVersionCache.cs` | Immutable per-entity transform versions and their GPU production |
| `MeshPassDesc`, `MeshPassTable` | `Pass/MeshPassDesc.cs` | Pass request: LightMode, queue range, sort mode, required entity flags, previous transforms |
| `MeshPassCache` | `Pass/MeshPassCache.cs` | Incremental membership, command refcounts, bindings, candidate table |
| `MeshCullingContext`, `MeshLODSelection` | `View/` | The culling slot (a GPU record) and LOD selection |
| `MeshDrawListBuild` | `DrawList/MeshDrawListBuild.cs` | The culling gather, sort and merge jobs; `SortKeyPacking` |
| `MeshDrawCPUBackend`, `MeshDrawGPUBackend` | `Backend/` | World-owned backends: instance tables, payloads, draw-list production, submission |
| `MeshGraphExtension`, `MeshDrawListRegistry` | `Graph/` | Per-builder graph extension and draw-list registry. A draw list is not an `ERGResourceType` |

### World

`MeshWorld.BeginUpdate` opens one transaction and snapshots revisions and dirty pages. Each table logs the slot state before its first change. `Commit` first asks every pass cache to prepare the bindings it would create; a failure leaves the transaction open and the lease's `Dispose` rolls it back. After that the transaction is closed, so nothing can revive a reclaimed slot. Commit then reclaims killed slots, retires sections and materials whose reference count stayed zero, applies pending mobility changes and publishes the dirty elements to every pass cache. `Rollback` replays the logs in reverse, rebuilds the touched section and material lookups and restores revisions and dirty pages.

Poses do not take part in transactions. `WriteEntityPose` writes one static pose; `UpdateDynamicPoses` reads every dynamic entity's `Transform` in an `IJobParallelForTransform`, writes changed poses by entity index and marks dirty pages in a second job. Both throw while a transaction is open. Changing a flag that decides membership (`CastShadow`, `ObjectMotion`) is a structural edit. No component polls its transform: a static pose is read when the entity is created, after `NotifyEntityChanged`, and, in the Editor, through `MeshComponentEditNotifier` (Undo-recorded edits of the object or a parent notify that hierarchy; undo, redo and hierarchy changes in Edit mode notify every static component).

### Pass

Membership of an element in a pass is: its material's route and stage enable the pass LightMode (`MaterialRouteUtility.RouteEnablesPass`, the one authority that also sets material pass state), its queue is inside the desc range, and its entity carries the required flags. Pass state on the material is not consulted. A command is keyed by (section slot, material slot) and binds the mesh, material, pass index and submesh range when it is created. Its state key packs a per-cache shader-state rank, the material slot and the section slot, each saturated to its segment. After a shader import `MeshPassShaderUtility.NotifyShadersChanged` advances an epoch and each cache resolves its pass indices again before its next draw list.

The nine passes (Depth, GBuffer, Forward, Motion, Shadow, TranslucentDepth, TranslucentT0, T1, T2) are created with the world, so shadow-caster bounds and residency see them before any draw list. A tag outside those nine fails at `CreateMeshDrawList`. Later membership changes upload candidate tables in residency. Motion and the translucent stages require the camera's mesh motion (`SetMeshMotion`).

### Frame

1. Proxy update: registration tasks, one `MeshWorldUpdate` for removals and every dirty component, explicit static poses, then dynamic poses. A component stages its sync inside the transaction and publishes it after the commit; an id is valid only in the world that issued it, and a new world resyncs every registered component. A shader import or a shader swap on a material makes the affected components validate again and the commands resolve their pass indices again.
2. `MeshWorld.UpdateResidency` uploads dirty entity columns, the next transform version and stale pass candidate tables.
3. `MeshWorld.BeginCamera` resets per-camera counters.
4. `InfinityRenderPipeline.RecordCamera` hands the camera's transform versions to the graph (`SetMeshMotion`) and creates its lists with `CreateMeshDrawList(new MeshDrawListDesc(context.mainCulling, InfinityPassIDs.X))` (light contexts for shadows) before the raster block that draws them, and passes each handle to its raster pass. Creating a list records its producer at that point; lists are not created inside their raster pass because the graph never moves passes and a producer between two raster passes would split their native render pass.
5. Creation freezes the pass cache revision and acquires capacity: a GPU list rents its payload, a CPU list its instance tables. A list with zero pass-cache members, or with nothing left of its context and pass, is empty: it acquires nothing and records no producer, its raster consumer and renderer list stay live, and submit still rejects a changed revision. A GPU list records one compute producer (`ComputeMeshDrawList`); a CPU list records one upload producer (`UploadMeshData`) and schedules its gather, sort and merge jobs at compile when its consumer is live. Compile culls a producer with its consumer.
6. Execute: the CPU upload completes the build jobs and uploads `instanceToEntity` and `instanceFade`; the CPU consumer issues one `DrawMeshInstancedProcedural` per merged run. The GPU producer uploads the command metadata and the culling context, runs `InitializeIndirectArgs` for both ranges of every command slot, then one build kernel (one thread per element slot) tests the element's entity against the context (and its occluder) and atomically appends it to its command's range. The consumer issues one `DrawMeshInstancedIndirect` per live command for its stable range and, for a command with members whose chain has a band, one more for its fading range. Raster submission only draws.
7. `ReleaseAll` completes and frees build jobs and retires payloads, instance buffers and transform-version references. After native submit, `FlushRetired` returns allocations to `ResourcePool` without clearing outstanding GPU use.

The CPU sorts instances by the pass key, then command, entity and fade, and merges adjacent instances of one command and one fading state. `StateSortKey` puts fading in bit 63 and the command `stateKey` in the low 63 bits, so stable instances precede fading ones and one command stays one run. The command `stateKey` leaves bit 63 clear by saturating its shader rank at 15 bits. The GPU does not read the instance key: it appends instances in candidate-slot order and submits one indirect draw per command in `stateKey` order. `TranslucentColorSortKey` packs priority in the high 16 bits and distance from far to near in the low 32. T0, T1 and T2 set `cpuBackend` and record on the CPU. The order is total.

### LOD

An entity carries a `MeshLODChain` of 1 to 8 levels in `LODGroup` semantics: level *i* draws while the screen size is at least its lower bound `screenSize[i]`, bounds strictly decrease, and below the last bound nothing draws (a last bound of 0 never culls). Screen size is the largest half-extent of the world bounds (`LODGroup.size` / 2 for an unrotated object; rotation grows the world bounds by up to √3) × `scale` × `bias` / distance, where `CameraCulling.LODProjection` takes the camera position, 1 / tan(fov / 2) or 1 / orthographic size, and `QualitySettings.lodBias`; orthographic projections use distance 1. Light contexts copy their camera's projection, so casters switch level with the visible object. Each element names its level.

The cross-fade band of level *i* lies above its bound and is `fadeWidth` × (previous bound − bound) wide, with a previous bound of 1 for level 0; a bound of 0 has no band. Inside the band the fade *f* runs from 0 at the bound to 1 at the band top. The CPU gather job (`MeshLODSelection`) selects `level` and `fade` for each element's entity; the GPU build kernel (`MeshPackLODVisibility` in `MeshLOD.hlsl`) packs one word, 0 or `1 | level << 8 | fade << 16`, with 65535 for stable and the band position quantized to [0, 65534). Both evaluate the same expression; `precise` keeps every list of a context on one answer.

A pass dithers LOD transitions when its queue range is opaque (`MeshPassDesc.ditherLODTransitions`). There, inside a band the selected level draws with +*f* and the next level with −*f* in the command's fading range; `LODDitheringTransition` keeps complementary pixels for the two signs, so the pair covers every pixel once. At *f* = 0 the outgoing level is gone. Translucent passes draw the selected level only and never fade. Every command has a stable and a fading range: GPU command metadata holds index count, start index, base vertex, stable base and fading base (stable base + member count), counts and indirect arguments are interleaved per (command, range), and instance capacity is twice the member count. Fading draws set the global keyword `INFINITY_LOD_DITHER` and clear it afterwards; shaders read the factor through `MeshInstanceFade`. Ray tracing takes level 0 only.

### Content

`MeshComponent` draws a `MeshAsset`: `meshes[i]` is LOD *i* (one mesh may fill several levels), `lODInfos[i].screenSize` its lower bound, `lODInfos[i].materialSlot[s]` the material slot of submesh *s*, and `fadeWidth` the band width. A submesh without a slot entry has no material and draws nothing; an explicit slot outside the asset's materials makes the asset invalid. The asset's materials are authoring defaults that the inspector copies into a component once, on assignment; the component's material array, indexed by slot, is the runtime authority. Components share one geometry state per asset (level meshes, submesh counts, union bounds and the element table, one entry per level and submesh). An edit of the asset or one of its meshes marks that state stale, and every component that references it rebuilds its entity on the next sync. Only a registered component holds a reference on its asset's state, and destroying the asset re-syncs its components. A component whose asset fails `MeshAsset.ThrowIfInvalid` is logged with its context and not drawn.

### Backends and shaders

`EMeshDrawBackendPolicy` is Auto, CPUDirect or GPUIndirect. A diagnostics override wins, then the Rendering Debugger override, then the asset; the pipeline resolves that once per camera into `MeshWorld.requestedBackend` and writes the debugger's backend state. Auto and GPU Indirect use the GPU backend when the device supports compute and instancing; a missing kernel of the mesh compute shader fails world construction. There is no process-global indirect-argument buffer and no same-frame visible-count readback. The compute shader is required on every backend because transform versions are produced on the GPU.

Shaders read `InstanceToEntity[SV_InstanceID + InstanceOffset]` through `MeshInstanceEntity`, then `EntityTransforms`, `EntityRenderingLayers` and, for motion, `PreviousEntityTransforms` guarded by generations. A mesh shader declares the SubShader tag `InfinityMeshInstancing = True`; a component whose material lacks it, or lacks a pass its route enables, is logged with its context and not drawn.

`ERenderingLayer : byte` is the shared mesh and light mask, with `Everything = 0xFF`. Surface masks in GBuffer C are an 8-bit unorm value. High bits are rejected.

Recording a list for a pass is:

```csharp
RGMeshDrawListHandle draws = graph.CreateMeshDrawList(new MeshDrawListDesc(cullingContext, InfinityPassIDs.RasterGBufferPass));
data.meshDrawList = pass.UseMeshDrawList(draws);
// execute:
cmdEncoder.SubmitMeshDrawList(data.meshDrawList);
```

Content code mutates `MeshComponent`. It does not call `CreateMeshDrawList`. Submitting a mesh draw list records its draw calls into the raster encoder. Native submit and a submission segment are command-buffer submission.

Draw markers are `Shadows.DrawMeshDirect` and `Shadows.DrawMeshIndirect` for the Shadow pass and `RenderLoop.DrawMeshDirect` and `RenderLoop.DrawMeshIndirect` for every other pass. `MeshDrawProfiling` owns the shared samplers. CPU schedule, completion, upload and draw markers include the pass in their name. Completion time can include main-thread job execution. `drawListProductions` counts recorded GPU producers and `drawListKernelDispatches` their kernels for the last camera, while a frame-tree export counts actual ready events per list and kernel. A producer is not recorded inside its consumer's profiling range, so a raster pass group contains that pass's draws. Shared sampler names do not merge graph nodes.

`TranslucentPass` draws MeshComponent content from the TranslucentDepth and T0/T1/T2 presets before the native renderer list of the same pass, with the pass's keywords and global inputs already bound. TranslucentDepth uses the state key. T0, T1 and T2 record on the CPU and order by priority, then distance from far to near. The two lists are sorted separately, so MeshComponent and native translucent renderers have no distance order between them. Out of scope for this pipeline: GPU sorting of draw keys, meshlets, and replacing renderer lists for Unity `MeshRenderer` objects.

### Contracts

- Entity data belongs to the entity: one logical object owns one entity slot, which is its transform and bounds record; submesh, material and pass add element and command references. An element is one submesh of one LOD level with one material.
- Stable structure is cached; view results are regenerated. Pass caches change incrementally at commit; visibility, LOD, sort and compaction are per draw list. There is no `MeshDrawRequest` type. Naming follows the flow `MeshDrawListDesc` → `CreateMeshDrawList` / `UseMeshDrawList` / `SubmitMeshDrawList`; a list holds `MeshDrawCall`s merged from `MeshDrawInstance`s of cached `MeshDrawCommand`s.
- A structural lease carries the transaction generation. Section and material reclaim waits for commit. Pass caches live as long as their world. Rollback never truncates free lists. `MeshWorldGPU.Update` throws while a transaction is open; replacement tables are published atomically and draw lists bind the frozen tables.
- Visibility is a pure function of (culling context, entity, occluder), evaluated inside each list's producer; no visibility result outlives its list. TempJob arrays do not survive the frame.
- An occluder belongs to a culling context and the latest registration wins. On the GPU backend a temporal camera with valid Hi-Z history registers the previous frame's pyramid for its main context (a history occluder proves nothing for an entity whose transform changed), draws depth, registers the early pyramid of that depth and records a second depth list. Only a list tested against the history pyramid records which candidates it drew; the next list of that context and pass, tested against a newer occluder, draws only the rest, so an instance draws at most once per context and pass in a frame. A list tested against this frame's depth, or against nothing, leaves nothing a newer occluder could reveal, so every later list of its context and pass is empty.
- GPU payload objects are empty between uses; physical buffers belong to `ResourcePool`. A payload lifetime is not permission to destroy a buffer or write it from the CPU. Capacity (member count, command high water) is acquired once before recording; execution checks it and never grows or recreates a buffer bound to a live command buffer. A draw list whose pass cache changed after record fails at execute. The stable and fading instance and argument ranges of every command are disjoint.
- CPU direct records no GPU mesh production. Backends hold no static mutable state. The candidate table holds elements of live entities only; neither backend tests an entity generation. A translucent instance's distance is measured from its context's LOD origin.
- `ERenderingLayer` is `enum : byte`, `[Flags]`, `Everything = 0xFF`; no integer layer index is reintroduced. Pass membership does not apply the mask; shadow views do, through visibility.
- Callers use `MeshAsset`, `Mobility`, the instance properties and `SetMaterials`; `Materials` is read-only. One structural transaction per frame syncs every dirty component.
- Notifications: `NotifyGeometryChanged(Mesh)` after in-place mesh edits, `NotifyGeometryChanged(MeshAsset)` after script edits of an asset, `MaterialRouteUtility.ApplyPassState` after route or stage edits, `NotifyMaterialRoutingChanged` after direct queue edits, `NotifyEntityChanged` after static pose, layer, scene or external entity edits from script. Editor edits notify through Undo and hierarchy events. Dynamic poses are read every frame. Ordinary uniforms do not invalidate draw structure. Inspector and Undo callbacks notify without saving assets.
- Every opaque mesh pass (Shadow, Depth, GBuffer, Forward, Motion) declares `multi_compile _ INFINITY_LOD_DITHER` and calls `LODDitheringTransition`.
- Mesh producer markers are `ComputeMeshDrawList` and `UploadMeshData`. A producer's in-pass scope is its pass token (`Depth`, `GBuffer`, `Motion`, `Forward`, `Shadow` or a translucent stage), then `InitializeIndirectArgs` and `BuildDrawList` on the GPU. Renderer-list submission adds no wrapper. Native `(RP ...)` labels stay as Unity emits them.

## 8. Materials

`_SurfaceRoute` is Deferred (0) or Forward (1). `_TranslucentStage` is None (0), T0 (1), T1 (2) or T2 (3). `MaterialRouteUtility.Read` rejects fractional, non-finite and out-of-range values. Subsurface requires opaque Deferred routing. `ApplyPassState` is the operation that writes renderer pass enables after a route or stage change. `ShaderGUI.ValidateMaterial` does not write serialized state.

GBuffer layout and YCoCg checkerboard encoding live in `Shaders/Library/GBufferPack.hlsl`. `GBufferPass` allocates three `R8G8B8A8_UNorm` targets and a `R16G16B16A16_SFloat` lighting target. InfinityLit exposes base color, normal, roughness, reflectance, specular level, emission color, subsurface profile and thickness, refraction, iridescence and pixel depth offset. It does not expose metallic, mask, occlusion, detail, height or emission textures. Shader Graph Lit has a Metallic block; the runtime lit constant buffer uses reflectance.

Surface shaders are `InfinityPipeline/InfinityLit`, `InfinityLit-Instanced`, `InfinityUnlit`, `InfinityDecal` and `TerrainLit`. Custom LightMode tags use the `Raster*Pass` names in `InfinityPassIDs`. Unity `ShadowCaster`, `Meta` and `SRPDefaultUnlit` stay unchanged.

### Contracts

- `MaterialRouteUtility` is the only route and stage parser; there is no second parser or rounding path. Exact values are validated before derived mutations. Runtime edits of `_SurfaceRoute` or `_TranslucentStage` call `ApplyPassState`. An inspector redraw does not apply derived pass state or mark every referencing `MeshComponent` dirty.
- Metal value and out helpers initialize their results before guarded work and return once where the cross-compiler requires a single exit. Resource access stays behind its validity gate. A rejected surface keeps its raw depth. Ray loops exit through an explicit found state and `break` without changing traversal order. Diagnostics are not suppressed and a callee result is not replaced with a caller-side default. Kernel bounds exits and group-barrier participation keep their meaning.
- A degenerate normalization is guarded before a reciprocal length. Required resources are bound and physical inputs validated at setup; no fallback texture substitutes. Compute passes take kernel index and thread-group size from `PipelineKernels` and derive dispatch group counts from that size. Deferred shading uses an 8×8 group; the clustered light lookup keeps 16×16 tiles.
- Shader warning acceptance is public `ShaderUtil` diagnostics after a production dispatch or draw, a fresh log window and the current Example feature chain. A fixture, a reimport or `FindKernel` alone is not that acceptance.

## 9. Lights

`LightSourceShape` stores the analytic emitter: `EInfinityLightType` (Directional, Point, Spot, Rect, Disk), `ESpotShape` (Cone, Pyramid, Box), source radius, soft source radius, source length, source width and height, barn doors, source angle, soft source angle, pyramid aspect and box size. When `InfinityAdditionalLightData` has authored the shape, that struct is authoritative and `LightShapeUtility.ApplyToUnityLight` writes the derived native fields on edits and setters only. A light with no authored shape derives `LightSourceShape` from the native `Light`. The GPU record `LightRecord` has no shape enum. Shape is a combination of parameters and `LightRecordFlags`.

`LightShapes.hlsl` evaluates specular with the representative-point construction and energy normalization, so a large emitter converges to the punctual result at distance. Sphere and disk diffuse use the horizon-clipped form factor. Lines and tubes use line irradiance. Rectangles use a polygon form factor. There is no LTC table. Soft source radius and soft source angle widen the highlight without moving it.

`LightContext.Build` runs per camera after culling. It collects visible lights, computes a conservative screen-tile rectangle on the CPU, sorts local lights by view depth, and builds 128 logarithmic depth bins. `LightTileMaskPass` writes one 32-bit mask word per 16×16 tile per 32 lights. `LightLoop.hlsl` (`BeginLightLoop`, `NextLight`) is the only loop. Consumers are deferred shading, forward shading, volumetric fog, volumetric clouds, contact shadows and the lighting debug view. Tiles are not culled by opaque depth, so translucent surfaces and volumes share the list. The local-light list rejects a count above `0xFFFF`.

Cookies and IES profiles share one atlas. Reflection probes are prefiltered into one atlas, axis-aligned, and limited to the asset slot count. Each atlas keeps one upload array, grown to its high water, and one queue observer created with the pipeline. Further probes are skipped in priority order. Unity culls probes by their box, so a sphere influence larger than the box can be culled while still visible. Probe rendering layers are not supported. Translucent T0 has no specular environment term.

`LightRecord` and its HLSL struct are compared by `Tools/StaticCheck/static_check.py` and by an EditMode test. A C# struct uploaded to a shader carries `// GPU-RECORD: <HlslStruct> <hlsl file>`.

### Contracts

- Unity `Light` owns color, intensity, temperature, range, spot angles, shadow mode, strength, bias, near plane, culling mask, rendering layers, bake type, bounce intensity and the cookie. `InfinityAdditionalLightData` owns `LightSourceShape` and the Infinity-only fields: shadow filter, update mode, contact shadow, volumetric weights, IES, distance fade and light layer. Derived native fields (`type`, `shapeRadius`, `areaSize`, `shadowAngle`) are written on user edits and API setters, never on load. Each visible light's `InfinityAdditionalLightData` is read once per camera from the registry (`RenderContext.GetWorldLight`) into `LightContext.AdditionalData`; record packing, cookies and shadow caching read it there and never call `TryGetComponent`.
- Atmosphere and light-list outputs are consumer inputs; producers do not prebind them as globals. Every light-list consumer declares the z-bin and tile-mask buffers and binds them with `LightLoopInputs`. Light-context uploads are Transfer producers. Replaced light-context buffers retire after submit.

## 10. Shadows

Cascade shadows use four splits in a 2×2 atlas. The asset owns resolution, split ratios, blend width and shadow distance. Local shadows use an atlas whose tile is `localShadowMapResolution / 4`. The default budget is 16 tiles. A spot, rectangle or disk uses one tile. A point, sphere, line or tube uses six cube faces. Candidates are scored by strength over distance, with single-tile shapes preferred on ties. Drops increment `LocalShadowBudgetDropped`. Rectangle and disk tiles cover a 120° cone in front of the emitter. Receivers outside that cone are unshadowed.

`EShadowFilter` is Hard, PCF, PCSS, VSM or EVSM, per light. `InfinityRenderPipelineAsset.Settings.shadowFilter` holds the quality preset or the custom filter settings. VSM and EVSM build a half-resolution moment atlas for the slices that use them. The keywords `INFINITY_CASCADE_SHADOW_MOMENTS` and `INFINITY_LOCAL_SHADOW_MOMENTS` compile that sample only when the camera produced the atlas, and the pass binds that atlas.

`EShadowUpdateMode` is EveryFrame, OnEnable or OnDemand. Directional lights update every frame. Cached local shadows keep the casters of their last render. Moving casters do not update those tiles, and there is no dynamic-caster overlay. `RequestShadowMapRendering` advances an OnDemand version.

Contact shadows trace the sun as one long ray and up to 24 local lights, largest on screen first, as short rays. Other directional lights are not included. The Volume owns the step counts. Volumetrics do not consume contact shadows.

Mixed lighting supports baked indirect and shadowmask. Distance shadowmask is the Quality shadowmask mode. Subtractive mixed lighting is not supported. Visible `Light.bakingOutput` decides shadowmask for native lights. Mesh state is not that authority.

### Contracts

- Cached depth-atlas tile copies stay raster: Unity `CopyTexture` forbids a partial depth-only copy.

## 11. Screen-space shading and subsurface

Screen-space reflection may trace at half resolution. Classify, resolve, temporal filter, bilateral cleanup and history stay at full resolution. History is the temporal output. `Samples` traces 1 to 4 rays inside `SSRTrace`. Each ray writes a Halcyon sample record: unweighted radiance, hit confidence, `Dir * HitT` and a signed inverse pdf. A positive inverse pdf is a hit, zero is empty, and a negative inverse pdf is a miss whose xyz is the unit direction. Facing failure, an edge fade of zero and invalid hit color are empty, so they do not add estimator weight. Resolve rebuilds the hit as that sample's origin plus `Dir * HitT` and rejects a direction into the receiver. It does not sample color and does not weight hits by the GGX pdf. The center pixel uses every hit. Neighbor reuse is a Poisson disk and reads only sample 0. A miss does not enter the average. The blend weight is the mean screen-edge fade of the hits, and the stored color is that average times the fade. Resolve also stores the mean and standard deviation of the unpremultiplied hit colors. Temporal weights the surface-motion and hit-point reprojections by `exp2(-10 * luma(dist))` on the unpremultiplied color and clamps that color to its interval. A history sample whose confidence is below half the current resolve is not reused, and a kept history is not clamped below this frame's color. The stored result is that color times this frame's edge fade. Bilateral cleanup filters color only and keeps the center edge fade. It runs after that only where the hysteresis-smoothed reconstruction variance is high. Classify writes a skip tile's records to zero. The indirect counter is cleared by `SSRClearArgs` in the same shader. Hi-Z and linear tiles share one indirect dispatch; the tile class selects the march. `SSRTRayCast.hlsl` clips each ray to the near and far planes and the screen. Hi-Z walks the closest and furthest pyramids. A hit is the ray segment crossing that raw-depth interval. A full-resolution texel is a point depth. A step that is already behind the next texel still hits when the shared edge lies between the two depths and either neighboring triple is colinear within 0.25 of that step. `SCREEN_TRACE_RELATIVE_THICKNESS` selects 2% of eye depth instead; production shaders do not declare it. There is no Volume thickness. Rough tiles march in screen space and refine a crossing with binary search. The refined segment must still cross the texel it lands on, and the hit stores that texel's depth. Trace-behind and toward-camera are bits of one uniform. Trace samples the accepted unsharpened previous frame through `TemporalColorSource`. That frame already contains the composite, so a later frame can show another bounce. A miss falls back to the probe and sky term. An empty sample does not.

Screen-space indirect diffuse has no resolve kernel and no ray-reuse controls. The work grid is always half resolution. `Samples` averages 1 to 4 cosine rays per work pixel. Those rays sample full-resolution accepted previous color. The production march is the same Hi-Z walk. Defining `SSGI_TRACE_LINEAR` selects the 2D march, with step count `NumSteps * 4` and four binary-search iterations. An 8×8 group compacts valid pixels in group shared memory and the valid lanes trace in that same dispatch. Denoising then runs two stages. Each stage accumulates temporally and then filters on the tangent plane. The first pass has a 6 m base radius and no normal weight. The second has a 3 m base radius and `saturate(dot)`. The radius is also scaled by view depth, a 3×3 variance of the temporal output, and ambient occlusion when that buffer exists. Each stage writes its own history, which is `lerp(filtered, temporal, 0.5 * accumSpeed)` with `accumSpeed` 0.93 when the history is mature. Bilateral upsample always writes full resolution from the second mix. Surface metadata is `R32G32_UInt`: exact depth bits, a 12+12 octahedral normal, 7 bits of age and 1 validity bit. Invalid or off-screen history has zero weight. Half-resolution samples keep the accepted view's quad phase with the history transaction, and reconstruction uses the representative sample positions, including the shorter last interval on an odd edge. Metadata is read with integer loads only, and all lanes take part in the group barriers of the fused trace. The world-space filter radius is `BaseRadius * F(viewZ) * F(variance) * F(AO)`; the three functions are monotone stand-ins because the Metro slide does not give their bodies. The production kernels do not write a ray record: ray-hit storage is diagnostic-only, a trace capture snapshots trace radiance before the first temporal stage, and diagnostic readback decodes metadata on the GPU because Metal does not expose `R32G32_UInt` to `AsyncGPUReadback`. Intensity is a Volume scale from 0 to 10. Low and Medium trace one ray. High and Ultra trace two.

Ground-truth ambient occlusion traces, filters and accumulates at half resolution, then upsamples. Its history is half resolution. Ray-traced ambient occlusion records at the internal resolution when Unified Ray Tracing has a context, an acceleration structure, `VisibilityRTAO.urtshader` and at least one instance. Otherwise ground-truth ambient occlusion owns the ambient-occlusion buffer. Ray-traced ambient occlusion does not use the occlusion history cache. `SystemInfo.supportsRayTracing` is not treated as a Metal capability. Metal uses the compute backend of Unified Ray Tracing.

Subsurface scattering, when the Volume is active and a diffusion profile exists, writes a diffuse-only component from deferred and opaque forward shading; the attachment exists only when the device supports it and the frame requests it. Indirect composition replaces that component's indirect term. `ComputeBurleySubsurface` filters the half-resolution diffuse buffer and recomposes full resolution as the original color minus the original diffuse plus the filtered diffuse. Specular and emissive light stay outside the filter. The material profile index selects the diffusion parameters. The Volume owns enable and sample count. The pipeline asset owns the diffusion profile list. No subsurface target is allocated when the feature is off.

### Contracts

- Screen-space reflection `Samples` is rays per pixel, 1 to 4; `ResolveSamples` is the Poisson reuse count, not a ray count. There is no world-space ray length. Hit color is the accepted unsharpened previous frame. `SCREEN_TRACE_RELATIVE_THICKNESS` and `SSGI_TRACE_LINEAR` are diagnostic variants. Bilateral reflection cleanup uses hysteresis 0.5 on the reconstruction variance.
- Subsurface routing requires opaque Deferred subsurface materials.
- Ray-traced occlusion selects `ComputeRayTracedOcclusion` or `RayTracingOcclusion` from its Unified Ray Tracing backend; its graph pass type stays RayTracing.

## 12. Atmosphere, fog, clouds and translucency

`AtmosphericalProfile` on the pipeline asset is the only atmosphere configuration. `AtmosphereParameter.FromProfile(null)` throws. `ThrowIfInvalid` rejects out-of-range thickness, scattering, heights, ozone and sun angle. Lengths are meters. Scattering and absorption are stored per kilometer and multiplied by `ScatterPerKmToPerMeter` (0.001) at bind. Volume does not override the atmosphere, and there is no `AtmosphereParameter.Default()`.

Lookup tables and the aerial-perspective volume are compute products. Cubemap faces are a `RWTexture2DArray`. Sky and fog are applied in `ComputeAtmosphericSkyAndFog` after opaque lighting and before translucent depth.

Volumetric fog and volumetric clouds record when their frame setting is on, the Volume component is active, and their kernels exist. Fog builds an 8×8-pixel froxel volume and reads the light loop. Clouds trace at half internal resolution, require the atmosphere transmittance table, and read the light loop and shadow inputs. Each keeps its own history.

Translucent depth is followed by clouds, fog, fog composite, T0, the color pyramid, T1 and T2. T1 is the refraction stage and reads the pyramid. T0 is lit before that pyramid. T2 is the last translucent stage and includes unlit and overlay-canvas geometry that is not the screen-space overlay list.

### Contracts

- The atmosphere compute shader uses one unit system, meters with scattering converted at bind; not both. Atmosphere tables are not produced with `Blit`, `SetRenderTarget` or `BuiltinRenderTextureType`.
- The per-camera view cache (sky-view table, aerial-perspective volume, sun buffer) keys on the atmosphere parameters, the sun direction, the camera position quantized to 1 m and the exact unjittered view direction and projection. The aerial-perspective volume is aligned to the frustum, so turning in place regenerates it.
- Atlases that live across cameras and frames follow `AtmosphereSharedCache`: pending reservations, commit on queue-accepted `MarkProduced`, `DiscardUnproducedPending` for failed cameras, physical retirement after submit. Cross-frame keys are instance IDs, not per-frame record indices.

## 13. Temporal resolve and output

`UNITY_MATRIX_VP` is `Matrix_ViewJitterProj`. Depth and GBuffer are rasterized in jittered clip space. Reconstruction from a depth buffer uses the jittered inverse view-projection. `FlipY` in this package means `GL.GetGPUProjectionMatrix` with `renderIntoTexture = false`. Motion vectors use the jittered position for coverage and the unjittered clip positions for velocity.

Temporal anti-aliasing reads the current frame at the texel center with point sampling. It does not bilinear-sample at `uv - jitter`. Color history is reprojected by motion. Depth history is reprojected by motion plus the change in jitter. Neighborhood clipping uses reversible luminance compression. Sharpening scales RGB together. The sharpen constant in `Compute_TemporalAntiAliasing.compute` is 0.35. One 8×8 dispatch with a one-pixel halo writes unsharpened `RGBA16F` color and sample age, and `R32` surface depth, to pending history. Sharpened color is display-only. Halo lanes participate in the group barrier.

Super resolution writes unsharpened `RGBA16F` color and display-grid `R32` depth to pending history in the same dispatch as the display color. Its sharpen constant is 0.5. The display result is not copied into history. Reset and off-screen history are not sampled.

`renderScale` is in `[0.5, 1]` and applies to Game cameras when super resolution is enabled. One per-camera descriptor owns internal and display size. Dimensions freeze after `BeginCameraRendering`.

`PrepareCamera` writes the camera's `ETemporalResolve` (None, TAA or SuperResolution) once, from its render policy and dimensions; feature requests, the resolve dispatch and the required-producer check all read it. `CameraFrameState.ResolveHistoryReset` is the only writer of `EHistoryReset`, in this priority: a non-temporal camera (preview, probe face), a capture or validation lock (Editor and Development builds), no committed frame, a game camera that skipped a frame, a rolled-back previous frame, changed dimensions, a cut or projection change. Passes read `resetsHistory` from `CameraRecordContext`. Scene view uses the full pipeline; a gap in the global frame counter does not reset a live Scene view. Reset frames use zero jitter. Game cameras recycle history after a frame gap greater than one.

`OutputPlan` chooses a direct camera target or a display-linear intermediate before post-processing. `PostCombine.hlsl` is the only grading, exposure, bloom, vignette and grain implementation. When no linear consumer exists, that draw also encodes and writes the imported camera target. Hardware-sRGB UI then blends on that target. HDR UI, a debug view, Editor overlays and a requested linear capture keep `DisplayColorBuffer`. A later draw encodes it. Encoded targets are never published as linear buffers. Debug view writes a linear quantity into the post buffer immediately before gizmos, and only when a debug view is active. It is not a second encoder.

`ImportBackbuffer` brings the camera target into the graph. An imported resource is not created or released by the pool. Player cameras that target a display resolve sRGB from `Display.requiresSrgbBlitToBackbuffer` when no render-texture format is known. HDR reads the active swapchain format, gamut and paper white. Linear Rec.2020, PQ Rec.2020 and linear Rec.709 scRGB are separate encodings. HLG is not available through Unity's color-gamut API. While HDR output is active the pipeline owns automatic tonemapping and restores it on exit.

Screen-space overlay UI records for a Game camera with no target texture, with `CreateUIOverlayRendererList` and `DrawRendererList`; it is not `DrawUIOverlay`. On the linear-intermediate path it draws into `DisplayColorBuffer` before encoding. On the direct path it draws into the hardware-sRGB target. Scene view does not draw that list. Screen-space camera and world-space canvases use the T2 renderer list.

`ReactiveMask` is an R8 attachment allocated only for temporal anti-aliasing. The first translucent color pass clears it even when it draws nothing. Later passes load it. Other temporal policies omit the attachment.

### Contracts

- On the compute side the jittered reconstruction matrix is `matrix_*FlipYJitter*`. Motion vectors are jitter-free; `SV_POSITION` uses the jittered view-projection, the clip positions used for velocity do not.
- Color history uses `uv - motion`. Raster depth history uses `uv - motion + previousJitterUV - currentJitterUV`. Motion metadata carries expected previous raw depth, current surface raw depth, validity and reserved data from one surface. Invalid, off-screen or disoccluded history has zero weight; multiplying undefined history by zero is not rejection. Sample age lives in the alpha of the `RGBA16F` history. Current eye depth is never compared with previous-camera eye depth.
- Sharpening never feeds history. Temporal blending is linear RGB. Output sharpening scales RGB together; a YCoCg Y offset that adds a constant to RGB is not that operation. `TAAJitter` has super-resolution consumers.
- Native renderer-list draws use `unity_MatrixPreviousM` and skinned previous positions. The pipeline keeps no second native pose history, sets no per-renderer property blocks and never substitutes another camera's latest object pose.
- Encoded targets never alias `PostProcessBuffer` or `DisplayColorBuffer`. A later output draw encodes an already composed buffer and does not grade again. Read-only post bypass may alias its source. An overlay writer requires its own target.
- The backbuffer enters only through `ImportBackbuffer`; `RTHandle.SetTexture` is internal to Core RP; rebinding reallocates when the identifier changes. `ReactiveMask` has no standalone clear dispatch.
- Display-depth upsampling exists only for recorded Editor overlays that need it. `TAAConfidenceBuffer` is created only when `debugView != None` or this frame's capture requests confidence. Capture changes neither debug view nor history.
- Explicit Unity camera targets are imported by identifier.

## 14. Editor and capture

Editor windows, inspectors and debugger panels are specified in [Docs~/Manual/editor.md](Docs~/Manual/editor.md) and [Docs~/Manual/rendering-debugger.md](Docs~/Manual/rendering-debugger.md). They use Unity Editor IMGUI and Editor styles, plus one UI Toolkit drawer for the default Volume profile. The package does not define a visual design system.

Diagnostics exist only in Editor and Development builds. The pipeline reaches them only through `PipelineDiagnostics`, outside the measured frame scope: validation scenarios register `PipelineFrameObserver`s on load, the render graph exposes one `IRGExecutionObserver` slot, and normal-frame capture runs at a fixed place in that dispatcher. Diagnostic components (`LivenessMarker`, `TemporalValidationCamera`, `CameraMovement`) keep their classes and serialized fields in every build, with their behavior compiled only for diagnostics. `RenderCaptureService` records normal-frame copies through graph transfers into session-owned staging. It does not replace `camera.targetTexture` and does not call `Camera.Render`. Capture selects an explicit Game camera, or the single active Game camera. Lighting is captured before scene-color ownership moves. Debug view and history are left unchanged.

Automated Editor commands use the CLI adapter under `Editor/Cli`, on `127.0.0.1`, with bearer authentication. They do not save assets. `com.unity.pipeline` screenshot is not a normal-frame capture. Capture does not query a retired semantic name at frame end.

## 15. Integration boundary

The ownership audit is [Docs~/Project/Audit/UnityIntegrationAudit.md](Docs~/Project/Audit/UnityIntegrationAudit.md). The rules that follow are the architectural boundary.

Native renderer content uses Unity culling and renderer lists. Custom mesh content uses `MeshWorld`. Ray tracing adapts native geometry through the public explicit-instance API of Unified Ray Tracing. Native renderer identity and custom draw generation are different. A mesh asset is not an instance identity. Native scene changes do not wait for a mesh-world revision.

Adaptive Probe Volumes are bound with Core RP's `ProbeReferenceVolume` APIs on the pipeline command buffer before the graph records. One integration owns initialization and cleanup. A material samples probes in world space. The pipeline does not upload a per-instance probe record for mesh content.

Shader Graph access is confined to `Editor/ShaderGraph/`. Runtime code does not reference Shader Graph internals. No other assembly uses an asmref, reflection into Unity internals, or a spoofed friend name for this purpose.

Custom VFX Graph outputs are unsupported. Particle systems use the translucent renderer list.

### Contracts

- Shader Graph 17.6 keeps `Target` and `SubTarget` internal. The Infinity target compiles into `Unity.ShaderGraph.Editor` through the single asmref at `Editor/ShaderGraph/` and references Infinity by strings, which `static_check.py` checks. Runtime does not depend on it.
- Adaptive Probe Volumes are bound with `ProbeReferenceVolume.BindAPVRuntimeResources` and `UpdateShaderVariablesProbeVolumes` before the graph records; no graph encoder exposes a `CommandBuffer` for that bind. Enable and disable of an entity each pair with one unregister, including disable followed by destroy.
- Native renderer content keeps lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion on Unity renderer lists. `MeshComponent` never discovers or changes a renderer and stores no lightmap or CPU per-instance probe data.
- Additional-data components own only Infinity extensions. They are added with Undo by `ObjectFactory.componentWasAdded` and by the Infinity inspectors; no public field is assigned to a camera or light by the user. Editor load, reload and scene save do not regenerate hidden data. Terrain sector regeneration is an explicit inspector action with Undo and prefab tracking.
- Unified Ray Tracing's explicit-instance API is wrapped by an isolated adapter; its persistent-scene scope is transient and released with the adapter. Valid hardware ray-tracing assets that are not yet integrated are kept.
- `SupportedRenderingFeatures.active.rendersUIOverlay` is true. Custom VFX Graph outputs stay unsupported: no friend-assembly shim and no Package Cache patch.

## 16. Limits present in the source

These are properties of the current implementation that no subsystem section states, not a verification record. [Docs~/Project/Status/FeatureStatus.md](Docs~/Project/Status/FeatureStatus.md) states what has actually been executed. None of them is hidden behind a fallback, a disabled pass or a dummy resource.

- The material conversion wizard reports the maps InfinityLit does not expose.
- Shader Graph scene depth is available in decal graphs, scene color in T1, and the fog node returns no fog. Graph motion is rigid. Shader Graph materials have no mesh-instancing path, so `MeshComponent` does not draw them.
- `MeshComponent` Receive Shadow and Affect Indirect Lighting are stored on the entity, and no pass reads them.
- The light and reflection-probe complexity views count clustered-loop visits, not lights that shade the pixel.
- Several recorded passes can contain no draws (`RenderDBuffer` without a DBuffer shader, `RenderLocalShadow` without local casters, `CopyHistory*` transfers). Frame Debugger may omit them. Cascade shadows still bind their target when the draw count is zero.
- Hi-Z, half-resolution geometry, lookup tables, light masks and the screen-space producers are async-eligible where the device allows. Overlap has not been measured.
- Ray-traced ambient occlusion Player shader load is unverified.
- Combine LUT writes Rec.709 linear and dispatches only when the grading key changes. The key holds only film and grading parameters: no output setting reaches the LUT shader.
- Image-regression baselines are per graphics API, not per GPU vendor, and are absent until recorded on a verified Editor.
- Validation scenes for decals, local lights and translucency have shown sky bands, dark triangles and cube blotches. The cause is not established.
