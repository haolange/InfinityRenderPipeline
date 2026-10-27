# Screen-space effects

Screen-space effects are Volume components with an enable flag and a Quality level. See [Quality](quality.md). Frame Settings can disable them per camera and for reflection-probe captures. Forward-route pixels are not traced. They keep probe and sky lighting.

| Effect | Volume menu | Recorded behavior |
|---|---|---|
| Screen Space Reflection | Lighting / Screen Space Reflection | Classify 8×8 tiles, trace GGX rays at half or full resolution, then resolve, accumulate and optionally filter at full resolution. History is the temporal result, before the spatial filter. Confidence replaces probe and sky reflection. |
| Screen Space Indirect Diffuse | Lighting / Screen Space Indirect Diffuse | Cosine rays against the accepted previous color. No ray-reuse resolve. Temporal and world-space spatial filters run on the trace grid. Reconstruction writes full resolution. History is the filtered working result. |
| Ambient occlusion | Lighting / Screen Space Ambient Occlusion | Horizon search, temporal accumulation and spatial filter at half resolution, then a full-resolution upsample. |
| Ray-traced ambient occlusion | Ray Tracing / Ray Traced Ambient Occlusion | Unified Ray Tracing at internal resolution when the pipeline asset enables ray tracing and the acceleration structure has instances. Otherwise horizon ambient occlusion owns the buffer. |
| Contact shadows | Shadowing / Contact Shadow | See [Shadows](shadows.md). |

Reflection parameters are in [Volumes](volumes.md). Indirect diffuse parameters are Max Distance, Thickness, Intensity Scale, Half Resolution, Num Steps, Temporal Weight, Temporal Scale and Spatial Passes. Low, Medium and High presets trace indirect diffuse at half resolution. Ultra traces at full resolution. Custom uses the Volume toggle.

Debug views are under Rendering Debugger > Infinity Screen Space. The fixture is Window > Infinity > Create Screen Space Fixture.

Verification state is in [Feature status](../Docs/FeatureStatus.md).
