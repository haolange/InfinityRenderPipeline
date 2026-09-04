# InfinityRP Full Rendering — Delivery Report (S0–S9)

Date: 2026-09-05
Package: `com.infinity.render-pipeline`
Verification platform: **Metal** (macOS). D3D12 / Vulkan stay `TODO(UNVERIFIED)`.
S9 scope: inspector / markers / leftover delete / docs. No rendering math or pass-order change.

## 1. Stages

| Stage | Status | What closed | Evidence |
|-------|--------|-------------|----------|
| S0 Tooling | completed | Refresh / Capture / Validation menus | `Logs/baseline/s0-*` |
| S1 Camera / Volume / History | completed | Frame state, HistoryCache field Equals | `Logs/baseline/s1-*`, volume dump |
| S2 Material / GBuffer / Decal | completed | Crytek GBuffer + DBuffer record | `Logs/baseline/s2-decal-play.png` |
| S3 Lights / Shadows | completed | Unity Light authority; CSM + local atlas | `Logs/baseline/s3-local-play.png` |
| S4 Pyramids / GTAO | completed | HiZ 4-mip/batch, ColorPyramid 2-mip/batch, GTAO chain | `Logs/baseline/s4-spazon-play.png` |
| S5 Atmosphere / IBL | completed | Profile-only; Shared / View / IBL caches | `Logs/baseline/s5-spazon-play.png` |
| S6 SSR / SSGI | in_progress | RayMarch → Spatial → Temporal → Bilateral + Composite | Validation_Temporal still open |
| S7 Volume / Translucent | in_progress | Phase 7 Fog/Cloud + FogComposite + T0/T1/T2 | Validation_Translucent still open |
| S8 Exposure / Output | in_progress | Exposure → Bloom → LUT → Vignette → Grain → OutputTransform | Validation_Output / Metal HDR still open |
| S9 Inspector / Cleanup / Docs | in_progress | SessionState inspectors, sub-markers, leftover delete, this report | Luna review + all scenes still open |

## 2. Locked RecordRG order (S7 / S8)

```text
0  CombineLUT, AtmosphericLUT
1  Depth, DBuffer, GBuffer, Motion
2  HiZ, HalfRes, ZBin
3  CascadeShadow, LocalShadow
5  GTAO, CopyHistoryOcclusion, ContactShadow
6  Deferred, Forward, SSS, AtmosphericSkyAndFog, OpaqueLightingPyramid,
   SSR, SSGI, ScreenSpaceComposite, OpaqueSceneColor
7  TranslucentDepth, VolCloud, VolFog, FogComposite, FoggedSceneColor,
   T0, ColorPyramid, T1, T2
8  TAA or SuperResolution, Post, OutputTransform, DisplayColorBuffer, Present
```

## 3. Metal-only verification

- Play captures and Editor.log windows used for S0–S5 were Metal.
- S6–S8 record paths exist in code. Temporal / translucent / output image gates are not closed.
- Metal HDR display encode is implemented in OutputTransform but hardware capture is `TODO(UNVERIFIED)`.
- D3D12 / Vulkan / non-Metal HDR are `TODO(UNVERIFIED)`.

## 4. TODO(UNVERIFIED)

- SSR/SSGI temporal quality, history rejection, cubemap-less HiC mip cone, still/moving framedump
- GTAO temporal quality and upsample edges
- Atmosphere cache-hit zero dispatch, SH energy, cubemap-array seams, still-frame IBL
- Fog/cloud temporal blend and CSM/local-shadow in volume lighting
- T0 glass / T1 refraction / T2 particle quality (Validation_Translucent)
- CombineLUT / OutputTransform Metal HDR hardware capture; D3D12/Vulkan
- HiZ / HalfRes / AtmoLUT async overlap (needs GPU Trace)
- ZBin overlap vs shadow raster (needs GPU Trace)
- Frame Debugger tree for draw-less Transfer / empty DBuffer / empty LocalShadow
- TAA blend 0.97, remaining thin-geometry ghosting on a moving camera
- Directional intensity `.rgb` vs `.a` consumer convergence (own change + capture)

## 5. Excluded from this delivery

| Item | Why |
|------|-----|
| Hardware RT (RTAO / RTGI) | Files stay; no RG pass. Out of scope. |
| Baked GI | Not implemented. |
| DOF | Not in RecordRG. README no longer lists it as completed. |
| Super-resolution expansion | Asset flag + pass exist; quality / history not a closed gate. |
| XR | VFX camera settings stay viewCount = 1. No XR path. |
| MSAA | Texture descriptors default to no MSAA. |
| Dynamic resolution | No scaler / dynamic viewport owner. |

## 6. S9 leftover delete (zero-ref)

Deleted only after `rg` showed no remaining references:

- `ScreenSpaceReflectionGenerator.cs` / `ScreenSpaceIndirectDiffuseGenerator.cs` / `ScreenSpaceAmbientOcclusionGenerator.cs` / `SpatialTemporalVarianceFilterGenerator.cs` — replaced by RG passes
- `DummyShaders.cs` — empty unused struct
- `ComputeCompress.cs` + `ComputeCompress.compute` — unused MonoBehaviour + shader
- `Pending.txt` — placeholder
- `UtilityPass.RenderSkyBox` + `CustomSamplerId.RenderSkyBox` — never recorded
- `CustomSamplerId.CopyMotionDepth`, `EPipelineProfileId.BeginFrameRendering` / `EndFrameRendering` — zero references

Kept: `TemporalAntiAliasingGenerator` (CameraUniform / AntiAliasingPass still call it), RTAO/RTGI files.
