# Infinity Render Pipeline — Architecture

This document describes the implementation in this package. When that implementation changes, revise the affected statement. Do not append iteration notes. Contributor obligations, including how this file stays short, are in [AGENTS.md](AGENTS.md). Authoring is in [Documentation~/index.md](Documentation~/index.md). Verification state is in [Docs/FeatureStatus.md](Docs/FeatureStatus.md). Archived plans and receipts under [Docs/History/](Docs/History) are evidence, not a specification.

## 1. System

InfinityRP is a Scriptable Render Pipeline. `InfinityRenderPipeline` derives from `RenderPipeline` and is split across partial files under `Runtime/RenderPipeline/`. A camera frame collects Unity culling results, builds an Infinity light context, records a render graph, executes that graph, and submits the scriptable render context.

Two geometry paths share the frame:

- Native renderers are submitted with Unity `CullingResults`, `RendererList` and `ShadowRendererList`. Lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion data stay on those renderer lists.
- `MeshComponent` instances are entities of `MeshWorld` and are submitted through `RGDrawListHandle`. A mesh component does not discover or modify a `Renderer`.

The package owns the render graph, the physical resource pool, image history, the mesh world and its GPU entity columns, light records, shadow allocation and the atmosphere profile. Unity owns the device, command submission, fences, native culling and the Volume manager.

## 2. Layout

| Path | Assembly | Owns |
|---|---|---|
| `Runtime/` | `Unity.RenderPipelines.Infinity.Runtime` | Pipeline, passes, mesh and light pipelines, volumes, components |
| `Editor/` | `Unity.RenderPipelines.Infinity.Editor` | Inspectors, menus, Shader Graph target, stripper, validation |
| `Editor/Cli/` | `Unity.RenderPipelines.Infinity.Cli.Editor` | Command adapter, compiled only with `INFINITY_UNITY_CLI` |
| `Editor/ShaderGraph/` | Compiled into `Unity.ShaderGraph.Editor` by one asmref | Shader Graph target only. It refers to Infinity by strings |
| `Shaders/` | `Unity.RenderPipelines.Infinity.Shaders` | HLSL, surface shaders, compute shaders |
| `Tests/Editor/` | `Unity.RenderPipelines.Infinity.Tests` | EditMode tests |
| `Tools/` | None | Offline checks, texture-view generation, refresh and capture scripts |

`Runtime/RenderPipeline/Pass/` groups the partial pipeline by domain: Geometry, Lighting, Shadow, ScreenSpace, Volumetric, Translucent, Temporal, Post, Output, Debug. Volume components live in `Runtime/Volume/`. The atmosphere profile lives in `Runtime/RenderingFeature/AtmosphericalFog/` and is not a Volume component.

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

`EQualityLevel` is Low, Medium, High, Ultra or Custom. `Resolve()` returns a settings struct. Custom reads the Volume parameters. Every other level reads the preset table in `Runtime/RenderPipeline/Quality/`. Passes consume the struct and do not branch on the quality level.

## 4. Camera frame

`Render(ScriptableRenderContext, List<Camera>)` is the frame entry. `EPipelineProfileId.RecordRG` is a profiler label around the recording block in `InfinityRenderPipeline.Render`. It is not a method.

Before that block the camera updates volumes, resolves frame features, culls, and builds `LightContext`. `ConfigureFrameFeatures` runs from the asset, the camera and the active volumes. `ConfigureContentFeatures` runs after `LightContext.Build` and decides tile masks, moment shadows, atlas updates and contact shadows from the collected lights.

Recording order, with the gate that skips a step:

1. **Frame constants.** `UploadLightData`, `ComputeLightCookies`, `ComputeReflectionProbes`, `ComputeCombineLuts`, `ComputeAtmosphericLUT`, `ComputeTemporalColorPyramid`. Cookies, probes and the color pyramid record only when their feature is enabled. The atmosphere lookup requires `atmosphericalProfile`.
2. **Geometry.** `PrepareMeshOcclusion` when temporal rendering, Hi-Z, GPU indirect drawing, live instances and a valid Hi-Z history are all present. Then `RecordMeshGpuCulls`, `RasterDepth`, optional `RecoverMeshOcclusion`, `RasterDBuffer` when the world contains decals, `RasterGBuffer`, `RasterMotion`.
3. **Depth-derived work.** `ComputeHiZ`, `ComputeHalfResDownsample`, `ComputeLightTileMask` when a local light list exists, ray-traced or ground-truth occlusion, `ComputeContactShadow`. When the device supports async compute, screen-space reflection and screen-space indirect diffuse record here.
4. **Shadows.** `RecordAllocatedShadowCasterSplits`, `FlushShadowCasterCulling`, `RasterCascadeShadow`, `RasterLocalShadow`, `ComputeShadowMoments` when a moment filter is in use.
5. **Lighting.** `ComputeDeferredShading`, optional deferred-lighting capture, `RasterForward`. Without async compute, reflection and indirect diffuse record here. Then `ComputeScreenSpaceComposite`, `ResolveOpaqueSceneColor`, `CopyHistorySSR`, `CopyHistorySSGI`, `ComputeBurleySubsurface`, `ComputeAtmosphericSkyAndFog`, optional lighting capture.
6. **Translucency.** `RasterTranslucentDepth`, volumetric cloud and its history copy, volumetric fog and its history copy, `ComputeFogComposite`, `ResolveFoggedSceneColor`, translucent T0, `ComputeColorPyramid`, translucent T1, translucent T2.
7. **Temporal resolve and output.** `ComputeSuperResolution` when the camera policy is temporal and super resolution is active; otherwise `ComputeAntiAliasing` when the policy is temporal. Preview cameras are not temporal. Then `RecordCameraOutput`.

