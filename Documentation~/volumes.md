# Volumes

All Infinity Volume components use `[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]`.

Optional features (`SSR`, `SSGI`, `SSAO`, fog, cloud, contact shadow, SSS, RTAO) expose an `enable` flag and implement `IsActive()`. Bloom / Vignette / Grain activate when `intensity > 0`. Tonemapping activates when `mode == ACES` (InfinityRP supports only None and ACES).

`overrideState` is only a scene-override marker. The default profile must contain every consumed type. `Validate & Complete` appends missing types. `Reset to packaged defaults` is a separate confirmed action.

## Quality presets

Features with a cost/quality trade-off (GTAO, RTAO, volumetric fog, volumetric cloud; SSR, SSGI and contact shadows as they are rebuilt in 0.5.0) have a **Quality** parameter:

- **Low / Medium / High / Ultra** select a preset that is hardcoded in `Runtime/RenderPipeline/Quality/QualityPresets.<Feature>.cs`. The quality parameters (sample counts, steps, resolution, temporal weights) are shown read-only.
- **Custom** uses the quality parameters authored on the Volume. This is the default, so profiles created before 0.5.0 keep their values.

Artistic parameters (intensity, radius, density, colors, distances) are always taken from the Volume. Rendering code only reads the resolved low-level settings, never the quality level, so presets are edited in one place in code.
