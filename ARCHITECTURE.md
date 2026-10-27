# Infinity Render Pipeline — Architecture

This document describes the implementation in this package. When that implementation changes, revise the affected statement. Do not append iteration notes. Contributor obligations, including how this file stays short, are in [AGENTS.md](AGENTS.md). Authoring is in [Documentation~/index.md](Documentation~/index.md). Verification state is in [Docs/FeatureStatus.md](Docs/FeatureStatus.md). Archived plans and receipts under [Docs/History/](Docs/History) are evidence, not a specification.

## 1. System

InfinityRP is a Scriptable Render Pipeline. `InfinityRenderPipeline` derives from `RenderPipeline` and is split across partial files under `Runtime/RenderPipeline/`. A camera frame collects Unity culling results, builds an Infinity light context, records a render graph, executes that graph, and submits the scriptable render context.

Two geometry paths share the frame:

- Native renderers are submitted with Unity `CullingResults`, `RendererList` and `ShadowRendererList`. Lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion data stay on those renderer lists.
- `MeshComponent` instances live in `MeshScene` and are submitted through `RGDrawListHandle`. A mesh component does not discover or modify a `Renderer`.

The package owns the render graph, the physical resource pool, image history, the mesh scene, GPU scene tables, light records, shadow allocation and the atmosphere profile. Unity owns the device, command submission, fences, native culling and the Volume manager.

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

`RGBuilder` records passes, textures, buffers, draw lists and renderer lists for one camera graph. Handles carry the graph generation. A handle from another graph is rejected.

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

`GpuScene.transformVersions` is the exception to image-history ownership. It holds immutable matrix and generation buffers for the scene. A camera keeps accepted and pending version references. A revision advances only when matrices or slot membership change. Dirty 64-slot pages upload once per version, and a compute producer copies the unchanged elements. Shader motion checks scene identity, snapshot range and slot generation.

Clear depth and sampled depth are different values. `ClearRenderTarget` and `AttachmentDescriptor.clearDepth` take `GraphicsUtility.ClearDepthFar`, which is normalized so that 1 means the far plane. Shader comparisons against a sampled depth buffer use `GraphicsUtility.SampledFarDepth`.

## 7. Mesh drawing pipeline

`MeshComponent` content is not submitted through a renderer list. The authoritative store is an SoA scene with separate lifetimes:

1. One logical instance and one exclusive transform.
2. Geometry sections and material records, reference-counted.
3. Per-pass bins and grouped command spans, rebuilt only on structural change.
4. Per-view visibility, from a CPU job or from GPU `CullFrustum`.
5. Per-draw-list submission payload, CPU procedural or GPU indirect.

```text
MeshInstanceRecord ──TransformId──► TransformRecord     (1:1)
        └── MeshDraw[] ──► MeshSectionId + MaterialDataId

PassBin            filtered, structurally sorted draws for one MeshPassId
MeshPassCommand    MeshDrawCommandKey + member span
MeshDrawCommand    mesh, section, material, count, offset (one view, one list)
```

`MeshInstanceId`, `TransformId`, `MeshDrawId`, `MeshSectionId` and `MaterialDataId` store an index and a generation. Generation 0 is invalid. `MeshSceneUpdate.CreateInstance` rejects a transform that already has an owner. `MatrixDuplicateRatio` is `TransformCount / LogicalInstanceCount` and is 1 in steady state.

