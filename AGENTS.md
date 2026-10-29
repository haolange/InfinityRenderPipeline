# InfinityRP contributor and agent specification

This document is normative for changes to `com.infinity.render-pipeline`. [ARCHITECTURE.md](ARCHITECTURE.md) describes the implementation. [Docs/FeatureStatus.md](Docs/FeatureStatus.md) records verification. [Documentation~/index.md](Documentation~/index.md) is the authoring manual. [Docs/History/](Docs/History) is immutable evidence.

A silent fallback is not a fix. Console output with no errors has hidden ownership, order and lifetime defects, including a depth clear that left the Game view empty.

## 1. Work tracking

Track active work in the conversation task list. Do not create a root task ledger. Completed verification belongs in `Docs/FeatureStatus.md`.

### Execution

Deliver the requested scope in the same interaction: decide, change, verify, and leave a connected result. Do not shrink it to a sample, a draft, or a proof of concept, and do not stop after the easy path, a plan, or a local patch that its callers never reach. Stages inside the interaction are allowed. Stop only when continuing could lose data, break an API, ABI, or serialized schema, force a wide or irreversible change, move a security or permission boundary, or change user-visible behavior that cannot be rolled back, or when the evidence supports no safe default. Ask in those cases. For every other ambiguity, state the default, why it was chosen, the risk, and the rollback, then finish the reversible work.

Do not create a git branch unless the user explicitly asks, or the session is already a worktree. Choosing an approach is not a branch. `Docs/History` does not authorize a push or new work.

Base a load-bearing decision on code, documents, logs, tests, historical assets, or an explicit user instruction. Keep the working state as a short summary that can be rechecked: goal size, evidence, decision, and what is still open.

Change only the requested code, documents, and the couplings that must move with them. Do not add flexibility, abstraction, or a repository-wide rewrite for later. Do not keep a second implementation, and do not add a compatibility shim to avoid deleting a replaced path. Principle 5 still requires that deletion once its consumers are closed.

The checks that match the goal are the stop condition. A static check does not close a runtime path. An independent verifier, when one is used, does not replace the implementing agent's own checks.

### Living documents

After an iteration, update each of `AGENTS.md`, `ARCHITECTURE.md`, and `DESIGN.md` that exists. Add a durable fact that became true, delete a statement that is no longer true, and rewrite a statement that changed. Do not append an iteration log. `AGENTS.md` is the short normative contract and stays the shortest of the three. `ARCHITECTURE.md` describes the implementation. `DESIGN.md` exists only for a visual design system this package actually has; it is absent on purpose. Prefer a tighter sentence, or a deletion, to a new section.

Player builds and packaging require explicit user authorization. Editor load and script reload never migrate, rebuild, delete or save user assets. A migration is explicit and targeted, with current-byte backups, an integrity check, a reopen and a no-op. Preflight never activates source scenes or instantiates prefabs.

An implementing agent owns planning, code and self-tests. An independent verifier reviews a candidate batch. A failed, partial or missing verification returns the batch and locks dependent batches. Verification does not downgrade a result. Model names are not part of this contract.

Without an Editor, the offline gate is `Tools/StaticCheck/`. Image results stay `TODO(UNVERIFIED)` until a captured frame confirms them.

### Intermediate data

1. Task output lives in `<Unity project root>/intermediate/<task-id>/<run-id>/`. For this workspace that root is the Example project's `intermediate/`, never the package root. `.gitignore` excludes `intermediate/`. The directory holds captures, repeated screenshots, test output, diagnostic log copies, temporary scripts, scratch projects and temporary archives. Do not accumulate those files in `/tmp`, `/private/tmp`, the home directory or an unrelated archive. If a tool forces another path, collect the output into the task directory and remove the tool's copy once it is no longer in use.
2. Before a large capture, record paths, approximate size and owner. Check free space and estimate size from resolution, format, buffer count and frame count. Bound the capture count.
3. After a completed task or subtask, stop producers, drain pending writes and readbacks, keep the required summary, and delete that task's intermediate data. GPU drain and disk cleanup are separate checks.
4. Data may remain between steps only for a named next consumer. Record the path, reason and consuming step. Delete it when that consumer finishes.
5. Before declaring the overall task complete, delete every intermediate path that task owns, including carry-over, and remove the empty task directory. If no other task owns data there, `intermediate/` is empty or absent. A blocked task reports retained paths, sizes and the next consumer, and is not described as complete.
6. Keep original failure evidence unchanged while diagnosis needs it. A durable raw archive exists only when a separate user requirement asks for one, and only after that archive is verified. Moving files is not cleanup.
7. Delete by recorded task path. Do not delete user source, baked data, Unity `Library`, engine caches, user logs, approved deliverables or another task's files. Do not recursively delete a shared `intermediate/` that contains other tasks. Do not commit intermediate payloads.
8. The delivery note states that the task paths are gone, the reclaimed space, and any cleanup blocker. A delete command alone is not that statement.

## 2. Principles

