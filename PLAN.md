# InfinityRenderPipeline - Full Rendering Pipeline Implementation Plan

## 1. Design Evaluation & Optimized Pipeline Architecture

### Current State Analysis
**Already Implemented (C# + Shader):**
- RasterPreDepthBuffer (DepthPass.cs)
- RasterGBuffer (GBufferPass.cs)
- RasterMotionVector - Object + Camera (MotionPass.cs)
- RasterForwardOpaque (ForwardPass.cs)
- ComputeColorLUT (CombineLutPass.cs)
- TAA (AntiAliasingPass.cs + TemporalAntiAliasingGenerator.cs)
- SkyBox rendering (UtilityPass.cs)
- Present (UtilityPass.cs)

**Shader Exists, C# Pass NOT Wired:**
- GTAO (Compute_GroundTruthOcclusion.compute + Volume params)
- SSR (Compute_ScreenSpaceReflection.compute + Volume params)
- SSGI (Compute_ScreenSpaceIndirectDiffuse.compute - partial)
- HiZ/Depth Pyramid (Compute_PyramidDepth.compute)
- Color Pyramid (Compute_PyramidColor.compute)
- Atmosphere (Compute_AtmosphericalFog.compute - stub)

**Not Implemented:**
- DBuffer, Shadows, ZBinning, ContactShadow, AtmosphericLUT, VolumetricFog,
  VolumetricCloud, DeferredShading, BurleySubsurface, AtmosphericSkyAndFog,
  TranslucentDepth, ForwardTranslucency, SuperResolution, PostProcessing,
  HalfRes Downsample

### Optimized Pipeline with Async Compute Scheduling

```
GPU Timeline (Graphics Queue ─── | Async Compute Queue ═══)

═══════════════════════════════════════════════════════════════
 PHASE 1: BASE GEOMETRY (Raster-heavy → perfect async overlap)
═══════════════════════════════════════════════════════════════
 [Graphics] RasterPreDepthBuffer
    ↕ [Async] ComputeColorLUT          ← 32³ ALU-bound LUT, zero bandwidth contention
 [Graphics] RasterDBuffer              ← Decals on depth/normal
 [Graphics] RasterGBuffer              ← Fill material data
 [Graphics] RasterMotionVector         ← Object + Camera
    - RasterObjectMotion
    - RasterCameraMotion

═══════════════════════════════════════════════════════════════
 PHASE 2: SHADOWS (Vertex/raster-bound → best async window)
═══════════════════════════════════════════════════════════════
 [Graphics] RasterCascadeShadowMap     ← Vertex/rasterizer-heavy
 [Graphics] RasterLocalShadowMap       ← Point/Spot shadow atlases
    ↕ [Async] ComputeHiZ              ← Depth pyramid, compute-bound mip gen
    ↕ [Async] ComputeHalfResDownsample ← Half-res depth+normal
    ↕ [Async] ComputeAtmosphericLUT   ← Transmittance/Scattering, pure ALU

═══════════════════════════════════════════════════════════════
 PHASE 3: SCREEN-SPACE EFFECTS (Compute-dominated after sync)
═══════════════════════════════════════════════════════════════
 ── Sync Point: HiZ + HalfRes + AtmosphereLUT ready ──
 [Compute] ComputeZBinningLightList    ← Tile/cluster light assignment
 [Compute] ComputeGroundTruthOcclusion ← GTAO from half-res (4-kernel pipeline)
 [Compute] ComputeContactShadow       ← Per-light screen-space shadows
 [Compute] ComputeScreenSpaceReflection ← HiZ ray-traced SSR
 [Compute] ComputeScreenSpaceIndirect  ← HiZ ray-traced SSGI

═══════════════════════════════════════════════════════════════
 PHASE 4: LIGHTING RESOLVE + OPAQUE (Mixed workloads)
═══════════════════════════════════════════════════════════════
 [Compute] ComputeDeferredShading      ← Tiled deferred resolve (GGX + AO + shadows)
 [Graphics] RasterForwardOpaque        ← Non-deferred opaques
    ↕ [Async] ComputeBurleySubsurface  ← SSS diffusion kernel, overlaps forward raster

═══════════════════════════════════════════════════════════════
 PHASE 5: ATMOSPHERE + VOLUMETRICS
═══════════════════════════════════════════════════════════════
 [Graphics] RasterAtmosphericSkyAndFog ← Sky + aerial perspective
 [Compute] ComputeVolumetricFog        ← Froxel ray-march
 [Compute] ComputeVolumetricCloud      ← Ray-marched cloud layer

═══════════════════════════════════════════════════════════════
 PHASE 6: TRANSLUCENT
═══════════════════════════════════════════════════════════════
 [Graphics] RasterTranslucentDepth     ← Translucent depth prepass
 [Compute] ComputeColorPyramid         ← Mip chain for refraction
 [Graphics] RasterForwardTranslucency  ← Translucent rendering

═══════════════════════════════════════════════════════════════
 PHASE 7: POST-PROCESSING
═══════════════════════════════════════════════════════════════
 [Compute] ComputeSuperResolution      ← Temporal upscaling (TAA replacement)
 [Compute] ComputePostProcessing       ← Bloom, DOF, color grading, tonemapping
 [Transfer] Present
```

### Key Async Compute Rationale:
1. **Phase 1 Async**: ComputeColorLUT is pure ALU on a tiny 32³ texture - zero bandwidth
   pressure while depth prepass is vertex/rasterizer-bound.
2. **Phase 2 Async**: Shadow rendering is vertex/rasterizer-bound (repeated geometry
   submission). HiZ/HalfRes/AtmLUT are compute-bound with modest bandwidth - excellent overlap.
3. **Phase 4 Async**: BurleySubsurface is a diffusion kernel (compute) overlapping with
   forward raster (vertex/pixel-bound).
4. **NOT Async**: SSR/SSGI/GTAO - these are bandwidth-heavy passes that would compete with
   each other. Better to run them sequentially on the main timeline.

---

## 2. Implementation Plan (Ordered by Dependencies)

### Step 1: Infrastructure Updates
- Add new CustomSamplerId entries for all new passes
- Add new InfinityShaderIDs for new buffer references
- Add new InfinityPassIDs for new shader tag IDs (ShadowPass, DBufferPass, etc.)
- Add new ComputeShader references to InfinityRenderPipelineAsset
- Add new Volume components for features that need runtime parameters

### Step 2: ComputeHiZ Pass (Async)
- Wire existing Compute_PyramidDepth.compute to a new HiZPass.cs
- Generate min/max hierarchical Z-buffer from depth buffer
- Enable async compute (overlaps with shadow rasterization)

### Step 3: ComputeHalfResDownsample Pass (Async)
- New compute shader: Compute_HalfResDownsample.compute
- Downsample depth to half-res, reconstruct normals from depth at half-res
- Enable async compute

### Step 4: RasterCascadeShadowMap
- New CascadeShadowPass.cs + compute cascade splits
- Render depth-only passes for each cascade into shadow atlas
- Shadow matrix computation, cascade blending

### Step 5: RasterLocalShadowMap
- New LocalShadowPass.cs
- Point light (cubemap) and spot light (single face) shadow maps
- Shadow atlas management

### Step 6: ComputeZBinningLightList
- New Compute_ZBinningLightList.compute + ZBinningPass.cs
- Z-bin + tile light assignment using structured buffers
- Wave intrinsics for efficient binning

### Step 7: ComputeGroundTruthOcclusion Pass
- Wire existing GTAO compute shader to new GTAOPass.cs
- 4-kernel pipeline: Trace → SpatialX → SpatialY → Temporal
- Half-res with bilateral upsample

### Step 8: ComputeContactShadow
- New Compute_ContactShadow.compute + ContactShadowPass.cs
- Per-pixel screen-space ray march toward each light
- Uses depth buffer for occlusion testing

### Step 9: ComputeScreenSpaceReflection Pass
- Wire existing SSR compute shader to new SSRPass.cs
- HiZ ray tracing + spatial/temporal filtering

### Step 10: ComputeScreenSpaceIndirect Pass
- Complete existing SSGI compute shader + new SSGIPass.cs
- HiZ ray tracing for indirect diffuse + temporal accumulation

### Step 11: ComputeDeferredShading
- New Compute_DeferredShading.compute + DeferredShadingPass.cs
- Tiled deferred: GBuffer decode → lighting accumulation
- Direct light (directional/local) + indirect (SH/probes + SSGI) + AO + shadows

### Step 12: ComputeBurleySubsurface (Async)
- New Compute_BurleySubsurface.compute + SubsurfacePass.cs
- Burley normalized diffusion profile convolution
- Uses existing LUT from DrawSystemLUT.shader

### Step 13: RasterDBuffer
- New DBufferPass.cs + shader changes for decal projection
- Project decals onto depth buffer, write to DBuffer textures
- Integrate DBuffer sampling into GBuffer pass

### Step 14: RasterAtmosphericSkyAndFog
- New AtmosphericSkyFogPass.cs
- Complete existing atmosphere compute shader for LUT generation
- Sky rendering with aerial perspective and fog integration

### Step 15: ComputeAtmosphericLUT (Async)
- New Compute_AtmosphericLUT.compute + AtmosphericLUTPass.cs
- Transmittance LUT + multi-scattering LUT
- Precomputed atmospheric scattering tables

### Step 16: ComputeVolumetricFog
- New Compute_VolumetricFog.compute + VolumetricFogPass.cs
- Froxel-based volumetric fog with temporal reprojection
- Light scattering integration per froxel

### Step 17: ComputeVolumetricCloud
- New Compute_VolumetricCloud.compute + VolumetricCloudPass.cs
- Ray-marched volumetric cloud layer
- Noise-based density, temporal reprojection

### Step 18: RasterTranslucentDepth + ForwardTranslucency
- New TranslucentPass.cs
- Depth prepass for translucent objects
- Forward rendering with refraction (samples color pyramid)

### Step 19: ComputeColorPyramid
- Wire existing Compute_PyramidColor.compute to new ColorPyramidPass.cs
- Gaussian mip chain from lighting buffer for refraction/SSR

### Step 20: ComputeSuperResolution
- New Compute_SuperResolution.compute + SuperResolutionPass.cs
- Temporal upscaling combining TAA with spatial upsampling
- Replaces current TAA pass

### Step 21: ComputePostProcessing
- New Compute_PostProcessing.compute + PostProcessingPass.cs
- Bloom (downsample + upsample chain)
- Depth of Field
- Final color grading (apply CombineLUT)
- Tonemapping + output encoding

### Step 22: Pipeline Integration
- Update InfinityRenderPipeline.Render() to call all passes in correct order
- Proper async compute fencing
- Resource lifetime management
- Volume parameter wiring