`m_RGBuilder.Execute` runs under the `ExecuteRG` profiler label. Draw-list release is paired with execution. A camera exception rolls the frame state back, discards unproduced shared reservations, and preserves the first exception. Queue-accepted work is still submitted. Physical retirement runs after a successful submit and a known completion fence.

Async eligibility is a queue assignment. It is not a measurement of overlap. The current Metal validation host reports no async compute support. Early occlusion recovery and late shading, subsurface, temporal resolve and post stay on the graphics queue because their inputs are the surrounding raster work.

## 5. Render graph

`RGBuilder` records passes, textures, buffers and renderer lists for one camera graph. Handles carry the graph generation. A handle from another graph is rejected. Work the core does not model attaches as one `IRGGraphExtension` per builder: the extension contributes inserted passes once per compile and clears its state whenever the recorded graph is dropped, with or without execution. A pass stores the extension's ids in `extensionUses`, and pass handles and raster encoders carry their builder's extension, so a draw list from another builder is never consumed or submitted. The render graph has no dependency on the mesh pipeline; mesh draw lists are that extension, in `MeshPipeline/Graph/`.

Compile runs structural culling first. Culling propagates across texture and buffer reads. A pass that writes an imported resource, or that calls `EnablePassCulling(false)`, stays live. The mesh compile extension then inserts one upload producer per live draw list and, for GPU indirect drawing, one compute compaction producer. Those producers are placed at the earliest index whose inputs exist. Liveness runs again. The compiler then assigns descriptor-level alias slots, submission segments and native render-pass groups.

A submission segment is a run of passes on one queue. It breaks when the queue changes, when a pass waits on a cross-queue fence, or when a create needs a slot released earlier in the same segment. One segment is one `ExecuteCommandBuffer` or `ExecuteCommandBufferAsync`. Adjacent raster passes merge into one native render pass when size, sample count and attachment limits allow and no pass samples an attachment written inside the group. Unity's attachment limit is eight, including depth. A non-imported attachment whose last use is a store becomes `DontCare`. On Metal, an attachment that never leaves that render pass can be memoryless, except a color UAV.

Unity exposes no GPU heap. Overlap is the alias-slot plan, executed by `ResourcePool`. Repeated imports of one physical texture or buffer share one graph identity. Conflicting logical views of one texture fail at record time.

Pass types are fixed by the work. Transfer copies. Raster draws. Compute dispatches. Ray tracing is the render-graph type of the ray-traced occlusion pass. Gizmos, wire overlay, final output and UI overlay stay raster. `SetRenderTarget` is not called inside a pass execute. Attachments are declared at record time.

`EnableNativeRenderPass(false)` is used only where a native render-pass attachment cannot represent the target: gizmos and wire overlay, because Unity forbids gizmos inside `BeginRenderPass`; final output, because the backbuffer has no owning `RenderTexture` from which to resolve `AttachmentDescriptor.graphicsFormat`; and UI overlay, because Unity does not expose the overlay renderer list as a native render-pass payload.

`RGBuilder.Execute` propagates the original exception. Graph commands are isolated from the enclosing camera profiler scope. Unreturned graph allocations return to the pool only after submit. `RGProfilingScope` is a pass range: on one segment the parent sampler begins on the first live pass and ends on the last. A segment split closes it and opens it again. It does not insert a pass. Pass GPU names come from `pass.customSampler`.

## 6. Resources and history

`ResourcePool` owns physical textures and buffers for the graph and for other clients. `BeginFrame` opens a recording epoch. Release keeps recording references and queued GPU use, including the overloads that have no fence. Same-queue reuse follows queue order. Opposite-queue reuse waits on a queued dependency. CPU `SetData` requires no recording or pending use and a CPU-queryable completion, or a separate writable allocation. A CPU frame transition is not permission to write.

Retention watermarks are 400 MiB for textures and 64 MiB for buffers. Resources idle for more than two successful submission epochs are evicted oldest first above those watermarks. The watermarks are not a working-set limit. Destruction waits for a CPU-queryable completion fence and does not block the CPU. Size counters are format estimates, not driver residency.

`HistoryCache` is the only image-history store. Readers import the accepted history. Writers import a pending target. A producer may write the pending target directly, as temporal anti-aliasing and super resolution do, or copy into it with an explicit CopyHistory transfer. Pending history is accepted only after the camera submission succeeds. A failed camera or a failed frame resets history. Committed and pending descriptors swap with their allocations. A failed pending resize does not replace committed history.

Logical width and height are the pixel coordinates and the dispatch bounds. Allocation width and height are the physical compatibility key. `TextureDescriptor.Equals` compares the full logical descriptor. Physical compatibility is a separate field-level compare. History is not rebuilt through `RenderTextureDescriptor`, because that conversion drops the depth-stencil format and the mip count.

Each camera has its own `TextureCapacityState` for internal and display extents. Capacity aligns to 64 texels, grows by at least 12.5% when exceeded, and shrinks after 30 accepted requests whose three-request peak is below 75% of the capacity area. Orientation changes reset that history. Failed submissions do not advance it. A capacity-only change does not invalidate history. Preview and capture cameras do not grow another view. Fixed lookup tables, atlases and external targets use exact allocation.