1. Entity data belongs to the entity. One logical object owns one `TransformId` and one bounds record. Submesh, material and pass add draw references.
2. Stable structure is cached. View results are regenerated. Pass bins rebuild on structural change. Visibility, LOD, sort and compaction are per request.
3. Passes record mesh work with `RGDrawListHandle` (`CreateDrawList`, `UseDrawList`, `Draw`). Content code mutates `MeshComponent`. Backends stay internal. There is no `MeshDrawRequest` type.
4. `CreateDrawList` records only. Structural culling runs first and crosses every resource type. Only live draw lists schedule work. A pass that must run calls `EnablePassCulling(false)`. Compiler-inserted mesh preparation moves to the earliest pass whose inputs exist. Recorded order is otherwise the schedule. Adjacent graphics work is one submission segment. Compatible raster passes are one native render pass. Overlap is a descriptor-level slot plan executed by `ResourcePool`. Handles carry a generation and are rejected after their graph.
5. A replaced path is deleted after its consumers are closed and the replacement owns the same responsibility. Two authorities for one quantity are not kept.

## 3. Names and files

| Kind | Convention | Examples |
|---|---|---|
| Namespace | `InfinityTech.<Domain>[.<Sub>][.Editor]` | `InfinityTech.Rendering.MeshPipeline` |
| Enum | `E` prefix | `EPassType`, `EMeshBackendPolicy` |
| Geometry or GPU record | `F` prefix | `FBound`, `FPlane`, `FLightRecord` |
| Handle | `Handle` suffix, no `F` | `TextureHandle`, `RGDrawListHandle`, `ObjectHandle<T>` |
| Render-graph type | `RG` prefix | `RGBuilder`, `RGRasterPassHandle` |
| Job | `*Job` suffix | `MeshInstanceCullingJob` |
| Private instance field | `m_` + PascalCase | `m_RGBuilder` |
| Static readonly | `s_` prefix | `s_Shader` |
| Shader property IDs | `ID_` or a name on `InfinityShaderIDs` | `TransformBuffer` |
| Shader bindings | `SRV_` / `UAV_` / `CBV_` | `SRV_DepthTexture` |

Extend `InfinityRenderPipeline` only through partial files under `Runtime/RenderPipeline/Pass/<domain>/`. Volume components live in `Runtime/Volume/<domain>/`. A pass file holds an `XxxPassData` struct, an optional `XxxPassUtilityData`, and the pipeline method. Prefer `#region` in Editor inspectors and avoid it on hot runtime paths. One primary type per file unless a pass or feature group is clearer together.

Assembly names are `Unity.RenderPipelines.Infinity.*`. Old identities appear only in temporary migration tooling and in immutable history, never as a compatibility shim.

Components prefer `[ExecuteAlways]` and `AddComponentMenu("InfinityRenderer/...")`. Asset fields are `[SerializeField] private`. Shader paths start with `InfinityPipeline/`. The shader tag is `RenderPipeline=InfinityRenderPipeline`. Compute files are `Compute_<Feature>.compute`. LightMode values match `InfinityPassIDs`.

Comments are English. A TODO has the form `// TODO: <action>`. Do not commit large blocks of commented-out code. When a change touches a misspelled symbol, correct that spelling. Do not add a new divergent spelling.

GPU work uses Raster for draw, Compute for dispatch, Copy for resource copy and Upload for CPU upload. Unity `ShadowCaster`, `Meta` and `SRPDefaultUnlit` remain the engine entry points. Infinity LightModes and the matching pass names use `RasterXXXPass`. Other owned raster shader names use the Raster prefix. Ray-traced occlusion selects `ComputeRayTracedOcclusion` or `RayTracingOcclusion` from its Unified Ray Tracing backend. Its graph pass type stays RayTracing. Pass names come from the existing `ProfilingSampler`. Old-name aliases are not added.

## 4. Hot paths

Hot paths are structs and Burst jobs with an explicit `Allocator` (`Persistent`, `TempJob` or `Temp`). `unsafe` is limited to native containers and culling jobs. Burst jobs do not capture managed delegates.

`MeshDrawCommandKey`, `MeshVisibilitySignature`, `BufferDescriptor` and `TextureDescriptor` use field-level `Equals`. `GetHashCode` only accelerates lookup. Filter, grouping and sort are three operations. One hash does not stand for all three. Sort keys saturate signed and unsigned 16-bit segments before packing. Silent truncation is not saturation.

Fixed object counts after warmup aim for zero managed allocation in normal library proxy update, graph record and execution, for stationary and moving objects. Growth, registration and requested diagnostics are separate. Diagnostic observers run outside the measured scope. Reuse does not shorten recording, submission, cancellation or completion ownership.

## 5. Mesh scene

| Module | Responsibility |
|---|---|
| `MeshScene` / `MeshSceneUpdate` | SoA tables, typed IDs, dirty pages, undo transactions |
| `GpuScene` | Dirty-page upload of transforms, layers, bounds and flags |
| `PassBinStore` / `MeshDrawCommandKey` | Per-pass filter, structural sort, command spans |
| `MeshVisibilityShare` | Interned CPU results in `MeshVisibilityCache.cs` |
| `MeshGpuVisibilityCache` | Physical visibility buffers per view |
| `MeshDrawCompiler` | `MeshPassExtractJob` |
| `MeshCandidateTableStore` | Per-pass GPU command and candidate tables |
| `MeshDrawPipeline` / `MeshWorld` | Extract, resolve, submit; per-scene ownership |
| `MeshDrawGPUBackend` | Per-draw-list payloads, compaction, indirect arguments |
| `RGDrawListContext` | Graph registry. Not an `ERGResourceType` |

