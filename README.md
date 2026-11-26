# Infinity Render Pipeline

Infinity Render Pipeline (InfinityRP) is a Scriptable Render Pipeline for Unity 6000.6. It targets compute-capable platforms and is built on Core RP, Shader Graph and VFX Graph 17.6. The package owns its render graph, physical resource pool, mesh world and light records. Unity remains the owner of native renderer culling, renderer lists, lightmaps and per-object lighting data.

`package.json` reports version 0.4.0. The unreleased changelog describes later source. This file describes the source as it is. It does not claim image or performance acceptance. Per-feature verification is [Docs~/Project/Status/FeatureStatus.md](Docs~/Project/Status/FeatureStatus.md).

## Requirements

- Unity 6000.6 (`unityRelease` 0f1 in this package)
- Core RP, Shader Graph and VFX Graph 17.6
- A platform with compute shaders

Assign an `InfinityRenderPipelineAsset` under Project Settings > Graphics and Quality. Runtime shaders, the blit material and the noise textures resolve from `InfinityRenderPipelineGlobalSettings` through `[ResourcePath]`. The pipeline asset does not reference those runtime shaders. Opening a project does not migrate or repair assets.

## Documentation

| Document | Role |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Implementation and normative system contracts: frame order, graph, mesh drawing pipeline, lighting, history and output |
| [AGENTS.md](AGENTS.md) | Principles, design rules, conventions and work method for contributors and coding agents |
| [Docs~/Manual/index.md](Docs~/Manual/index.md) | Authoring: settings, lights, materials, volumes, Editor windows, passes and derived pipelines |
| [Docs~/Project/Status/FeatureStatus.md](Docs~/Project/Status/FeatureStatus.md) | What has been recorded, tested, image-checked and measured |
| [Docs~/Project/Audit/UnityIntegrationAudit.md](Docs~/Project/Audit/UnityIntegrationAudit.md) | Ownership boundary with Unity and inspected engine packages |
| [CHANGELOG.md](CHANGELOG.md) | Source history |
| [Docs~/Record/History/](Docs~/Record/History) | Archived plans and receipts. They do not authorize new work |

## What a frame records

`InfinityRenderPipelineBase.Render` runs the frame and `InfinityRenderPipeline.RecordCamera`, the desktop pipeline's one hook, records each camera; writing a pass or another pipeline on the base is described in [Passes and pipelines](Docs~/Manual/architecture.md). Each producer runs only when its frame feature, volume activity and required resources allow it. The dependency order is specified in [ARCHITECTURE.md](ARCHITECTURE.md).

- Mesh drawing through explicit views (`CullMeshView`) and `CreateMeshDrawList`, `UseMeshDrawList` and `SubmitMeshDrawList`, with one pass cache per entry of the pipeline's mesh pass table (nine for the desktop pipeline), owned by the mesh world
- One opaque color path for deferred and forward surfaces: two RGBA8 GBuffer targets, an R11G11B10 Scene Color that the geometry draw fills with emission and indirect diffuse, and an optional RGBA8 sideband (diffuse share, two mixed-light visibilities, four light channels, skin profile). Deferred shading runs in place in compute; forward shading writes its complete result in one draw; both compose screen-space reflection and indirect diffuse in the same execution
- One clustered light list: 128 logarithmic depth bins and 16×16 screen tiles, shared by deferred shading, forward shading, volumetric fog and volumetric clouds
- Analytic light shapes on `InfinityAdditionalLightData`: point, sphere, line, tube, cone, pyramid and box spots, rectangle, disk and a directional sun disk. Specular uses a representative point on the emitter with energy normalization. Diffuse uses a horizon-clipped form factor for spheres and disks, line irradiance for lines and tubes, and a polygon form factor for rectangles
- Cookies and IES profiles in one atlas
- Four cascade splits for the directional light, and a tiled local-shadow atlas. Filters are Hard, PCF, PCSS, VSM and EVSM. Local lights may update every frame, on enable, or on demand
- Screen-space contact shadows
- Baked lighting for native renderers: lightmaps, shadowmask, light probes, light-probe proxy volumes and Adaptive Probe Volumes. Reflection probes are prefiltered into one atlas
- Atmosphere lookup tables from `AtmosphericalProfile`, sky image-based lighting, volumetric fog and volumetric clouds
- Screen-space ambient occlusion, with ray-traced ambient occlusion when Unified Ray Tracing is ready
- Stochastic screen-space reflection, and screen-space indirect diffuse that traces the accepted previous color
- Screen-space subsurface scattering of skin (`InfinitySkin`, dual-lobe Beckmann specular), deferred decals, and three translucent stages
- Temporal anti-aliasing or fixed-ratio super resolution, exposure, bloom, an ACES film curve, color grading, vignette, grain, and one output encode per camera target
- Editor authoring: Shader Graph targets, inspectors, the Rendering Debugger, a render-graph viewer, material conversion and validation fixtures