Screen-texture bindings carry logical and physical metadata. Normalized sampling uses `TextureView.hlsl`. Integer loads use logical pixels. Material texture sampling is unchanged. Binding registries are generated with `python3 -B Tools/TextureViews.py --write`.

`MeshWorldGpu.transformVersions` is the exception to image-history ownership. It holds immutable matrix and generation buffers per entity slot. A camera keeps accepted and pending version references. A revision advances only when matrices or slot membership change. Dirty 64-slot pages upload once per version, and a compute producer copies the unchanged elements. Shader motion checks world identity, snapshot range and slot generation.

Clear depth and sampled depth are different values. `ClearRenderTarget` and `AttachmentDescriptor.clearDepth` take `GraphicsUtility.ClearDepthFar`, which is normalized so that 1 means the far plane. Shader comparisons against a sampled depth buffer use `GraphicsUtility.SampledFarDepth`.

## 7. Mesh drawing pipeline

`MeshComponent` content is not submitted through a renderer list. Layers, from stable to per request:

1. **World**: slot tables for entities, elements, sections and materials, owned by `MeshWorld`, one per `RenderContext`.
2. **Pass**: one `MeshPassCache` per interned `MeshPassDesc`: which elements belong to the pass and which cached command each draws with.
3. **View**: per-camera visibility of entity slots, from a Burst job or from GPU culling.
4. **Draw list**: one request for one view and one pass: gather, sort and merge on the CPU backend, or compaction on the GPU backend.
5. **Graph**: the builder's `MeshGraphExtension` inserts each live list's producers and submits it from its raster consumer.

```text
MeshEntity  (slot = transform slot)  desc, LOD chain, element chain   transforms[], bounds[] parallel columns
  └─ MeshElement (one submesh of one LOD)  section ─► MeshSection (mesh, submesh, refCount)
                                           material ─► MeshMaterial (id, queue, route, stage, shader, refCount)
MeshPassCache   candidates[element] = (entity, command | lod << 24)   commands[] = key (section, material), refCount, stateKey
MeshDrawListBuild  items (key, command, entity, depth, fade) ─sort─► draws (command, fading, offset, count)
                                                                    + instanceToEntity[] + instanceFade[]
```

Index spaces are entity, element, command, draw and instance; arrays are named `<from>To<to>`. Ids store an index and a generation. Live generations are odd, dead ones even. A killed slot returns to its free list only when the transaction commits, so an id handed out by a rolled-back transaction never matches a later slot.

| Type | File | Responsibility |
|---|---|---|
| `MeshWorld` | `MeshPipeline/World/MeshWorld*.cs` | Tables, revisions, dirty pages, pose writes, dynamic poses, interned pass caches, backends, residency |
| `MeshWorldUpdate` | `World/MeshWorldUpdate.cs` | Structural transaction lease and per-table undo logs |
| `SlotTable<T>` | `World/SlotTable.cs` | Fixed-index storage with generations and commit-time reclaim |
| `MeshWorldGpu` | `World/MeshWorldGpu.cs` | Entity columns (bounds, flags, masks, rendering layers, LOD chains) by dirty 64-slot pages |
| `MeshTransformVersionCache` | `World/MeshTransformVersionCache.cs` | Immutable per-entity transform versions and their GPU production |
| `MeshPassDesc`, `MeshPassPresets` | `Pass/MeshPassDesc.cs` | Pass request: LightMode, queue range, sort mode, required entity flags, previous transforms |
| `MeshPassCache` | `Pass/MeshPassCache.cs` | Incremental membership, command refcounts, bindings, candidate table |
| `MeshView`, `MeshVisibilityKey`, `MeshVisibilityShare` | `View/` | Views, culling identity, per-camera CPU visibility and LOD jobs |
| `MeshGpuVisibilityCache` | `View/MeshGpuVisibilityCache.cs` | One resident GPU visibility buffer per key |
| `MeshDrawListBuild` | `DrawList/MeshDrawListBuild.cs` | Gather, sort and merge jobs; `SortKeyPacking` |
| `MeshCpuDrawBackend`, `MeshGpuDrawBackend` | `Backend/` | World-owned backends: instance tables, payloads, culling, compaction, submission |
| `MeshGraphExtension`, `RGDrawListContext` | `Graph/` | Per-builder graph extension and draw-list registry. A draw list is not an `ERGResourceType` |

### World

`MeshWorld.BeginUpdate` opens one transaction and snapshots revisions and dirty pages. Each table logs the slot state before its first change. `Commit` first asks every pass cache to prepare the bindings it would create; a failure leaves the transaction open and the lease's `Dispose` rolls it back. After that the transaction is closed, so nothing can revive a reclaimed slot. Commit then reclaims killed slots, retires sections and materials whose reference count stayed zero, applies pending mobility changes and publishes the dirty elements to every pass cache. `Rollback` replays the logs in reverse, rebuilds the touched section and material lookups and restores revisions and dirty pages.

Poses do not take part in transactions. `WriteEntityPose` writes one static pose; `UpdateDynamicPoses` reads every dynamic entity's `Transform` in an `IJobParallelForTransform`, writes changed poses by entity index and marks dirty pages in a second job. Both throw while a transaction is open. Changing a flag that decides membership (`CastShadow`, `ObjectMotion`) is a structural edit.