1. `MeshSceneUpdate` undoes every mutation. `Dispose` without `Commit` restores live counts, relations, revisions and dirty ranges through the undo log. Free-list membership and high-water marks stay with `Free*`, `Restore*` and deferred reclaim. Rollback does not truncate free lists. A structural edit is not left half applied.
2. A section or material whose reference count reaches zero inside `BeginUpdate`…`EndUpdate` is queued. `Commit` flushes the queue. `Rollback` discards it after undo restores the counts.
3. Visibility results are interned by `MeshVisibilityShare`. `ReleaseAllDrawLists` owns that handle lifetime. TempJob cull arrays do not survive the frame.
4. GPU payload objects in the backend pool are empty. Physical buffers belong to `ResourcePool`. A payload lifetime is not permission to destroy a buffer or to write it from the CPU.
5. No process-global indirect or argument buffer is shared across draw lists or cameras.
6. GPU culling indexes instance slots. Compacted indices and CPU `MeshDrawList.instanceIndices` are transform indices.
7. `ERenderingLayer` is the shared mesh and light mask (`enum : byte`, `[Flags]`, `Everything = 0xFF`). Do not reintroduce an integer layer index. Pass-bin rebuild does not apply this mask. Shadow views do, through visibility.
8. `ReleaseAll` and `ReleaseAllDrawLists` perform logical cleanup and `RetirePayload` only. After native submit, `FlushRetiredPayloads` and `FlushRetiredBuffers` return allocations without clearing outstanding use. Physical destruction requires no recording references and completed GPU use. Native submit, queueing the final fence, and accepting the frame transaction are three results. A fence that was never queued does not acknowledge work.
9. Before recording, compute the payload budget and call `EnsureCapacity` once. During recording, `RequireCapacity` checks only. Do not grow or recreate a buffer already bound to a live command buffer.
10. `CreateInstance` rejects an owned transform. Do not share one transform across instances without that rejection.
11. `GpuScene.Update` throws while a transaction is open. Replacement attribute tables are published atomically. Draw lists bind the frozen tables.
12. Policy order is an explicit camera or test override, then the Rendering Debugger override, then the asset `meshBackendPolicy`. Auto is capability-based. CPU direct records no GPU mesh culling or compaction.
13. `MeshComponent` keeps its serialized field identities. Callers use `MeshAsset`, `Mobility`, the instance properties and `SetMaterials`. `Materials` is read-only. Do not add a public mutable material array or per-instance resource polling.
14. Call `NotifyGeometryChanged` after in-place mesh edits, `MaterialRouteUtility.ApplyPassState` after route or stage edits, `NotifyMaterialRoutingChanged` after direct queue or pass edits, and `NotifyInstanceChanged` after static pose or external instance edits. Dynamic world matrices are read automatically. Ordinary uniforms do not invalidate draw structure. Inspector and Undo callbacks notify without saving assets.
15. Change queues deduplicate until publication. One transaction applies changed transforms and bounds. A `MeshSceneUpdate` lease carries a generation. A copied or expired lease cannot mutate a newer transaction. Instance draw links remove only that instance's draws.
16. Mesh preparation markers are `ComputeMeshCulling`, `ComputeMeshCompaction` and `UploadMeshData`, without view-domain prefixes. Draw markers are `RenderLoop.DrawMeshDirect` / `Indirect` or `Shadows.DrawMeshDirect` / `Indirect`, selected from `EMeshViewKind`. `MeshDrawProfiling` owns those samplers. Renderer-list submission adds no wrapper. Shadow slice and face labels, and native `(RP ...)` labels, stay as Unity emits them.
17. Steady state requires `MeshScene.MatrixDuplicateRatio == 1.0`.

```csharp
RGDrawListHandle draws = graph.CreateDrawList(meshView, MeshPassId.GBuffer);
data.draws = pass.UseDrawList(draws);
cmdEncoder.Draw(data.draws);
```

## 6. Graph, resources and output

