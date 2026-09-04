# InfinityRP Full Rendering — Delivery Report

Date: 2026-09-05
Package: `com.infinity.render-pipeline`
Verification platform this session: **Metal** (macOS, Unity 6000.5.3f1). D3D12 / Vulkan stay `TODO(UNVERIFIED)` until the recipes in §5 are run on those editors.

## 1. Stages (S0–S9 record paths)

| Stage | Status | What closed | Evidence |
|-------|--------|-------------|----------|
| S0 Tooling | completed | Refresh / Capture / Validation menus | `Logs/baseline/s0-*` |
| S1 Camera / Volume / History | completed | Frame state, HistoryCache field Equals | `Logs/baseline/s1-*`, volume dump |
| S2 Material / GBuffer / Decal | completed | Crytek GBuffer + DBuffer record | `Logs/baseline/s2-decal-play.png` |
| S3 Lights / Shadows | completed | Unity Light authority; CSM + local atlas | `Logs/baseline/s3-local-play.png` |
| S4 Pyramids / GTAO | completed | HiZ 4-mip/batch, ColorPyramid 2-mip/batch, GTAO chain | `Logs/baseline/s4-spazon-play.png` |
| S5 Atmosphere / IBL | completed | Profile-only; Shared / View / IBL caches | `Logs/baseline/s5-spazon-play.png` |
| S6 SSR / SSGI | completed | RayMarch → Spatial → Temporal → Bilateral + Composite | `Logs/baseline/s6-*` |
| S7 Volume / Translucent | completed | Phase 7 Fog/Cloud + FogComposite + T0/T1/T2 | `Logs/baseline/s7-translucent-play.png` |
| S8 Exposure / Output | completed | Exposure → Bloom → LUT → Vignette → Grain → OutputTransform | `Logs/baseline/s8-output-play.png` |
| S9 Inspector / Cleanup / Docs | completed | SessionState inspectors, leftover delete, docs | this report |

Image / Frame Debugger / GPU-Trace quality for S4–S8 stays `TODO(UNVERIFIED)` where noted in AGENTS.md.

## 2. Spazon convergence (R0–R6)

| Row | Status | Metal evidence | Open gate |
|-----|--------|----------------|-----------|
| R0 DebugView | completed | 10 views + `Logs/debug/Scene_Spazon-stats.json`. Validation_Output Albedo mean ≈ 0.180 | Marker ROI oversized |
| R1 Default Volume / Output | completed | IdentityLut deleted. `volumeProfile` → `SetCustomDefaultProfiles`. Gizmo before OutputTransform. Format `B8G8R8A8_SRGB` / Linear / HardwareSRGB | Gray-card Game sRGB ≈ 0.31 vs gate [0.44, 0.48]; linear dump 0.078 |
| R2 Atmosphere | completed | Physical `ThrowIfInvalid`. Earth fixture `deltaUvDaylight=0.007`, L0 r/b=0.55 | zenith/horizon 0.15 (gate [0.2,0.8]); sun/sky 3.9 (SkyView has no solar disc) |
| R3 GBuffer | completed | Metal raster→compute 5/5. Albedo DebugView is beige stone + authored banners | Stone ROI \|Co\|≈0.07 vs gate 0.05 (warm albedo) |
| R4 Screen-space denoise | completed | SpatialRadius, miss-fill, TAA-style temporal, AO owner=Deferred IBL, GTAO Volume gate, NumRays=2 | Play inter-frame SSR 0.09 (dump cycles TAA kernel) |
| R5 TAA / SceneView | completed | 3×3 HistoryDepth reject, 8-frame ramp, gap reset + jitter=0, SceneView linger 120. TAAConfidence mean 0.975 | `camera.Render()` Game dumps reset history (FramePairDiff 62% invalid). Scene drag SSIM unverified |
| R6 Docs | completed | AGENTS / DESIGN / PLAN / this report. Luna: zero dual-authority P0 | `TemporalAntiAliasingGenerator` still live jitter+dispatch |

Phase 8 after R5:

```text
TAA(+Confidence when DebugView≠None) → Exposure → Bloom → CombineLUT(stack)
→ Vignette/Grain → DebugView(linear) → Gizmo/WireOverlay(linear)
→ OutputTransform(unique encode, authority chain) → Present
```

## 3. Locked RecordRG order

```text
0  CombineLUT, AtmosphericLUT
1  Depth, DBuffer, GBuffer, Motion
2  HiZ, HalfRes, ZBin
3  CascadeShadow, LocalShadow
5  GTAO (Volume override only), CopyHistoryOcclusion, ContactShadow
6  Deferred, Forward, SSS, AtmosphericSkyAndFog, OpaqueLightingPyramid,
   SSR, SSGI, ScreenSpaceComposite, OpaqueSceneColor
7  TranslucentDepth, VolCloud, VolFog, FogComposite, FoggedSceneColor,
   T0, ColorPyramid, T1, T2
8  TAA or SuperResolution, Post, DebugView, Gizmo/WireOverlay,
   OutputTransform, DisplayColorBuffer, Present
```

## 4. Metal numbers (this session)