### Pass

Membership of an element in a pass is: its material's route and stage enable the pass LightMode (`MaterialRouteUtility.RouteEnablesPass`, the one authority that also sets material pass state), its queue is inside the desc range, and its entity carries the required flags. Pass state on the material is not consulted. A command is keyed by (section slot, material slot) and binds the mesh, material, pass index and submesh range when it is created. Its state key packs a per-cache shader-state rank, the material slot and the section slot, each saturated to its segment. After a shader import `MeshPassShaderUtility.NotifyShadersChanged` advances an epoch and each cache resolves its pass indices again before its next draw list.

The nine presets (Depth, GBuffer, Forward, Motion, Shadow, TranslucentDepth, TranslucentT0, T1, T2) are interned when the world is created, so shadow-caster bounds and residency see them before any draw list. Another desc is built from the world on its first request, and its candidate table is written into a fresh buffer at that moment; later changes upload in residency like the presets. A world interns at most 64 descs and never evicts one. Motion and the translucent stages require the view's previous transforms.

### Frame

1. Proxy update: registration tasks, one `MeshWorldUpdate` for removals and every dirty component, explicit static poses, then dynamic poses. A component stages its sync inside the transaction and publishes it after the commit; an id is valid only in the world that issued it, and a new world resyncs every registered component. A shader import or a shader swap on a material makes the affected components validate again and the commands resolve their pass indices again.
2. `MeshWorld.UpdateResidency` uploads dirty entity columns, the next transform version and stale pass candidate tables.
3. `MeshWorld.BeginCamera` resets per-camera counters and visibility interning.
4. Passes call `CreateDrawList(meshView, MeshPassPresets.X)`. On the GPU backend that declaration also requests `EnsureMeshGpuCull` for the view.
5. Compile releases draw lists with no live consumer. Live CPU lists acquire interned visibility and schedule their gather, sort and merge jobs; live GPU lists rent their payload. The extension inserts one upload producer per list and, for GPU indirect, one compute producer, then moves them to the earliest pass whose inputs exist.
6. Execute: the CPU upload completes the build jobs and uploads `instanceToEntity` and `instanceFade`; the CPU consumer issues one `DrawMeshInstancedProcedural` per merged run. The GPU producer runs `ClearCounts`, `Compact` (one thread per element slot of the candidate table) and `BuildIndirectArgs`; the consumer issues one `DrawMeshInstancedIndirect` per live command for its stable range and, for a command with members whose chain has a band, one more for its fading range. Raster submission only draws.
7. `ReleaseAll` completes and frees build jobs, releases visibility references and retires payloads, instance buffers and transform-version references. After native submit, `FlushRetired` returns allocations to `ResourcePool` without clearing outstanding GPU use.

Sort modes: `State` orders by state key; `FrontToBack` puts a saturated camera distance in decimeters in front of the state key's segments, each saturated to 16 bits; `BackToFront` orders by priority, then exact distance descending, and always records on the CPU backend. Outside back to front, stable items come before fading ones, so a list changes the dither keyword once. Ties break by command, distance, entity and fade (back to front compares distance before command), so the order is total. The GPU backend issues its commands in state-key order; it has no per-instance distance order.

### LOD

An entity carries a `MeshLodChain` of 1 to 8 levels in `LODGroup` semantics: level *i* draws while the screen size is at least its lower bound `screenSize[i]`, bounds strictly decrease, and below the last bound nothing draws (a last bound of 0 never culls). Screen size is the largest half-extent of the world bounds (`LODGroup.size` / 2) × `scale` × `bias` / distance, where `MeshLodProjection.FromCamera` takes the camera position, 1 / tan(fov / 2) or 1 / orthographic size, and `QualitySettings.lodBias`; orthographic views use distance 1. Shadow views copy their camera's projection, so casters switch level with the visible object. The projection is part of the per-render `MeshVisibilityKey`; the resident GPU visibility buffers leave it out, because it moves with the camera every frame. Each element names its level.

The cross-fade band of level *i* lies above its bound and is `fadeWidth` × (previous bound − bound) wide, with a previous bound of 1 for level 0; a bound of 0 has no band. Inside the band the fade *f* runs from 0 at the bound to 1 at the band top. The CPU job (`MeshLodSelection`) writes per entity `visible`, `lod` and `fade`; GPU culling (`MeshPackLodVisibility` in `MeshLod.hlsl`) writes one word, 0 or `1 | level << 8 | fade << 16`, with 65535 for stable and the band position quantized to [0, 65534). Both evaluate the same expression in the same kernel or job as frustum culling, including both Hi-Z phases.

A pass dithers LOD transitions when its queue range is opaque (`MeshPassDesc.ditherLodTransitions`). There, inside a band the selected level draws with +*f* and the next level with −*f* in the command's fading range; `LODDitheringTransition` keeps complementary pixels for the two signs, so the pair covers every pixel once. At *f* = 0 the outgoing level is gone. Translucent passes draw the selected level only and never fade. Every command has a stable and a fading range: GPU command metadata holds index count, start index, base vertex, stable base and fading base (stable base + member count), counts and indirect arguments are interleaved per (command, range), and instance capacity is twice the member count. Fading draws set the global keyword `INFINITY_LOD_DITHER` and clear it afterwards; shaders read the factor through `MeshInstanceFade`. Ray tracing takes level 0 only.

### Content