1. Do not disable a designed path to quiet the Console. Forbidden examples are `EnableAsyncCompute(false)` as a correctness switch, replacing `throw` with `return false`, a dispatch with count 0 that only looks wired, and `if (shader == null) return` inside execute. An unimplemented feature is not recorded.
2. An invalid `RGTextureHandle` or `RGBufferHandle`, a failed query, or a failed resolve fails at record or setup. Do not bind `Texture2D.blackTexture`, a lighting buffer used as history, or an empty `RenderTargetIdentifier`.
3. Transfer copies. Raster draws. Compute dispatches. Do not move a draw into Transfer to avoid an API. Do not call `SetRenderTarget` inside execute.
4. Image history lives only in `HistoryCache`. Import accepted history for reads and pending targets for writes. Fused temporal producers may write the pending target. Other producers use CopyHistory. Accept pending history only after successful submission. A same-frame scoper ID that has not been written is not history.
5. Downstream reads the semantic ID the last producer registered. `DisplayColorBuffer` is the present source when a linear intermediate exists. Do not guess among `SuperResolutionBuffer`, `AntiAliasingBuffer` and `LightingBuffer`.
6. Unity's `temporary render texture` message means an empty identifier. It does not mean the graph called `GetTemporaryRT`. Check handle validity, pass order and which producer must register the ID.
7. A workaround is marked `// TODO: <root cause>` and is not the final design. Ownership, order and lifetime come before an API migration.
8. Graph textures are created, imported or taken from `ResourcePool`. Hot paths do not call `GetTemporaryRT`.
9. A missing volume or a disabled optional feature skips recording. A missing shader or kernel for an optional feature also skips recording. Lighting, active temporal anti-aliasing and display throw at record when their contract is broken. Execute does not contain `if (shader == null) return`.
10. Feature code uses `IComputeCommands`, `IRasterCommands`, `IRaytracingCommands` and `ITransferCommands`. Graph encoders implement them. Outside the graph, wrap a `CommandBuffer` with `CommandBufferCommands`. Encoders do not define `implicit operator CommandBuffer`.
11. Mip chains stay in one compute pass. The graph tracks resources, not subresources. Loop the dispatches inside one execute.
12. Lookup tables, froxel volumes and cubemaps are compute. Atmosphere tables are not produced with `Blit`, `SetRenderTarget` or `BuiltinRenderTextureType`. Cubemap faces are a `RWTexture2DArray`.
13. `ReadOnlyDepthStencil` is set only when the pass declared read-only depth and does not write it.
14. `CommandBufferPool.Get()` is unnamed. Camera and pass groups use `ProfilingScope(cmd, sampler)`. A pass GPU name comes only from `pass.customSampler`. A `ProfilingSampler` name must not match a `cmd.BeginSample(string)` argument. String `BeginSample` is only an in-pass marker with no sampler of the same name (`BloomDownsample`, `BloomUpsample`). `ProfilingScope(sampler)` without a command buffer is a CPU-only track.
15. `EnableNativeRenderPass(false)` is allowed only for gizmos and wire overlay, final output, and UI overlay, for the API reasons in [ARCHITECTURE.md](ARCHITECTURE.md). A new use needs the same kind of hard limit. Overlay is not `DrawUIOverlay`.
16. Clear depth uses `GraphicsUtility.ClearDepthFar`. Sampled depth comparisons use `GraphicsUtility.SampledFarDepth`.
17. A color-only target uses the color-only `SetRenderTarget` overload. An empty depth identifier is not passed in a `RenderTargetBinding`.
18. The backbuffer enters through `ImportBackbuffer`. `RTHandle.SetTexture` is internal to Core RP. Rebinding reallocates when the identifier changes.
19. History reallocates on the field-level physical descriptor compare. Do not compare through `RenderTextureDescriptor`.
20. `ReleaseBuffer` and `ReleaseTexture` preserve recording references and queued use. Cancelled recordings release only their recording references. Partially queued or uncertain submissions stay protected. Pool return is not proof of safe CPU reuse.
21. Compile RAW, WAR and WAW on the effective queue, gated by device async-compute and fence support. Pooled allocations keep last-use fences from both queues. Never query an async-queue fence as a CPU completion signal on a Metal device that does not support it.
22. `RGBuilder.Execute` propagates the original failure. Attachment Load is a read. Depth that a later effect or capture consumes is stored. Atmosphere and light-list outputs are consumer inputs. Producers do not prebind those outputs as globals. Every light-list consumer declares the z-bin and tile-mask buffers and binds them with `FLightLoopInputs`. Light-context uploads are Transfer producers. An imported `GraphicsBuffer` is not cloned into a `ComputeBuffer` descriptor. Replaced light-context buffers retire after submit.
23. History write reservations do not mutate committed descriptors. Swap both descriptor and allocation on commit. Preserve committed history if the pending write rolls back. Shared-cache validity comes from queue-accepted production, independent of camera success. Failed camera or frame transactions force a history reset. Frame cleanup keeps the first exception and still attempts submit for earlier accepted work.
24. Camera framebuffer descriptors declare Internal or Display sizing. Fixed lookup tables, atlases and external targets stay Exact. Each camera owns its capacity history. Capacity aligns to 64 texels, grows by at least 12.5% when exceeded, and shrinks after 30 accepted requests whose three-request peak is below 75% of capacity area. Orientation changes reset that history. Failed submissions do not advance it.
25. Pool retention watermarks are 400 MiB for textures and 64 MiB for buffers. Idle age greater than two successful submission epochs selects the oldest entries above the watermark. Eviction waits for a CPU-queryable fence. Counters are estimates.
26. Generate screen-binding registries with `python3 -B Tools/TextureViews.py --write`. The offline gate checks them. Do not generate bindings by loading assets.
27. `FOutputPlan` selects direct or display-linear output before post-processing. Encoded targets never alias `PostProcessBuffer` or `DisplayColorBuffer`. HDR UI, debug view, Editor overlays and requested linear captures keep a linear intermediate. Hardware-sRGB SDR UI blends on the final target. Post-combine owns grading, exposure, bloom, vignette and grain through `PostCombine.hlsl`. A later output draw encodes an already composed buffer and does not grade again. Read-only post bypass may alias its source. An overlay writer requires its own target.
28. `ReactiveMask` exists only for temporal anti-aliasing. The first translucent color pass clears it even with zero draws. Later passes load it. Other policies omit the output and compact the motion and metadata targets. There is no standalone clear dispatch.
29. Display-depth upsampling exists only for recorded Editor overlays that need it. Cached depth-atlas tile copies stay raster. Unity `CopyTexture` forbids a partial depth-only copy.
30. Debug view writes linear quantities only, immediately before gizmos and wire overlay, and only into the post buffer when a debug view is active. `TAAConfidenceBuffer` is created only when `debugView != None` or this frame's capture requests confidence. Capture does not change debug view or history.
31. One live draw list receives one upload producer and, for GPU indirect, one compute producer with visibility, metadata, index and argument dependencies. Raster consumers only draw. Batch index and argument ranges are disjoint. Payload capacity is acquired before recording and does not grow during execution.

