# Infinity Render Pipeline

Infinity Render Pipeline (InfinityRP) is a Scriptable Render Pipeline for Unity 6000.6. It targets compute-capable platforms and is built on Core RP, Shader Graph and VFX Graph 17.6. The package owns its render graph, physical resource pool, mesh scene and light records. Unity remains the owner of native renderer culling, renderer lists, lightmaps and per-object lighting data.

`package.json` reports version 0.4.0. The unreleased changelog describes later source. This file describes the source as it is. It does not claim image or performance acceptance. Per-feature verification is [Docs/FeatureStatus.md](Docs/FeatureStatus.md).

## Requirements

- Unity 6000.6 (`unityRelease` 0f1 in this package)
- Core RP, Shader Graph and VFX Graph 17.6
- A platform with compute shaders

Assign an `InfinityRenderPipelineAsset` under Project Settings > Graphics and Quality. Runtime shaders, the blit material and the BestFit LUT resolve from `InfinityRenderPipelineGlobalSettings` through `[ResourcePath]`. The pipeline asset does not reference those runtime shaders. Opening a project does not migrate or repair assets.

## Documentation

| Document | Role |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Implementation: frame order, graph, mesh scene, lighting, history and output |
| [AGENTS.md](AGENTS.md) | Normative rules for contributors and coding agents |
| [Documentation~/index.md](Documentation~/index.md) | Authoring: settings, lights, materials, volumes, Editor windows |
| [Docs/FeatureStatus.md](Docs/FeatureStatus.md) | What has been recorded, tested, image-checked and measured |
| [Docs/UnityIntegrationAudit.md](Docs/UnityIntegrationAudit.md) | Ownership boundary with Unity and inspected engine packages |
| [CHANGELOG.md](CHANGELOG.md) | Source history |
| [Docs/History/](Docs/History) | Archived plans and receipts. They do not authorize new work |

## What a frame records

Recording follows `InfinityRenderPipeline.Render`. Each producer runs only when its frame feature, volume activity and required resources allow it. The dependency order is specified in [ARCHITECTURE.md](ARCHITECTURE.md).

- Deferred shading from a thin GBuffer, plus a forward surface route
- One clustered light list: 128 logarithmic depth bins and 16×16 screen tiles, shared by deferred shading, forward shading, volumetric fog and volumetric clouds
- Analytic light shapes on `InfinityAdditionalLightData`: point, sphere, line, tube, cone, pyramid and box spots, rectangle, disk and a directional sun disk. Specular uses a representative point on the emitter with energy normalization. Diffuse uses a horizon-clipped form factor for spheres and disks, line irradiance for lines and tubes, and a polygon form factor for rectangles
- Cookies and IES profiles in one atlas
- Four cascade splits for the directional light, and a tiled local-shadow atlas. Filters are Hard, PCF, PCSS, VSM and EVSM. Local lights may update every frame, on enable, or on demand
- Screen-space contact shadows
- Baked lighting for native renderers: lightmaps, shadowmask, light probes, light-probe proxy volumes and Adaptive Probe Volumes. Reflection probes are prefiltered into one atlas
- Atmosphere lookup tables from `AtmosphericalProfile`, sky image-based lighting, volumetric fog and volumetric clouds
- Screen-space ambient occlusion, with ray-traced ambient occlusion when Unified Ray Tracing is ready
- Stochastic screen-space reflection, and screen-space indirect diffuse that traces the accepted previous color
- Subsurface diffusion of a separate diffuse component, deferred decals, and three translucent stages
- Temporal anti-aliasing or fixed-ratio super resolution, exposure, bloom, an ACES film curve, color grading, vignette, grain, and one output encode per camera target
- Editor authoring: Shader Graph targets, inspectors, the Rendering Debugger, a render-graph viewer, material conversion and validation fixtures

Not recorded: depth of field, motion blur, MSAA, dynamic resolution, XR, and hardware ray tracing outside Unified Ray Tracing. Custom VFX Graph outputs are unsupported. Standard particle systems use the translucent renderer list.

## Mesh content

Add `Mesh Component` to a renderable object. `RenderingLayer` is an `ERenderingLayer` bit mask (`Everything = 0xFF`), not an integer layer index. One live instance owns one `TransformId`. In steady state `MeshScene.MatrixDuplicateRatio` is 1.

Configure instances through `MeshAsset`, `Mobility`, the typed properties and `SetMaterials`. `Materials` is read-only. Dynamic world matrices, including parent motion, are read automatically. After an in-place mesh edit call `NotifyGeometryChanged`. After a static pose or external instance edit call `NotifyInstanceChanged`. After a route or stage edit call `MaterialRouteUtility.ApplyPassState`. After a direct queue or pass edit call `NotifyMaterialRoutingChanged`. Ordinary material uniforms do not rebuild draw structure.

The asset field `meshBackendPolicy` selects the mesh backend. Auto uses GPU indirect drawing when instancing, compute and the four mesh-draw kernels are available, and CPU procedural drawing otherwise. The Rendering Debugger can override that choice for the session. CPU and GPU backends share the scene and the transform-version history.

Instanced materials bind `transformBuffer`, `previousTransformBuffer`, `instanceIndexBuffer` and `instanceIndexOffset`. See `InfinityLit-Instanced.shader`. EditMode tests live under `Tests/Editor`.

## Native renderer content

Unity `MeshRenderer` and related renderers are drawn from Unity culling results and renderer lists, including lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion. A `MeshComponent` does not take over a renderer and does not store lightmap or CPU per-object probe data. Its material may sample Adaptive Probe Volumes in world space. Realtime lights and atmosphere lighting remain pipeline inputs.

## Example

[InfinityExample](https://github.com/haolange/InfinityExample)

![InfinityExample frame](https://user-images.githubusercontent.com/12471727/130435193-ab3519fe-cc88-4287-ade9-024fea5b642f.png)
