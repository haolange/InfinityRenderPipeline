# Pipeline Asset

The pipeline asset owns quality and scene-wide rendering settings. Runtime shaders stay on Global Settings. See [Global Settings](global-settings.md).

## Settings

The pipeline reads these values once when it is created. Edit them in the Inspector, which recreates the pipeline; a value written from script at run time has no effect.

- `renderScale` in `[0.5, 1]` — Game cameras only, Super Resolution path. Internal size is `ceil(display * scale)`.
- Opaque Color Path: the GBuffer, Scene Color, sideband and motion formats are fixed (128 bits of geometry color per pixel); the Inspector shows their status. Depth Stencil Format is `D24S8` or `D32FS8`; an unsupported choice falls back to the other with a platform-override status, and a device with neither, or without a required color format, cannot create the pipeline. Rendering Layers enables the four light channels on deferred surfaces.
- Optional `qualityVolumeProfile` passed to `VolumeManager.Initialize(default, quality)`.
- Shadows ([Shadows](shadows.md)), the light-probe system and Adaptive Probe Volumes ([Baked lighting](baked-gi.md)), reflection-probe resolution and slot count, the cookie atlas, frame settings for cameras and reflection-probe captures, output mode, and default materials.
- `atmosphericalProfile`. Atmosphere is not a Volume. Scattering coefficients on the profile are stored per kilometer and converted to per meter when bound.
- Diffusion profiles for skin scattering, at most 15 (more blocks pipeline creation). Each profile sets scatter color, distance, surface albedo, maximum radius and thickness (the share of the diffuse that scatters). The Volume only enables the feature and sets the sample count.
- `meshBackendPolicy`: Auto, CPU direct, or GPU indirect. See [Mesh content](mesh.md).

Multiple assets can bind to Quality Settings. Switching pipelines recreates the render pipeline and `Dispose` restores `SupportedRenderingFeatures`, `Shader.globalRenderPipeline`, and the captured `GraphicsSettings` flags.
