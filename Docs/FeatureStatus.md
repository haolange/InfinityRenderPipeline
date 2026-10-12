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
| Light shapes and record v2 (0.5.0) | Yes | Unverified-0.5 (shape, record layout) | Open | Open | None | Area shapes shade as punctual / equal-area square until S8 |
| Lightmapper delegate (0.5.0) | Editor bakes | No | Open | — | None | |
| Clustered lights (tile + z-bin) | Yes | Partial | Open | Open | macOS Metal | 0.5.0 S7 replaces it |
| Cascade + local shadows (PCF 3×3) | Yes | Partial | Open | Open | macOS Metal | 0.5.0 S9 replaces filtering |
| Contact shadow (first directional light) | Yes | No | Open | Open | macOS Metal | 0.5.0 S10 replaces it |
| Atmosphere LUTs + sky IBL | Yes | Yes (parameters, cache) | Open | Open | macOS Metal | |
| GTAO | Yes | Yes (numeric) | Open | Open | macOS Metal | |
| RTAO (UnifiedRayTracing) | When URT is ready | Partial | Open | Open | Editor only | Player shader load unverified |
| SSR (stochastic, 0.5.0) | Yes | Unverified-0.5 (numeric, presets) | Open | Open | None | Never compiled by Unity yet |
| SSGI | Yes | Yes (numeric) | Open | Open | macOS Metal | 0.5.0 S14 replaces it |
| Burley SSS | Yes | No | Open | Open | macOS Metal | |
| Volumetric fog / cloud | Yes | Partial | Open | Open | macOS Metal | |
| Translucent T0 / T1 / T2 | Yes | Partial | Failing | Open | macOS Metal | Validation_Translucent artifacts |
| Baked lighting (lightmap, probe SH, shadowmask) | Partial | Partial | Open | Open | macOS Metal | 0.5.0 S12 completes it |

## Temporal, post and output

| Feature | Recorded | EditMode | Image | Perf | Platforms | Notes |
|---|---|---|---|---|---|---|
| TAA (+ separate sharpen pass) | Yes | Yes | Open | Open | macOS Metal | One-dispatch TAA + sharpen is not implemented |
| Fixed-ratio super resolution | Game cameras | Partial | Open | Open | macOS Metal | |
| Exposure, Bloom, CombineLUT (ACES), Vignette, Film Grain | Yes | Yes | Open | Open | macOS Metal | |
| Present (transfer encoding) | Yes | Yes (GPU gray card) | Accepted 2026-09-13 | Open | macOS Metal | SDR sRGB; HDR output unverified |
| SceneView sizing / UI overlay gate | Yes | Yes | Accepted 2026-09-13 | — | macOS Metal | |
| DebugView | Yes | Partial | Open | — | macOS Metal | |

## Out of scope

Hardware ray tracing outside UnifiedRayTracing, depth of field, motion blur, other new post effects, XR, MSAA,
dynamic resolution and super-resolution upscalers beyond the fixed-ratio path.