Not recorded: depth of field, motion blur, MSAA, dynamic resolution, XR, and hardware ray tracing outside Unified Ray Tracing. Custom VFX Graph outputs are unsupported. Standard particle systems use the translucent renderer list.

## Mesh content

Add `Mesh Component` to a renderable object. `RenderingLayer` is an `ERenderingLayer` mask of four light channels (`Everything = 0x0F`), not an integer layer index. One component is one mesh entity; its entity slot is its transform slot.

A component draws a `MeshAsset`: one mesh per LOD level with its screen size, material slots and fade width. Every submesh of every level is one element; the component's materials, indexed by material slot, draw them. Levels switch at `LODGroup` screen sizes and cross-fade with dithering in opaque passes. Authoring is in [Mesh content](Docs~/Manual/mesh.md). The Mesh Component inspector creates a one-level asset from a Mesh. Configure instances through `MeshAsset`, `Mobility`, the typed properties and `SetMaterials`. `Materials` is read-only. Dynamic world matrices, including parent motion, are read automatically. After an in-place mesh edit call `NotifyGeometryChanged(Mesh)`, and after a script edit of a MeshAsset call `NotifyGeometryChanged(MeshAsset)`; inspector edits notify automatically. After a static pose or external entity edit call `NotifyEntityChanged`. After a route or stage edit call `MaterialRouteUtility.ApplyPassState`. After a direct queue or pass edit call `NotifyMaterialRoutingChanged`. Ordinary material uniforms do not rebuild draw structure.

The asset field `meshBackendPolicy` selects the mesh backend. Auto uses GPU indirect drawing when the device supports compute and instancing, and CPU procedural drawing otherwise; translucent color passes, which need strict back-to-front order, always draw on the CPU. Every kernel of the mesh compute shader is required on both backends. The Rendering Debugger can override that choice for the session. CPU and GPU backends share the mesh world and the transform-version history.

Mesh shaders declare the SubShader tag `InfinityMeshInstancing = True` and read `InstanceToEntity`, `InstanceOffset`, `EntityTransforms`, `EntityRenderingLayers` and, for motion, `PreviousEntityTransforms` through `GPUScene.hlsl`; opaque passes also read `MeshInstanceFade` and declare `multi_compile _ INFINITY_LOD_DITHER` for the LOD cross-fade. See `InfinityLit-Instanced.shader`. EditMode tests live under `Tests/Editor`.

## Native renderer content

Unity `MeshRenderer` and related renderers are drawn from Unity culling results and renderer lists, including lightmaps, legacy probes, light-probe proxy volumes, shadowmask and motion. A `MeshComponent` does not take over a renderer and does not store lightmap or CPU per-object probe data. Its material may sample Adaptive Probe Volumes in world space. Realtime lights and atmosphere lighting remain pipeline inputs.

## Example

[InfinityExample](https://github.com/haolange/InfinityExample)

![InfinityExample frame](https://user-images.githubusercontent.com/12471727/130435193-ab3519fe-cc88-4287-ade9-024fea5b642f.png)
