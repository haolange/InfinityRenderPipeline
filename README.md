## InfinityRenderPipeline

InfinityRenderPipeline (InfinityRP) is a RenderGraph-native Scriptable Render Pipeline for Unity 6000.6
(CoreRP / Shader Graph / VFX 17.6). It targets compute-capable platforms and is organized around a strict separation of
passes, resources and features, with a modern feature baseline as the default design target.

### Documentation

- [Docs/FeatureStatus.md](Docs/FeatureStatus.md) — what is recorded, tested, image-verified and measured, per feature
- [DESIGN.md](DESIGN.md) — architecture as implemented: RenderGraph, Mesh Drawing Pipeline, frame order, ownership contracts
- [AGENTS.md](AGENTS.md) — coding conventions and rules for contributors and coding agents
- [CHANGELOG.md](CHANGELOG.md)
- [Documentation~/index.md](Documentation~/index.md) — user documentation
- [Docs/History/](Docs/History) — archived plans, delivery reports and evidence (immutable)

Framebuffer resources distinguish logical viewport size from physical capacity,
with bounded idle retention. TAA uses a fused resolve/sharpen dispatch; Mesh Drawing
Pipeline preparation is explicit in RenderGraph. Platform and runtime evidence is
recorded in [FeatureStatus](Docs/FeatureStatus.md).

### Features

The pipeline currently records:

- ThinGBuffer deferred shading plus a forward route, a clustered list (logarithmic depth bins + tile bitmasks) of
  local lights and reflection probes shared by every lit pass
- Mesh Drawing Pipeline: SoA `MeshScene`, RenderGraph DrawLists, CPU and GPU-indirect backends, conservative two-phase GPU occlusion and Closest/Furthest HiZ, scene-shared immutable transform versions
- Lights authored in Unreal form (point / sphere / line / tube, cone / pyramid / box spots, rect, disk, sun disk) with
  Karis representative-point specular; cookies and IES in one atlas
- Cascade and local shadows for every shape with Hard / PCF / PCSS / VSM / EVSM filters, cached (OnEnable / OnDemand)
  local shadows, shadowmask; screen-space traced contact shadows
- Baked GI: lightmaps, Shadowmask / Distance Shadowmask, light probes, LPPV, Adaptive Probe Volumes, baking sky;
  reflection probes in a prefiltered atlas
- Hillaire atmosphere LUTs with sky IBL (SH + GGX prefilter); volumetric fog and clouds
- GTAO visibility-domain temporal accumulation, RTAO (UnifiedRayTracing), stochastic SSR with full-resolution
  ray-reuse Resolve/Temporal/Spatial, and SSGI with direct previous-color tracing and recurrent world-space filtering.
  SSR Trace can be half resolution; SSGI uses a half-resolution working chain plus geometric reconstruction
  (Ultra remains full resolution). Both share accepted unsharpened TAA/SR color and its mip chain
- Half-resolution Burley diffusion of a separate diffuse component with full-resolution recomposition, DBuffer decals, three-stage translucency (T0 / T1 refraction / T2)
- TAA, fixed-ratio super resolution, exposure (manual / histogram), bloom, ACES tonemapping and grading LUT, vignette,
  film grain, raster final output with demand-driven intermediate colors and display-transfer encoding
- Editor: Shader Graph target (Lit / Unlit / Decal), Render Pipeline Wizard, Frame Settings, Light Explorer, Rendering
  Debugger panels and Scene view Draw Modes, Render Graph Viewer, material conversion, create menus, validation
  fixtures and image regression, shader stripping

Verification state differs per feature; [Docs/FeatureStatus.md](Docs/FeatureStatus.md) is authoritative. Nothing in
this README claims image or performance acceptance.

Current per-feature verification and platform limits are recorded in [Docs/FeatureStatus.md](Docs/FeatureStatus.md).

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

Mesh Drawing Pipeline draw markers use `RenderLoop.*` for ordinary views and `Shadows.*` for shadow views. Preparation shares `ComputeMeshCulling`, `ComputeMeshCompaction` and `UploadMeshData` across views. Native SRPBatcher and Mesh Direct/Indirect draws are siblings; preparation remains explicit outside Raster. See [design](DESIGN.md#explicit-meshdraw-preparation).

### Example

[ExampleProject](https://github.com/haolange/InfinityExample)

![image](https://user-images.githubusercontent.com/12471727/130435193-ab3519fe-cc88-4287-ade9-024fea5b642f.png)

### Runtime Mesh updates

Configure `MeshComponent` through `MeshAsset`, `Mobility`, typed instance properties and `SetMaterials`; `Materials` is read-only. Dynamic world transforms update automatically, including parent motion. Call `NotifyInstanceChanged()` after static pose changes, `NotifyGeometryChanged()` after modifying Mesh geometry in place, and `MeshComponent.NotifyMaterialRoutingChanged(material)` after direct queue/pass edits. Route/stage edits use `MaterialRouteUtility.ApplyPassState(material)`. Ordinary color/texture uniforms retain Unity Material behavior. These APIs never migrate assets on load. The removed Renderer baked-source field is not replaced by a compatibility API.

Select the Mesh backend with the RP Asset's `meshBackendPolicy` (Auto by default). Rendering Debugger provides a temporary override and clear action. CPU Direct and GPU Indirect share scene data and motion history. See [Feature status](Docs/FeatureStatus.md) for actual platform/GC/HDR verification limits.

### Native HDR output

Enable HDR display support in Unity Player Settings and activate the display through Unity's HDR output API before selecting HDR output on the RP Asset. Match the encoding to the active swapchain: this Mac's XDR output reports a floating-point, linear Rec.2020 target and uses `Rec2020_Linear`; PQ and scRGB describe different contracts. The pipeline validates the active mode, format and gamut, owns output conversion and temporarily disables Unity's automatic HDR tonemapping. Existing serialized encoding numbers retain their values. See the [convergence receipt](Docs/History/MainThread-20261025/Report.md) for the native Metal evidence and remaining platform limits.

Native Unity Renderer content uses Unity's renderer lists and per-object lighting/motion data. Custom Mesh content remains independent: no Renderer takeover, Lightmap data or CPU per-object Probe sampling. The instanced shader can sample world-space APV; realtime lighting and atmosphere/IBL remain available. See [Unity integration ownership](Docs/UnityIntegrationAudit.md).
