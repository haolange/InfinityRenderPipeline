# Pipeline Asset and quality

The RP Asset owns quality, not resources:

- `renderScale` in `[0.5, 1]` — Game cameras only, Super Resolution path. Internal size is `ceil(display * scale)`.
- Optional `qualityVolumeProfile` passed to `VolumeManager.Initialize(default, quality)`.
- Shadows, atmosphere, diffusion profiles, output mode, default materials.

Multiple assets can bind to Quality Settings. Switching pipelines recreates the render pipeline and `Dispose` restores `SupportedRenderingFeatures`, `Shader.globalRenderPipeline`, and the captured `GraphicsSettings` flags.
