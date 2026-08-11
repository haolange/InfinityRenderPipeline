## InfinityRenderPipeline

InfinityRenderPipeline is a graphics research SRP for Unity (InfinityRP).

### Documentation

- [AGENTS.md](AGENTS.md) — coding conventions and agent collaboration rules
- [DESIGN.md](DESIGN.md) — Mesh Drawing Pipeline design (as implemented)
- [Docs/MeshPipeline-Baseline.md](Docs/MeshPipeline-Baseline.md) — migration baseline
- [Docs/MeshPipeline-Delivery-Report.md](Docs/MeshPipeline-Delivery-Report.md) — Closure D (D1–D6) delivery + cross-platform verification
- [PLAN.md](PLAN.md) — broader rendering-feature roadmap (lighting/volumetrics/post)

### Feature

Completed:

- ThinGBuffer
- TemporalAA
- RenderGraph (custom RGBuilder + async compute hooks)
- DiaphragmDOF
- MaskOnly PreDepth
- ScreenSpaceGlobalIllumination
- StochasticScreenSpaceReflection
- Ground-truth ambient occlusion family
- Instanced terrain / runtime virtual texture / foliage systems (separate from MeshScene)
- **Mesh Drawing Pipeline** — `MeshScene` SoA + RDG `RGDrawListRef` + CPU/GPU backends (per-payload indirect, Auto fallback); Motion / CascadeShadow / LocalShadow (Spot + Point **6-face**) MeshDraw; instance-indexed GPU cull (compact → transform); exclusive TransformId 1:1 ownership; shared `renderingLayer` (`ERenderingLayer : byte` flags); shadow MeshDraw uses `light.cullingMask` + `shadowLayer`; no HZB / GPU radix sort / full GPU LOD in this closed slice

Development:

- Atmospherical Fog
- Z-Binning tile-based lighting

Planned:

- ScreenSpaceShadow
- Volumetric Fog & Cloud
- ScreenSpaceRefraction
- Separable Subsurface Scatter
- Broader PBR shading models
- Patch shadow maps / PCSS
- DXR-based probes / larger GI

### Mesh Drawing Pipeline (quick start)

1. Add `Mesh Component` to renderable objects (Infinity path). Set `renderingLayer` as **flags** (`ERenderingLayer : byte`, default `LightLayerDefault`) — not an int layer index. Each instance owns exactly one `TransformId` (no shared transforms).
2. On the pipeline asset, assign **Mesh Draw Pipeline CS** (`Compute_MeshDrawPipeline.compute`) to enable Auto GPU-indirect when supported.
3. Materials should provide Infinity pass tags and, for the instanced path, bind `transformBuffer` / `previousTransformBuffer` / `instanceIndexBuffer` / `instanceIndexOffset` (see `InfinityLit-Instanced.shader`).
4. LocalShadow / CascadeShadow MeshDraw uses the light’s Unity `cullingMask` plus `LightComponent.shadowLayer` for Infinity casters; Unity MeshRenderers still go through RendererList. Spot or Point lights with shadows enabled are required for LocalShadow.
5. Run EditMode tests under `Tests/Editor`.

Invariant: one logical instance → one exclusive transform record (`MatrixDuplicateRatio == 1`).

### Example

[ExampleProject](https://github.com/haolange/InfinityExample)

![image](https://user-images.githubusercontent.com/12471727/130435193-ab3519fe-cc88-4287-ade9-024fea5b642f.png)
