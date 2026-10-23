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
