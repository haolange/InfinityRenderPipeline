# Pipeline Asset

The pipeline asset owns quality and scene-wide rendering settings. Runtime shaders stay on Global Settings. See [Global Settings](global-settings.md).

## Settings

- `renderScale` in `[0.5, 1]` — Game cameras only, Super Resolution path. Internal size is `ceil(display * scale)`.
- Optional `qualityVolumeProfile` passed to `VolumeManager.Initialize(default, quality)`.
- Shadows ([Shadows](shadows.md)), the light-probe system and Adaptive Probe Volumes ([Baked lighting](baked-gi.md)), reflection-probe resolution and slot count, the cookie atlas, frame settings for cameras and reflection-probe captures, output mode, and default materials.
- `atmosphericalProfile`. Atmosphere is not a Volume. Scattering coefficients on the profile are stored per kilometer and converted to per meter when bound.
- Diffusion profiles for subsurface scattering. The Volume only enables the feature and sets the sample count.
- `meshBackendPolicy`: Auto, CPU direct, or GPU indirect. See [Mesh content](mesh.md).

Multiple assets can bind to Quality Settings. Switching pipelines recreates the render pipeline and `Dispose` restores `SupportedRenderingFeatures`, `Shader.globalRenderPipeline`, and the captured `GraphicsSettings` flags.
