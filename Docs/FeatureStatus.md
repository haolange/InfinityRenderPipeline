# Feature status

This is the only document that states how far each feature is verified. Other documents describe design and use;
they link here instead of claiming "completed".

Latest bounded acceptance: [2026-10-25 main-thread / memory / SSGI / native HDR receipt](History/MainThread-20261025/Report.md).
Earlier dated sections below retain their original verification boundaries; the latest section supersedes them only for the cases it actually exercised.

Columns:

- **Recorded**: the pass records in the RenderGraph when the feature is enabled.
- **EditMode**: covered by EditMode tests in `Tests/Editor` (CPU contracts or GPU numeric contracts).
- **Image**: a captured normal frame was inspected and accepted. `Open` means not yet captured or not accepted.
- **Perf**: GPU timing measured. `Open` means no accepted GPU measurement.
- **Platforms**: platforms where the Editor actually rendered it. D3D12 and Vulkan are unverified everywhere.

0.5.0 rows are marked `0.5.0` and stay `Open` in Image/Perf until verified on a real Editor.

## Geometry and lighting

| Feature | Recorded | EditMode | Image | Perf | Platforms | Notes |
|---|---|---|---|---|---|---|
| Mesh Drawing Pipeline (CPU + GPU backends) | Yes | 2026-10-25: 96 Mesh cases, value leases, explicit updates and custom culling matrices | Complete Scene_Spazon; Asset CPU / Debugger GPU / cleared override verified | 32+120 CPU Proxy/Render measurements at 1k/10k and 50k stress; GPU time unavailable | macOS Metal | Batched dynamic transforms, explicit static/resource updates, shared versions. Bounded normal library allocation results below. Hardware Async and native failure recovery remain open. |
| ThinGBuffer + Deferred shading | Yes | Yes (pack contract) | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: bounded eight normal fixtures / ground and marker controls | Open | macOS Metal | Finite-plane horizon and marker shadow controls accepted; no atmosphere shader repair or arbitrary-scene guarantee. See closure picture report. |
| Forward (forward-route materials) | Yes | Partial | Open | Open | macOS Metal | |
| Decal projection / RP replacement | Yes | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: 5 lifecycle cases | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: actual DBuffer overlays after replacement, disable/restore and second camera | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | One-shot context recovery; no source asset migration. Finite-plane horizon band traced to disabled planetary ground in a fixed-camera control, not an atmosphere algorithm repair. |
| Light shapes and record v2 (0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: shape GPU 3, record contracts, actual Deferred weight GPU | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: all shapes, glossy/rough, radius, backface, barn-door and term controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Karis / Frostbite approximation. Fixed-input RGBA32F weight equations pass; strict normal RG16F half-ULP decomposition remains unproved. |
| Lightmapper delegate (0.5.0) | Editor bakes | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: shape/record/angular GPU contracts | 2026-10-21–23 / Unity 6000.6.0f1 / macOS Metal: bounded S6/S12 actual bakes | — | macOS Metal | Authored Disk uses native Rectangle carrier and Disc baking. Matched spot window/intensity controls accepted; original spatial/caster residuals remain separately reported. |
| Light cookies + IES atlas (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (5 allocator/state + 11 transaction tests) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (Editor whole-frame, 3×300 before/after) | macOS Metal | Normal-frame consumers, real IES prefab import, reimport and camera/queue recovery verified; timing is not isolated pass cost or causal speedup. Native Point clears a 2D cookie; the retained-invalid-reference Inspector warning is not reachable through that setter. See S11 report. |
| Clustered light list v2 (log z-bins + tile bitmasks, 0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: binning 6, actual Deferred GPU 8; exact-index probe | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: point-light Deferred/Forward, inside/behind/far, multi-camera; S6-matched fog/cloud | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Caller-owned out state; matched compute projection. Whole-frame area/local workload timing does not isolate clustering cost. Other graphics APIs remain unverified. |
| Cascade + local shadows (1–4 cascades with blending; tent PCF, PCSS, VSM, EVSM; every light shape, 0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (allocation, filtering, GPU moments, cache transactions, Shadowmask filtering) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Normal frames and native atlas/dispatch/Inspector evidence; historical S9 requires the existing S12 matrix-alias repair. Small EVSM half residual remains reported. Shadowmask filtering accepted; bounded S12 GI/APV fixtures verified separately. |
| Screen-space traced (contact) shadows: sun + 24 local lights (0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: allocation 2/2; capture 13/13 | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: normal sun/local/Forward, R8/R32 outputs, 30-light allocation, off and missing-producer controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Single visible-depth approximation; thin-caster positive controls and thick-caster misses retained. Low/Ultra native step counts verified; no quality or performance improvement inferred from different global RNG frames. |
| Reflection probes in the clustered list + probe atlas (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (17 layout/priority/transaction tests; actual GPU filter contracts) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal: normal baked/custom/realtime, blend/priority/projection, matched Deferred/Forward, same-frame failure and reimport controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Axis-aligned and existing capacity limits remain. Three independent fresh-RP first/stable Game pairs pass; historical purple/black root cause unknown. Composite baked-environment/realtime-probe timing includes six faces, not APV/lightmap or isolated pass cost. |
| Shader Graph target: Lit / Unlit / Decal (0.5.0) | Graph import | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: generated graphs and contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: bounded Lit/Unlit/Decal, Forward, translucent and baked consumers | Open | macOS Metal | Actual generated/imported consumers remain accepted. Three cold probe-fixture first/stable pairs do not reproduce historical purple/black outputs; root cause unknown. Bounded final Display regression passes, not every graph configuration. Scene Depth outside decals and Fog node return defaults. |
| Atmosphere LUTs + sky IBL | Yes | Yes (parameters, cache) | Open | Open | macOS Metal | |
| GTAO | Yes | Yes (numeric) | Open | Open | macOS Metal | |
| RTAO (UnifiedRayTracing) | When URT is ready | Partial | Open | Open | Editor only | Player shader load unverified |
| SSR (stochastic) | Yes | 2026-10-25: 8 selected SSR cases and shared packed metadata numeric contract | Full Scene_Spazon, 1666x1756; finite full Resolve/Temporal/output | 32+120 matched CPU scopes; no isolated GPU time | macOS Metal | Optional half Trace; Resolve/Temporal/Spatial/history stay full resolution. RG32UInt metadata, accepted unsharpened TAA/SR mip source. |
| SSGI | Yes | 2026-10-25: 39 selected SSGI cases, LDS binning and temporal/reconstruction contracts | Complete Scene_Spazon and supplemented chain; finite half work/full output | Stable pool reduced; no isolated GPU time | macOS Metal | No Resolve or Skip clear. Compact rays, diagnostic-only RayHit, shared Trace/Spatial storage, recurrent world-space Spatial and bilateral reconstruction. General quality acceptance stays bounded to tested content. |
| Burley SSS | Yes | No | Open | Open | macOS Metal | |
| Volumetric fog / cloud | Yes | Partial | Open | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: fog workload 3×300 composite Editor frames; cloud Open | macOS Metal | |
| Translucent T0 / T1 / T2 | Yes | Partial | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: bounded fixture regression and ground control | Open | macOS Metal | Prior band disposition accepted; inherited S15 consumers retained. No unrelated fog-node implementation claim. |
| Baked lighting: lightmap, Shadowmask / Distance Shadowmask, probes, LPPV, APV, terrain, baking sky (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (CPU contracts and actual GPU readbacks) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (bounded task fixtures) | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: baked-environment/probe composite only; APV/lightmap timing Open | macOS Metal | Real bounded S12 lightmap/probe/APV/sky, Terrain and shadowmask evidence retained. Closure adds a durable native BakedLeft cubemap plus realtime-probe composite timing; it does not replace the invalid S12 GPU timing or measure APV/lightmap/baking cost. Meta holes, Player and other APIs remain unverified. |

## Temporal, post and output

| Feature | Recorded | EditMode | Image | Perf | Platforms | Notes |
|---|---|---|---|---|---|---|
| TAA (+ separate sharpen pass) | Yes | Yes | Open | Open | macOS Metal | One-dispatch TAA + sharpen is not implemented |
| Fixed-ratio super resolution | Game cameras | Partial | Open | Open | macOS Metal | |
| Exposure, Bloom, CombineLUT (ACES), Vignette, Film Grain | Yes | Yes | Open | Open | macOS Metal | |
| Present (transfer encoding) | Yes | 2026-10-25: Output cases plus native-linear primaries/paper-white GPU test | SDR and actual internal-XDR linear Rec.2020 HDR, native RGBA16Float attachment, UI/exposure and mode restoration | Open | macOS Metal Editor | HDR available/active/format/gamut validated; no absolute photometry, other HDR swapchains or Player acceptance. |
| SceneView sizing / UI overlay gate | Yes | Yes | Accepted 2026-09-13 | — | macOS Metal | |
| DebugView | Yes | Partial | Open | — | macOS Metal | |
| Rendering Debugger panels, debug views 19–29, Scene view Draw Modes, pass timings (0.5.0) | Yes | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: catalog and mode contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: 20 bounded native debug/Draw Mode views | — | macOS Metal | Current viewport and camera identity/restoration verified. Timings UI visibility does not provide valid GPU timing. See S15 report. |
| Render Graph Viewer (0.5.0) | While open | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: snapshot contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: camera/Scene switching and SSR off/on | — | macOS Metal | Imported-resource markers and CPU closed/open controls verified; no GPU speedup inferred. See S15 report. |
| Material conversion (Built-in / URP / HDRP → Infinity) and create menus (0.5.0) | Editor | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: three conversion contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: three native conversions and 16 creation operations | — | macOS Metal | Bounded source cases, canonical map/color/route/pass state and Undo/Redo verified. Not all 17 registered providers were exercised. Masks, occlusion, detail, height and emission maps are dropped. See S15 report. |
| Image regression over the validation fixtures (0.5.0) | Normal Play frames | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: comparison16, request1, save ownership6; actual recovery controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: eight fixed-input cases compare Pass on main and repaired S16 | — | macOS Metal | Same accepted baselines reused; real light-change Fail/restored Pass, missing/corrupt baseline, state/metadata write failures, pending cancellation, foreign capture and dirty recovery verified. Known sky/dark defects remain Open in their feature rows; consistency is not complete image correctness. See [S16 report](../Documentation~/Evidence/Verification-0.5-2026-10-19/S16-report.md). |

## Out of scope

Hardware ray tracing outside UnifiedRayTracing, depth of field, motion blur, other new post effects, XR, MSAA,
dynamic resolution and super-resolution upscalers beyond the fixed-ratio path.

S17 directory-layout verification (2026-10-23, Unity 6000.6.0f1 / macOS Metal): all 57 moved script/meta GUID bindings and 15 default Volume components resolve. Eight fixtures have no Missing Script and retain their expected Profile components. Repaired historical and final main candidates execute 1,826 EditMode cases (1,773 Passed, 0 Failed, 53 existing Skipped). Callbacks are complete after two reload/resume boundaries; native framework XML omits 33 actual checkpointed skips. The same 16 S16 baseline files remain byte-identical and all eight normal-frame comparisons pass. Two early fixture creators now persist their authored components; only two task-owned profiles were explicitly repaired. These checks do not close the image or performance Open cells above. See the S17 report for identities and evidence.

Final marker retirement (2026-10-23 / Unity 6000.6.0f1 / macOS Metal): the remaining 45 categorized contracts were actually Passed in the final full/category runs. Only their 29 Category attribute lines in nine files were removed. A subsequent cold compile and nine-class recheck pass 64/64 with all 45 exact identities preserved. No test body, assertion or parameter was removed, and image/performance boundaries are unchanged.

## Approved closure acceptance — 2026-10-23

Original InfinityExample, Unity 6000.6.0f1 / CoreRP 17.6 / Shader Graph 17.6 / Apple M3 Max / macOS Metal.
Current Infinity-only EditMode: 625 discovered, started and finished; 625 Passed, zero Failed/Skipped, framework and
callbacks complete. S1 combines its traceable historical related contracts/behavior with this result under the
explicitly approved revised scope. The historical repaired whole-Example run is waived, not represented as executed.
The 53 historical external SDK skips retain their actual reasons outside Infinity acceptance.

Eight normal Game regressions pass against the original 16 byte-identical S16 baseline files. No baseline or tolerance
was changed. Three independent cold Editors pass fresh-RP first Game and +120 accepted-submission stable frames;
necessary six-face Reflection preproduction is explicit. Historical purple/black causes remain unknown, currently not
reproduced. This does not assert every optional image/platform path is verified.

Seven fixed 1280×720 High workloads have valid unique positive CPU/GPU timestamps, 120 accepted warmup frames and
three groups of 300 samples per version. Timing is whole Editor with a fixed camera RenderTexture, without final
backbuffer Present/uGUI. Decal replacement restores missing draws, so its work differs. Same-code observation-state
controls explain the initially repeatable probe timing offset without establishing an OS cause or strict performance
equivalence. No stable regression attributable to the patch was found. See the
[closure report](../Documentation~/Evidence/Verification-0.5-2026-10-19/Closure-20261023/closure-report.md)
for bounded results, original failures, source identities and retained limitations. D3D12/Vulkan, Player, HDR and
unrelated Open cells are unchanged. Ready recommendation applies to the approved closure scope.

## Execution naming convergence — 2026-10-24

Baseline main 2e05bc2. Pass recording and GPU markers now distinguish Raster,
Compute, Copy and Upload. Ten custom LightModes and matching shader names gain
Raster; Unity ShadowCaster/Meta/SRPDefaultUnlit stay unchanged. RTAO marker comes
from the owned URT backend. Shader Graph generation, material routing, mesh pass
lookup, stripping and Example TerrainDependencyControl consumers were updated.
No old-name aliases, asset-load migration, branch, Player build or package build.

Unity 6000.6.0f1, macOS / Metal / Apple M3 Max. Final source recompile completed
with 0 errors / 0 warnings. StaticCheck: 0 errors / 0 warnings; offline tests 11/11.

| Final relevant test group | Passed | Run identity |
| --- | ---: | --- |
| MaterialRouteTests | 18 | a77ca655-be84-4d6f-91a4-e9dc51a73820 |
| MeshPassShaderUtilityTests | 3 | ba55a070-d0ae-4f01-ab96-942d2aad5e71 |
| ShaderStrippingTests | 16 | aae0202a-aa60-46d8-80ff-e5bcc825ad02 |
| ColorOutputGpuTests | 13 | c777177e-9e92-4bed-9cb4-d238cb218d3f |
| ShadowDepthCopyGpuTests | 1 | 88f36863-59e7-4c5a-97a3-abd8ba61dd09 |
| SceneViewOutputTests | 10 | 994203d5-3c15-4adb-b39b-c19736a21fec |
| PipelineSeamTests | 2 | d393b84b-67a2-4c42-b642-daa2738bbc9d |

All 63 selected tests passed with complete framework and callback accounting.
The final MeshPassShaderUtility rerun covers the additional RasterDefaultFullScreen
shader name. Actual generated/imported Lit/Unlit/Decal graphs had 11/1/1 passes,
no shader errors and successful RasterGBufferPass/RasterTranslucentT2Pass/
RasterDBufferPass lookup. Temporary graph assets and asmref verification bridge
were removed and the final source recompiled.

Scene_Spazon normal Game capture: five successful submissions, one DisplayColor
and Depth frame, zero pending readbacks. The inspected image contains the expected
atrium geometry; finite colors. This is a rendering smoke check, not a matched
before/after image regression. Frame Debugger exported 132 actual events, including
RasterDepthPass, RasterGBufferPass, RasterMotionPass, RasterForwardPass and
RasterPresent draws. Native ShadowCaster remains in the shadow route.

Transient Play-only RTAO Volume and RP clone: actual Compute backend, no blocking
reason, 70 acceleration instances; captured Occlusion finite with range [0,1].
Compiled graph records ComputeRayTracedOcclusion as RayTracing, as intended.
One additional scaled frame records RasterResolveDisplayDepth, RasterUIOverlay
and RasterPresent. This proves Overlay recording, not a UI pixel regression.
Cached-shadow copy passed its existing GPU test; a scene-driven cache save/load
cycle was not captured. Hardware RT and non-Metal graphics APIs remain
TODO(UNVERIFIED). Player RTAO shader loading is an existing separate gap.

Original failures remain distinct: initial URT test assembly reference omission
was corrected; three stripping fixture filename failures were fixed by moving
shader/meta together with GUIDs intact, then 16/16 passed. An initial test run
interrupted by script reload was cancelled and excluded. One cold Editor launch
exited before CLI connection; retry succeeded, cause unestablished. An ordinary
sandbox offline-test attempt lacked intermediate write access; the permitted
retry passed 11/11. No Library/DataStore changes were made. Auto-review rejected
a persistent preview image; that extra file was removed, and image inspection
used only task-owned intermediate output.

Cleanup: own Editor 53804 closed gracefully; Play/Frame Debugger off, original RP binding restored, original scene clean. Task intermediate root and task log absent; final deletion reclaimed at least 67826460 payload bytes / 123207680 allocated bytes (earlier partial cleanup not counted). AppleDouble sidecar disappearance required FileNotFound-only cleanup tolerance. No persistent preview or validation bridge remains.


## 2026-10-24 output and transparent attachment convergence

Implemented on `main` at baseline `2e05bc2`, preserving the preceding uncommitted
execution/tag migration. No branch, commit, push or Player build. Unity 6000.6.0f1,
macOS Editor / Metal / Apple M3 Max. This receipt supersedes the earlier output
and reactive-mask verification scope; earlier dated receipts remain historical.

Changed: raster final combination shares grading/exposure/Bloom/vignette/grain
HLSL with intermediate output. Direct output removes the post-combine UAV texture
and the extra final sample/draw. Hardware-sRGB SDR UI draws on the final target
with an explicit camera viewport. Linear consumers retain one intermediate and
an output-only encoding draw. Post bypass aliases read-only input; an overlay
writer first obtains an independent raster copy. Editor depth upsampling is
consumer-driven. Cached-shadow save/restore uses RasterCopyShadowDepthTiles.
ReactiveMask is TAA-only: first transparent color attachment Clear(0), subsequent
stages Load; no clear dispatch, and SR/non-TAA variants omit that MRT.
Post/fog kernels resolve cached names rather than depending on kernel order.
The root task ledger was retired to the single historical
[0.5.0 plan](History/PLAN-0.5.0-completed.md); active tasks live in conversation.

StaticCheck: 0 errors / 0 warnings; offline negative controls 11/11;
`git diff --check` clean. Final source compilation completed without errors.
124 selected tests passed with complete framework/callback accounting:

| Group | Passed | Run identity |
| --- | ---: | --- |
| ColorOutputGpuTests | 13 | 648a7149-694e-47cc-a1a8-550b640f486b |
| OutputConvergenceTests | 17 | 157f70db-61ec-424c-8f61-e3cc74ea3a30 |
| FogTranslucentFeatureTests | 4 | 83cb08bf-1acf-432f-a598-06ed0fe3ac9c |
| TemporalResolveGpuTests | 10 | e48276ed-5c18-4a7c-9bb4-1a2970fb0bbe |
| SceneViewOutputTests | 10 | 2848b6af-a60d-413d-b277-5486d2edddbe |
| MaterialRouteTests | 18 | 09c8475d-8c35-4270-acdd-77d4b8b69045 |
| ShaderStrippingTests | 16 | 13ee89ba-0e2b-497a-98bc-767962ab148f |
| ShadowDepthCopyGpuTests | 1 | f886622e-97f7-422e-bb57-890b5322e70d |
| LocalShadowCacheTests | 19 | dd9272b0-156e-4330-924f-16c5944e39f1 |
| ExposureOutputTests | 16 | f4877225-24f7-42fc-8126-c0bb80560cd0 |

GPU output tests compare direct composition against RGBA16F intermediate plus
encoding for LinearTarget, SDR shader-sRGB, PQ, HLG and scRGB, using a nonuniform
source and active grading/exposure/vignette/grain. Hardware-sRGB UI blending is
compared with linear composition plus encoding. The partial-viewport test checks
12 changed pixels and 36 preserved destination pixels. Existing transfer tests
cover finite outputs and coverage; existing depth-tile GPU tests compare every
pixel, including neighboring tiles and unequal atlas sizes.
Actual generated/imported Lit, Unlit and Decal Shader Graphs found their new
RasterGBufferPass / RasterTranslucentT2Pass / RasterDBufferPass and warmed shader
variants, including reactive output enabled/disabled. This round does not claim
an exhaustive visual acceptance of every graph configuration.

Normal-frame evidence:

- Scene_Spazon / MainCamera, 1666x1756: baseline and final normal captures each
  completed 33 accepted frames (32 warmup + one readback), zero outstanding
  resources and no non-finite DisplayColor values. The inspected candidate image
  contains the expected atrium geometry. Scene animation phases differ across
  Play sessions, so the whole-image difference is not a matched regression gate.
- Actual SDR UI panel drew on direct output. Direct compiled graph has one
  RasterFinalOutput and RasterUIOverlay. An SR direct resource snapshot contains
  imported DisplayBackbuffer but no PostProcessTexture or ReactiveMask texture.
- Empty transparency produced an all-zero ReactiveMask. T1-only produced 0/128;
  T2-only produced 0/191 without T1 residue. T0/T1/T2 together produced
  0/64/128/191 with counts 220794/3331/2896/3379. Motion metadata remained finite.
  SR at 320x180 internal / 640x360 display recorded neither reactive allocation
  nor depth upsampling while overlays were off.
- The same scaled camera with Gizmos on recorded RasterPostCombine,
  RasterUpscaleDepthForEditorOverlays and RasterGizmos; its displayed scene and
  gizmo wires were inspected. Partial camera viewport preserved prior output
  outside the camera region. Equal-size depth aliases the existing attachment.
- Actual post-disabled SceneView captures completed with zero outstanding
  resources: read-only PostProcess capture has no RasterOutputCopy; enabling
  Gizmos adds that copy before the writer. Actual Albedo DebugView snapshot
  records RasterPostCombine -> ComputeDebugView -> RasterFinalOutput.
- Two OnDemand spot tiles: first frame rendered 2 tiles, cache-hit frames rendered
  0; an explicit request for one light rendered exactly 1, subsequent frames 0.
  Compiled resources record RasterCopyShadowDepthTiles. Exact neighboring depth
  preservation is independently covered by the GPU tile test.

Performance: baseline/direct/UI runs each used 32 warmup + 120 recorded frames at
1666x1756. GPU nanoseconds and GPU sample blocks were zero in all runs. CPU/Editor
samples are not GPU measurements and no millisecond speedup is claimed. The
verified direct-path structural saving is one RGBA16F full-size intermediate,
one separate final sample/draw and one post-combine dispatch; TAA also removes
one standalone reactive clear dispatch. At this resolution the removed color
allocation is 23,403,968 bytes and the nominal store+load traffic is 46,807,936
bytes/frame, before compression, tiling and cache effects. UI samples have no
matched pre-change UI baseline, so they establish execution, not a timing delta.

Tool failures are separate from rendering results: initial compile transition
errors were repaired and superseded by final successful compilation/captures.
The first Shader Graph synchronous CLI call exceeded its 5-second wait; a
compiled asynchronous bridge completed all three imports and warmups. A hidden
SceneView capture changed dimensions and failed; a bounded owned SceneView retry
completed both post-disabled paths. Frame Debugger full export failed first
on changing event count and again on unavailable event-0 payload timeout;
partial export is not accepted as a complete frame tree. Execution names and
resource ownership are instead verified by live compiled RenderGraph snapshots.
The fresh final-render log increment (starting at byte 1061611) contains no new
render exceptions, shader errors, invalid kernels or assertions.

Platform boundary: Metal Editor runtime and controlled RenderTextures verified.
D3D12, Vulkan, actual HDR display presentation and hardware DispatchRays remain
TODO(UNVERIFIED). Existing Player RTAO shader loading is an independent known gap;
no Player result is inferred from Editor or URT. These changes do not change
ray-tracing scheduling or atlas layout.

Cleanup: owned Editor PID 62747 closed gracefully. Play, pause and Frame Debugger
were off; original RP binding and clean Scene_Spazon restored, SceneView effects
on / Gizmos off. Temporary graphs, verification bridge and transient Play objects
are absent. All captures/tests/readbacks drained before shutdown. Exact task root
`intermediate/output-convergence/` deleted and absence verified, reclaiming
237,504,085 payload bytes / 312,999,936 allocated bytes. No persistent capture
archive or preview was created. Existing project/user changes were preserved.


## RT capacity, fused TAA and MeshDraw graph convergence — 2026-10-24

Continues main `2e05bc2` and the existing uncommitted naming/output/transparent
attachment changes. No branch, commit, push, serialized user setting, dynamic
resolution controller or Player build. Main Agent owned design, implementation,
review and Unity operations; Luna was used only for bounded source searches.
Conversation Todo tracked baseline, capacity/views/retention, TAA, MeshDraw,
documents and final validation/cleanup; no active PLAN.md was created.

Changed: explicit Internal/Display sizing, per-camera 64-texel capacity buckets,
12.5% growth, three-accepted-request peaks and 30-request/75%-area shrink;
field-equal physical compatibility including MSAA; one shared transient pool
with 400/64 MiB texture/buffer retention watermarks and fence-delayed eviction.
Texture binding metadata and owned screen-sampling helpers preserve logical
coordinates and clamp each mip's valid rectangle. Material sampling stays native.
History reads keep their accepted texture and previous view until a pending
writer commits; failed resized writers cannot replace accepted history. A change
only to physical capacity does not invalidate otherwise valid history.
TAA solves an 8x8 tile plus halo, half-quantizes shared RGB, and writes unsharpened
history/depth and sharpened display in one dispatch. The runtime Sharpen kernel
and two TAA history-copy passes are removed. MeshDraw compaction/upload are
explicit graph producers; disjoint batch ranges prevent later preparation from
overwriting earlier output. The unused CPU immediate-submit entry was removed;
callers submit through RGDrawList. Neutral functional scopes and MeshDraw markers
leave Unity's own batching labels intact.

Unity 6000.6.0f1 / OSXEditor / Metal / Apple M3 Max. Final script recompile:
0 errors / 0 warnings. StaticCheck: 0 errors / 0 warnings. Offline negative and
registry tests: 12/12 passed. Final accepted relevant EditMode/GPU cases:
237 passed, 0 failed, 0 skipped (deduplicated groups below; this is not a full-suite run).

| Group | Passed | Run identity |
|---|---:|---|
| MeshSceneTests | 32 | `8a1026a9-96c7-4825-b35c-2a96bdcf33a9` |
| CompactContractTests | 3 | `e07ee794-8b11-4b97-a244-6cc53ac4e96c` |
| PipelineSeamTests | 2 | `088d9efb-4a73-4da5-bf1c-638da694d5bf` |
| TemporalResolveGpuTests | 14 | `aa6bb33e-ea6a-4569-8403-f2ba4e8f59cd` |
| OutputConvergenceTests | 17 | `d8458a7e-73f1-4e2b-a869-f81b9661d7a2` |
| ColorOutputGpuTests | 13 | `f8fd00fe-4da1-490a-98a6-079a6a7958bf` |
| MaterialRouteTests | 18 | `3cefacb8-3c17-408e-88c8-206ca244ccc6` |
| ShaderStrippingTests | 16 | `d4d8c461-f2fb-415e-af54-f4346b98c8ef` |
| ShadowDepthCopyGpuTests | 1 | `39d7ae64-7299-46e0-865b-c984d507f086` |
| TemporalAntiAliasingTests | 4 | `e0e028d9-9b5f-4a87-8c00-97aa399c1fd3` |
| GBufferContractTests | 11 | `4f5b5679-9df9-4c6e-ac68-9b12c40942ea` |
| VisualContractGpuTests | 12 | `a487e29c-11dc-4fea-a64f-9514dcd0fcb9` |
| ScreenSpaceModeTests | 5 | `7e1fbe5b-92c3-4795-9baf-fa6d6b1a9298` |
| ScreenSpaceDenoiseTests | 6 | `6f19b6a5-a947-44a3-9b0d-eefe62b31d75` |
| StochasticSSGINumericTests | 4 | `4a34a4b8-ae83-4ffe-b81e-efc5b542f035` |
| SSGITemporalResponseGpuTests | 20 | `e89a80b8-4922-4f4c-a86e-41fe984bbb01` |
| StochasticSSRNumericTests | 5 | `bcc6eb4c-a5ae-4ca6-bc40-3ed3f9781dd5` |
| MeshDrawBatchGpuTests | 1 | `d1353b19-02d3-405c-9bfc-34df7d3fff90` |
| TextureCapacityTests | 8 | `f9c54b52-e915-429b-a531-7c3b558f1a63` |
| HistoryCacheCommitTests | 6 | `67cd9328-f6dd-4e52-8c3c-c3e51aea88ca` |
| RenderGraphFailureTests | 6 | `15db7149-9a6f-4da7-ad18-b4db57b10d7c` |
| SceneViewOutputTests | 10 | `d7e4e264-0d07-4592-b589-395d62da3b71` |
| RGDrawListLifetimeTests | 10 | `666f7b5c-71df-4d50-9f4b-18b0c016a9ab` |
| RenderCaptureTests | 13 | `24be5558-3beb-42f6-9c07-16e073ad2367` |

Normal-frame and resource evidence:

- Scene_Spazon, frozen animation/time, fixed MainCamera. Normal 640x360 capture
  completed after 64 successful warmup submissions. Display, accumulation,
  previous color, current/previous depth, ReactiveMask and confidence were finite;
  all readbacks reached terminal retirement. History sample age reached 32.
- Final 115-frame resize sequence covered bucket-local oscillation, 1280x720,
  stable 320x180, portrait 360x640, and return oscillation. Bucket-local frames
  1–19 kept texture allocation count 78; the final 48 frames kept 173.
  Capacity shrank from 1280x768 to 320x192 after the history/hysteresis window.
  Portrait reset to 384x640. Idle eviction count reached 23; pending retirement
  drained without a CPU wait. Counters estimate pooled texture storage only,
  excluding histories, driver overhead and unrelated GPU allocations. They are
  not process VRAM measurements or a hard working-set limit.
- A zero-warmup capture started in the same main-thread invocation as changing
  640x360 to 961x541. Current accumulation/depth/display were 961x541 while the
  captured previous color/depth remained 640x360. Reset was true, confidence age
  was 1, and all buffers were finite. This proves distinct current/previous views
  on an actual resize frame, beyond the rollback unit tests.
- Odd 641x361 output with offset viewport and transient SDR overlay UI completed
  finite color/TAA/reactive captures. SR ran 320x180 Internal to 640x360 Display.
  Temporary RGBA16F camera RenderTexture plus Albedo DebugView produced finite
  output and depth. These controlled tests do not accept a real HDR display.
- Two isolated Preview renders requested 193x129 and 241x173 GUI points; Retina
  targets were 386x258 and 482x346. GPU readbacks were finite. Preview cleanup
  left only Main and the owned SceneView state; Main retained its own capacity.
  Owned SceneView, aligned to the fixed camera, visibly rendered geometry,
  generated graph materials and Gizmos; normal color/depth capture was finite.
  An initial misaligned empty SceneView was excluded from image acceptance.
- Lit, Unlit and Decal graphs were generated and synchronously imported with
  RasterGBufferPass / RasterTranslucentT2Pass / RasterDBufferPass found and no
  shader errors. Actual transient scene draws populated GBuffer/DBuffer and the
  transparent ReactiveMask reached 1. Transparent reactive-on/off variants were
  warmed where declared. Temporary assets and bridge were then removed.
- Live RenderGraph Viewer showed ComputeMeshDrawCompact as a Graphics-queue
  Compute node reading visibility/candidates/metadata and writing indices/counts/
  indirect args before RasterDepth. CPU/no-consumer/shared-list cases are covered
  by compiled graph tests; the GPU two-batch test verifies disjoint args/indices.
  Actual Game snapshot has one ComputeAntiAliasing and no temporal Sharpen or
  TAA color/depth CopyHistory nodes. The two-stage numerical oracle is test-only.
- Poisoned padding/odd mips and point/bilinear/trilinear numeric tests passed.
  Filter coordinate precision is bounded at 1/512; fractional mip selection uses
  an independent logical filter oracle because native Metal LOD quantization
  need not be bit-identical. Fused temporal comparisons allow half-storage ULPs.

Performance: baseline, candidate and UI candidate each used 32 warmup plus 120
samples at 640x360. GPU nanoseconds and GPU block counts were zero throughout;
no millisecond GPU speedup is claimed and CPU samples are not substituted.
Verified changes remove one sharpen dispatch and two TAA history transfers, and
stabilize pooled allocation during bucket-local resize. Explicit MeshDraw nodes
make existing work visible; their extra graph nodes are not extra compaction.
Halo solving adds arithmetic, so its net GPU benefit remains unmeasured.

Superseded failures are distinct from acceptance: the first async-queue fence
could not be queried on this Metal device and was replaced by CPU-synchronization
fences; initial mip/native-reference assumptions were corrected with an independent
filter oracle; direct-output unit fixtures needed explicit exact-view metadata;
initial temporary Shader Graph keyword and CLI helper calls were corrected.
The last history audit repaired resize read replacement and added tests plus a
fresh real resize capture. Final rendering log from byte 14806752 has zero new render
exceptions, shader errors, invalid kernels or assertions before restoration.

Platform boundaries: Metal Editor and controlled RTs verified. D3D12, Vulkan,
actual HDR display and hardware DispatchRays remain TODO(UNVERIFIED). Retention
eviction requires supportsGraphicsFence; other backends retain entries until
teardown and are not claimed to satisfy the bounded-retention runtime gate.
Existing independent Player RTAO shader loading remains out of scope. The current
normal-frame fixture has no new claim of exhaustive optional-feature, multi-camera
compositing or all-platform image equivalence.


Cleanup completed: owned Editor PID 78645 exited gracefully. Original clean
Scene_Spazon, source RP binding and 1666x1756 camera viewport were restored;
Play, pause and Frame Debugger were off, and the temporary camera target was
null. All capture requests were Completed with zero outstanding readbacks;
no tests or sampling callbacks remained active. The task-owned graphs, bridge,
SceneView, transient objects and Python cache are absent. Exact task directory
`intermediate/rt-taa-mesh/` and its sibling OS sidecar were deleted and absence
verified: 140,735,307 payload bytes / 338,165,760 allocated bytes reclaimed.
Only this receipt and source/tests/design documentation remain. Earlier user
changes are preserved; HEAD remains main 2e05bc2.

Shutdown tooling note: after Mono/UDS cleanup the Unity Pipeline HTTP server
logged one ObjectDisposedException while replying to its own quit request.
The CLI independently confirmed graceful process exit. This happened during
server disposal after rendering stopped; it is not a render/capture failure.

### Mesh profiling domains and hierarchy — 2026-10-24

Baseline: clean `main / 590f470`. This change only adjusts Mesh profiler names,
their internal view context and consumers. Shader contracts, algorithms, graph
dependencies, queues and resource lifetimes are unchanged. No branch, commit,
push, Player build or system shutdown was performed.

- `MeshDrawProfiling` owns seven cached Compute/Upload samplers and four Draw
  names. `EMeshViewKind` selects RenderLoop versus Shadows; Timings uses the same
  sampler instances. RendererList submits directly without an extra wrapper.
- Static gate: 0 errors / 0 warnings; all 12 static-check regression/negative
  tests passed. Unity 6000.6.0f1 script compilation: 0 errors / 0 warnings.
- EditMode: 23 passed, 0 failed, 0 skipped. MeshDrawProfilingTests 7
  (`389f1380-9b61-45ff-b21b-1bda437c8d52`), RGDrawListLifetimeTests 11
  (`70cd9bba-ef20-4720-8793-408939e1ca64`), CompactContractTests 3
  (`4962cc6a-7f3f-4aa9-b325-6de02b90dd74`), PipelineSeamTests 2
  (`10f3f408-f7f4-4b60-8c3c-152782c8c284`). Coverage includes Auto, CPU/GPU,
  shared/culled DrawLists, view-domain names, cached identity and submit seams.
- Original Example was occupied by another session. All CLI operations targeted
  a task-owned minimal host under `intermediate/mesh-marker/run01/host`, PID
  13241 / port 7801, using the working package and a disposable MarkerScene.
  Native and Mesh cubes, ground, directional cascades and a local spot shadow
  provided real geometry. No original scene, camera or Editor was operated.
- Metal / Apple M3 Max: Auto, CPU Direct and GPU Indirect each completed one
  640x360 normal color/depth capture after eight warmup frames. All six readbacks
  were finite, with zero outstanding requests. A normal-frame color preview
  visibly showed both colored cubes, ground and shadows. CPU compiled graph had
  zero Mesh culling/compaction nodes; GPU/Auto kept preparation before Raster.
- GPU Frame Debugger export completed: 62/62 event payloads ready. Depth,
  GBuffer and ObjectMotion contain RenderLoop.DrawMeshIndirect beside the native
  RenderLoop.DrawSRPBatcher. Four CascadeSlice regions and LocalShadowSlice0
  contain Shadows.DrawMeshIndirect beside Shadows.DrawSRPBatcher. Each owned
  indirect event reports one draw call; Unity reports zero vertex/instance
  counts for these indirect events, so those counters are not an instance proof.
  ClearCounts / CompactIndices / BuildIndirectArgs remain under the view-domain
  compaction node, outside Raster. No RendererList wrapper remains.
- CPU Frame Debugger topology: 32 events with the corresponding Direct names,
  native siblings and preserved shadow regions. The first Mesh depth event had
  a ready payload (24 vertices, one instance, one draw). Full payload export
  timed out twice on native SRPBatcher event 1; later payloads remain unverified,
  rather than being accepted from their placeholder zero counters. Normal CPU
  rendering and graph tests passed independently. Export restoration reported
  no cleanup error; no further retries or exporter changes were made.

The controlled host used Gamma color space; this is a marker/hierarchy smoke
test, not acceptance of the original Scene_Spazon image or Linear/HDR output.
D3D12 and Vulkan remain TODO(UNVERIFIED). No GPU performance benefit is claimed.
Cold shader import emitted existing HLSL uninitialized-value warnings, with no
shader errors. The marked rendering log had no invalid kernels or assertions;
its two exceptions were the explicitly reported CPU debugger payload timeouts.
Initial host/test setup errors were corrected before the accepted runs above;
the static negative tests were rerun with task-directory write permission.

Cleanup: backend override cleared, time scale restored, Play and pause off;
capture Completed with zero outstanding readbacks and debugger export inactive.
Owned Editor PID 13241 exited gracefully. The exact `intermediate/mesh-marker/`
directory and sibling OS sidecar were deleted and absence verified, reclaiming
320,207,846 payload bytes / 1,275,068,416 allocated bytes (including the disposable
host Library). Original user scenes/settings were never modified. Unity emitted
thread-finalization / readonly-database shutdown messages after stopping; CLI
independently confirmed process exit. Only source, tests and this receipt remain.

### Mesh preparation sampler unification — 2026-10-24 follow-up

This follow-up supersedes the seven preparation samplers in the preceding receipt.
There are exactly three cached preparation samplers: `ComputeMeshCulling`,
`ComputeMeshCompaction` and `UploadMeshData`. Main/cascade/local views share
sampler identity while retaining separate graph nodes and parent scopes. Only
Draw names retain RenderLoop / Shadows domains. Obsolete view parameters and
domain-specific preparation samplers are removed; current rules and consumers
are updated. Existing earlier working-tree changes are preserved.

- Static gate: 0 errors / 0 warnings; 12 static regression/negative tests passed.
  Unity 6000.6.0f1 recompile reported up-to-date, 0 errors / 0 warnings.
- EditMode: 23 passed, 0 failed, 0 skipped. Profiling 7
  (`8742c443-aef1-404a-af82-e0efa9d6f234`), lifecycle 11
  (`7f1d4734-e4b5-4b95-a772-e8a3c70c3ecb`), compact 3
  (`6213a1a7-b5c9-4467-b6f2-a2355f7bb684`), seam 2
  (`66c5717a-42dd-4fdb-99cc-b56e5339e7d1`). The main/cascade/local culling test
  verifies three distinct nodes with the same name; shared/dead DrawList and
  CPU/GPU/Auto preparation tests remain passing.
- Metal / M3 Max: one normal GPU Indirect Scene_Spazon MainCamera frame at
  640x360 after eight warmup frames completed, with finite color/depth readbacks
  and zero outstanding requests. Main Agent inspected its normal color preview.
  The actual compiled graph contains five ComputeMeshCulling, seven
  ComputeMeshCompaction and seven UploadMeshData nodes; no RenderLoop/Shadows
  preparation prefixes remain. No new rendering exceptions, shader errors,
  invalid kernels or assertions occurred after the pre-Play log mark.
- No new Frame Debugger payload export or performance benchmark was performed;
  the preceding export limitation remains explicit. D3D12/Vulkan remain
  TODO(UNVERIFIED). No performance improvement is claimed from renaming.

Validation used a task-owned Editor PID 51500 on the idle original project.
The first CLI-launched process exited before readiness; the managed retry
provided all accepted results. An unsupported combined test filter matched zero
tests and was superseded by the four passing class runs. Capture setup requests
missing fixture/ROI were rejected before starting and corrected explicitly.

Cleanup verified: Play off, time scale restored, clean Scene_Spazon still active,
MainCamera back to 1666x1756, normal capture Completed with zero outstanding
readbacks. Owned Editor PID 51500 exited gracefully. The task directory
`intermediate/mesh-sampler-unify/` and its sibling OS sidecar are absent;
3,716,994 payload bytes / 23,068,672 allocated bytes were reclaimed. No source
scene/settings save, branch, commit, push or shutdown was performed.

### Metal Shader defined outputs — 2026-10-24

The reported SSGI LoadSurface, TAA depth conversion, SSR reprojection, texture UV
and sphere/disk illuminance diagnostics were reproduced on Metal. The original
six compute shaders additionally reported the same diagnostic on directly
coupled trace, atmosphere, light, shadow and probe helpers (47 flagged symbols,
including out parameters). Actual consumption later exposed ContactShadowFromMask
and the sky's GGX prefilter; these are included in the repair.

Helpers now initialize meaningful results/out values before guarded work and
converge to one return. Ray traversal retains its original order and formulas,
using found state and break for termination. Rejected surfaces preserve raw
depth; their geometry stays zero. Shared SSGI surface loading lives in one HLSL
include used by production and focused GPU tests. The two-dispatch TAA oracle
remains test-only. No warning suppression, compiler-backend switch, feature
disable, resource fallback, queue change or serialized schema change was added.

Verification:

- Static gate: 0 errors / 0 warnings; 12 static regression/negative tests passed.
  Unity 6000.6.0f1 script recompile: up-to-date, 0 errors / 0 warnings.
- DXC: 41 kernel/variant combinations passed across the six affected compute
  shaders, CubeFilter, the new contract fixture and the temporal reference.
  This is an offline syntax/binding gate, not D3D12 hardware acceptance.
- Metal / Apple M3 Max: 86 unique EditMode tests passed, 0 failed / skipped.
  ShaderInitializationGpuTests 14 (`8f4ceca7-4abf-40d9-801a-cdcabc7ccf89`),
  StochasticSSGINumericTests 4 (`a163afb3-02d1-4f95-83e9-1c6e5061749e`),
  StochasticSSRNumericTests 5 (`5e671fba-ab56-4966-a792-80f54f3cfcf6`),
  TemporalResolveGpuTests 14 (`c96e4c8d-3991-4d22-a77b-8a5561e50aa8`),
  TextureCapacityTests 8 (`06c80b91-d5d9-4b35-ba19-41cff408fa59`),
  LightShapeGpuTests 3 (`c40870eb-92ea-40b6-9322-ad450ef94edd`),
  ShadowSamplingGpuTests 16 (`4bac9227-139e-4008-8ed8-b2f22601fa6a`),
  CubemapArraySamplingGpuTests 2 (`3e099006-cf4e-4f57-9afc-8dcb6e1ff589`),
  SSGITemporalResponseGpuTests 20 (`5d05b323-dc87-4fbd-802e-1255480cabee`).
- New contracts cover perspective/orthographic and both depth directions,
  each rejected projection, horizon-clipped sphere/disk illuminance, rejected
  surface metadata, Hi-Z/linear hits and misses, zero-length rejection, and
  contact-mask selection. The CPU-double irradiance oracle allows max(2e-6,
  2e-4 * expected) for Metal transcendental approximations; formulas are unchanged.
- Six bounded 320x180 normal frames completed in an owned linear-color fixture.
  Four covered actual Deferred AO/contact keyword combinations; two repeated
  the contact combinations after the final helper repair. All color/depth/SSR/
  SSGI readbacks were finite and drained. Final SSR RGB max was 0.540527 and SSGI
  RGB max 0.102478, with 4173 / 7726 nonzero RGB pixels respectively. Actual graphs
  contained deferred, atmosphere composite, opaque/color pyramids, SSR, SSGI and
  TAA producers; Main ran normally and MainDebug ran in the temporal GPU tests.
- After final dispatch, public ShaderUtil diagnostics reported zero uninitialized
  warnings and zero errors on all six targets plus CubeFilter. The final marked
  rendering log had zero new uninitialized warnings, shader errors, exceptions,
  invalid kernels or assertions. Existing Deferred X4714/register-pressure and
  signed-divide/modulus advisories were also present in the original baseline;
  GTAO retains its unrelated truncation/pow diagnostics. These are not accepted
  as a generic warning-free pipeline. No GPU performance benefit is claimed.

Main Agent inspected the normal color preview: geometry, reflections and lit
ground were visible. The fixture also shows the already-listed horizon dark band;
this task neither repairs that independent image issue nor establishes a matched
Scene_Spazon image baseline. D3D12/Vulkan remain TODO(UNVERIFIED); no Player built.

The original project Editor was occupied, so only disposable hosts were operated.
The first host required uGUI/TMP for existing tools; package Search indexing then
stalled the host main thread (confirmed by a bounded sample). A fresh host with
package indexing disabled provided accepted results. Early host startup failures,
fixture type/pragma import failures, oracle tolerance failures and non-idle test
requests are superseded by the final runs, rather than counted as acceptance.
The user confirmed concurrent default-resource serialization changes belong to
another session; those assets were not reverted or included in this repair.

Cleanup verified: all six capture sessions were Completed with zero outstanding
requests; every submitted buffer readback was terminal and retired. The owned
host's command channel was lost during its final reload/restore, so clean
in-Editor restoration was not claimed. Graceful quit was unreachable; CLI closed
only owned PID 60527 with its explicit project path and --force. No owned Unity
producer remained. The exact `intermediate/metal-init/` tree and OS sidecar are
absent, reclaiming 349,086,031 payload bytes / 1,557,659,648 allocated bytes.
The Test Framework's forced DefaultCompany/host/TestResults.xml mirror was
identified as this task's final 14-case run and removed. Original user scenes
were never loaded; disposable host assets/settings were deleted with the host.
No branch, commit, push, Player build or system shutdown was performed.


## InfinityExample full-chain Metal warning closure — 2026-10-24

This supersedes the earlier six-target/320x180 fixture-only warning verdict for
current Example acceptance. Naming, routing, serialized settings and the user's
existing uncommitted resources are preserved. Main Agent used the explicitly
authorized open Editor, PID 53421: Unity 6000.6.0f1 / Metal / Apple M3 Max,
Scene_Spazon, MainCamera 1666x1756, Quality 0,
Assets/Profile/RenderPipelineAsset.asset.

The initial eight loaded compute assets exposed 34 diagnostic entries. The
broader raster inventory exposed another 11 entries. Repairs:

| Diagnostic | Repair |
| --- | --- |
| Cookie SelectChannels; VSM/EVSM moment outputs | Initialized result, guarded assignment, one return; channel/warp formulas preserved. |
| Cloud scene limit, sphere roots, shell intervals and LightMarch | Explicit result/control flow; stable quadratic roots, interval ordering, ground rejection and Beer/powder integration retained. |
| Volumetric shadow receiver divide-by-zero | Defined reciprocal length for a degenerate tangent; zero-normal/degenerate projected planes keep zero gradient. |
| GTAO truncation and negative pow domain | Explicit motion `.xy` and AO `.r`; saturate the physical AO base before pow, without abs. |
| Cascade signed modulus/division | Unsigned slice bit operations retain the 2x2 atlas layout. |
| Deferred X4714 | 8x8 shading workgroup; named kernel and queried group dimensions frozen at recording; clustered tiles remain 16x16. |
| Deprecated LUT pragma; signed fixture comparison | Current debug-symbol pragma and explicit unsigned case bound. |
| Probe outputs; Terrain matrix macro redefinitions | Initialized probe-local outputs and one return retain APV/LPPV/SH precedence; ShaderVariables is the sole previous-matrix macro owner. |

Validation completed:

- Static gate: 0 errors / 0 warnings; 12 static regression/negative tests passed.
  Unity script compilation: 0 errors / 0 warnings. DXC was not rerun in this
  round; Metal production compilation/execution is the platform evidence.
- 85 unique EditMode tests passed, zero failed/skipped in their final runs:
  ShadowSamplingGpuTests 17, LightCookieProductionGpuTests 6,
  DeferredLightLoopGpuTests 13, CloudShellGpuTests 3,
  ShadowMomentProductionGpuTests 13, GBufferContractTests 11,
  ShaderInitializationGpuTests 14, BakedGITests 8.
  Test receipts respectively: `7892c360-865b-4b82-972c-1b72e5a9af55`,
  `5ae11caf-7a48-41d3-b5d9-a7b881b62111`,
  `85361a50-b5da-40c6-b872-4fbcbd67ef3b`,
  `79491c60-ea66-4aa5-be99-ffeb99553be1`,
  `676133c7-f89f-4a79-a012-1c4da4bb470f`,
  `398a08e7-e69f-4b8b-a05e-5a11bd2fa47f`,
  `c2bd5cdb-0236-4b2c-8895-214abf4f8b1e`,
  `cfa0c855-e758-4b49-bcab-ea4a2cc426d0`.
- Original full Scene_Spazon before/after normal frames used the original
  resolution, camera and quality. Depth, GBuffer, motion, HiZ, SSR, SSGI,
  Deferred, TAA, both color pyramids, post-processing and display all reported
  requested/supported/produced. DisplayColor, depth, SSR and SSGI readbacks
  completed with zero nonfinite values; actual geometry/materials, illumination,
  shadows and output were visible in Main Agent's preview inspection.
- A runtime-only additive supplement enabled GTAO, contact shadows, cloud/fog,
  2D/cube cookies and local VSM/EVSM lights while the original full chain stayed
  active. All those feature states reported produced. The accepted fog history
  was read through HistoryCache: logical 209x220x64, capacity 216x224x64;
  all effective texels were finite (11,770,880 nonzero components).
- MainCamera is below the cloud model's ground. A separate temporary 1666x1756
  camera at altitude 1000 exercised nonempty cloud integration in the loaded
  original world: 3179 cloud pixels, transmittance minimum 0.000262737,
  RGB maximum 88.125; cloud/display/depth readbacks were finite. The original
  camera transform was not moved.
- After production execution, public ShaderUtil reported zero warnings/errors
  across 27 distinct loaded compute assets and 26 loaded package/Example raster
  shaders. No Shader Graph was used in the current scene/inventory, so this is
  not an all-Graph/all-variant acceptance claim. The final live log window
  (PlayerLoop frame 5091 through 7441) contained zero new shader warnings/errors,
  uninitialized/divide-by-zero messages, invalid kernels, assertions or exceptions.
- The before/after display component mean absolute difference was 0.001939.
  These are visual/finite-value regression frames, not a bit-exact oracle:
  temporal phases and initial game times (0.02 versus 0 seconds) differ. The
  production numeric tests provide the branch/numeric contracts. No GPU timing
  or performance gain is claimed.

Superseded attempts are not passes: two initial Deferred fixture cases could
not create a readable UInt Texture2D on Metal; the final fixture uses the
production UInt RenderTexture layout and all four AO/contact combinations pass.
An invalid fog capture semantic and an additive-camera scene selector were
rejected before capture. The first cloud request timed out because background
Game View repaints did not advance the PlayerLoop; after the authorized Editor
was focused, actual frame progression and nonempty production cloud execution
were verified. Unity's unrelated Curl certificate error is infrastructure noise,
not counted as shader acceptance or hidden by a warning filter.

Platform limits: Metal current-chain acceptance only. D3D12/Vulkan and real HDR
displays remain TODO(UNVERIFIED). RTAO is disabled on the original RP Asset;
its existing Player-loading gap remains independent. APV/LPPV data-driven image
acceptance and unused Shader Graph variants are not established by this run.
No Player build, branch, commit, push or shutdown was performed.

Restoration verified: stopped/unpaused, timeScale 1, Frame Debugger off, sole
Scene_Spazon clean, original MainCamera position/rotation and 1666x1756,
Quality 0 and original RP Asset. User Editor stays open. All 642 baseline
source asset/ProjectSettings/resource hashes match; no source asset was saved.
Four successful single-frame capture groups used 292,549,600 raw bytes; total
intermediate data was under 283 MiB. Task data and owned forced test-result
copies are removed only after producer/readback drain; final cleanup is recorded
below after checking their absence.

Cleanup complete: tests inactive; capture finished with zero outstanding requests;
all owned transient objects absent. The project task directory
`intermediate/example-warning-closure` and the byte-identical framework copy
`~/Library/Application Support/Infinity/MeshBatch/TestResults.xml` were removed
and checked absent. An unrelated DefaultCompany test result did not match any
task receipt and was preserved. DataDisk AppleDouble removal raced with directory
cleanup; only already-missing sidecars were tolerated, followed by an explicit
absence check. No Library/ShaderCache cleanup occurred.


## Visibility / shared history / effect-chain upgrade — 2026-10-25

Implemented and exercised on Unity 6000.6.0f1 / Metal / Apple M3 Max in one
independent task-owned mirror of the complete Scene_Spazon, Quality 0,
MainCamera 1666x1756. The source checkout stayed on main / 2485580 with the
candidate uncommitted. See [delivery receipt](History/Visibility-20261025/README.md)
and its compact machine-readable evidence; historical sections above describe
older implementations and do not override this section.

- RenderGraph handles RAW/WAR/WAW, physical import aliases and queue-aware reuse.
  ResourcePool owns recording leases, individual pending submission epochs,
  submitted completion and uncertain outcomes. CPU uploads acquire writable
  storage or publish independent replacements; same-queue ordered reuse remains.
- Mesh transforms use scene-owned immutable matrix/generation versions. Views
  retain accepted/pending references; 64-slot dirty patches and GPU copy/patch
  preserve sparse-view history without per-camera pose arrays. Native renderers
  retain Unity's own motion history.
- Closest/Furthest half-base R32 HiZ, early Furthest and conservative two-stage
  GPU visibility are integrated. The native-occluder recovery control observed
  first/deferred/recovered/final visibility 0/1/1/1 and real recovered depth.
- SSR alone keeps full Resolve/Temporal/Spatial/history, with optional half Trace.
  SSGI has no Resolve: half Trace/Temporal/world-space Spatial/history reconstruct
  at full resolution. Ultra uses full work resolution; Custom keeps HalfResolution.
  Both use accepted unsharpened TAA/SR color mips and previous-object depth metadata.
  SSGI reconstruction respects all four rotating quad phases and odd last intervals.
- GTAO accumulates linear visibility with normal/depth/age; artistic shaping is
  applied after reconstruction. SSGI retains filtered recurrent history; SSR
  retains pre-spatial history.
- Half-resolution Burley SSS filters a separate optional diffuse attachment and
  recomposes main minus original diffuse plus filtered diffuse. Specular/emission
  stay unscattered. Material profile indices own diffusion parameters; Volume
  activation no longer overrides all surfaces with profile zero.
- AO/contact and previous-color effects precede independent shadow rasterization.
  Graphics history copies follow that window. Early recovery, late SSS, TAA/SR
  and post stay on Graphics where no independent raster window exists.

Verification receipts include 12 resource intervals, four actual Metal delayed/
incomplete submission cases, six submission outcomes, 45 RG passes plus one
hardware-async skip, 92 Mesh passes, nine dual-HiZ cases, 13 shared temporal-color
cases, four SR history cases, 18 GTAO cases, 16 stochastic SSR/SSGI cases,
21 SSGI response cases, nine SSS GPU cases, 11 camera history cases, 22 quality
cases, 13 Deferred GPU cases and five feature-registry cases. Counts describe
individual selected batches, not a deduplicated whole-repository test total.

Final original-settings capture at game frame 9533 completed 32 warmup submissions
plus one frame, with zero outstanding readbacks and zero nonfinite values across
12 buffers. SSR Resolve/Temporal/meta/output were 1666x1756; SSGI Trace/Temporal/
meta/hit distance were 833x878 and final output was 1666x1756. Previous color was
accepted UnsharpenedTAA. Main Agent inspected the presented image. A separate
runtime-only supplement retained SSR/SSGI/TAA while producing GTAO, SSS, cloud,
fog, Cookie, EVSM/contact and generated Lit/Unlit/Decal Graph draws. Eight output
readbacks were finite. Cloud output was neutral in this camera: visible-cloud
quality is not accepted by that result.

Generated Lit/Unlit/Decal Graph assets imported and their 13 passes plus the Lit
Forward SSS variant compiled on Metal. Final public ShaderUtil diagnostics were
empty for 26 loaded package ComputeShaders and 28 package/generated Shaders;
the restarted candidate rendering log had no shader errors/warnings, invalid
kernels, unbound properties or assertions. A pre-existing cold C# compile warning
in the LPPV validation-scene fixture remains separately recorded (CS0618).

Matched baseline/candidate static, dynamic and two-camera cases each used 32
warmup plus 120 accepted main-camera submissions, with animation frozen and the
same fixed camera/effects. Allocations stayed constant within every sampling
window. Static candidate transform productions/uploads stayed at 1/640 total;
dynamic single/two-camera cases each produced 119 versions and 85,680 patch bytes
across 119 sample intervals. Per-camera pose arrays/uploads were removed. This
small scene has nine transform slots: dirty-page patch uploads can cost more than
a single old per-view matrix upload, so no universal bandwidth reduction is
claimed. Estimated pooled texture bytes rose from 522,615,920 to 558,742,496 in
the main-view case; the extra HiZ, geometric metadata and history mip work is not
reported as memory savings. These are format estimates, not driver residency.
GPU timing was unavailable/zero; no acceleration claim is made.

**Resource-pool whole lifecycle remains PARTIAL.** This host reports
supportsAsyncCompute=false. Hardware cross-queue execution/overlap and real native
Submit-failure recovery remain TODO(UNVERIFIED), even though Metal delayed,
cross-camera, cancelled and submitted-but-incomplete tests passed. Unknown native
submission outcomes stay quarantined. D3D12, Vulkan, real HDR display and unrun RT
backends remain TODO(UNVERIFIED); the existing independent Player RTAO loading gap
is unchanged. Arbitrary-scene quality and a high-sample reference sweep are not
implied by these bounded normal frames and numeric controls.

Restoration and final cleanup evidence is maintained in the delivery receipt.
The 642 protected source files matched the initial hashes except the explicitly
removed SSGI ResolveSamples/ResolveRadius entries in the package default Profile.
No source scene/material/LUT was saved or overwritten for validation.

## Main-thread / GC / memory / SSGI binning / native HDR — 2026-10-25

The [delivery receipt](History/MainThread-20261025/Report.md) and
[structured evidence](History/MainThread-20261025/Evidence.json) record the latest
full Scene_Spazon comparison on a single owned Metal Editor. The source scene,
1666x1756 main camera, Quality 0 and enabled SSR/SSGI/TAA/Deferred/post chain were
preserved. Generated Lit/Unlit/Decal Graphs actually drew; runtime-only supplements
produced SSS, fog/cloud, Cookie/EVSM and DBuffer data. Main cloud RGB was empty;
a cloud-layer camera separately produced nonempty finite RGB, and finite fog
history contained nonzero scattering. These controls do not accept all content.

- Final selected batches: 458 passed executions, zero failed, one hardware Async
  skip; 443 distinct passing names and one distinct skipped name. The latest
  Mesh batch has 96 cases. Final import/render diagnostics were empty for 27
  loaded package ComputeShaders and 15 Shaders/Graphs. Static gate and 12 negative
  checks passed. No Library/ShaderCache deletion was used.
- Calibrated native GC.Alloc counters replaced an invalid Mono byte counter.
  Fixed-size, prewarmed normal library Proxy scopes recorded zero allocations
  in stationary and moving 1k/10k/50k windows. Complete Render still recorded four
  Unity-native Submit GUILayout allocations per frame; these are reported rather
  than subtracted from the total. One earlier nonzero CPU window was repeated
  successfully and remains documented. Active RTAO, setup/growth and diagnostics
  are outside this zero-GC acceptance.
- At 10k, Proxy p50 fell from approximately 64 ms stationary / 190–194 ms fully
  moving to 0.10 ms / 21–22 ms. Fully moving 50k still costs about 105 ms Proxy CPU.
  These are CPU measurements; no GPU speedup is claimed. Static scenes upload
  zero transform/attribute bytes; existing safe attribute version uploads remain
  measurable during motion and are not claimed as a bandwidth optimization.
- Pool texture estimates are 497.421 MiB versus 532.858 MiB at this round's
  `a4f016c` baseline and 498.405 MiB at matched `2485580`. Pool+history+other created
  texture estimates fell 63.985 MiB against `2485580`; this is not an ownership
  transfer or driver-residency measurement. Full physical ledgers are retained.
- Native internal-XDR HDR was available and active with no transition pending:
  linear Rec.2020, R16G16B16A16_SFloat, 160-nit paper white. The actual Metal color
  attachment was RGBA16Float. UI steps, primaries/reference-white GPU controls,
  exposure and SDR restoration passed. Absolute photometric accuracy, other HDR
  encodings/displays, Player, D3D12 and Vulkan remain unverified.

**Resource-pool whole lifecycle remains PARTIAL.** This host has no exposed Async
Compute queue. Metal delayed/cancelled/incomplete/cross-camera tests pass, but
actual hardware cross-queue execution and native driver Submit-failure recovery
remain unverified. The independent Player RTAO loading gap and unexecuted RT
backends are unchanged. Cleanup/restoration proof belongs to the receipt; it does
not broaden these platform results.
