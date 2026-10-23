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
| ThinGBuffer + Deferred shading | Yes | Yes (pack contract) | Failing | Open | macOS Metal | Validation scenes show sky bands, dark triangles and cube blotches |
| Forward (forward-route materials) | Yes | Partial | Open | Open | macOS Metal | |
| Light shapes and record v2 (0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: shape GPU 3, record contracts, actual Deferred weight GPU | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: all shapes, glossy/rough, radius, backface, barn-door and term controls | Open | macOS Metal | Karis / Frostbite approximation. Fixed-input RGBA32F weight equations pass; strict normal RG16F half-ULP decomposition remains unproved. |
| Lightmapper delegate (0.5.0) | Editor bakes | No | Open | — | None | |
| Light cookies + IES atlas (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (5 allocator/state + 11 transaction tests) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (Editor whole-frame, 3×300 before/after) | macOS Metal | Normal-frame consumers, real IES prefab import, reimport and camera/queue recovery verified; timing is not isolated pass cost or causal speedup. Native Point clears a 2D cookie; the retained-invalid-reference Inspector warning is not reachable through that setter. See S11 report. |
| Clustered light list v2 (log z-bins + tile bitmasks, 0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: binning 6, actual Deferred GPU 8; exact-index probe | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: point-light Deferred/Forward, inside/behind/far, multi-camera; S6-matched fog/cloud | Open | macOS Metal | Caller-owned out state; matched compute projection. Other graphics APIs and performance remain unverified. |
| Cascade + local shadows (1–4 cascades with blending; tent PCF, PCSS, VSM, EVSM; every light shape, 0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (allocation, filtering, GPU moments, cache transactions, Shadowmask filtering) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal | Open | macOS Metal | Normal frames and native atlas/dispatch/Inspector evidence; historical S9 requires the existing S12 matrix-alias repair. Small EVSM half residual remains reported. Shadowmask filtering accepted; bounded S12 GI/APV fixtures verified separately. |
| Screen-space traced (contact) shadows: sun + 24 local lights (0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: allocation 2/2; capture 13/13 | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: normal sun/local/Forward, R8/R32 outputs, 30-light allocation, off and missing-producer controls | Open | macOS Metal | Single visible-depth approximation; thin-caster positive controls and thick-caster misses retained. Low/Ultra native step counts verified; no quality or performance improvement inferred from different global RNG frames. |
| Reflection probes in the clustered list + probe atlas (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (17 layout/priority/transaction tests; actual GPU filter contracts) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal: normal baked/custom/realtime, blend/priority/projection, matched Deferred/Forward, same-frame failure and reimport controls | Open | macOS Metal | Axis-aligned; spherical influence uses an axis-aligned radius cube for projection. Historical S12/S13 sky frames match exactly; final main has a separate Lighting difference under investigation. See S13 report. |
| Shader Graph target: Lit / Unlit / Decal (0.5.0) | Graph import | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: generated graphs and contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: bounded Lit/Unlit/Decal, Forward, translucent and baked consumers | Open | macOS Metal | Actual Lit/Unlit/Decal imports and separately inspected generated variants; Meta/emission and normal-frame controls. Historical transient black outputs recovered but root cause remains unknown; final Display residuals are retained. Scene Depth outside decals and Fog node return defaults. See S15 report. |
| Atmosphere LUTs + sky IBL | Yes | Yes (parameters, cache) | Open | Open | macOS Metal | |
| GTAO | Yes | Yes (numeric) | Open | Open | macOS Metal | |
| RTAO (UnifiedRayTracing) | When URT is ready | Partial | Open | Open | Editor only | Player shader load unverified |
| SSR (stochastic, 0.5.0) | Yes | 2026-10-23 Unity 6000.6.0f1 / macOS Metal (5 production GPU cases; quality presets) | Open | Open | macOS Metal | Shared-core numeric regression rerun on S14 and main; no additional SSR image or isolated performance acceptance inferred. |
| SSGI (on the stochastic SSR core, 0.5.0) | Yes | 2026-10-23 Unity 6000.6.0f1 / macOS Metal (4 production numeric + 20 temporal/spatial cases; quality presets) | 2026-10-23 Unity 6000.6.0f1 / macOS Metal (bounded red bounce, offscreen fallback, normal orbit/disocclusion, history and native dispatch controls) | Open | macOS Metal | Original filtering retained. Small numeric residuals may last about 14 frames; sampled normal output accepted without a zero-noise/zero-ghosting claim. Whole-Editor timing does not isolate SSGI cost. See S14 report. |
| Burley SSS | Yes | No | Open | Open | macOS Metal | |
| Volumetric fog / cloud | Yes | Partial | Open | Open | macOS Metal | |
| Translucent T0 / T1 / T2 | Yes | Partial | Failing | Open | macOS Metal | Validation_Translucent artifacts |
| Baked lighting: lightmap, Shadowmask / Distance Shadowmask, probes, LPPV, APV, terrain, baking sky (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (CPU contracts and actual GPU readbacks) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (bounded task fixtures) | Open | macOS Metal | Real lightmap/probe/APV/sky bakes; native/Mesh caster controls, Terrain near/far/holes and realtime atlas contribution, dynamic Mesh SH, LPPV gradient, APV L1/L2/Off and probe debug verified. Meta holes, Player and other APIs unverified. Timing produced zero valid GPU samples; no performance pass. See S12 ledger and playbook. |

## Temporal, post and output

| Feature | Recorded | EditMode | Image | Perf | Platforms | Notes |
|---|---|---|---|---|---|---|
| TAA (+ separate sharpen pass) | Yes | Yes | Open | Open | macOS Metal | One-dispatch TAA + sharpen is not implemented |
| Fixed-ratio super resolution | Game cameras | Partial | Open | Open | macOS Metal | |
| Exposure, Bloom, CombineLUT (ACES), Vignette, Film Grain | Yes | Yes | Open | Open | macOS Metal | |
| Present (transfer encoding) | Yes | Yes (GPU gray card) | Accepted 2026-09-13 | Open | macOS Metal | SDR sRGB; HDR output unverified |
| SceneView sizing / UI overlay gate | Yes | Yes | Accepted 2026-09-13 | — | macOS Metal | |
| DebugView | Yes | Partial | Open | — | macOS Metal | |
| Rendering Debugger panels, debug views 19–29, Scene view Draw Modes, pass timings (0.5.0) | Yes | Unverified-0.5 (catalog, mode sync) | Open | — | None | Compiled in Unity; native panel and Draw Mode acceptance remains open |
| Render Graph Viewer (0.5.0) | While open | Unverified-0.5 (snapshot contract) | Open | — | None | |
| Material conversion (Built-in / URP / HDRP → Infinity) and create menus (0.5.0) | Editor | Unverified-0.5 (Standard conversion) | Open | — | None | Masks, occlusion, detail, height and emission maps are dropped |
| Image regression over the validation fixtures (0.5.0) | Play mode | Unverified-0.5 (comparison) | — | — | None | No baselines yet: record them on the first verified Editor |

## Out of scope

Hardware ray tracing outside UnifiedRayTracing, depth of field, motion blur, other new post effects, XR, MSAA,
dynamic resolution and super-resolution upscalers beyond the fixed-ratio path.