| Check | Menu / tool | Result |
|-------|-------------|--------|
| Output format | first-frame `Debug.Log` | `MainCamera` Game `B8G8R8A8_SRGB` Linear `HardwareSRGB` |
| GBuffer contract | `Infinity/Validation/Run GBuffer Contract Tests` | passed=5 ignored=0 failed=0 |
| SkyView Earth 45° | `Dump Atmosphere Earth Fixture` | ΔuvDaylight 0.007; L0 r/b 0.552; zenith/horizon 0.153; sun/sky 3.91 |
| TAAConfidence | `Capture Debug Views` Play | mean 0.975 |
| Default Volume | `Dump Active Volume Stacks` | `InfinityDefaultVolumeProfile` on Game + SceneView |
| Console | new Editor.log window after Refresh | 0 InfinityRP / 0 CS after T0–T5 compiles |

Spazon beauty still shows residual noise, a magenta/purple open-sky hole (no GBuffer), and blown highlights. Those are `TODO(UNVERIFIED)` image gates, not missing record paths.

## 5. Cross-platform recipes (D3D12 / Vulkan)

Take this report to a Windows D3D12 or Vulkan editor. Do **not** treat Metal numbers as portable. Run each row; keep `TODO(UNVERIFIED)` until that platform’s capture matches the threshold.

| Item | Menu | Expected | Platform note |
|------|------|----------|---------------|
| Refresh + compile | Assets > Refresh; diagnose only the new `Logs/Editor.log` window | 0 InfinityRP errors, 0 CS | Same as Metal |
| GBuffer contract | `Infinity/Validation/Run GBuffer Contract Tests` | 5/5. Albedo abs &lt; 2/255; BestFit &lt; 1° | reversed-Z does not change this test (it writes/reads GBuffer, not depth compares) |
| Output format | first-frame log + `Dump Gray Card Mean` | decision from target → active → editor present RT → lastKnown; **never** `GetGraphicsFormat(LDR)` or HDR format guess | D3D12 SDR Game is often `R8G8B8A8_SRGB` or `B8G8R8A8_SRGB`. Vulkan may differ. HDR encode still only in OutputTransform |
| Gray card | `Open Output Fixture` + Capture Game center 32×32 | sRGB mean ∈ [0.44, 0.48] | Metal observed 0.31 — close this gate per platform |
| Atmosphere | `Upgrade Atmospherical Profile` then `Dump Atmosphere Earth Fixture` | ΔuvDaylight &lt; 0.02; L0 r/b ∈ [0.5, 1.2] | `GetGPUProjectionMatrix` FlipY differs; SkyView UV convention must stay (azimuth, latitude²) |
| DebugView Albedo | `Capture Debug Views` on Spazon | stone ROI not purple; banners authored-saturated | Same decode as Metal (`GBufferPack.hlsl`) |
| TAA still frame | Play 60 frames, two **Game-tab screenshots** (not `camera.Render()` to a temp RT) | non-marker changed pixels &lt; 0.5%; TAAConfidence mean &gt; 0.9 and not constantly 1 | `camera.Render()` + `targetTexture` swap resets history — invalid |
| SceneView | drag then stop; `CaptureSceneView.sh` / OS screenshot | 2 frames later confidence &gt; 0.8; SSIM vs Game (no gizmo ROI) &gt; 0.9 | Editor present RT path is the format-authority third link |
| HDR | enable display HDR, capture OutputTransform | PQ/HLG encode only from OutputTransform | Metal HDR hardware capture still open; D3D12/Vulkan must dump `HDROutputSettings` |
| Frame Debugger | `Dump Frame Debugger` | Phase 8 order matches §3 | Transfer `CopyHistory*` have no draws; do not treat omission as a skip |
| Reversed-Z | raster depth + HiZ + TAA HistoryDepth | `GraphicsUtility.ClearDepthFar` for clears; `SampledFarDepth` only in shaders | Mixing the two clears to near plane and kills geometry |
| GPU Trace | optional | HiZ / HalfRes / AtmoLUT async overlap | `TODO(UNVERIFIED)` on Metal too |

## 6. Legacy

Kept on purpose:

- `TemporalAntiAliasingGenerator` — live Halton jitter + TAA dispatch. Split later; do not delete.
- RTAO / RTGI files — no RG pass; hardware RT out of scope.

Removed earlier (S9): SSR/SSGI/GTAO/SVGF Generator classes, DummyShaders, ComputeCompress.

## 7. Excluded

| Item | Why |
|------|-----|
| Hardware RT | Files stay; no RG pass |
| Baked GI / DOF / XR / MSAA / dynamic resolution | Not in RecordRG |
| Super-resolution quality | Asset flag + pass exist; not a closed gate |
| Preview camera temporal gating | Documented independent defect |

## 8. Archive (example project, not in package git)

Copy under `InfinityExample/Logs/baseline/`:

- `t0-*` DebugView + liveness
- `t1-output-game.png`
- `t2-spazon-game.png`, `Logs/debug/atmosphere-earth-skyview-stats.json`
- `Logs/debug/Scene_Spazon-Albedo.png` (T3)
- `t4-a/`, `t4-b/`, `t4-spazon-play.png`
- `t5-play-a.png`, `t5-play-b.png`, `t5-play-diff.json`
