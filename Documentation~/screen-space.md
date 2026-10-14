# Screen-space effects

All screen-space effects are Volume components with an **enable** flag and a **Quality** level (see
[Quality](quality.md)); Frame Settings can switch them off per camera and for reflection probe captures.

| Effect | Volume component | Notes |
|---|---|---|
| Screen Space Reflection | Lighting / Screen Space Reflection | Stochastic, after Frostbite: tile classification (Hi-Z / linear / skip), half-resolution GGX rays, ray-reuse resolve from the lit color pyramid, temporal and optional spatial denoising. Confidence replaces probe / sky reflections. |
| Screen Space Indirect Diffuse | Lighting / Screen Space Indirect Diffuse | The SSR core with cosine rays; missed rays keep the baked / probe / sky ambient. |
| Ambient occlusion (GTAO) | Lighting / Screen Space Ambient Occlusion | Ground-truth horizon-based AO with temporal accumulation. |
| Ray-traced AO | Ray Tracing / Ray Traced Ambient Occlusion | UnifiedRayTracing; needs the RP Asset's Enable Ray Trace. Falls back to GTAO when unavailable. |
| Contact shadows | Shadowing / Contact Shadow | See [Shadows](shadows.md). |

SSR and SSGI only trace deferred pixels; forward-route materials keep their probe and sky lighting. Both read the
current frame's opaque lighting (one bounce).

The parameters of SSR and SSGI are described in [Volumes](volumes.md). Debug views (Rendering Debugger → Infinity
Screen Space): AO, SSR, SSGI, SSR Tile Class (blue Hi-Z, orange linear, gray skipped), SSR Ray Hit (green hit, red
miss), SSR Confidence, SSR History Age. Fixture: Window → Infinity → Create Screen Space Fixture.