## 7. Temporal

1. `UNITY_MATRIX_VP` is `Matrix_ViewJitterProj`. Reconstruction from screen UV and the depth buffer uses the jittered inverse. On the compute side that matrix is `matrix_*FlipYJitter*`.
2. Motion vectors are jitter-free. `SV_POSITION` uses the jittered view-projection. The clip positions used for velocity do not.
3. Temporal anti-aliasing reads `screenUV = (id.xy + 0.5) * texelSize` with point clamp. Do not bilinear-sample at `screenUV - TAAJitter`.
4. Color history uses `uv - motion`. Raster depth uses `uv - motion + previousJitterUV - currentJitterUV`. Motion metadata carries expected previous raw depth, current surface raw depth, validity and reserved data from one surface. Invalid, off-screen or disoccluded history has zero weight. Sample age lives in the alpha of the `RGBA16F` history. Do not compare current eye depth with previous-camera eye depth.
5. Sharpening never feeds history. Temporal blending is linear RGB. Output sharpening scales RGB together. A YCoCg Y offset that adds a constant to RGB is not that operation. The anti-aliasing sharpen constant is 0.35. Super resolution writes unsharpened color and display-grid surface depth to pending history in its production dispatch. Its sharpened display result has no history-copy path. Reset and off-screen super-resolution history is not sampled. Multiplying undefined history by zero is not rejection. `TAAJitter` has super-resolution consumers. Do not remove that upload without those consumers.
6. Scene-view history advances by accepted camera submissions. A global frame gap does not reset a live Scene view. Cuts, projection or descriptor changes and failed submission do. New and reset frames use zero jitter. Do not substitute another camera's latest object pose. Native renderer-list draws use `unity_MatrixPreviousM` and skinned previous positions. The pipeline keeps no second native pose history and sets no per-renderer property blocks.
7. The anti-aliasing dispatch is one 8×8 tile plus a one-pixel halo. Halo lanes participate in the group barrier outside the logical image.

## 8. Features