`MeshComponent` draws a `MeshAsset`: `meshes[i]` is LOD *i* (one mesh may fill several levels), `lODInfos[i].screenSize` its lower bound, `lODInfos[i].materialSlot[s]` the material slot of submesh *s*, and `fadeWidth` the band width. A submesh without a slot entry has no material and draws nothing; an explicit slot outside the asset's materials makes the asset invalid. The asset's materials are authoring defaults that the inspector copies into a component once, on assignment; the component's material array, indexed by slot, is the runtime authority. Components share one geometry state per asset (level meshes, submesh counts, union bounds and the element table, one entry per level and submesh). An edit of the asset or one of its meshes marks that state stale, and every component that references it rebuilds its entity on the next sync. Only a registered component holds a reference on its asset's state, and destroying the asset re-syncs its components. A component whose asset fails `MeshAsset.ThrowIfInvalid` is logged with its context and not drawn. Loading never converts 0.4 data: the explicit migration menu assigns each component a shared one-level asset of its old mesh.

### Backends and shaders

`EMeshBackendPolicy` is Auto, CpuDirect or GpuIndirect. The camera or test override wins, then the Rendering Debugger override, then the asset. Auto and GPU Indirect use the GPU backend when the device supports compute and instancing and the mesh compute shader has its kernels. There is no process-global indirect-argument buffer and no same-frame visible-count readback. The compute shader is required on every backend because transform versions are produced on the GPU.

Shaders read `InstanceToEntity[SV_InstanceID + InstanceOffset]` through `MeshInstanceEntity`, then `EntityTransforms`, `EntityRenderingLayers` and, for motion, `PreviousEntityTransforms` guarded by generations. A mesh shader declares the SubShader tag `InfinityMeshInstancing = True`; a component whose material lacks it, or lacks a pass its route enables, is logged with its context and not drawn.

`ERenderingLayer : byte` is the shared mesh and light mask, with `Everything = 0xFF`. Surface masks in GBuffer C are an 8-bit unorm value. High bits are rejected.

Public recording for a pass is:

```csharp
RGDrawListHandle draws = graph.CreateDrawList(meshView, MeshPassPresets.GBuffer);
data.draws = pass.UseDrawList(draws);
// execute:
cmdEncoder.Draw(data.draws);
```

Content code mutates `MeshComponent`. It does not call `CreateDrawList`.

Draw markers are `RenderLoop.DrawMeshDirect` and `RenderLoop.DrawMeshIndirect` for ordinary views, and `Shadows.DrawMeshDirect` and `Shadows.DrawMeshIndirect` for shadow views, selected from `EMeshViewKind`. Preparation samplers are `ComputeMeshCulling`, `ComputeMeshCompaction` and `UploadMeshData`, shared across views and owned by `MeshDrawProfiling`. Shared sampler names do not merge graph nodes.

`TranslucentPass` draws MeshComponent content from the TranslucentDepth (front to back) and T0/T1/T2 (back to front, CPU) presets before the native renderer list of the same pass, with the pass's keywords and global inputs already bound. The two lists are sorted separately, so MeshComponent and native translucent renderers have no distance order between them. Out of scope for this pipeline: GPU sorting of draw keys, meshlets, and replacing renderer lists for Unity `MeshRenderer` objects.

## 8. Materials

`_SurfaceRoute` is Deferred (0) or Forward (1). `_TranslucentStage` is None (0), T0 (1), T1 (2) or T2 (3). `MaterialRouteUtility.Read` rejects fractional, non-finite and out-of-range values. Subsurface requires opaque Deferred routing. `ApplyPassState` is the operation that writes renderer pass enables after a route or stage change. `ShaderGUI.ValidateMaterial` does not write serialized state.

GBuffer layout and YCoCg checkerboard encoding live in `Shaders/ShaderLibrary/GBufferPack.hlsl`. `GBufferPass` allocates three `R8G8B8A8_UNorm` targets and a `R16G16B16A16_SFloat` lighting target. InfinityLit exposes base color, normal, roughness, reflectance, specular level, emission color, subsurface profile and thickness, refraction, iridescence and pixel depth offset. It does not expose metallic, mask, occlusion, detail, height or emission textures. Shader Graph Lit has a Metallic block; the runtime lit constant buffer uses reflectance.

Surface shaders are `InfinityPipeline/InfinityLit`, `InfinityLit-Instanced`, `InfinityUnlit`, `InfinityDecal` and `TerrainLit`. Custom LightMode tags use the `Raster*Pass` names in `InfinityPassIDs`. Unity `ShadowCaster`, `Meta` and `SRPDefaultUnlit` stay unchanged.

## 9. Lights

`FLightShape` stores the analytic emitter: `EInfinityLightType` (Directional, Point, Spot, Rect, Disk), `ESpotShape` (Cone, Pyramid, Box), source radius, soft source radius, source length, source width and height, barn doors, source angle, soft source angle, pyramid aspect and box size. When `InfinityAdditionalLightData` has authored the shape, that struct is authoritative and `LightShapeUtility.ApplyToUnityLight` writes the derived native fields on edits and setters only. A light with no authored shape derives `FLightShape` from the native `Light`. The GPU record `FLightRecord` has no shape enum. Shape is a combination of parameters and `FLightRecordFlags`.

`LightShapes.hlsl` evaluates specular with the representative-point construction and energy normalization, so a large emitter converges to the punctual result at distance. Sphere and disk diffuse use the horizon-clipped form factor. Lines and tubes use line irradiance. Rectangles use a polygon form factor. There is no LTC table. Soft source radius and soft source angle widen the highlight without moving it.

