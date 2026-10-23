# Feature status

This is the only document that states how far each feature is verified. Other documents describe design and use;
they link here instead of claiming "completed".

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
| Mesh Drawing Pipeline (CPU + GPU backends) | Yes | Yes | Open | Open | macOS Metal | No HZB occlusion, GPU sort or GPU LOD |
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
| SSR (stochastic, 0.5.0) | Yes | 2026-10-23 Unity 6000.6.0f1 / macOS Metal (5 production GPU cases; quality presets) | Open | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Shared-core numeric regression rerun on S14 and main; no additional SSR image or isolated performance acceptance inferred. |
| SSGI (on the stochastic SSR core, 0.5.0) | Yes | 2026-10-23 Unity 6000.6.0f1 / macOS Metal (4 production numeric + 20 temporal/spatial cases; quality presets) | 2026-10-23 Unity 6000.6.0f1 / macOS Metal (bounded red bounce, offscreen fallback, normal orbit/disocclusion, history and native dispatch controls) | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Original filtering retained. Small numeric residuals may last about 14 frames; sampled normal output accepted without a zero-noise/zero-ghosting claim. Whole-Editor timing does not isolate SSGI cost. See S14 report. |
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
| Present (transfer encoding) | Yes | Yes (GPU gray card) | Accepted 2026-09-13 | Open | macOS Metal | SDR sRGB; HDR output unverified |
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