1. Atmosphere lives only on `AtmosphericalProfile`. There is no Volume override and no `AtmosphereParameter.Default()`. `FromProfile(null)` throws. `ThrowIfInvalid` rejects out-of-range thickness, scattering, Mie, heights, ozone and sun angle, not only zeros. Lengths are meters. Scattering and absorption are stored per kilometer and converted with `AtmosphereParameter.ScatterPerKmToPerMeter` at bind. The compute shader uses one of those units, not both.
2. Default Volume values come only from `InfinityDefaultVolumeProfileSettings`. Quality-level Volume values come from the optional asset `qualityVolumeProfile`. The constructor calls `VolumeManager.Initialize(defaultProfile, qualityProfile)` once. Do not register that profile again as a custom default. There is no second hardcoded default and no identity LUT used as one. Atmosphere, deferred shading and temporal anti-aliasing are not Volume features. The Editor discovers Volume types by reflection. A Player registers only the components listed on the global default profile. Missing types return null. Do not substitute a hardcoded value for a missing Player component. The default profile includes the required color and exposure components and every consumed optional type. Required exposure reads the resolved camera stack. `overrideState` does not gate it. Combine LUT has no inactive-component constant fallback. Exposure is a multiply before the LUT. Tonemap is None or ACES.
3. A Volume feature exposes `quality` plus its low-level parameters. `Resolve()` returns `F<Feature>Settings`. Custom reads the Volume. Other levels read `QualityPresets.<Feature>`. Passes do not branch on `EQualityLevel`.
4. Unity `Light` owns color, intensity, temperature, range, spot angles, shadow mode, strength, bias, near plane, culling mask, rendering layers, bake type, bounce intensity and the cookie. `InfinityAdditionalLightData` owns `FLightShape` and the Infinity-only fields: shadow filter, update mode, contact shadow, volumetric weights, IES, distance fade and light layer. Derived native fields (`type`, `shapeRadius`, `areaSize`, `shadowAngle`) are written on user edits and API setters, never on load. A light without an authored shape derives `FLightShape` from the native fields. The GPU record has no shape enum.
5. Atlases that live across cameras and frames follow `AtmosphereSharedCache`: pending reservations, commit on queue-accepted `MarkProduced`, `DiscardUnproducedPending` for failed cameras, physical retirement after submit. Cross-frame keys are instance IDs, not per-frame record indices.
6. Volume, asset, kernel and dependency configuration run before culling. Recording that depends on scene content is decided after `LightContext.Build`.
7. A C# struct uploaded to a shader carries `// GPU-RECORD: <HlslStruct> <hlsl file>`. `Tools/StaticCheck/static_check.py` and an EditMode test compare field order and types with the HLSL struct.
8. Screen-space reflection may trace at half resolution. Resolve, temporal, bilateral cleanup and history stay full resolution. `Samples` is the ray count traced per reflective pixel, from 1 to 4. `ResolveSamples` is the Poisson reuse count and is not that count. Trace writes one Halcyon record per ray: unweighted radiance, hit confidence, `Dir * HitT` and a signed inverse pdf. A positive inverse pdf is a hit, zero is an empty sample, and a negative inverse pdf is a miss whose xyz is the unit direction. Resolve places the hit at that sample's own origin plus `Dir * HitT`. Facing failure, an edge fade of zero and invalid hit color are empty samples and do not enter the average. A miss does not enter it either. The blend weight is the mean screen-edge fade of the hits; no hit leaves that weight at 0. Resolve averages the hit colors and does not sample color. The center pixel uses every hit. Poisson neighbors use only sample 0. Resolve writes that average premultiplied by the edge fade, plus the mean and standard deviation of the unpremultiplied hit colors. Temporal samples the surface motion vector and the hit point, weights them by `exp2(-10 * luma(dist))` on the unpremultiplied color, and clamps that color to its interval. A history sample whose confidence is below half the current resolve is not reused, and a kept history is not clamped below this frame's color. The stored result is that color times this frame's edge fade. Bilateral cleanup filters color only and keeps the center edge fade. It runs after temporal only where the reconstruction variance, smoothed with hysteresis 0.5, is high. History is the temporal output. Classify clears skipped tiles. One indirect dispatch traces a tile with either the Hi-Z walk or the 2D march. A ray ends at the near plane, the far plane or the screen edge. There is no world-space maximum distance. Hi-Z uses the closest and furthest depth pyramids. A hit is the ray segment crossing that raw-depth interval. A full-resolution texel is a point depth. A step that is already behind the next texel still hits when the shared edge lies between the two depths and either neighboring triple is colinear within 0.25 of that step. `SCREEN_TRACE_RELATIVE_THICKNESS` selects 2% of eye depth instead; production shaders do not declare it. There is no Volume thickness. Rough tiles use a 2D march plus binary refinement. Trace-behind and toward-camera are one uniform flag for the dispatch. Hit color is the accepted unsharpened previous frame, sampled while tracing.
9. Screen-space indirect diffuse has no ray-reuse resolve. The work grid is half resolution. `Samples` averages that many cosine rays per work pixel, from 1 to 4. Rays sample full-resolution accepted previous color. The production march is Hi-Z. `SSGI_TRACE_LINEAR` selects the 2D march. An 8×8 group compacts valid pixels in group shared memory and those lanes trace in the same dispatch. Denoising is two stages. Each stage accumulates temporally, then filters in world space on the tangent plane, then mixes the filter with the temporal output. The first spatial pass uses a 6 m base radius and no normal weight. The second uses 3 m and `saturate(dot)`. Each stage keeps its own half-resolution history, and that history is the mix. Bilateral upsample always writes full resolution.
10. Half-resolution indirect-diffuse samples keep the accepted view's quad phase with the history transaction. Reconstruction uses the representative positions, including the shorter last interval on an odd edge. Metadata is `R32G32_UInt`. Integer loads only. Diagnostic readback decodes on the GPU because Metal does not expose that format to `AsyncGPUReadback`. Ray-hit storage is diagnostic-only. A trace capture snapshots trace radiance before the first temporal stage. All lanes participate in the group barriers of the fused trace. The production path does not output a ray pdf or raw depth. The world-space radius is `BaseRadius * F(viewZ) * F(variance) * F(AO)`. The three functions are monotone stand-ins; the Metro slide does not give their bodies.
11. Subsurface owns an optional diffuse-only attachment when it is supported and requested. Deferred and forward produce it. Indirect composition replaces its indirect term. Half-resolution diffusion recomposes main color as main minus original diffuse plus filtered diffuse. Specular and emissive light never enter the filter. The material profile index selects the profile. The Volume owns enable and `numSamples`. The asset owns diffusion profiles. No subsurface allocation or extra output exists when the feature is off. Routing still requires opaque Deferred subsurface.
12. Async eligibility is not evidence of overlap. History copies of previous-color effects sit after the independent raster window. A graphics transfer immediately after an async producer joins that producer. Early visibility recovery and immediately dependent late effects stay on graphics by design.
13. Native HDR state is queried live. Encoding matches the active swapchain transfer and primaries. Linear Rec.2020, PQ Rec.2020 and linear Rec.709 scRGB are different contracts. HLG needs a native HLG interface, which the current color-gamut API does not provide. HDR automatic tonemapping has one owner and is restored on exit.
14. Subsurface sample count lives only on the Volume. Fixed-ratio super resolution uses asset `renderScale` in `[0.5, 1.0]`, default 1, for Game cameras when super resolution is enabled. One per-camera descriptor owns internal and display size. Explicit Unity camera targets are imported by identifier. Dimensions freeze after `BeginCameraRendering`.

## 9. Shaders and materials