`LightContext.Build` runs per camera after culling. It collects visible lights, computes a conservative screen-tile rectangle on the CPU, sorts local lights by view depth, and builds 128 logarithmic depth bins. `LightTileMaskPass` writes one 32-bit mask word per 16×16 tile per 32 lights. `LightLoop.hlsl` (`BeginLightLoop`, `NextLight`) is the only loop. Consumers are deferred shading, forward shading, volumetric fog, volumetric clouds, contact shadows and the lighting debug view. Tiles are not culled by opaque depth, so translucent surfaces and volumes share the list. The local-light list rejects a count above `0xFFFF`.

Cookies and IES profiles share one atlas. Reflection probes are prefiltered into one atlas, axis-aligned, and limited to the asset slot count. Further probes are skipped in priority order. Unity culls probes by their box, so a sphere influence larger than the box can be culled while still visible. Probe rendering layers are not supported. Translucent T0 has no specular environment term.

`FLightRecord` and its HLSL struct are compared by `Tools/StaticCheck/static_check.py` and by an EditMode test. A C# struct uploaded to a shader carries `// GPU-RECORD: <HlslStruct> <hlsl file>`.

## 10. Shadows

Cascade shadows use four splits in a 2×2 atlas. The asset owns resolution, split ratios, blend width and shadow distance. Local shadows use an atlas whose tile is `localShadowMapResolution / 4`. The default budget is 16 tiles. A spot, rectangle or disk uses one tile. A point, sphere, line or tube uses six cube faces. Candidates are scored by strength over distance, with single-tile shapes preferred on ties. Drops increment `LocalShadowBudgetDropped`. Rectangle and disk tiles cover a 120° cone in front of the emitter. Receivers outside that cone are unshadowed.

`EShadowFilter` is Hard, PCF, PCSS, VSM or EVSM, per light. The asset `ResolveShadowFilter` supplies the quality preset or the custom filter settings. VSM and EVSM build a half-resolution moment atlas for the slices that use them.

`EShadowUpdateMode` is EveryFrame, OnEnable or OnDemand. Directional lights update every frame. Cached local shadows keep the casters of their last render. Moving casters do not update those tiles, and there is no dynamic-caster overlay. `RequestShadowMapRendering` advances an OnDemand version.

Contact shadows trace the sun as one long ray and up to 24 local lights, largest on screen first, as short rays. Other directional lights are not included. The Volume owns the step counts. Volumetrics do not consume contact shadows.

Mixed lighting supports baked indirect and shadowmask. Distance shadowmask is the Quality shadowmask mode. Subtractive mixed lighting is not supported. Visible `Light.bakingOutput` decides shadowmask for native lights. Mesh state is not that authority.

## 11. Screen-space shading and subsurface

Screen-space reflection may trace at half resolution. Classify, resolve, temporal filter, bilateral cleanup and history stay at full resolution. History is the temporal output. `Samples` traces 1 to 4 rays inside `SSRTrace`. Each ray writes a Halcyon sample record: unweighted radiance, hit confidence, `Dir * HitT` and a signed inverse pdf. A positive inverse pdf is a hit, zero is empty, and a negative inverse pdf is a miss whose xyz is the unit direction. Facing failure, an edge fade of zero and invalid hit color are empty, so they do not add estimator weight. Resolve rebuilds the hit as that sample's origin plus `Dir * HitT` and rejects a direction into the receiver. It does not sample color and does not weight hits by the GGX pdf. The center pixel uses every hit. Neighbor reuse is a Poisson disk and reads only sample 0. A miss does not enter the average. The blend weight is the mean screen-edge fade of the hits, and the stored color is that average times the fade. Resolve also stores the mean and standard deviation of the unpremultiplied hit colors. Temporal weights the surface-motion and hit-point reprojections by `exp2(-10 * luma(dist))` on the unpremultiplied color and clamps that color to its interval. A history sample whose confidence is below half the current resolve is not reused, and a kept history is not clamped below this frame's color. The stored result is that color times this frame's edge fade. Bilateral cleanup filters color only and keeps the center edge fade. It runs after that only where the hysteresis-smoothed reconstruction variance is high. Classify writes a skip tile's records to zero. The indirect counter is cleared by `SSRClearArgs` in the same shader. Hi-Z and linear tiles share one indirect dispatch; the tile class selects the march. `SSRTRayCast.hlsl` clips each ray to the near and far planes and the screen. Hi-Z walks the closest and furthest pyramids. A hit is the ray segment crossing that raw-depth interval. A full-resolution texel is a point depth. A step that is already behind the next texel still hits when the shared edge lies between the two depths and either neighboring triple is colinear within 0.25 of that step. `SCREEN_TRACE_RELATIVE_THICKNESS` selects 2% of eye depth instead; production shaders do not declare it. There is no Volume thickness. Rough tiles march in screen space and refine a crossing with binary search. The refined segment must still cross the texel it lands on, and the hit stores that texel's depth. Trace-behind and toward-camera are bits of one uniform. Trace samples the accepted unsharpened previous frame through `TemporalColorSource`. That frame already contains the composite, so a later frame can show another bounce. A miss falls back to the probe and sky term. An empty sample does not.