| Type | File | Responsibility |
|---|---|---|
| `MeshScene`, `MeshSceneRecords` | `MeshPipeline/MeshScene.cs`, `MeshSceneRecords.cs` | SoA tables, free lists, ownership map, dirty pages, revisions |
| `MeshSceneUpdate` | `MeshSceneUpdate.cs` | Transaction and undo log. `Dispose` without `Commit` rolls back |
| `GpuScene` | `GpuScene.cs` | GPU mirror. Dirty 64-slot runs of transforms, layers, bounds, flags |
| `MeshTransformVersionCache` | `MeshTransformVersionCache.cs` | Immutable transform versions and their GPU production |
| `MeshWorld` | `MeshWorld.cs` | Per-scene owner of the scene, GPU scene, visibility, pass registry and draw pipeline |
| `PassRegistry`, `MeshPassTypes` | `PassRegistry.cs`, `MeshPassTypes.cs` | Nine `MeshPassId` definitions |
| `PassBinStore` | `PassBinStore.cs` | Filter, structural sort, grouping by `MeshDrawCommandKey` |
| `MeshVisibilityShare` | `MeshVisibilityCache.cs` | Interned CPU visibility by `MeshVisibilitySignature` |
| `MeshGpuVisibilityCache` | `MeshGpuVisibilityCache.cs` | One GPU visibility buffer per view key |
| `MeshDrawCompiler` | `MeshDrawCompiler.cs` | `MeshPassExtractJob` |
| `MeshCandidateTableStore` | `MeshCandidateTableStore.cs` | Per-pass GPU command and candidate tables |
| `MeshDrawGPUBackend` | `MeshDrawGPUBackend.cs` | Payloads, compaction, indirect arguments |
| `RGDrawListContext` | `RenderGraph/RGDrawList.cs` | Graph registry. A draw list is not an `ERGResourceType` |

`MeshPassId` is Depth, GBuffer, Forward, Motion, Shadow, TranslucentDepth, TranslucentT0, TranslucentT1, TranslucentT2. The frame calls `CreateDrawList` for depth, a recovered depth view, GBuffer, forward, motion, cascade shadow and local shadow. Translucent stages are drawn with `RendererListDesc` in `TranslucentPass`. Their mesh pass identifiers remain registered and are not a second submit path.

`PassBinStore` keeps a draw when it is eligible, inside the pass queue range, and alive. Motion rejects camera-only motion. The bin order is `CompareStructural` (queue, material, mesh, section). `defaultSort` and `MeshSortKey.PackSortKey` are not applied to bins. Rendering-layer and culling-mask tests belong to `MeshView` and the visibility job, not to the bin rebuild. `filterRenderingLayers` is set on cascade and local shadow views.

`MeshDrawCommandKey` and `MeshVisibilitySignature` use structured `Equals`. Hash codes only accelerate lookup. `MeshSortKey` saturates each 16-bit segment before packing.

### Transaction

`MeshScene.BeginUpdate` allows one transaction and snapshots revisions and dirty pages. Every mutation pushes an undo entry. Section and material slots whose reference count reaches zero during the transaction are queued. `Commit` flushes that queue. `Rollback` discards it after undo restores the counts, increments `BinEpoch`, and does not truncate free lists. `GpuScene.Update` refuses to run while a transaction is open.

### Frame

1. `RenderContext` applies dynamic transforms and drains dirty mesh components through one `MeshSceneUpdate`.
2. `MeshWorld.UpdateResidency` uploads dirty GPU-scene pages.
3. `MeshWorld.BeginCamera` resets per-camera counters.
4. Passes call `CreateDrawList(meshView, passId)`. On the GPU-indirect policy that declaration also requests `EnsureMeshGpuCull`.
5. Compile drops draw lists with no consumer, selects the backend, and inserts upload and compaction.
6. Execute draws. `ReleaseAll` retires payloads and visibility references. After native submit, `FlushRetired` returns allocations to `ResourcePool` without clearing outstanding GPU use.

CPU direct drawing acquires interned visibility, runs `MeshPassExtractJob`, and calls `DrawMeshInstancedProcedural` with transform indices. GPU indirect drawing culls once per view key, builds a candidate table of `(instance slot, local command)` for the whole bin, and at execute runs `ClearCounts`, `Compact` and `BuildIndirectArgs`. Compacted indices are transform indices. Shaders read `transformBuffer`, `previousTransformBuffer` and `instanceIndexBuffer`.