`MaterialRouteUtility` validates surface route and translucent stage and applies pass state. Do not add a second parser or a rounding path. `ShaderGUI.ValidateMaterial` is read-only. An inspector redraw does not apply derived pass state or mark every referencing `MeshComponent` dirty. Runtime edits of `_SurfaceRoute` or `_TranslucentStage` call `ApplyPassState`. Exact values are validated before derived mutations. Subsurface requires opaque Deferred routing.

GBuffer encode and decode symmetry is owned by `GBufferPack.hlsl`.

Metal value and out helpers initialize their results before guarded work and return once where the cross-compiler requires a single exit. Resource access stays behind its validity gate. A rejected surface keeps its raw depth. Ray loops exit through an explicit found state and `break`, without changing traversal order. Do not suppress diagnostics or replace a callee result with a caller-side default. Kernel bounds exits and group-barrier participation keep their existing meaning.

Guard a degenerate normalization before a reciprocal length. Bind required resources and validate physical inputs at setup. Do not substitute a fallback texture. Compute passes changed while closing a diagnostic resolve kernels by name and store kernel and thread-group sizes in pass data at record time. Deferred shading uses an 8×8 group. The clustered light lookup keeps 16×16 tiles. Warning acceptance is public `ShaderUtil` diagnostics after a production dispatch or draw, a fresh log window, and the current Example feature chain. A fixture, a reimport or `FindKernel` alone is not that acceptance.

## 10. Unity integration

- Shader Graph 17.6 keeps `Target` and `SubTarget` internal. The Infinity target is compiled into `Unity.ShaderGraph.Editor` through the single asmref at `Editor/ShaderGraph/`. Only that folder uses Shader Graph internals. It references Infinity by strings. `static_check.py` checks those strings. Runtime does not depend on it. No other assembly uses an asmref, reflection into Unity internals, or a spoofed friend name for this purpose.
- Adaptive Probe Volumes are bound with `ProbeReferenceVolume.BindAPVRuntimeResources` and `UpdateShaderVariablesProbeVolumes` on the pipeline command buffer, before the graph records. No graph encoder exposes a `CommandBuffer` for that bind. One integration owns initialization and cleanup. Enable and disable of an entity each pair with one unregister, including disable followed by destroy.
- Native renderer content uses Unity culling and renderer lists, including lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion. `Light.bakingOutput` on a visible light decides shadowmask. Mesh state does not.
- `MeshComponent` never discovers or changes a renderer and stores no lightmap or CPU per-instance probe data. Its material may sample Adaptive Probe Volumes in world space. Atmosphere and realtime lights remain pipeline inputs.
- Unified Ray Tracing's public explicit-instance API is wrapped by an isolated adapter. Native renderer identity and custom draw generation are distinct. A mesh resource is not an instance identity. Native scene changes do not wait for mesh-scene revisions. The persistent-scene scope is transient and is released with the adapter.
- Additional-data components own only Infinity extensions. They are added with Undo by `ObjectFactory.componentWasAdded` and by the Infinity inspectors. Do not expose a public field that the user assigns to a camera or light. Editor load, reload and scene save do not migrate or regenerate hidden data. Terrain sector regeneration is an explicit inspector action with Undo and prefab tracking.
- The file inventory is `Docs/UnityIntegrationAudit.md` and `Docs/UnityBoundaryInventory.csv`. Inventory hashes describe inspected bytes, not runtime verification.

Runtime resources live only in Global Settings `IRenderPipelineResources` containers with `[ResourcePath]`. The pipeline asset never references runtime shaders. Its material fields are optional overrides for objects created from the GameObject menu. A missing required resource fails at record or setup. `ResolveDefaultVolumeProfile` throws when the Global Settings slot is empty. There is no `Resources.Load` fallback. Player shader stripping lives in `Editor/ShaderStripper/`.

`SupportedRenderingFeatures.active.rendersUIOverlay` is true. Screen-space overlay uses `CreateUIOverlayRendererList` and `DrawRendererList` for a Game camera with no target texture: on the hardware-sRGB target after direct output, or on `DisplayColorBuffer` before encoding when a linear intermediate is required. Scene view never draws that list. Screen-space camera and world-space canvases use the translucent renderer list.

`Dispose` restores `SupportedRenderingFeatures.active`, `Shader.globalRenderPipeline` and the `GraphicsSettings` flags captured at construction.

Custom VFX Graph outputs are unsupported. Do not add a friend-assembly shim or patch Package Cache to revive them. Standard `ParticleSystem` rendering uses the translucent renderer list.

Debug state is runtime. `InfinityDebugDisplaySettings` owns the debug view and its panels. Do not serialize `debugView` on the pipeline asset. Do not add a debugger control without a consumer.

## 11. Serialized data

Native legacy fields and current effective values are different evidence. Before a schema cleanup, identify the owner, the bind source and the per-object field identities, and prove every non-approved field unchanged. Do not infer old units or authority from a discarded field.

An approved schema change uses a fresh durable backup and a written intent before the save, an exact allowed delta, stable GUID and local IDs, preservation of unrelated scene dirtiness, a second save that is byte-identical, and a separate no-op. Loading never migrates.

