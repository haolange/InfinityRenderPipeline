## InfinityRenderPipeline

InfinityRenderPipeline is a graphics research SRP for Unity (InfinityRP).

### Documentation

- [AGENTS.md](AGENTS.md) — coding conventions and agent collaboration rules
- [DESIGN.md](DESIGN.md) — Mesh Drawing Pipeline + full rendering order (as implemented)
- [Docs/MeshPipeline-Baseline.md](Docs/MeshPipeline-Baseline.md) — MeshDraw migration baseline
- [Docs/MeshPipeline-Delivery-Report.md](Docs/MeshPipeline-Delivery-Report.md) — Closure D (D1–D6)
- [Docs/FullRendering-Delivery-Report.md](Docs/FullRendering-Delivery-Report.md) — S0–S9 full-rendering delivery
- [PLAN.md](PLAN.md) — stage table (S0–S9)

### Feature

Completed (record path; image quality stays `TODO(UNVERIFIED)` unless a capture is listed in PLAN):

- ThinGBuffer (Crytek A/B + GBufferC flags/SSS)
- TemporalAA (jitter-free motion, direct-texel history)
- RenderGraph (custom RGBuilder + async compute hooks)
- Screen-space GI / reflection (RayMarch → Spatial → Temporal → Bilateral + Composite)
- Ground-truth ambient occlusion (Trace → SpatialX/Y → Temporal → Upsample)
- Atmosphere LUT + IBL (Profile-only; Shared / View / IBL caches)
- Z-Binning tile / z-bin light lists
- Volumetric fog and cloud (after T0 depth) + FogComposite
- Translucent T0 / T1 refraction / T2
- Exposure / Bloom / CombineLUT / Vignette / FilmGrain / OutputTransform
- Instanced terrain / runtime virtual texture / foliage systems (separate from MeshScene)
- **Mesh Drawing Pipeline** — `MeshScene` SoA + RDG `RGDrawListRef` + CPU/GPU backends (per-payload indirect, Auto fallback); Motion / CascadeShadow / LocalShadow (Spot + Point **6-face**) MeshDraw; instance-indexed GPU cull (compact → transform); exclusive TransformId 1:1 ownership; shared `renderingLayer` (`ERenderingLayer : byte` flags); shadow MeshDraw uses `light.cullingMask` + `shadowLayer`; no HZB / GPU radix sort / full GPU LOD in this closed slice

Out of this delivery:

- Hardware RT (RTAO/RTGI files stay; no RG pass)
- Baked GI
- Depth of field
- Super-resolution expansion
- XR
- MSAA
- Dynamic resolution

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