Screen-space indirect diffuse has no resolve kernel and no ray-reuse controls. The work grid is always half resolution. `Samples` averages 1 to 4 cosine rays per work pixel. Those rays sample full-resolution accepted previous color. The production march is the same Hi-Z walk. Defining `SSGI_TRACE_LINEAR` selects the 2D march, with step count `NumSteps * 4` and four binary-search iterations. An 8×8 group compacts valid pixels in group shared memory and the valid lanes trace in that same dispatch. Denoising then runs two stages. Each stage accumulates temporally and then filters on the tangent plane. The first pass has a 6 m base radius and no normal weight. The second has a 3 m base radius and `saturate(dot)`. The radius is also scaled by view depth, a 3×3 variance of the temporal output, and ambient occlusion when that buffer exists. Each stage writes its own history, which is `lerp(filtered, temporal, 0.5 * accumSpeed)` with `accumSpeed` 0.93 when the history is mature. Bilateral upsample always writes full resolution from the second mix. Surface metadata is `R32G32_UInt`: exact depth bits, a 12+12 octahedral normal, 7 bits of age and 1 validity bit. Invalid or off-screen history has zero weight. Half-resolution samples keep the accepted view's quad phase with the history transaction, and reconstruction uses the representative sample positions, including the shorter last interval on an odd edge. Metadata is read with integer loads only, and all lanes take part in the group barriers of the fused trace. The world-space filter radius is `BaseRadius * F(viewZ) * F(variance) * F(AO)`; the three functions are monotone stand-ins because the Metro slide does not give their bodies. The production kernels do not write a ray record: ray-hit storage is diagnostic-only, a trace capture snapshots trace radiance before the first temporal stage, and diagnostic readback decodes metadata on the GPU because Metal does not expose `R32G32_UInt` to `AsyncGPUReadback`. Intensity is a Volume scale from 0 to 10. Low and Medium trace one ray. High and Ultra trace two.

Ground-truth ambient occlusion traces, filters and accumulates at half resolution, then upsamples. Its history is half resolution. Ray-traced ambient occlusion records at the internal resolution when Unified Ray Tracing has a context, an acceleration structure, `VisibilityRTAO.urtshader` and at least one instance. Otherwise ground-truth ambient occlusion owns the ambient-occlusion buffer. Ray-traced ambient occlusion does not use the occlusion history cache. `SystemInfo.supportsRayTracing` is not treated as a Metal capability. Metal uses the compute backend of Unified Ray Tracing.

Subsurface scattering, when the Volume is active and a diffusion profile exists, writes a diffuse-only component from deferred and opaque forward shading; the attachment exists only when the device supports it and the frame requests it. Indirect composition replaces that component's indirect term. `ComputeBurleySubsurface` filters the half-resolution diffuse buffer and recomposes full resolution as the original color minus the original diffuse plus the filtered diffuse. Specular and emissive light stay outside the filter. The material profile index selects the diffusion parameters. The Volume owns enable and sample count. The pipeline asset owns the diffusion profile list. No subsurface target is allocated when the feature is off.

## 12. Atmosphere, fog, clouds and translucency

`AtmosphericalProfile` on the pipeline asset is the only atmosphere configuration. `AtmosphereParameter.FromProfile(null)` throws. `ThrowIfInvalid` rejects out-of-range thickness, scattering, heights, ozone and sun angle. Lengths are meters. Scattering and absorption are stored per kilometer and multiplied by `ScatterPerKmToPerMeter` (0.001) at bind. Volume does not override the atmosphere.

Lookup tables and the aerial-perspective volume are compute products. Cubemap faces are a `RWTexture2DArray`. Sky and fog are applied in `ComputeAtmosphericSkyAndFog` after opaque lighting and before translucent depth.

Volumetric fog and volumetric clouds record when their frame setting is on, the Volume component is active, and their kernels exist. Fog builds an 8×8-pixel froxel volume and reads the light loop. Clouds trace at half internal resolution, require the atmosphere transmittance table, and read the light loop and shadow inputs. Each keeps its own history.

Translucent depth is followed by clouds, fog, fog composite, T0, the color pyramid, T1 and T2. T1 is the refraction stage and reads the pyramid. T0 is lit before that pyramid. T2 is the last translucent stage and includes unlit and overlay-canvas geometry that is not the screen-space overlay list.

## 13. Temporal resolve and output

`UNITY_MATRIX_VP` is `Matrix_ViewJitterProj`. Depth and GBuffer are rasterized in jittered clip space. Reconstruction from a depth buffer uses the jittered inverse view-projection. `FlipY` in this package means `GL.GetGPUProjectionMatrix` with `renderIntoTexture = false`. Motion vectors use the jittered position for coverage and the unjittered clip positions for velocity.

Temporal anti-aliasing reads the current frame at the texel center with point sampling. It does not bilinear-sample at `uv - jitter`. Color history is reprojected by motion. Depth history is reprojected by motion plus the change in jitter. Neighborhood clipping uses reversible luminance compression. Sharpening scales RGB together. The sharpen constant in `Compute_TemporalAntiAliasing.compute` is 0.35. One 8×8 dispatch with a one-pixel halo writes unsharpened `RGBA16F` color and sample age, and `R32` surface depth, to pending history. Sharpened color is display-only. Halo lanes participate in the group barrier.

Super resolution writes unsharpened `RGBA16F` color and display-grid `R32` depth to pending history in the same dispatch as the display color. Its sharpen constant is 0.5. The display result is not copied into history. Reset and off-screen history are not sampled.

