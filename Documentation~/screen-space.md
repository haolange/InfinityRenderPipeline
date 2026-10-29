# Screen-space effects

Screen-space effects are Volume components with an enable flag and a Quality level. See [Quality](quality.md). Frame Settings can disable them per camera and for reflection-probe captures. Forward-route pixels are not traced. They keep probe and sky lighting.

| Effect | Volume menu | Recorded behavior |
|---|---|---|
| Screen Space Reflection | Lighting / Screen Space Reflection | Classify 8×8 tiles, trace GGX rays at half or full resolution, then resolve, accumulate and optionally filter at full resolution. History is the temporal result, before the spatial filter. Confidence replaces probe and sky reflection. |
| Screen Space Indirect Diffuse | Lighting / Screen Space Indirect Diffuse | Half-resolution cosine rays against the accepted previous color. No ray-reuse resolve. Two temporal-then-world-space stages, 6 m then 3 m. Bilateral upsample writes full resolution. Each stage history is its mix. |
| Ambient occlusion | Lighting / Screen Space Ambient Occlusion | Horizon search, temporal accumulation and spatial filter at half resolution, then a full-resolution upsample. |
| Ray-traced ambient occlusion | Ray Tracing / Ray Traced Ambient Occlusion | Unified Ray Tracing at internal resolution when the pipeline asset enables ray tracing and the acceleration structure has instances. Otherwise horizon ambient occlusion owns the buffer. |
| Contact shadows | Shadowing / Contact Shadow | See [Shadows](shadows.md). |

Reflection parameters are in [Volumes](volumes.md). Samples is the number of rays traced per pixel. Each ray is a Halcyon record of unweighted radiance, confidence, `Dir * HitT` and a signed inverse pdf. Resolve rebuilds the hit from that sample's own origin. Resolve Samples is neighbor reuse of sample 0 and is separate. Indirect diffuse parameters are Trace Behind, Toward Ray, Intensity Scale, Num Steps, Samples, Temporal Weight and Temporal Scale. A hit is the ray segment crossing the Hi-Z depth interval. A full-resolution texel is a point depth. A step already behind the next texel still hits when the shared edge lies between the two depths and a neighboring triple is colinear within 0.25 of that step. The alternate 2% eye-depth test is `SCREEN_TRACE_RELATIVE_THICKNESS`, and production shaders do not declare it. There is no Volume thickness. The indirect-diffuse work grid is half resolution at every quality level. Intensity Scale reaches 10.

Debug views are under Rendering Debugger > Infinity Screen Space. The fixture is Window > Infinity > Create Screen Space Fixture.

Verification state is in [Feature status](../Docs/FeatureStatus.md).
