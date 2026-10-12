## InfinityRenderPipeline

InfinityRenderPipeline (InfinityRP) is a RenderGraph-native Scriptable Render Pipeline for Unity 6000.6
(CoreRP / Shader Graph / VFX 17.6). It targets compute-capable platforms and is organized around a strict separation of
passes, resources and features, with a modern feature baseline as the default design target.

### Documentation

- [Docs/FeatureStatus.md](Docs/FeatureStatus.md) — what is recorded, tested, image-verified and measured, per feature
- [DESIGN.md](DESIGN.md) — architecture as implemented: RenderGraph, Mesh Drawing Pipeline, frame order, ownership contracts
- [AGENTS.md](AGENTS.md) — coding conventions and rules for contributors and coding agents
- [PLAN.md](PLAN.md) — the active task ledger (0.5.0 upgrade)
- [CHANGELOG.md](CHANGELOG.md)
- [Documentation~/index.md](Documentation~/index.md) — user documentation
- [Docs/History/](Docs/History) — archived plans, delivery reports and evidence (immutable)

### Features

The pipeline currently records:

- ThinGBuffer deferred shading plus a forward route, a clustered local-light list (logarithmic depth bins + tile bitmasks) shared by every lit pass
- Mesh Drawing Pipeline: SoA `MeshScene`, RenderGraph DrawLists, CPU and GPU-indirect backends, GPU frustum culling
- Cascade and local (spot, point 6-face) shadow atlases; contact shadows
- Hillaire atmosphere LUTs with sky IBL (SH + GGX prefilter); volumetric fog and clouds
- GTAO, RTAO (UnifiedRayTracing), SSR and SSGI with temporal and spatial denoising
- Burley subsurface scattering, DBuffer decals, three-stage translucency (T0 / T1 refraction / T2)
- TAA, fixed-ratio super resolution, exposure (manual / histogram), bloom, ACES tonemapping and grading LUT, vignette,
  film grain, display-transfer encoding in the final Present pass
- Rendering Debugger panels and debug views, normal-frame capture service, shader stripping

Verification state differs per feature; [Docs/FeatureStatus.md](Docs/FeatureStatus.md) is authoritative. Nothing in
this README claims image or performance acceptance.

The 0.5.0 upgrade ([PLAN.md](PLAN.md)) adds stochastic SSR, area lights, a full baked-GI and reflection-probe stack,
cookies/IES, PCSS/VSM/EVSM shadows, screen-space traced shadows, quality presets and HDRP-level Editor tooling.

Not supported: hardware ray tracing outside UnifiedRayTracing, depth of field, motion blur, XR, MSAA, dynamic
resolution, VFX Graph outputs.

### Mesh Drawing Pipeline (quick start)

1. Add `Mesh Component` to renderable objects. Set `renderingLayer` as flags (`ERenderingLayer : byte`, default
   `LightLayerDefault`), not an int layer index. Each instance owns exactly one `TransformId`.
2. Runtime shaders, including the Mesh Draw Pipeline compute shader, resolve from Infinity Global Settings
   (`[ResourcePath]`).
3. Materials provide Infinity pass tags and, for the instanced path, bind `transformBuffer` /
   `previousTransformBuffer` / `instanceIndexBuffer` / `instanceIndexOffset` (see `InfinityLit-Instanced.shader`).
4. Local and cascade shadow MeshDraw uses the light's `cullingMask` plus its rendering-layer mask for Infinity casters;
   Unity MeshRenderers still go through RendererLists.
5. Run the EditMode tests under `Tests/Editor`.

Invariant: one logical instance owns one exclusive transform record (`MatrixDuplicateRatio == 1`).

### Example

[ExampleProject](https://github.com/haolange/InfinityExample)

![image](https://user-images.githubusercontent.com/12471727/130435193-ab3519fe-cc88-4287-ade9-024fea5b642f.png)
