# Cameras

`InfinityAdditionalCameraData` is the only per-camera Infinity storage:

- Volume layer mask and volume trigger
- Temporal Override: `FollowPipeline`, `ForceNativeAA` (overrides global upscaling — routing single-mode upscalers such as `SGSR` to `Native` `1.0x` `NativeAA`), or `Off`
- Custom Frame Settings, which override the pipeline asset's camera frame settings feature by feature

The Infinity camera inspector is active when an Infinity pipeline asset is assigned. Its sections are Projection, Rendering, Environment, Frame Settings and Output.

Preview cameras use the default Volume profile only and do not run temporal anti-aliasing or super resolution. Scene view uses the full pipeline. Its Volume mask and trigger come from the single active Game camera when exactly one exists. Otherwise the mask is everything and the trigger is the Scene view transform. A gap in the global frame counter does not reset Scene view history. A cut, a projection change or a failed submission does.

Game cameras recycle temporal history when more than one frame was skipped. New and reset frames use zero jitter. Quality-preset upscaling and negative surface `ViewTemporalMipBias()` apply to Game cameras when `Upscaling` is resolved for that camera. Explicit camera target textures are imported by identifier. Their dimensions freeze after `BeginCameraRendering`.