`renderScale` is in `[0.5, 1]` and applies to Game cameras when super resolution is enabled. One per-camera descriptor owns internal and display size. Dimensions freeze after `BeginCameraRendering`.

Scene view uses the full pipeline. A gap in the global frame counter does not reset a live Scene view. Cuts, projection changes, descriptor changes and failed submission do. New and reset frames use zero jitter. Preview cameras do not run the temporal resolve. Game cameras recycle history after a frame gap greater than one.

`FOutputPlan` chooses a direct camera target or a display-linear intermediate before post-processing. `PostCombine.hlsl` is the only grading, exposure, bloom, vignette and grain implementation. When no linear consumer exists, that draw also encodes and writes the imported camera target. Hardware-sRGB UI then blends on that target. HDR UI, a debug view, Editor overlays and a requested linear capture keep `DisplayColorBuffer`. A later draw encodes it. Encoded targets are never published as linear buffers. Debug view writes a linear quantity into the post buffer immediately before gizmos, and only when a debug view is active. It is not a second encoder.

`ImportBackbuffer` brings the camera target into the graph. An imported resource is not created or released by the pool. Player cameras that target a display resolve sRGB from `Display.requiresSrgbBlitToBackbuffer` when no render-texture format is known. HDR reads the active swapchain format, gamut and paper white. Linear Rec.2020, PQ Rec.2020 and linear Rec.709 scRGB are separate encodings. HLG is not available through Unity's color-gamut API. While HDR output is active the pipeline owns automatic tonemapping and restores it on exit.

Screen-space overlay UI records for a Game camera with no target texture. On the linear-intermediate path it draws into `DisplayColorBuffer` before encoding. On the direct path it draws into the hardware-sRGB target. Scene view does not draw that list. Screen-space camera and world-space canvases use the T2 renderer list.

`ReactiveMask` is an R8 attachment allocated only for temporal anti-aliasing. The first translucent color pass clears it even when it draws nothing. Later passes load it. Other temporal policies omit the attachment.

## 14. Editor and capture

Editor windows, inspectors and debugger panels are specified in [Documentation~/editor.md](Documentation~/editor.md) and [Documentation~/rendering-debugger.md](Documentation~/rendering-debugger.md). They use Unity Editor IMGUI and Editor styles, plus one UI Toolkit drawer for the default Volume profile. The package does not define a visual design system.

`RenderCaptureService` records normal-frame copies through graph transfers into session-owned staging. It does not replace `camera.targetTexture` and does not call `Camera.Render`. Capture selects an explicit Game camera, or the single active Game camera. Lighting is captured before scene-color ownership moves. Debug view and history are left unchanged.

Automated Editor commands use the CLI adapter under `Editor/Cli`, on `127.0.0.1`, with bearer authentication. They do not save assets. `com.unity.pipeline` screenshot is not a normal-frame capture.

## 15. Integration boundary

The ownership audit is [Docs/UnityIntegrationAudit.md](Docs/UnityIntegrationAudit.md). The rules that follow are the architectural boundary.

Native renderer content uses Unity culling and renderer lists. Custom mesh content uses `MeshWorld`. Ray tracing adapts native geometry through the public explicit-instance API of Unified Ray Tracing. Native renderer identity and custom draw generation are different. A mesh asset is not an instance identity. Native scene changes do not wait for a mesh-world revision.

Adaptive Probe Volumes are bound with Core RP's `ProbeReferenceVolume` APIs on the pipeline command buffer before the graph records. One integration owns initialization and cleanup. A material samples probes in world space. The pipeline does not upload a per-instance probe record for mesh content.

Shader Graph access is confined to `Editor/ShaderGraph/`. Runtime code does not reference Shader Graph internals. No other assembly uses an asmref, reflection into Unity internals, or a spoofed friend name for this purpose.

Custom VFX Graph outputs are unsupported. Particle systems use the translucent renderer list.

## 16. Limits present in the source

These are properties of the current implementation, not a verification record. [Docs/FeatureStatus.md](Docs/FeatureStatus.md) states what has actually been executed.

- Mesh bins ignore `defaultSort`.
- Cached OnEnable and OnDemand local shadows keep the casters of their last render.
- Rectangle and disk shadow tiles cover a 120° cone.
- Reflection probes are axis-aligned, slot-limited and not filtered by rendering layer. Translucent T0 has no specular environment term.
- Contact shadows cover at most 24 local lights and the sun.
- InfinityLit has no metallic, mask, occlusion, detail, height or emission map. The conversion wizard reports the dropped maps.
- Shader Graph scene depth is available in decal graphs, scene color in T1, and the fog node returns no fog. Graph motion is rigid.
- The light and reflection-probe complexity views count clustered-loop visits, not lights that shade the pixel.
- Several recorded passes can contain no draws. Frame Debugger may omit them. Cascade shadows still bind their target when the draw count is zero.
- Hi-Z, half-resolution geometry, lookup tables, light masks and the screen-space producers are async-eligible where the device allows. Overlap has not been measured.
- Ray-traced ambient occlusion records only when Unified Ray Tracing is ready and the acceleration structure has instances. Player shader load is unverified.
- Combine LUT writes Rec.709 linear and dispatches only when the grading key changes.
- Image-regression baselines are per graphics API and are absent until recorded on a verified Editor.
- Validation scenes for decals, local lights and translucency have shown sky bands, dark triangles and cube blotches. The cause is not established.