Renaming a serialized Volume, component or asset field resets user values because `FormerlySerializedAs` is not used. Prefer adding a field. Every unavoidable rename is listed in the verification playbook of that change. Replaced types are deleted after consumer closure and an explicit Example-project migration.

Schema, compilation and image checks are separate gates. A schema or compile pass does not make a frame correct.

## 12. Editor interaction and verification

Automated Editor interaction uses the Unity CLI connection to an explicitly selected project (`Editor/Cli`, compiled only with `INFINITY_UNITY_CLI`). Commands take explicit arguments, return status and evidence paths, and never save assets. Use `127.0.0.1`. Keep bearer authentication and loopback enforcement. Never print or archive credentials.

Never use `unity run`, a batch build or a second Editor against a project an Editor already holds. Confirm the project and process ID before a command. `com.unity.pipeline` `screenshot` replaces `camera.targetTexture` and calls `Camera.Render`. It is not a normal-frame, history, temporal or window capture. Use the graph transfer capture and observe the real window. Long-running operations expose status and cancellation. A timeout is not permission to submit again, release resources or restart the Editor.

Scene view creation, close, docking and restoration run on the Editor update pump. The render callback may advance a bounded motion pose only. Closing a Scene view inside its own render or `OnGUI` stack destroys an active camera and render texture and can crash native outline drawing. Preserve tab ownership and position. Setting `EditorWindow.position` can undock the view.

Capture uses an explicitly selected active Game camera, or the one unambiguous active Game camera. Do not assume the MainCamera tag. Capture lighting before its ownership moves into scene color. Do not query a retired semantic name at frame end.

EditMode tests live under `Tests/Editor`.

After an InfinityRP code or shader change, do not wait for the user to compile:

1. Confirm the owned Editor and project path. Never operate another session. When that source is occupied, use the authorized independent task mirror and its Library.
2. Refresh with the Unity CLI or `Tools/RefreshUnityEditor.sh` / `.ps1`, which call `AssetDatabase.Refresh`. UI inspection uses computer use on the verified owned window.
3. Wait for compilation and domain reload. Confirm the candidate DLL loaded. Read diagnostics from that Editor's `Application.consoleLogPath`. A previously stable log is not acceptance.
4. Diagnose only the new log after the mark. Fix the first real owner, order, lifetime or shader-contract defect.
5. Capture with `Tools/CaptureUnityWindow.sh` or `Tools/CaptureUnityWindow.ps1` and inspect the image. Any change to clears, depth, attachments, pass order or present is unverified until the image is inspected.
6. Repeat until the new log window has no InfinityRP flood and the captured frame is correct. Log-only results keep `TODO(UNVERIFIED)`.

A Game view that is not redrawing produces identical screenshots. Require Play mode, or at least one pair of captures that differ in the Game view region, before treating images as liveness evidence.

## 13. Defects that must stay visible

Do not hide the following behind a fallback, a disabled pass or a dummy resource. Current verification limits are in `Docs/FeatureStatus.md`.

- Mesh bins sort only by `CompareStructural`. `defaultSort` is not applied.
- Validation scenes for decals, local lights and translucency have shown sky bands, dark triangles and cube blotches. The cause is undiagnosed.
- Cached OnEnable and OnDemand local shadows keep the casters of their last render. Moving objects do not update them. There is no dynamic-caster overlay.
- Rectangle and disk shadow tiles cover a 120° cone in front of the emitter.
- Reflection probes are axis-aligned, limited to the asset slot count, and have no rendering-layer test. Unity culls them by box bounds, so a larger sphere influence can be culled while visible. Translucent T0 has no specular environment term.
- Shader Graph scene depth is bound only in decal graphs, and scene color only in translucent T1. The fog node returns no fog. Graph motion assumes rigid motion.
- Contact shadows cover at most 24 local lights per camera, largest on screen first. Directional lights other than the sun get none.
- InfinityLit has no metallic, mask, occlusion, detail, height or emission map. Converters drop them and the wizard lists them.
- Light and reflection-probe complexity views count clustered-loop visits, not lights that reach the pixel.
- Image regression has no baselines until they are recorded on a verified Editor. Baselines are per graphics API, not per GPU vendor.
- Several passes record and emit no draws, so Frame Debugger may omit them (`RenderDBuffer` without a DBuffer shader, `RenderLocalShadow` without local casters, `CopyHistory*` transfers). `RenderCascadeShadow` records and binds its target even with zero draws.
- Hi-Z, half-resolution geometry, lookup tables, light masks, ambient occlusion, contact shadows and previous-color effects are async-eligible where supported. Overlap is unmeasured. The current Metal validation host reports no async compute.
- Ray-traced ambient occlusion records only when Unified Ray Tracing's context, acceleration structure and `VisibilityRTAO.urtshader` are ready and the structure has instances. Otherwise ground-truth ambient occlusion owns the result and the debugger shows the reason. Player shader load is unverified.
- Combine LUT is keyed by grading, the ACES curve and output mode. A cache hit dispatches nothing. The LUT writes Rec.709 linear only.

A replaced path is deletable only after consumer closure and equivalent graph ownership are proved. "Not wired yet" is not "obsolete". Without an Editor, consumer closure is the differential compile gate plus a repository-wide search, including `Editor/Cli`, docs and tests. Valid hardware ray-tracing assets that are not yet integrated are kept.
