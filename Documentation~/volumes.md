# Volumes

All Infinity Volume components use `[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]`.

Optional features (`SSR`, `SSGI`, `SSAO`, fog, cloud, contact shadow, SSS, RTAO) expose an `enable` flag and implement `IsActive()`. Bloom / Vignette / Grain activate when `intensity > 0`. Tonemapping activates when `mode == ACES` (InfinityRP supports only None and ACES).

`overrideState` is only a scene-override marker. The default profile must contain every consumed type. `Validate & Complete` appends missing types. `Reset to packaged defaults` is a separate confirmed action.

## Quality presets

Features with a cost/quality trade-off (SSR, GTAO, RTAO, volumetric fog, volumetric cloud; SSGI and contact shadows as they are rebuilt in 0.5.0) have a **Quality** parameter:

- **Low / Medium / High / Ultra** select a preset that is hardcoded in `Runtime/RenderPipeline/Quality/QualityPresets.<Feature>.cs`. The quality parameters (sample counts, steps, resolution, temporal weights) are shown read-only.
- **Custom** uses the quality parameters authored on the Volume. This is the default, so profiles created before 0.5.0 keep their values.

Artistic parameters (intensity, radius, density, colors, distances) are always taken from the Volume. Rendering code only reads the resolved low-level settings, never the quality level, so presets are edited in one place in code.

## Screen Space Reflection

Stochastic SSR after Frostbite (Stachowiak 2015). Per frame:

1. **Classify** 8x8 trace tiles by the smallest roughness inside: tiles without a reflective deferred pixel are skipped;
   tiles whose smoothest pixel is below **Linear Roughness** trace with the precise Hi-Z walk; the others use the
   cheaper linear march. Forward-route pixels and pixels rougher than **Max Roughness** are never traced and keep
   probe/sky reflections.
2. **Trace** one GGX visible-normal sample per trace pixel (blue-noise Sobol sequence). **Half Resolution** traces one
   pixel of every 2x2 quad, rotating through the quad each frame. **Brdf Bias** trims the lobe tail.
3. **Resolve** at full resolution reuses **Resolve Samples** neighboring rays within **Resolve Radius** (ratio
   estimator weighted by the pixel's own BRDF over each ray's pdf) and reads the lit color pyramid at the mip that
   matches each ray's cone (filtered importance sampling). Pixels smoother than **Mirror Roughness** use only their
   own mirror ray. Screen edges (**fade**), back faces and the last 10% of **Max Distance** lower the confidence.
4. **Temporal** accumulation reprojects both the surface and the reflection's virtual image, clips history to the
   neighborhood (**Temporal Scale** standard deviations) and tracks per-pixel history age up to **Temporal Weight**.
5. An optional a-trous **Spatial** pass smooths rough reflections.

The reflected color is the current frame's opaque lighting (one bounce). Rendering Debugger → Debug View offers
SSR Tile Class (blue Hi-Z, orange linear, gray skipped), SSR Ray Hit (green hit, red miss), SSR Confidence and SSR
History Age.