`EMeshBackendPolicy` is Auto, CpuDirect or GpuIndirect. The camera or test override wins, then the Rendering Debugger override, then the asset. Auto and an explicit GPU request both require instancing, compute, the mesh-draw compute shader and its four kernels. A payload that cannot be sized falls back to CPU direct and increments `GpuOverflowCount`. There is no process-global indirect-argument buffer and no same-frame visible-count readback.

`ERenderingLayer : byte` is the shared mesh and light mask, with `Everything = 0xFF`. Surface masks in GBuffer C are an 8-bit unorm value. High bits are rejected.

Public recording for a pass is:

```csharp
RGDrawListHandle draws = graph.CreateDrawList(meshView, MeshPassId.GBuffer);
data.draws = pass.UseDrawList(draws);
// execute:
cmdEncoder.Draw(data.draws);
```

Content code mutates `MeshComponent`. It does not call `CreateDrawList`. There is no `MeshDrawRequest` type.

Draw markers are `RenderLoop.DrawMeshDirect` and `RenderLoop.DrawMeshIndirect` for ordinary views, and `Shadows.DrawMeshDirect` and `Shadows.DrawMeshIndirect` for shadow views, selected from `EMeshViewKind`. Preparation samplers are `ComputeMeshCulling`, `ComputeMeshCompaction` and `UploadMeshData`, shared across views and owned by `MeshDrawProfiling`. Shared sampler names do not merge graph nodes.

Out of scope for this pipeline: GPU sorting of draw keys, GPU LOD, meshlets, and replacing renderer lists for Unity `MeshRenderer` objects. `RefineVisibilityHiZ` is present and empty.

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

Screen-space reflection may trace at half resolution. Classify, resolve, temporal filter, spatial filter and history stay at full resolution. History is the temporal output, copied before the spatial filter. Trace classification, GGX rays and ray reuse are implemented in `Compute_ScreenSpaceReflection.compute`. The reflected color is the current frame's opaque lighting.

Screen-space indirect diffuse has no resolve kernel and no ray-reuse controls. Cosine rays sample the accepted unsharpened color pyramid directly. Trace, temporal filter, world-space spatial filter and history share the selected work grid. Reconstruction writes full resolution. Low, Medium and High presets trace at half resolution. Ultra traces at full resolution. Custom keeps the Volume `HalfResolution` value. History is the filtered working result. Surface metadata is `R32G32_UInt`: exact depth bits, a 12+12 octahedral normal, 7 bits of age and 1 validity bit. Invalid or off-screen history has zero weight. Rays are binned per 8×8 tile in group shared memory, with one compact interval per occupied tile and a 64-wide indirect trace.

Ground-truth ambient occlusion traces, filters and accumulates at half resolution, then upsamples. Its history is half resolution. Ray-traced ambient occlusion records at the internal resolution when Unified Ray Tracing has a context, an acceleration structure, `VisibilityRTAO.urtshader` and at least one instance. Otherwise ground-truth ambient occlusion owns the ambient-occlusion buffer. Ray-traced ambient occlusion does not use the occlusion history cache. `SystemInfo.supportsRayTracing` is not treated as a Metal capability. Metal uses the compute backend of Unified Ray Tracing.

Subsurface scattering, when the Volume is active and a diffusion profile exists, writes a diffuse-only component from deferred and opaque forward shading. Indirect composition replaces that component's indirect term. `ComputeBurleySubsurface` filters the half-resolution diffuse buffer and recomposes full resolution as the original color minus the original diffuse plus the filtered diffuse. Specular and emissive light stay outside the filter. The material profile index selects the diffusion parameters. The Volume owns enable and sample count. The pipeline asset owns the diffusion profile list. No subsurface target is allocated when the feature is off.

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

Native renderer content uses Unity culling and renderer lists. Custom mesh content uses `MeshScene`. Ray tracing adapts native geometry through the public explicit-instance API of Unified Ray Tracing. Native renderer identity and custom draw generation are different. A mesh asset is not an instance identity. Native scene changes do not wait for a mesh-scene revision.

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
