# Feature status

The plain account of the 2026-11-17 CPU and allocation work is [CPU time](CpuTime.md). The plain account of raster deferred shading is [Raster deferred shading](RasterDeferred.md). The sections below are the measurement record.

### Native terrain and emissive stencil follow-up — 2026-11-27

Unity `6000.6.0f1`, Apple M3 Max, Metal, with independent APFS project libraries. The Example snapshot is `c66ee7d`. The compute baseline uses the package at `46139510`, the matching global settings asset, and the three instanced materials from before `5b65a36`. Evidence paths are relative to the task run roots at collection time.

| Gate | Result | Evidence |
|---|---|---|
| Native Terrain and decals | Pass | `candidate/terrain-depth-store/capture.json`, `terrain-stencil/capture.json`, `terrain-basemap/capture.json`, and the Game window showed the near splat layers and distant basemap, terrain stencil gray, the green clustered decal and red Direct decal. At the overlapping terrain pixel the DBuffer was blue `(5, 97, 242, 255)` and GBuffer A kept the terrain `(128, 26, 122, 55)`. The exposed part of the same native mesh received the blue decal. The Direct test pixel changed GBuffer A to `(217, 25, 13, 55)`. The final graph records the Direct draw outside the native GBuffer pass. `batch1-terrain-defaults/results.xml` passed 7/7. |
| Deferred emission admission | Pass | `candidate/batch1-emissive-final/results.xml` passed both production pass draws. The stale-enabled forward pixel kept Scene Color `(0.125, 0.25, 0.375, 1)`. The deferred pixel read `(1.125, 0.25, 1.375, 1)`. `batch1-shaders.json` recorded a compiled emissive pass with zero messages for InfinityLit and generated Shader Graph Lit. |
| Spazon Game view and Surface Stencil | Pass | `candidate` and `baseline` each captured `spazon-locked-{beauty,stencil}` at game time zero, with matching view and projection matrices and the same nine authored rotations. Viewed both Game windows. The Surface Stencil buffers matched byte for byte. DisplayColor mean absolute linear RGB difference was `0.00018634`, with maximum `0.05908203`. |
| Clustered decal across surface routes | Pass | `candidate/spazon-locked-fixture/capture.json` and its Game window show the green stripe on both the deferred and forward emissive controls. Their stripe centers both read `(5, 184, 36)` in GBuffer A RGB. The compute baseline shows the stripe on the deferred control. |
| Emission with GTAO, SSGI and direct light | Pass | `candidate/{effects-on,gtao-off,ssgi-off,light-off}/capture.json`. Across the three GTAO/SSGI conditions at game time zero, the packed `Lighting − DeferredLighting` difference varied by at most `(0.00226, 0.015625, 0)` RGB, within one encoding step per channel. With the fixture light disabled, green in `DeferredLighting` changed from `1.046875` to `0.46484375` at the same emissive pixel. The enabled-effect captures have nonzero SSGI and occlusion below 1. |
| Native Renderer and MeshComponent | Pass with identical lighting inputs | `candidate/spazon-parity-{native,mesh}-common/capture.json` and the Game windows show the same object at the same pose. The 40×40 interior ROI matched exactly in DisplayColor, Lighting, Depth and both GBuffers. The fixture removed the scene's 279 legacy probes in Play mode to give both draws the same lighting inputs. With the original scene probes, the native Renderer uses its own baked probe path and its Lighting differs. |
| Full EditMode | Pass for executable tests | `candidate/editmode-{1,2}/results.xml` each completed with 1,029 passed and zero failures out of 1,030 discovered. `ShaderStrippingTests` were 16/16 in both runs. The first run resumed after a synchronous Metal shader-variant compilation observed in `editmode-1/editor-sample.txt`. Both runs ended in the same Editor. |
| Cross-queue GPU test | Blocked | The existing `CrossQueueHazardsAndPhysicalReuse_PreserveValues` requires independent async compute. Both XML reports contain the hardware skip. |
| DX12 and Vulkan | Not run | Unity's stencil documentation specifies green for DX12. The installed Core RP helper selects green with Unity's shared Direct3D shader define and red for Metal and Vulkan. The candidate compiled the Metal debug kernel with zero messages in `candidate/stencil-shader.json`. |

The terrain Frame Debugger enumerated 21 events, including `RasterDBuffer` and `RasterDirectDecals`. Native event payload collection timed out at event 1. The completed graph transfer and Game window establish the image checks above.

The source project saved no assets. The baseline-only resource and material files were restored from Git into its APFS copy. Both validation Editors stopped after readback retirement. The two `intermediate/raster-deferred-round3` directories were removed after recording these results. Both project libraries were retained.

#### Spazon GPU frame time

Game camera at 1698×1750. `FrameTimingManager.gpuFrameTime`, 45 warmup timings and 80 positive frame samples per window. Candidate and `46139510` alternated with the other task Editor paused. Each entry is a window median in milliseconds. The last column is the median of the four window medians. The accepted runs are `candidate/round-{1..4}.csv` and `baseline/round-{1..4}.csv`.

| Build | Round 1 | Round 2 | Round 3 | Round 4 | Median |
|---|---:|---:|---:|---:|---:|
| Candidate | 112.1665 | 112.6836 | 112.3143 | 112.9228 | 112.4990 |
| `46139510` | 1.1563 | 1.1564 | 1.1563 | 1.1565 | 1.1564 |

### Direct GBuffer decal blend feasibility

| Gate | Result | Evidence |
|---|---|---|
| ShaderLab dual-source factors | Unsupported in Unity 6000.6.0f1 | The [Unity 6.6 Blend reference](https://docs.unity.com/en-us/engine/6000.6/manual/materials-and-shaders/shaders/reference/sl-reference/sl-commands/sl-blend) lists the available factors without `Src1Color` or `Src1Alpha`. In the Metal Editor, `ShaderUtil.CreateShaderAsset` and synchronous `ShaderUtil.CompilePass` accepted the single-target `Blend One Zero, One Zero` control with zero diagnostics. `Blend One Src1Color, One Src1Alpha` and `Blend One One, One Src1Alpha` both failed ShaderLab parsing at line 2 with `unexpected TVAL_ID, expecting TVAL_VARREF or TVAL_BMODE`. Neither variant exposed a pass to compile. Raw results were recorded in `candidate/dual-source.json` and `candidate/dual-source-alpha.json`. |

The parser rejected the render state before Metal could draw it. Direct GBuffer decals retain albedo and metallic blending and the Replace Normal clip pass. Use the DBuffer target for soft roughness and specular. The proposed dual-source numeric cases did not run because the state could not compile. DX12 and Vulkan did not run.

### Raster deferred shading — 2026-11-27 (Unity 6000.6.0f1, Apple M3 Max, Metal)

Package `main` `05e7189a`. Example global settings `c66ee7d` points `deferredShadingShader` at `Shaders/Pass/Lighting/DeferredShading.shader`. Contract in ARCHITECTURE.md §8 and §16. The readable account, including which example assets were saved and which local material edits were left uncommitted, is [Raster deferred shading](RasterDeferred.md).

| Gate | Result | Evidence |
|---|---|---|
| Static checks | Pass | `static_check.py` 0 errors, 0 warnings. `test_static_check.py` 53/53. |
| C# compile | Pass | Editor refresh loaded Runtime, Editor, Tests, Validation and CLI assemblies. The console window after that refresh had no CS error. |
| Shader compile | Pass | `ShaderUtil.GetShaderMessages` reported 0 errors for `DeferredShading.shader`, `InfinityLit.shader` and `InfinityDecal.shader`. |
| Deferred GPU tests | Pass | `DeferredLightLoopGpuTests` 19/19. Covers light accumulation, weight split, AO on I, packed Scene Color, SSGI confidence, pixels without bit 1, and 19×19 coverage. |
| Route and decal tests | Pass | `MaterialRouteTests` 21/21. `ReflectionAndDecalTests` 4/4 after the cleanup fix in `a9651337`. |
| Full EditMode, two runs | Fail | Run 1: 1014 passed, 11 failed, 1 skipped, of 1026. Run 2: 1015 passed, 10 failed, 1 skipped. The decal failure is gone in run 2. The remaining 10 are `ShaderStrippingTests` aborted by Unity's log `attempt to write a readonly database` from `ShaderUtil.ShaderHasError`. The project Library is on exFAT (`/Volumes/DataDisk`). No assertion inside those tests failed. |
| Game view | Pass | Play mode on Scene_Spazon. The courtyard, banners, character and direct light are visible. No black frame and no missing geometry. |
| Frame tree | Pass | 137 events. `RasterDeferredShading` is `DrawProcedural` immediately after SSGI upsample. No `ComputeDeferredShading` and no `InfinityLit-Instanced`. Emissive and forward recorded no draws: no deferred material had nonzero emission, and this scene has no forward draw. Frame Debugger omits empty passes. |
| GPU time | Dropped | No alternating baseline against `46139510`. |

### Opaque color path convergence — 2026-11-26 (Unity 6000.6.0f1, Apple M3 Max, Metal)

Source branch `claude/happy-feynman-ekmpz3`, starting at `dc9a5464`. Runtime contract in ARCHITECTURE.md §§4, 8, 11 and 13. The candidate and exact `04f5a570` baseline ran in isolated APFS projects with the same pre-migration Spazon scene and Game view dimensions. The explicit EVSM and skin cases used a temporary Play Mode scene, volume, diffusion profile and sun filter override alongside Spazon. The run paths below identify raw artifacts at collection time. This section preserves the verified results after task cleanup.

#### Verification

| Gate | Result | Evidence |
|---|---|---|
| Static checks | Pass | `static_check.py` reported 0 errors and 0 warnings. `test_static_check.py` passed 52/52. |
| C# compile and reload | Pass | `run-01/refresh-final.txt` verified fresh Runtime, Editor, Tests, Validation and CLI DLLs. `Application.consoleLogPath` reported 0 CS errors in the marked window. |
| Shader compilation | Pass | `run-03/shader-diagnostics-final.txt` recorded 28 assets with 0 errors, including forced raster pass compilation, the corrected Temporal Color Source compute fixture and a generated Lit Shader Graph. |
| EditMode and GPU | Pass | `run-03/editmode-final/results.xml` reported 1023 passed, 0 failed and 1 skipped, matching `run-01/editmode-03/results.xml`. `CrossQueueHazardsAndPhysicalReuse_PreserveValues` requires async compute, which the Metal device does not expose. The production deferred kernel read and wrote R11G11B10F in place and returned the expected 0.25 RGB. |
| Game frame tree | Pass | `run-01/spazon-fixture-frame/frame-tree.json` captured 172 native events. The final compiled graph in `run-01/final-reflection-on/` recorded ShadowMoments 30, SSR 31, SSGI 32, Deferred 33, Forward 34, both SSGI copies 37–38 and Scatter passes 39–40. `ComputeScreenSpaceComposite` was absent. The native Extract and Scatter draws each used three vertices. |
| Game image and attachment readback | Pass | Viewed the actual Editor Game window, then the graph-transfer captures from `MainCamera`, 1698×1750. `run-01/spazon-beauty/capture.json` reported 0 non-finite values for each of nine buffers, including R11G11B10F DeferredLighting and Lighting, the two RGBA8 GBuffer targets and RGBA8 Sideband. |
| Debug views | Pass | Viewed Surface Stencil, Diffuse Ratio, Rendering Layers and Mixed Light Visibility. `run-01/final-stencil/` recorded 91,311 gray deferred, 14,331 orange skin, 25,782 blue forward and 2,638,210 green-tinted deferred pixels. Diffuse Ratio was 0 on sky, 1.0 at the forward sphere center and 254/255 at the skin sphere center. Mixed Light Visibility varied the first baked slot across the scene, with the second slot fully visible. |
| GTAO, SSGI, SSR and scattering | Pass | Viewed the five control and single-effect-off DisplayColor captures in `run-01/effects-*/` and the final SSR on/off pair. GTAO on/off produced identical RGB at the emission control center. The forward skin reflector had SSR confidence 1 and SSGI confidence 0.113 at the sampled pixel. Its Lighting RGB was (4.375, 0.213, 6.000) with SSR and (0.0161, 0.0159, 0.0205) with SSR disabled. The skin region also wrote nonzero extracted irradiance. Turning scattering off changed packed Lighting RGB by 0.000173 mean absolute per-channel difference over its 4,053-pixel control region. |
| Source asset migration | Partial | Saved the pipeline asset and 29 routed materials. The protected `Scene_Spazon` contains 37 masks with value `0x21`; the migration replica clamped all 37 to `0x01` and a second invocation changed zero. The protected scene still triggers the source layer validator on entering Play. |

#### GPU frame time

`FrameTimingManager.gpuFrameTime`, 45 warmup frames then 80 positive samples per window, candidate and base alternated, the other Editor paused. Each entry is that window's median. The summary takes the median of the four window medians.

| Build | Round 1 | Round 2 | Round 3 | Round 4 | Median |
|---|---:|---:|---:|---:|---:|
| Candidate | 12.9329 ms | 12.9186 ms | 12.9384 ms | 12.9311 ms | 12.9320 ms |
| `04f5a570` | 18.4801 ms | 18.4649 ms | 18.4931 ms | 18.4808 ms | 18.4805 ms |

The candidate used 5.5485 ms less GPU time per frame, a 30.02% reduction. The windows are `run-01/branch-final-round-{1..4}.txt` and `run-02/base-final-round-{1..4}.txt`.

#### Example assets saved

The source project received the exact bytes verified in its independent APFS copy. The pipeline asset kept its active serialized values, GUID and local ID. Every material's only serialized field change was its disabled shader pass list. The second save was byte-identical and the separate repeat route operation was a no-op. No legacy InfinityLit materials had SSS enabled.

- `Assets/Profile/RenderPipelineAsset.asset` and `Assets/Animation/Anim_MAT.mat`.
- Under `Assets/Material/` the saves were `M_Black.mat`, `M_BoxA.mat`, `M_BoxA_Instance.mat`, `M_BoxB.mat`, `M_BoxB_Instance.mat`, `M_BoxC.mat`, `M_BoxC_Instance.mat`, `M_MaskA.mat`, `M_MaskB.mat`.
- Under `Assets/Material/Spazon/` the saves were `M_Spazon_Arch.mat`, `M_Spazon_Brick1.mat`, `M_Spazon_Ceilling.mat`, `M_Spazon_Column1.mat`, `M_Spazon_Column2.mat`, `M_Spazon_Column3.mat`, `M_Spazon_Fabrick1.mat`, `M_Spazon_Fabrick2.mat`, `M_Spazon_Fabrick3.mat`, `M_Spazon_Fabrick4.mat`, `M_Spazon_Fabrick5.mat`, `M_Spazon_Fabrick6.mat`, `M_Spazon_Flagpole.mat`, `M_Spazon_Floor.mat`, `M_Spazon_Lion1.mat`, `M_Spazon_Lion2.mat`, `M_Spazon_Roof.mat`, `M_Spazon_Vase.mat`, `M_Spazon_Window.mat`.

The channel migration replica was saved at `Assets/Scene/Spazon/__OpaqueColorChannelMigration.unity` within each migration mirror. The original source scene's SHA-256 remained `6c7f075f86e1cae95f70561d72fd6ee4c49f66e4d0c64ecd7f764cc5175ae03e`. Source asset hashes and save paths were recorded in `run-03/migration-published.tsv`; scene and material no-op checks were recorded in `run-03/migration-result.tsv`.

One repeated test run in `run-01/editmode-final/` stalled in native `AsyncGPUReadbackBuffer::Update` while running `FilmGrain_PreservesMeanAndBlackWithoutBloomBinding(0.02f)`. The separately owned migration mirror completed the final suite. D3D12 and Vulkan remain TODO(UNVERIFIED).

#### Sideband clear and Editor readback follow-up

The Metal follow-up used task-owned `opaque-color-round2-20261126/{candidate,base}` APFS mirrors. Evidence paths below are relative to that run root and identify the raw artifacts at collection time.

| Gate | Result | Evidence |
|---|---|---|
| C# compile and reload | Pass | `candidate/refresh.txt` confirmed all five InfinityRP assemblies current, with zero CS errors in the new Editor log window. |
| Uncovered sideband GPU regression | Pass | `candidate/negative-clear/results.xml` failed at discarded pixel (1, 0) with alpha 255 when the descriptor used the default clear. With the production zero clear, the test passed in both full runs. |
| Rendering Layers and Subsurface Profile | Pass | `candidate/{RenderingLayers,SubsurfaceProfile}/capture.json` and the Depth, Sideband and DisplayColor readbacks show black sky with all four sideband bytes zero, across 143,866 and 143,932 zero-depth pixels respectively. |
| EditMode on the same Editor twice | Pass | `candidate/editmode-{1,2}/results.xml` each recorded 1,024 passed, zero failed and one existing hardware skip for async compute. Both runs exited normally. The previous `run-01/editmode-final/editor-sample.txt` shows the FilmGrain test waiting in the native Metal readback update. The shared color-test fixture now queues the callback behind the draw or dispatch and yields the Editor update loop while retaining resources. Numeric assertions are unchanged. |
| DisplayColor and SSR comparison | Pass | Both builds show the upper-left flag's purple speckles and the upper-right pink flag shape. `base/None-base-lock/` and `candidate/None-candidate-repeat/` contain DisplayColor. `{base,candidate}/SSR-lock/` contains the SSR debug frames. Both use the same Spazon snapshot, Game camera matrices and scene time zero. The baseline uses its InfinityLit SSS route for the temporary skin geometry. |
| Surface Stencil comparison | Existing behavior in both | `base/ShadingModel-stencil/` and `candidate/SurfaceStencil/` each contain 2,638,173 green-tinted deferred pixels at the same temporal jitter. The legacy debug probe enabled a stencil view and overlaid bit 0 over Shading Model without changing that build's motion submission. `candidate/motion-inventory.tsv` lists 45 static native renderers still configured for object motion with the motion pass enabled. The opaque renderer list includes them, and the depth-equal motion pass writes bit 0 even when velocity is zero. |

The first candidate capture showed a cyan emission control. Two repeat captures, including one using the original capture buffers, both showed the baseline's pink control. SSR was zero at the control sample in all three candidate captures.


#### Convergence follow-up — static gate only

After `e073562`: `DeferredLightLoopGpuTests` and `SubsurfaceGpuTests` await their readbacks through `GpuReadbackTestUtility.Await` instead of `WaitForCompletion`, keeping their targets alive when a request does not finish; the B10G11R11 Scene Color Sample exemption is limited to Metal; the Hi-Z interval sentence moved to the shared trace description in ARCHITECTURE §11. `static_check.py` 0 errors, 0 warnings; `test_static_check.py` 52/52. Editor compile and the two converted fixtures: TODO(UNVERIFIED). 22 other test files still call `WaitForCompletion`; none blocked in the two full runs above.

### Phase P2: Camera State Extensions, Unified TRC, 64-bit MotionMetadata & Inspector/Debugger UI — 2026-11-26 (macOS Metal, Unity 6000.6.0f1, APFS mirror)

Verified against `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` (Apple M3 Max, Metal, Unity `6000.6.0f1`), keeping source project assets and `Scene_Spazon` (`dirty: false`) untouched.

- **Static & metadata gates**: `python3 -B Tools/StaticCheck/static_check.py --repo .` -> `0 error(s), 0 warning(s)` (including `Runtime/Temporal` layering and hot-path rules); `test_static_check.py` -> `52/52 OK`; `GpuRecordLayout.generated.cs` current.
- **Shader diagnostics (`ShaderUtil`)**: `Compute_TemporalAntiAliasing.compute`, `Compute_GroundTruthOcclusion.compute`, `Compute_ScreenSpaceReflection.compute`, `Compute_ScreenSpaceIndirectDiffuse.compute`, `Compute_DeferredShading.compute`, `InfinityLit.shader`, `InfinityLit-Instanced.shader`, and `DrawFullScreen.shader` -> `0` error issues (`SHADER_ISSUES=0`).
- **EditMode & Metal GPU contract tests (`112/112` passed, `0` failed, `0` skipped)**: `TemporalAndCameraStateTests` (`3/3`), `CameraFrameStateTests` (`11/11`), `MeshOcclusionIntegrationTests` (`1/1`), `TemporalResolveGpuTests` (`14/14`), `GtaoTemporalGpuTests` (`18/18`), `AnalyticShadowTests` (`3/3`), `ReflectionAndDecalTests` (`3/3`), `ReflectionProbeTests` (`7/7`), `DBufferCompositeGpuTests` (`1/1`), `GBufferContractTests` (`13/13`), and `GpuRecordLayout` (`38/38`).
- **Metal PlayMode capture (`Scene_Spazon`, `993×1330`, `MainCamera`)**: `spazon-capture` `Completed` (`nonFinite: 0` across `Lighting`, `DisplayColor`, `AntiAliasing`, `Motion` `R16G16_SFloat`, `MotionMetadata` `R32G32_SFloat`, `GBufferA` `R8G8B8A8_SRGB`, `GBufferB` `R8G8B8A8_UNorm`, `Depth` `R32_SFloat`, `TAAConfidence` `R32G32B32A32_SFloat`), `DisplayColor.png` & `Lighting.png` visually inspected cleanly, `0` InfinityRP/Shader/Metal errors in `Editor.log`.

### Phase P1-B: Analytic Shadow & Occlusion — 2026-11-26 (macOS Metal, Unity 6000.6.0f1, APFS mirror)

Verified against `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` (Apple M3 Max, Metal, Unity `6000.6.0f1`), keeping source project assets and `Scene_Spazon` (`dirty: false`) untouched.

- **Static & metadata gates**: `python3 -B Tools/StaticCheck/static_check.py --repo .` -> `0 error(s), 0 warning(s)` (including `Runtime/AnalyticShadow` layering and hot-path rules); `test_static_check.py` -> `52/52 OK`; `GpuRecordLayout.generated.cs` updated (`AnalyticOccluderRecord` 112B, `AnalyticShadowUniform` 48B).
- **Shader diagnostics (`ShaderUtil`)**: `Compute_DeferredShading.compute`, `Compute_AnalyticShadowContract.compute`, `InfinityLit.shader`, and `InfinityLit-Instanced.shader` -> `0` error issues (`SHADER_ISSUES=0`).
- **EditMode & GPU contract tests**: `AnalyticShadowTests` (`3/3`, covering Geometry vs Indirect vs Light-Extruded Direct Shadow bounds, zero-GC `AnalyticShadowContext.Build`, and Metal GPU contract separating Indirect Occlusion & Direct Soft Shadow across Sphere/Capsule/Box/Ellipsoid + `ERenderingLayer` filtering) and `GpuRecordLayout` (`38/38`) passed (`41/41`).
- **Metal PlayMode capture (`Scene_Spazon`, `993×1330`, `MainCamera`)**: `spazon-capture` `Completed` (`nonFinite: 0` across `Lighting`, `DisplayColor`, `GBufferA`, `GBufferB`, `SurfaceMetadata`, `Depth`, `TAAConfidence`), `DisplayColor.png` & `Lighting.png` visually inspected cleanly, `0` InfinityRP/Shader/Metal errors in `Editor.log`.

### Phase P1-A: Reflection & Decal Systems — 2026-11-26 (macOS Metal, Unity 6000.6.0f1, APFS mirror)

Verified against `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` (Apple M3 Max, Metal, Unity `6000.6.0f1`), keeping source project assets and `Scene_Spazon` (`dirty: false`) untouched.

- **Static & metadata gates**: `python3 -B Tools/StaticCheck/static_check.py --repo .` -> `0 error(s), 0 warning(s)` (including `Runtime/Decal` & `Runtime/Reflection` layering and hot-path rules); `test_static_check.py` -> `52/52 OK`; `GpuRecordLayout.generated.cs` updated (`DecalRecord` 128B, `DecalUniform` 48B).
- **Shader diagnostics (`ShaderUtil`)**: `Compute_DeferredShading.compute`, `Compute_DebugView.compute`, `Compute_DBufferContract.compute`, `Compute_DecalClusterContract.compute`, `InfinityDecal.shader`, `InfinityRasterReflection.shader`, `InfinityLit.shader`, and `InfinityLit-Instanced.shader` -> `0` error issues (`SHADER_ISSUES=0`).
- **EditMode & GPU contract tests**: `ReflectionAndDecalTests` (`3/3`, incl. `OrientedBox`/independent projection proxy/capture origin, zero-GC `DecalContext.Build`, and Metal `ClusteredDecal_And_RasterReflection_ComposeCleanly` GPU contract), `ReflectionProbeTests` (`7/7`), `DBufferCompositeGpuTests` (`1/1`), and `GpuRecordLayout` (`36/36`) all passed (`47/47`).
- **Metal PlayMode capture (`Scene_Spazon`, `993×1330`, `MainCamera`)**: `spazon-capture` `Completed` (`nonFinite: 0` across `Lighting`, `DisplayColor`, `GBufferA`, `GBufferB`, `SurfaceMetadata`, `Depth`, `TAAConfidence`), `DisplayColor.png` & `Lighting.png` visually inspected cleanly, `0` InfinityRP/Shader/Metal errors in `Editor.log`.

### Phase P0-B: Spatial Foundation & Reflection Decoupling — 2026-11-26 (macOS Metal, Unity 6000.6.0f1, APFS mirror)

Verified against `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` (Apple M3 Max, Metal, Unity `6000.6.0f1`), keeping source project assets and `Scene_Spazon` (`dirty: false`) untouched.

- **Static & layering gates**: `python3 -B Tools/StaticCheck/static_check.py --repo .` -> `0 error(s), 0 warning(s)` (including `Runtime/Spatial` & `Runtime/Reflection` layering and hot-path rules); `test_static_check.py` -> `52/52 OK`; `GpuRecordLayout.generated.cs` updated (`LightLoopUniform` 48B, `ReflectionUniform` 48B, `ReflectionProbeRecord` 112B).
- **Shader diagnostics (`ShaderUtil`)**: `Compute_DeferredShading.compute`, `Compute_DebugView.compute`, `Compute_ContactShadow.compute`, `Compute_VolumetricFog.compute`, `Compute_VolumetricCloud.compute`, `Compute_SpatialClusterContract.compute`, `Compute_CubemapArraySamplingContract.compute`, `InfinityLit.shader`, and `InfinityLit-Instanced.shader` -> `0` issues (`SHADER_ISSUES=0`) after single-exit fixes in `SpatialZBinTile.hlsl`, `SpatialClusterList.hlsl`, and `LightLoop.hlsl`.
- **EditMode & GPU contract tests**: `SpatialFoundationTests` (`4/4`, incl. zero-GC steady-state cluster rebuild and Metal `ClusterPriorityOrder` GPU readback), `ReflectionProbeTests` (`7/7`), `ReflectionProbeTransactionTests` (`11/11`), `DeferredLightLoopGpuTests` (`13/13` warm), and `GpuRecordLayout` (`34/34`) all passed (`69/69`).
- **Metal PlayMode capture (`Scene_Spazon`, `993×1330`, `MainCamera`)**: `spazon-capture` `Completed` (`successfulFrames: 31`, `capturedFrames: 1`, `nonFinite: 0` across `Lighting`, `DisplayColor`, `GBufferA`, `GBufferB`, `SurfaceMetadata`, `Depth`), `DisplayColor` & `Lighting` visually inspected cleanly, `0` InfinityRP/Metal errors in `Editor.log`.

### Phase P0-A: Surface, Lighting & Attachment Configuration — 2026-11-26 (macOS Metal, Unity 6000.6.0f1, APFS mirror)

Verified against `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` (Apple M3 Max, Metal, Unity `6000.6.0f1`), keeping source project assets and `Scene_Spazon` (`dirty: false`) unmodified.

- **Static & metadata gates**: `python3 -B Tools/StaticCheck/static_check.py --repo .` -> `0 error(s), 0 warning(s)`; `test_static_check.py` -> `52/52 OK`; `.meta`, `TextureViews.py --write`, and `GpuRecordLayout.generated.cs` up to date. `InfinityRenderPipelineAsset` uses additive serialized configuration fields only (`Compact64Bit` default, `SeparateBuffer` SSS default).
- **Shader & Editor compilation**: `Tools/RefreshUnityEditor.sh` -> `EDITOR_READY=1`, `ASSEMBLIES_CURRENT=1`, `REFRESH_STABLE=1`. `ShaderUtil` reported `0` errors and `0` warnings across the 11 core compute and surface shaders.
- **EditMode & GPU contract tests**: full run `editmode-tests-1` (`987` discovered: `983` passed, `1` pre-existing skipped `RenderGraphQueueGpuTests.CrossQueueHazardsAndPhysicalReuse_PreserveValues`, `1` pre-existing baseline failure `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`, plus `GBufferContractTests` `13/13` and `ShaderStrippingTests` `16/16` passing on targeted re-runs after fixes).
- **Metal PlayMode capture & CPU probes (`Scene_Spazon`, `1698×1750`, `MainCamera`)**: `spazon-capture-3` completed cleanly (`status: "Completed"`, `successfulFrames: 31`, `capturedFrames: 1`, `error: ""`, `nonFinite: 0` across all 9 captured buffers: `Lighting`, `DisplayColor`, `GBufferA` `R8G8B8A8_SRGB`, `GBufferB` `R8G8B8A8_UNorm`, `SurfaceMetadata` `R8G8_UNorm`, `Depth` `R32_SFloat`, `Occlusion` `R8_UNorm`, `SSR`, `SSGI`). Visually inspected GPU readback PNG conversions (`DisplayColor`, `GBufferA`, `GBufferB`, `Lighting`, `SurfaceMetadata`). CPU marker probe (`60` sample frames, `Release`): `Inl_RecordRG` `0.50 ms`, `Inl_ExecuteRG` `1.13 ms`, `Game` `nativeSubmitAllocationsMean: 0`, `alloc-stacks.txt`: `frame -1`.
- **TODO(UNVERIFIED)**: OS window capture via `Tools/CaptureUnityWindow.sh` (`macOS` Screen Recording permission unavailable in headless shell); `HighPrecision96Bit`, `BestFitYCoCg64Bit`, and `SceneColorAlpha` PlayMode scene captures (shader/layout unit coverage only); D3D12 and Vulkan backends.

### Lighting & shadow refactor (P0–P4) — 2026-11-25 (macOS Metal, Unity 6000.6.0f1, APFS mirror)

Verified against `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` (Apple M3 Max, Metal, Unity `6000.6.0f1`), keeping `/Volumes/DataDisk/Projects/Unity/InfinityExample` `ProjectSettings/*`, `InfinityExample.slnx`, and source `Assets/Scene/` untouched.

- **Static & schema gates**: `python3 -B Tools/StaticCheck/static_check.py --repo .` -> `0 error(s), 0 warning(s)`; `test_static_check.py` -> `52/52 OK`; `Tests/Editor/Fixtures/LightInspector.prefab` formally migrated (`Save #1 == Save #2`, GUID/local IDs preserved, `.meta` SHA-256 unchanged). `InfinityRenderPipelineAsset` serialized schema unchanged.
- **Shader diagnostics (`ShaderUtil.GetComputeShaderMessages`)**: `Compute_DeferredShading.compute`, `Compute_VolumetricFog.compute`, `Compute_VolumetricCloud.compute`, and `ShadowSamplingContract.compute` compiled with `0 errors, 0 warnings` on Metal.
- **EditMode & GPU contract tests**: `LightShapeTests`, `LightIntensityUnitTests`, `LightRecordTests`, `BakedGITests`, `LightShapeGpuTests`, `LightAngularGpuTests`, `DeferredLightLoopGpuTests`, `LocalShadowFrustumTests`, `ShadowSamplingGpuTests`, `ShadowAtlasLayoutTests` (including `0`-byte steady-state `GC.Alloc`), `ShadowResolutionHistoryTests` (`0`-byte steady-state `GC.Alloc`), `ShadowBudgetTests`, `LocalShadowCacheTests` (`0`-byte steady-state `GC.Alloc`), and `GpuRecordLayout` (`33/33` including `96`-byte `LocalShadowSliceGPU`) all passed after final edits.
- **RenderGraph PlayMode captures (`993×1330` `DisplayColor` + untonemapped `Lighting`)**: `Validation_AreaLights`, `Validation_ShadowShapes` (14-tile P3 `Rect`/`Line`/`Tube`/`Disk` PCSS, `nonFinite: 0` across `5,282,760` samples), and 19-tile mixed-tier `Validation_Shadows` (`100.0%` occupancy `[1024:1, 512:10, 256:8]`, `downgraded: 2`, `dropped: 0`, `cachedTiles: 1`, `renders: 0`, `nonFinite: 0`) visually inspected cleanly.
- **TODO(UNVERIFIED)**: OS window-chrome screenshot capture via `Tools/CaptureUnityWindow.sh` (`macOS` Screen Recording permission unavailable in headless shell); D3D12 and Vulkan backends untested on this host.

### Derived remainder view, second local verification — 2026-11-21 (macOS Metal, Unity 6000.6.0f1)

Scene_Spazon, GPU Indirect, Game focused at 993×1330, Scene views closed, still camera. Two branch → `7f64ef22` → `7f64ef22` → branch rounds used 120 deduplicated GPU frame timings per window after 90 warmup timings. These are whole-frame Editor timings, not isolated pass timings. The mean of the four window medians is 22.507 ms on the branch and 22.692 ms on the baseline; no window regressed by more than 5% against its round's higher baseline median. No pass-marker investigation was required.

| Window | GPU median (ms) | GPU p10–p90 (ms) | CPU median (ms) |
|---|---:|---:|---:|
| branch 1 | 22.528 | 22.029–23.670 | 8.323 |
| baseline 1 | 22.857 | 21.909–23.853 | 8.352 |
| baseline 2 | 22.837 | 22.077–23.763 | 8.323 |
| branch 2 | 22.652 | 21.602–23.475 | 8.304 |
| branch 3 | 22.394 | 21.720–23.555 | 8.345 |
| baseline 3 | 22.634 | 21.608–23.625 | 8.316 |
| baseline 4 | 22.440 | 21.609–23.317 | 8.320 |
| branch 4 | 22.455 | 21.791–23.473 | 8.335 |

The phase-locked moving-camera captures at the same resolution have two branch outcomes: `phasek-b1` and `phasek-b3` match each other; `phasek-b2` matches all three byte-identical baseline runs. Frame 1 matches. At frame 10 the first captured difference is DisplayColor (53 pixels, x 484–499, y 538–550, maximum 6 float16 ULP, none above 8/255); DeferredLighting, SSR, SSGI, Lighting and SceneColor match. At frame 30 DeferredLighting still matches and SSR first differs (56 pixels, exactly 1 float16 ULP). The graph is identical between the branch outcomes; no single resource has been identified as the cause. The earlier 1698×955 Lighting difference of about 0.12 was not reproduced by this 993×1330 protocol and is not established as fixed. Subsurface is disabled in this scene and was not captured.

The five `SRV_MeshViewDrawn` kernel 8 warnings occurred during shader reimport Submit when switching packages, paired with `_CommandMeta` kernel 6 warnings; stable Play did not emit them. The remainder dispatch binds `SRV_MeshViewDrawn` before dispatch. This evidence does not support adding a dummy binding.

Follow-up on the APFS mirror: Unity 6000.6 Mono `HttpListener` with the Pipeline package's wildcard prefix raised `SocketException` (address invalid); the explicit `127.0.0.1` prefix succeeded. A mirror-local Pipeline copy using loopback restored the authenticated CLI. Three new branch captures used Depth, Motion, MotionMetadata, TAAHistoryColor, TAAHistoryDepth and DisplayColor, with continuous steps and captures only at frames 1, 10 and 30. All six buffers were byte-identical across those three runs at each frame. Their frame-10 DisplayColor hash was `65fd863000a1ff03`, neither old A (`2d98e818a93b2265`) nor old B (`e822650ef61130e1`); even frame 1 differs from the old capture set although position, jitter and history-reset metadata match. The new capture set and reopened Editor do not reproduce either old outcome, so these inputs cannot identify the resource behind the old A/B difference. No Runtime change was made. Capture finished, the scene was not dirty or saved, and InteractionMode was 0. The scene's on-disk SHA-256 remained `f824ae3f3d0a1ba6423838ec598ca261ef915f1180a1d66ec31ad541fda83904`.

**Do not merge into `main`:** the branch image is not single-valued and the difference is unattributed. GPU time and the reimport warning are not current blockers. D3D12 and Vulkan remain unverified.

Further GPU-backend attribution, same mirror and Metal Editor: both the baseline and this branch ran two-stage occlusion. A temporary diagnostic in isolated copies captured Depth immediately after `RasterDepthRecovery`, before GBuffer: frames 1, 10 and 30 are byte-identical between `7f64ef22` and this branch, with zero differing depth texels. The diagnostic inserts a capture pass and is not part of the shipped graph. In a separate normal capture, Depth and GBuffer A/B/C also match byte for byte on all three frames, while SSR and DisplayColor differ; geometry agreement alone does not establish final-color equivalence.

`DerivedRemainder_PreservesCurrentLodAndFadeAcrossDispatchGroups` passed both dither modes on Metal: 129 entity slots span three thread groups and cover history exclusion, current invisibility, two LOD levels and stable or fading values against the CPU list semantics. `static_check` reports 0 errors and 0 warnings. After an Editor refresh, four original-protocol branch runs produced a third frame-10 hash on the first run and the old B hash on the next three; old A was not reproduced. A separate initial-frame difference was traced as far as BakedDiffuse before the two-stage path, but its cause and its relationship to the old A/B difference are not established. No remainder Runtime fix is justified by these observations. Original A/B attribution remains open and the merge decision above is unchanged.

### Derived remainder view, local Editor — 2026-11-20 (macOS Metal, Unity 6000.6.0f1)

The recovery view is derived. `CullMeshViewRemainder(drawnView, currentView)` records a view with no producer, and its depth list runs `BuildDrawListRemainder`, which reads `SRV_MeshViewDrawn` (the history view's words) and otherwise the main view's word. `MeshViewCullingRemainder` is deleted. This round did not change Runtime, shaders or tests. The static gate already recorded for `710d9e94` stands: `static_check` 0 errors, `test_static_check.py` 52 tests, and the closure of the deleted kernel. The source project is on exFAT. The Editor is pid 14169 on the APFS mirror `/Users/gushihao/UnityWork/InfinityExample-arch-20261118`, whose package symlink pointed at this checkout or at a worktree of `7f64ef22`. No scene was saved. D3D12 and Vulkan are not on this machine.

Compile: Runtime, Editor, Tests, Validation and Cli loaded with 0 CS errors. On Metal, `FindKernel` resolves `BuildDrawList` at 7 and `BuildDrawListRemainder` at 8, and `MeshViewCullingRemainder` is absent. The Editor log has five `Property (SRV_MeshViewDrawn) at kernel index (8) is not set` lines, each in the same `ScriptableRenderContext.Submit` as `SRV_MeshViewVisibility` at kernel 7 and `_CommandMeta` at kernel 6. That is five submits out of 116 play-mode enters, not one warning per frame. The `7f64ef22` session logs the same class of warning for `_MeshCullFurthest` at kernel 6, which is `MeshViewCullingRemainder` on that commit. The warning was not attributed to a remainder dispatch that omitted the bind.

EditMode, 907 tests: 905 passed, 1 failed, 1 skipped, 112.8 s. The failure is `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)` and the skip is `RenderGraphQueueGpuTests.CrossQueueHazardsAndPhysicalReuse_PreserveValues`. `MeshDrawListLifetimeTests` 35, `MeshOcclusionGpuTests` 5, `MeshOcclusionIntegrationTests` 1 and `MeshDrawProfilingTests` 6 passed.

Images, Scene_Spazon, Game view 1698×955, GPU Indirect. The history-locked frame (`historyReset` true, jitter 0, pose `(8, -1.25, 0.6)`) is byte-identical to `7f64ef22` in DisplayColor and Depth. The moving-camera protocol clears `CameraUniform.m_CommittedFrames`, repeats the same two cuts and two still frames, then moves; frames 1, 10 and 30 share pose and current and previous jitter with `7f64ef22`. Depth is byte-identical on those three frames. DisplayColor is not: frame 1 max absolute difference 0.045898 with 14 pixels above 8/255, frame 10 max 0.009033 with none, frame 30 max 0.019043 with none. A second `7f64ef22` run of that protocol is byte-identical to the first in both buffers, so the gap is not the baseline repeating itself. A three-frame set at the same phase has byte-identical GBuffer A, B, C and Depth on both branch runs. One of those runs is also byte-identical to `7f64ef22` in Lighting and DisplayColor; the other differs in Lighting and DisplayColor only. Two more branch runs match each other in DeferredLighting, SSR, SSGI, Lighting and Depth, and that Lighting matches `7f64ef22`. The pixels that move are the opaque color after lighting. GBuffer and depth do not. The cause was not attributed to one line, and the remainder view kernel was not restored.

Frame tree, Scene_Spazon, Game, Scene views closed, two-stage occlusion on: one `meshViews` scope, 6 dispatches (`MeshViewCullingOccludedHistory` ×1, `MeshViewCulling` ×4, `MeshViewCullingOccluded` ×1). Exactly one `ComputeMeshView` is between `ComputeEarlyFurthestHiZ` and `RasterDepthRecovery`, and its kernel is `MeshViewCullingOccluded`. The depth build scope is `BuildDrawList` ×1 and `BuildDrawListRemainder` ×1. There is no `MeshViewCullingRemainder`. On the two-stage still frame `MeshWorldStats.viewProductions` is 6, and on `7f64ef22` it is 7. The 6 is the history view, the main view against the early occluder, and the four cascade views, which is the same count as the frame tree. The counter is every GPU view producer of the last camera. It is not a separate main-camera field of 2, and it was not changed to become one. `drawListProductions` stays 8 and `drawListKernelDispatches` stays 16.

Allocations, Release, Game only, GPU Indirect, Scene views closed, warmup 120, sample 30, no capture pose handler. Game `frameAllocationsMean` is 1, 0, 0 and `nativeSubmitAllocationsMean` is 0. Scene view and other windows record 0 calls. A `GC.Alloc` recorder calibrated with one `new object()` per camera (40 of 40) then reads 0 for the frame preamble, camera enter, setup, record, execute and submit across the next 40 cameras. An earlier probe, taken while capture handlers were still subscribed, read 13, 12, 12; the same probe on `7f64ef22` with one handler read 3, 2, 2. Those two runs are not the measurement. On the paired marker logs, `Mesh.RecordViewProducer` is 6 against 7 and `RG.Execute.Create` and `RG.Execute.Release` are 45 against 46.

The initial GPU timing protocol reported medians of 0.208, 0.211, 0.216 and 0.264 ms. These measurements are invalid and do not establish a performance result; the second local verification above replaces them.

Not merged into `main`. Depth, GBuffer and the frame tree match the derived view. DisplayColor is not stably byte-identical to `7f64ef22`; see the second verification above for the performance result and outstanding image attribution.

### Per-view CPU mesh visibility, local Editor — 2026-11-20 (macOS Metal, Unity 6000.6.0f1)

The source project is on exFAT, so this round used an APFS mirror at `/Users/gushihao/UnityWork/InfinityExample-mvc-20261120` whose package symlink pointed at this checkout. Editor pid 62251. Evidence was `intermediate/mesh-view-culling-verify/run1`. `static_check` 0 errors, and `test_static_check.py` 52 tests passed. D3D12 and Vulkan are not on this machine.

Compile: Runtime, Editor, Tests, Validation and Cli (`INFINITY_UNITY_CLI`) loaded with 0 CS errors. The refresh window has no Burst error, no shader error and one `[InfinityRP]` present line. `Compute_MeshDrawPipeline` imported on Metal. `FindKernel` resolved all 9 kernels of that commit, including `MeshViewCulling` 3, `MeshViewCullingOccluded` 4, `MeshViewCullingOccludedHistory` 5 and the remainder view kernel 6, deleted since (see the derived remainder section above). The Burst job compiled: the profiler records `MeshViewCullingJob (Burst)`.

EditMode, Infinity assemblies, 906 tests: 904 passed, 1 failed, 1 skipped in 49.8 s. The Mesh filter passed 119, including the six new visibility tests, `CoincidentLightViews_EachDrawTheSameCasters`, the pack and element-range tests, and the three CPU/GPU parity tests. The RenderGraph filter passed 88 and skipped 1. The full-suite failure is `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)` (`(0, 0, 0, 0)` expected 1) and the skip is `RenderGraphQueueGpuTests.CrossQueueHazardsAndPhysicalReuse_PreserveValues` (no async compute), the same pair as the 898 / 1 / 1 record. The six tests added since that record passed. No scene was saved.

Images, Play, history locked, `gameTime` 8, jitter 0, Game camera explicit, 1698×955. The Rendering Debugger backend override and the capture request were the same policy. Camera positions matched `a20f777`. Matrix CPU Direct and GPU Indirect depth are identical to `a20f777` (max absolute difference 0, same 650467 nonzero texels). Matrix CPU Direct DisplayColor mean absolute difference is 4.97e-6, 16 pixels above 8/255; GPU Indirect is 5.41e-6 and 35 pixels. Those pixels are scattered. LOD on `MeshEntity_1`, two in-memory levels, screen size 0.185, same camera pose on both builds: depth is identical on both backends (985509 nonzero texels), GBuffer B and GBuffer C are identical, and GBuffer A differs by 527 pixels on CPU Direct and 491 on GPU Indirect, scattered rather than a missing surface. `CascadeShadow` is 4096² and all zeros on this branch and on `a20f777`, for the default Matrix camera and for the LOD camera. The empty atlas is an existing phenomenon on both builds, so it shows no casters to inspect. Scene_Spazon depth has the same 1577556 nonzero texels as `a20f777`. CPU Direct has 31923 texels above 1e-4 (max 0.011) and GPU Indirect 22548 (max 0.011). DisplayColor pixels above 8/255 are 21064 (1.30%) on CPU Direct and 12506 (0.77%) on GPU Indirect. The depth and color differences sit on the ceiling lamps and the two hanging props; the cloister, banners and columns match. `Time.frameCount` is not the same on the two loads.

Release, Game view only, Scene_Spazon still, warmup 300, sample 300. GPU Indirect `frameAllocations` windows are 0, 0, 0. CPU Direct windows are 0.109, 0, 0; every stack is `ProfilerSession::EnablePlaymode` or `NativeInputSystem.NotifyBeforeUpdate`, the same first-window pair as `a20f777`, and the last two windows are 0. `nativeSubmitAllocations` is 0. `Mesh.ScheduleViewCulling` is 0.0155 ms at 5 calls on CPU Direct and 0 calls on GPU Indirect. `Mesh.DeclareList` is 0.064 ms at 12 calls versus 0.064 ms at 12 on CPU Direct, and 0.103 ms at 13 versus 0.114 ms at 13 on GPU Indirect. `Mesh.RecordViewProducer` is 0 versus 0 on CPU Direct, and 0.030 ms at 7 versus 0.033 ms at 7 on GPU Indirect. `Mesh.RecordProducer` is 0.022 ms at 7 versus 0.019 ms at 7 on CPU Direct, and 0.025 ms at 8 versus 0.028 ms at 8 on GPU Indirect. The per-pass markers are `MeshCPU.Schedule.*`: on CPU Direct, Depth 0.0146 ms, GBuffer 0.0043 ms, Motion 0.0041 ms and Shadow 0.0149 ms at 4 calls, against 0.0165, 0.0041, 0.0036 and 0.0131 ms on `a20f777`. `infinity_mesh_state` `lastCameraViewCullingJobs` is 5 on CPU Direct (the main view and four cascade views; T0, T1, T2, Forward and TranslucentDepth have 0 members) and 0 on GPU Indirect. On GPU Indirect the counter is 1 when the main view's T0–T2 lists have MeshComponent members, and 0 otherwise. Scene_Spazon has no translucent MeshComponent, so 0 is the value that scene produces.

The first profiler window kept 228 of 1500 `MeshViewCullingJob` samples, so that window did not establish the worker comparison. The follow-up did, in `intermediate/mesh-view-culling-followup/run1`, on the same Editor. The scene is Scene_Spazon plus an unsaved 10000-entity set spawned the way the 2026-11-08 probe spawns: 10008 live entities, high water 10008, nothing saved. CPU Direct, Release, Game view only, `Profiler.maxUsedMemory` 512 MB, warmup 120, sample 60. Both builds kept every one of the 60 frames, and every frame has 7 `MeshCPU.Complete.*` samples. On this branch every frame has 5 `Mesh.ScheduleViewCulling` calls and `lastCameraViewCullingJobs` 5. `MeshViewCullingJob (Burst)` samples are 2870 against those 300 calls, 29 to 58 samples in a frame. The job is an `IJobParallelFor` with batch 64, and the profiler records one sample per worker that ran it, so those two counts are not equal; no sampled frame lacks worker samples. Worker means, summed on the worker threads: this branch 8.043 ms per frame (cull 0.789, range 0.636 to 1.798, plus gather 7.254, range 4.323 to 19.112) and `a20f777` gather 10.867 ms (range 4.576 to 14.049). Main-thread `MeshCPU.Complete.*` means: 0.812 ms (range 0.537 to 1.869) against 1.105 ms (range 0.592 to 1.441). `Mesh.ScheduleViewCulling` is 0.00948 ms at 5 calls. The branch worker total is 26% below the baseline gather, and the complete total is lower as well, so the branch is not more expensive on the line that requires the worker total to be more than 10% higher and the complete total to rise. Scheduling was not changed. `a20f777` has no `viewCullingJobs` field. GPU Indirect on the same unsaved set: with no translucent MeshComponent, T0, T1 and T2 have 0 members and `lastCameraViewCullingJobs` is 0; after one T0 MeshComponent is added (T0 and TranslucentDepth each have 1 member, 10009 entities), the counter is 1. D3D12 and Vulkan stay TODO(UNVERIFIED).

### Per-view CPU mesh visibility, static gate only — 2026-11-19 (no Editor)

Every view a live CPU list reads is now culled once on the CPU, by one Burst `MeshViewCullingJob` over every entity slot of the world, scheduled at compile into words the view registry pools; every CPU gather of the view depends on that job and only filters its words (`MeshDrawLod.Range`, the C# mirror of `MeshElementRange`). The GPU path is unchanged. The CPU instance fade is now the 16-bit band position the GPU reads, so CPU factors move by at most 1/65534 and a band position below one step draws no outgoing level, as on the GPU. `IRGGraphExtension` is disposed with its builder, which completes any job still reading the words and frees the pool. An independent review of `f43d24a` found no compile, job-safety or HLSL-equivalence error; its findings (words sized by one list's snapshot, disposal without completing jobs, a reuse test that pointer equality alone could pass, acronym casing, diagram notation) are fixed in the commit after it. A third commit names the per-view culling `MeshViewCulling` on both sides (CPU job, GPU kernels and their in-pass scope, marker, counter, CLI field; the recording API `CullMeshView` and the pass `ComputeMeshView` keep their names) and schedules every view's culling before the builds, one per view a live CPU list reads.

Passed at the static gate, before the Editor round above: `static_check` 0 errors, including the new `meshvisibility` (the gather of `a20f777` fails it with ten findings); `test_static_check.py` 52 tests; tree-sitter C# (0.26.1) parse of the 17 changed C# files under the four `UNITY_EDITOR` / `DEVELOPMENT_BUILD` define sets with 0 new syntax errors (a deliberately broken copy reports 4); a repository-wide closure of `MeshDrawLod.Factor`, the old `GatherOnCpu` and `MeshDrawListBuild.Schedule` signatures and the gather's `entities`, `context` and `lod` fields (Runtime, Editor, `Editor/Cli`, Tests, Tools, documents). Compilation, the shader import, the EditMode groups and the images are in the 2026-11-20 section.

New or changed tests, run in that section: `MeshDrawListLifetimeTests.OneView_SchedulesItsCPUVisibilityOnce_ForEveryCPUListOfIt`, `ViewWithoutALiveCPUList_SchedulesNoCPUVisibility`, `ViewWithGPUAndCPULists_EvaluatesOncePerResidency`, `CPUVisibility_ReturnsWithTheGraph_AndTheNextGraphReusesIt` (pool size read by reflection), `CPUVisibility_CoversListsRecordedAfterACommit`, and `viewCullingJobs` or `cpuVisibility` assertions in `LiveCpuLists_EachScheduleTheirOwnBuild`, `CoincidentLightViews_EachDrawTheSameCasters` (`190d0be`), `UnusedDrawList_SchedulesNothing`, `EmptyList_SkipsPreparation_AndRejectsCacheChanges` and `CompiledGraph_ProducesEachLiveListOnce_BeforeItsFirstConsumer`; `MeshLodTests.Pack_EncodesTheLevelAndTheQuantizedBandPosition`, `ElementRange_IsComplementaryInsideTheBand_AndStableOnlyWithoutDither` (last level at fade bits 0, and a band position below one step), `TranslucentBuild_DrawsTheSelectedLevelWithoutFadingRuns`; the parity tests `MeshCullingContextTests.GPUView_AgreesWithCPUView_AndTheirListsAgree`, `MeshLodTests.GpuLodSelection_AgreesWithCpuSelection` (both dither modes) and `MeshDrawListTests.GpuBuild_MatchesTheCpuBuildPerCommandRange` now compare the view words slot by slot and then each list; `RGGraphExtensionTests.Extension_SeesLivenessAndUses_AndClearsWithTheGraph` checks the extension is disposed once.

TODO(UNVERIFIED): D3D12 and Vulkan, which are not on this machine. The CPU Direct worker comparison and the GPU Indirect count are in the 2026-11-20 section.

### Architecture convergence, local Editor — 2026-11-18 (macOS Metal, Unity 6000.6.0f1, partial)

The source project is on exFAT. Unity 6000.6.0f1 aborts on the second open of that Library (`Library Redirect Path: Library/` then `stoull: no conversion`; [issue 25823](https://issuetracker.unity3d.com/issues/25823), fixed in 6000.6.9f1, which is not installed). The Library was not deleted. Verification uses an APFS mirror at `/Users/gushihao/UnityWork/InfinityExample-arch-20261118` whose package is a symlink to this checkout. Editor pid 13327. `static_check` 0 errors.

Compile: Runtime, Editor, Tests, Validation and Cli (`INFINITY_UNITY_CLI`) loaded with 0 CS errors. `Compute_MeshDrawPipeline` imported on Metal and all 9 kernels resolve (`FindKernel` ≥ 0). D3D12 and Vulkan are not on this machine.

EditMode, Infinity assemblies, 897 tests: first run 886 passed, 10 failed, 1 skipped. Nine failures were one `MissingReferenceException` from `TerrainModule.ProcessView` reading a destroyed `TerrainData` (`9efea8d`). After that fix: 895 passed, 1 failed, 1 skipped in 17.9 s. The failure is `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`, the same failure recorded on earlier gates. The skip is `RenderGraphQueueGpuTests.CrossQueueHazardsAndPhysicalReuse_PreserveValues` (no async compute).

Scene_Spazon Play, MainCamera, 993×1330, Metal, Apple M3 Max. DisplayColor `R16G16B16A16_SFloat` captured (`intermediate/arch-convergence-verify/run1/capture-spazon`). The frame shows the cloister, banners, stained glass and cast shadows. It is not compared with `155c2ef`. The compiled graph places `ComputeScreenSpaceReflection` and `ComputeScreenSpaceIndirect` on the graphics queue before `RasterCascadeShadow`. Seven `ComputeMeshView` passes: five before `RasterDepth` (the depth view and four cascade slices) and two after `ComputeEarlyFurthestHiZ` (recovery and main). `RasterDepth` is native group 0; `RasterDepthRecovery` and `RasterGBuffer` share group 1; object and camera motion share group 2; T1 and T2 share group 8. `ComputeMeshDrawList` sits immediately before the raster group that consumes it.

Round 2, same Editor and mirror, evidence in `intermediate/arch-convergence-verify/run2/`. History-locked frame 1, motion behaviours disabled, `timeScale` 0, 993×1330, against a worktree of `155c2ef` loaded in the same Editor.

Scene_Spazon DisplayColor, PostProcess, CascadeShadow, LocalShadow, SSR, SSGI, Depth and TAAConfidence are byte-identical to `155c2ef` (mean absolute difference 0). PostProcess and DisplayColor are also identical to each other on this frame; the Game view has no UI that would separate them. BoxMatix_MeshPipeline depth, cascade shadow, local shadow and TAA confidence are identical. Its DisplayColor mean absolute difference is 1.26e-5, and 60 pixels (0.0045%) differ by more than 8/255, scattered across the frame rather than in one object. Matrix does not enable SSR, SSGI, fog or cloud, so those buffers were not requested. A Spazon capture after 30 successful frames without the history lock (`Time.frameCount` still 1) has identical depth and DisplayColor mean absolute difference 0.00216, with 1.85% of pixels above 8/255; SSR mean absolute difference is 0.00075 and 0.26% of pixels are above 8/255. That is temporal shading on unchanged depth, not a missing caster.

Coincident light views: `CoincidentLightViews_EachDrawTheSameCasters` and `CoincidentLightViews_BothDrawTheSameInstances` passed (`190d0be`). The atlas image of two identical spots was not captured.

Spazon, Game view only, GPU Indirect, camera still, Release, warmup 300, sample 300. `Inl_RecordRG` 0.929 ms, `Inl_ExecuteRG` 1.723 ms, `Inl_ProcessLight` 0.155 ms, `UpdateMeshWorldGPU` 0.017 ms. `Mesh.DeclareList` 0.218 ms at 13 calls, `Mesh.RecordViewProducer` 0.063 ms at 7 calls, `RG.Compile.Place` 0.155 ms. Game `frameAllocations` windows are 0.109, 0, 0. The first window is `ProfilerSession.EnablePlaymode` and `NativeInputSystem.NotifyBeforeUpdate`, tagged Game-or-shared, not a render-path allocation. The last two windows are 0. `nativeSubmitAllocations` is 0.

Round 3. Play was not paused: at one sample `Time.frameCount` was 4732 and `timeScale` was 1. The earlier “frame 1 after 30 warm-up frames” counted Game view repaints inside one player frame (`gameTime` 0). `2c887d1d` makes capture copy `FrameContext.frame` and count a player frame once. A later warmup of 10 recorded frame 8141 on the evidence, the producer and the completion together; the live counter was 8111 before the capture and 8169 after it.

Matrix CPU Direct, history locked, both sides frame 35, jitter 0, depth identical. DisplayColor still differs: mean absolute difference 1.41e-5, 80 pixels above 8/255. `gameTime` was 2.42 on this branch and 1.10 on `155c2ef`, so the pair is not the same moment. The GPU list kernel does append with `InterlockedAdd`, but this pair does not show that the 60 GPU pixels are only that order. Evidence: `intermediate/arch-convergence-verify/run3/`.

Round 4, same Editor and mirror. `completionFrame` is `Time.frameCount` at the readback callback. Matrix Play, Main Camera, 993×1330, Metal: evidence, producer and submission frame 17, completion frame 19 (`intermediate/arch-convergence-verify/run4/clock-capture`). `RenderCaptureTests` 13 passed, including `completionFrame >= producerFrame` (`run4/tests-capture-clock`).

`CameraSetup` no longer stores a frame. `PipelineDiagnostics.BeforeCamera` takes the `FrameContext` and capture reads `frameContext.frame`. After that compile, the same Matrix capture recorded evidence, producer and submission frame 19 and completion frame 21 (`run4/clock-after-framesource`). `RenderCaptureTests` 13 passed again (`run4/tests-frame-source`). `static_check` 0 errors, 51 static-check tests passed. The scene was not saved.

A beauty capture fails when `CameraSetup.debugView` is not `None`. `RenderCaptureRequest.allowDebugView` records that view and still copies `PostProcess` from `RasterPostCombine`. `RenderCaptureTests` 14 passed (`run4/tests-debug-view`). Scene_Spazon, history locked, gameTime 8, 993×1330 (`run4/head-spazon-debug-*`): Albedo, MotionMagnitude, PreTonemapLuma, LightComplexity, ReflectionProbeAtlas and CascadeShadowAtlas. DisplayColor differs from PostProcess on each of those views (477,976 to 1,320,690 pixels above 8/255). PostProcess differs from the beauty PostProcess by at most 98 such pixels. The example project has no reflection probe, so ReflectionProbeAtlas is the missing kernel (magenta). MotionMagnitude is about 0 because the camera is still. The scene was not saved.

Round 4 against worktree `155c2ef`, same Editor, Metal, Apple M3 Max, Game view 993×1330. Evidence is `intermediate/arch-convergence-verify/run4/`. `static_check` 0 errors. EditMode 898 passed, 1 failed, 1 skipped in 14.7 s (`run4/tests-editmode-round4`). The failure and the skip are the same `HierarchicalRay_HitIsOneActualSurfaceTexel(True)` and `CrossQueueHazardsAndPhysicalReuse_PreserveValues` as the 895/897 run. Three capture-clock tests account for the added passes. No scene or pipeline asset was saved. The pipeline asset hash stayed `A1B1E703681B`.

Matrix, `Time.captureDeltaTime` landed on gameTime 3, history reset, jitter 0. GPU Indirect DisplayColor mean absolute difference 1.075e-5, 98 pixels above 8/255, depth identical (`headt-matrix-lock-b0`, `base-matrix-lock-b0`, frames 101 and 100). CPU Direct mean 1.138e-5, 92 pixels, depth identical (frames 151 and 116). A second branch CPU capture at the same gameTime differs by 94 such pixels (`headt-matrix-lock-b1` frame 151, `headt2-matrix-lock-b1` frame 157). Baseline GPU versus CPU differs by 89. Depth is identical in each pair. The scatter is frame-index shading that one binary already produces. `Time.frameCount` does not reset on Play, so the absolute frame numbers differ.

Spazon along one path, positions identical, gameTime 8.02000, 8.20003 and 8.60006 versus 8.02000, 8.20003 and 8.60005 (`headt-spazon-move-*`, `baset-spazon-move-*`). DisplayColor pixels above 8/255: 28433, 29696, 4734. Depth texels above 1e-4: 51447, 21957, 4940. Frame 1 zero-depth masks differ by 184 texels (121 only on this branch, 63 only on the baseline). Frame 30 zero-depth masks are identical, 16031 texels each. Motion `R16G16_SFloat` max absolute difference 0.00168, 0.00045, 0.00105. The depth differences are the animated character, the rotating stained glass and the banner edges. Those objects follow `Time.frameCount`, which cannot be equalized. The cloister coverage at frame 30 matches.

Fog and cloud, play-mode volume, density 0.004 (`headb-matrix-fog`, `base-matrix-fog`), gameTime 8. DisplayColor mean absolute difference 6.99e-6, 32 pixels above 8/255. SceneColor, last writer `RasterTranslucentT2`, mean 5.55e-6, 29 pixels. Both graphs record `ComputeAtmosphericSkyAndFog`, `RasterTranslucentDepth`, `ComputeVolumetricCloud`, `CopyHistoryVolumetricCloud`, `ComputeVolumetricFog`, `CopyHistoryVolumetricFog`, `ComputeFogComposite`, then `RasterTranslucentT0`, `T1` and `T2`. DisplayColor versus SceneColor is 3069 pixels on this branch and 3071 on the baseline.

Super resolution, internal 497×665, display 993×1330, gameTime 8 (`head-matrix-sr`, `base-matrix-sr`). DisplayColor and PostProcess mean absolute difference 2.14e-6, 4 pixels above 8/255. On each build the two buffers are equal.

LOD, `MeshEntity_1`, two in-memory levels, screen size 0.185, gameTime 8, jitter 0 (`headc-matrix-lod-*`, `base-matrix-lod-*`). GPU Indirect depth matches the baseline and CPU Direct depth matches the baseline. Color differs by 85 pixels above 8/255 on each backend. GPU versus CPU depth differs by 250 pixels, max 0.002039, on this branch and on `155c2ef`.

Debug views already cited above, compared with `base-spazon-debug-*` at gameTime 8 and jitter 0. Motion, LightLoop and ReflectionProbeAtlas DisplayColor have 0 pixels above 8/255. ReflectionProbeAtlas is (1, 0, 1) on both builds. Shadow has 1520, SceneColor 28468, GBuffer 77098, and the beauty frame 33016. Those follow the stained-glass frame-clock difference. On `155c2ef` the PostProcess capture's producer is `RasterUIOverlay`, the same buffer as DisplayColor.

Coincident spots on Spazon, gameTime 8, local atlas 2048² `R32_SFloat` (`headc-spazon-spots-b0`, `headc-spazon-spots-b1`, `base-spazon-spots-b0`, `base-spazon-spots-b1`). Tiles r0c0 and r0c1 each hold 248271 nonzero texels. Those masks match across GPU Indirect, CPU Direct, this branch and the baseline. The other tiles are empty. Depth inside the caster differs: one branch tile versus the baseline tile reaches 0.022, with 2992 texels above 0.001.

Allocations, Scene_Spazon, Release, `captureAllocations` true. Game view only, warmup 120, sample 30 (`head-alloc-still-gpu`, `head-alloc-move-gpu`, `head-alloc-still-cpu`): Game windows 0, 0, 0 and SceneView calls 0. Scene and Game together (`head2-alloc-still-gpu`, `head2-alloc-move-gpu`, `head2-alloc-still-cpu`): GPU Indirect, still and moving, Game 1, 1, 1 and SceneView 7, 7, 7; CPU Direct, Game 71, 71, 71 and SceneView 7, 7, 7. Every stack in `head2-alloc-*/alloc-stacks.txt` is `ProfilerSession::EnablePlaymode` → `WalkScriptingMetaData` → `mono_string_new`, or `NativeInputSystem.NotifyBeforeUpdate`. `nativeSubmitAllocations` is 0. The render path contributes 0. ARCHITECTURE §16 records the stacks.

Pipeline replacement in Play: `shadowDistance` 128 to 64 through `RequestEditorRecreation`, a few player frames, then restored to 128 and `ClearDirty` (`head-rebuild.json`). Counts stayed `1|0|0|1|0|0` for the light table, decal table, probe table, light components, decal components and probe components. The pipeline instance identity changed. The example project contains one additional light and no decal or reflection probe, so the empty tables stayed empty. The asset was not dirty afterwards and its hash was unchanged.

Timing, Release, GPU Indirect, camera still, warmup 300, sample 300. A branch window whose `cpuFrameTime` sat at 100 ms is discarded (`head-spazon-gpu`, `head-matrix-gpu`). The quiet pair (`headb-spazon-gpu`, `headb-matrix-gpu`, `base-spazon-gpu`, `base-matrix-gpu`): Spazon `Inl_RecordRG` + `Inl_ExecuteRG` 0.849 ms versus 0.855 ms; Matrix 0.536 ms versus 0.532 ms. Later 120-sample `cpuFrameTime` medians are 8.36 ms versus 8.30 ms on Spazon and 8.31 ms versus 8.31 ms on Matrix (`*-gpu-dist.json`). Record sums are 0.271 versus 0.280 ms on Spazon and 0.166 versus 0.165 ms on Matrix. Mesh sums are 0.178 versus 0.193 ms and 0.066 versus 0.064 ms. `Mesh.DeclareList` is 13 calls and 0.0669 versus 0.0728 ms on Spazon, 9 calls and 0.0395 versus 0.0420 ms on Matrix. `Mesh.RecordViewProducer` is 7 calls and 0.0184 ms on Spazon, 3 calls and 0.0110 ms on Matrix. `RG.Compile` sums are 0.212 versus 0.202 ms and 0.122 versus 0.120 ms. `RG.Compile.Place` is 0.0557 versus 0.0499 ms on Spazon. `RG.Execute.Create` and `Release` are 48 versus 41 calls on Spazon and 28 versus 25 on Matrix, matching the view-producer counts. The open scene has 8 entities. No CPU marker delta exceeds 0.018 ms.

`FrameTiming.gpuFrameTime` over 120 playing samples, Scene view closed: Spazon median 33.61 ms (min 29.72, p90 36.99) versus 25.18 ms (p10 24.19, p90 29.71); Matrix median 9.35 ms versus 12.21 ms. `ProfilerRecorder` with `GpuRecorder` on the pass markers stays at 0 in this Editor, so the Spazon increase of 8.4 ms has no pass split. Round 5 attributed it (below).

Round 5, same mirror and Editor, Apple M3 Max, Metal (no async compute), Spazon 993x1330, Scene view closed, branch and `155c2ef` alternated in one session (b1 base1 base2 b2 b3 base3 base4 b4): branch medians 31.49–33.66 ms, baseline 25.21–25.95 ms, no overlap (branch p10 ≥ 30.77, baseline p90 ≤ 28.74). Metal System Trace: SSR shader time moved into the `ComputeHiZ` encoder (0.84 → 4.35 ms) and Sky, ColorPyramid and the SSR history copy grew long peaks. Moving only the SSR and SSGI record calls after `RasterForward` gave 25.17 ms (p10 24.54, p90 25.70), inside the baseline range. Cause: their record position ahead of the shadows (`0274084`). They now record after `RasterForward` on every device. Round 6 measured that committed order (`a29bb5d`, Runtime `b03422f98fe9dcbb`) on the same mirror and Editor. Compile reloaded that DLL with 0 CS errors. EditMode is 898 passed, 1 failed, 1 skipped (`run6/editmode-2`); the failure is `HierarchicalRay_HitIsOneActualSurfaceTexel(True)` and the skip is `CrossQueueHazardsAndPhysicalReuse_PreserveValues`. Spazon 993×1330, Scene view closed, 120 samples, alternating branch, baseline, baseline, branch and then the same four again (`run6/b1.json` through `run6/b4.json`, `run6/base1.json` through `run6/base4.json`): branch medians 25.47, 25.04, 25.22 and 25.07 ms, baseline medians 25.28, 25.65, 25.00 and 25.10 ms. Every branch median is inside 25.00–25.65 ms. The highest branch median is 1.86% above the lowest baseline median. CPU medians are 8.25–8.35 ms. Matrix, one pair at the same resolution (`run6/matrix-b.json`, `run6/matrix-base.json`): branch median 15.62 ms, baseline 16.25 ms, with CPU near 24 ms on both. Time-locked Spazon captures, motion scripts disabled before the first update (`run6/sync2-head-lock`, `run6/sync2-base-lock`, `run6/sync2-head-f30`, `run6/sync2-base-f30`): at gameTime 0 with history reset, and at gameTime 0.5 (frame 32, `captureDeltaTime` 1/60) without it, DisplayColor, SSR and SSGI are byte-identical to `155c2ef`. The reset SSR and SSGI buffers are zero on both; at 0.5 s SSR mean is 0.0958 and SSGI mean is 0.00391. The frame tree (`run6/frame-tree/frame-tree.json`) runs cascade slices 0–3, then `ComputeDeferredShading`, `RasterForward`, the SSR kernels, the SSGI kernels, then `ComputeScreenSpaceComposite`. Game-only allocations after warmup 120 (`run6/alloc-game-clean/markers.json`) are 0, 0, 0, and Scene view calls are 0. The Spazon GPU frame-time comparison passes.

### Architecture convergence, static gate only — 2026-11-18 (no Editor)

Every item below passed the static gate and nothing else: tree-sitter C# syntax under four define sets with no new error, `static_check` 0 errors, its 51 tests, and a repository-wide consumer closure of every removed symbol (Runtime, Editor, `Editor/Cli`, Tests, Tools and the living documents). Compilation, shader compilation, tests, images and timings are TODO(UNVERIFIED); the checklist at the end closes them.

- Rules: AGENTS R17 chooses scratch by its last reader and R18 derives order; `framealloc` checks frame- and camera-rate methods, and `layering` covers `Runtime/Mesh/Graph`.
- Allocation: the seven frame-rate managed allocations are gone. Local shadow allocation keeps cached sampling and budget scratch in `Allocator.Temp` and `ResolveSampling` writes a caller span; shadow-moment blur names are constants; the cached-shadow and atmosphere observers live with their passes; dependency reasons are composed once per pair; mesh residency scratch is Temp and dynamic-pose counters TempJob; shadow caster slots are a `Dictionary` keyed on light and scene mask.
- Measurement: one record per `Render` call with its camera types and native-submit allocations, Editor and Development only; `infinity_cpu_markers_start` takes `includeSceneView` and reports allocations per camera type.
- Cleanup: `BaseComponent` is merged into `EntityComponent`; commented-out code, empty Unity messages and the TODO format follow AGENTS §3; no capability is removed.
- Scene world: component registries are the authority; `SceneWorld` prepares the mesh, light, probe, decal and terrain tables, commits them together and rolls every table back when one fails, and a new pipeline rebuilds every table, so a replaced pipeline keeps its lights; no static work queue remains.
- Contexts: `RenderContext` is the pipeline-lifetime root, `FrameContext` one `Render` call's engine facts, `ViewContext` one camera's setup, `SceneCullingResults` and frame facts; passes never reach the root (`passroot`).
- Frame derivation: features resolve once in `FrameFeatureSet.ResolveFrame` and `ResolveContent`; scene-color stages come from `SceneColorKeys`; the debug view writes `DebugViewBuffer`; SSR and SSGI record at one position on every device, after `RasterForward`.
- Passes: every pass is an `internal sealed class` built once with constructor-injected dependencies, recording from `in ViewContext`; final output is `FinalOutputPass`.
- Pipeline base: `InfinityRenderPipelineBase` owns the frame; a pipeline supplies `PipelineCapabilities` (features, required producers, async compute, mesh pass table, shadow caster pass) and `RecordCamera`; `TestRenderPipeline` is a second, compiled reader; construction and disposal failures are aggregated (`pipelinebase`).
- Mesh explicit views: passes create lists from the `RGMeshViewHandle` they receive (`CullMeshView`, `CullMeshViewRemainder(drawnView, currentView)`, `CreateMeshDrawList(view, MeshPassId)`); two-stage occlusion is the depth, recovery and main views of `MeshMainViews`, the recovery view derived from the other two; the backend derives from the pass's sort key; shadow passes cull light views with `MeshOccluder.None` (`meshpasses`).
- Per-view GPU visibility: a GPU view's first nonempty list declares one `ComputeMeshView` producer (`MeshViewCulling`, `MeshViewCullingOccluded` or `MeshViewCullingOccludedHistory`) that writes a packed word per entity, and every list runs `BuildDrawList` over it; a remainder view has no producer, and its list runs `BuildDrawListRemainder` over its current and history views' words, which must cover the same entity slots; the CPU keeps one gather per list (since 2026-11-19 the CPU evaluates each view once too; see that section). The tests assert that a nonempty GPU list declares exactly one view producer, compare the producer scopes with the kernels the compute shader declares and the backend resolves, keep the last-level fade-0 case out of the visibility-versus-drawn comparison explicitly, and release view payloads when an assert fails.
- Documents: ARCHITECTURE states the code above; [Passes and pipelines](../../Manual/architecture.md) covers authoring.

The extra view-producer dispatch and the visibility-buffer count against `155c2ef` are in the 2026-11-18 local Editor section above. Pass GPU time is not available from `ProfilerRecorder` in this Editor. The Render Graph Viewer lifetime bands were not read.

#### Local verification checklist

1. Refresh and compile: confirm the owned Editor and its process ID, refresh with the Unity CLI or `Tools/RefreshUnityEditor.sh`, wait for compilation and domain reload, and confirm the candidate DLL loaded. The new window of `Application.consoleLogPath` has no CS error in Runtime, Editor, Tests, Validation or `Editor/Cli` (with `INFINITY_UNITY_CLI`) and no InfinityRP flood, and `Compute_MeshDrawPipeline.compute` compiles every kernel without warnings on Metal, and on D3D12 and Vulkan where available.
2. EditMode tests (`infinity_tests_start`), by group:
   - Scene world: `SceneWorldTransactionTests`, `DecalContextRegistrationTests`, `MeshComponentRegistrationTests`, `MeshComponentEditNotifierTests`, `TerrainAuthoringContractTests`.
   - Frame derivation and contexts: `FogTranslucentFeatureTests`, `FeatureRegistryTests`, `RGScoperTests`, `CameraFrameStateTests`, `QualityLifecycleTests`, `AtmosphereCacheTests`.
   - Pipeline base: `PipelineCapabilitiesTests`; `TestRenderPipeline` compiles.
   - Allocation and lights: `LocalShadowCacheTests`, `ShadowBudgetTests`, `ShadowCasterAllocationTests`, `LightBinningTests`.
   - Mesh: `MeshDrawListLifetimeTests`, `MeshPassCacheTests`, `MeshWorldTests`, `MeshDrawListTests`, `MeshLodTests`, `MeshDrawProfilingTests`, `MeshCullingContextTests`, `MeshMotionHistoryTests`, `NativeViewMotionHistoryTests`, `MeshResidencySubmissionTests`, `MeshTransformVersionGpuTests`, `MeshOcclusionIntegrationTests`, `MeshOcclusionGpuTests`.
   - GPU and CPU parity, view words then lists: `MeshCullingContextTests.GPUView_AgreesWithCPUView_AndTheirListsAgree`, `MeshLodTests.GpuLodSelection_AgreesWithCpuSelection`, `MeshDrawListTests.GpuBuild_MatchesTheCpuBuildPerCommandRange`.
   - CPU view visibility: `MeshDrawListLifetimeTests.OneView_SchedulesItsCPUVisibilityOnce_ForEveryCPUListOfIt`, `ViewWithoutALiveCPUList_SchedulesNoCPUVisibility`, `ViewWithGPUAndCPULists_EvaluatesOncePerResidency`, `CPUVisibility_ReturnsWithTheGraph_AndTheNextGraphReusesIt`, `CPUVisibility_CoversListsRecordedAfterACommit`, `CoincidentLightViews_EachDrawTheSameCasters`; `MeshLodTests.ElementRange_IsComplementaryInsideTheBand_AndStableOnlyWithoutDither`; `RGGraphExtensionTests`.
   - Render graph: `RenderGraphCompileTests`, `RenderGraphFailureTests`, `RGGraphExtensionTests`.
3. CPU-marker probes (`infinity_cpu_markers_start`, Play mode, Release code optimization, warmup 300, sample 300, `captureAllocations` true) on Scene_Spazon and Matrix with GPU Indirect and CPU Direct: Game view only (`includeSceneView` false) gives `frameAllocations` 0, 0, 0; Scene and Game views together (`includeSceneView` true) give allocation stacks per camera type, which name the owner of the seven allocations in ARCHITECTURE §16. `infinity_runtime_probe_start` reports `nativeSubmitAllocations`.
4. Marker comparison against the B11a baseline and `95ea92b`: `Record.*` (including `Record.UploadCameraUniforms`, `Record.CullMainMeshViews`, `Record.CreateMeshEarlyOccluder`, `Record.CullEarlyMeshViews` and `Record.EnsureRequiredProducers`), `Mesh.*` with `Mesh.DeclareList`, `Mesh.RecordViewProducer` and `Mesh.ScheduleViewCulling`, and `RG.Compile.*`. On CPU Direct the worker time of `MeshViewCullingJob` plus every `MeshDrawGatherJob` is not higher than the gathers of `a20f777`, and `MeshWorldStats.viewCullingJobs` (`infinity_mesh_state` `lastCameraViewCullingJobs`) is one per view with a live CPU list. The frame-tree export's `meshViews` shows one `MeshViewCulling` dispatch per culled view (1, or 2 with two-stage occlusion, plus one per rendered shadow slice), `MeshWorldStats.viewProductions` agrees, and `meshDrawLists` shows one `BuildDrawList` per nonempty GPU list.
5. DisplayColor captures (`infinity_capture_start` on an explicitly selected Game camera, or Image Regression against the recorded per-API baselines), each matching the previous baseline: Scene_Spazon and Matrix on CPU Direct (Rendering Debugger backend override) against `a20f777`; two-stage occlusion on Scene_Spazon and Matrix with no popping or double draw, and LOD bands that dither without holes in depth, GBuffer and shadow; cascade and local shadows; SSR and SSGI on and off, including Metal without async compute; volumetric fog and clouds; TAA and super resolution; every debug view, with a `PostProcess` capture holding the post output. Replace the pipeline asset in Play mode and confirm local lights and decals still render.
6. Render-graph pass order (Render Graph Viewer or the frame-tree export) on Scene_Spazon: SSR and SSGI sit after `RasterForward` and before `ComputeScreenSpaceComposite`; a frustum or light view's `ComputeMeshView` sits at the start of the graph, the main view's right after `ComputeEarlyFurthestHiZ`, which precedes `RasterDepthRecovery`, and the recovery list's `ComputeMeshDrawList` after both the depth and main view producers; every `ComputeMeshDrawList` and `UploadMeshData` sits before its consumer's native group without splitting it; `RasterDepth` and `RasterGBuffer` keep their native merge groups.
7. Inspect each captured image and read only the new log window. A log-only result stays TODO(UNVERIFIED).

### Residency scratch — 2026-11-18 (macOS Metal, Unity 6000.6.0f1)

Scene_Spazon, MainCamera, GPU Indirect, camera still, Release. Warmup 300, mean of the next 300. The scene has 4 dynamic entities, so `Mesh.PrepareTransformVersion` and `Mesh.UploadColumns` run once per frame (1.003 calls). `frameAllocations` is 0.11, 0, 0. The last two windows are 0 while those stages run. `MeshWorldTests` 10 passed, including a second static residency update and a second pose update at 0 managed bytes. The transform-version GPU test passed. `MeshMotionHistoryTests` 8 passed.

### Caster slots — 2026-11-18 (macOS Metal, Unity 6000.6.0f1)

`ShadowCasterAllocationTests` 8 passed. One hundred queried lights leave 100 slots. After 90 are destroyed and the frame advances by 2, the live slot count is at most 10.

### Record placement — 2026-11-18 (macOS Metal, Unity 6000.6.0f1)

Parent `Inl_RecordRG`, Scene_Spazon, MainCamera, GPU Indirect, camera still, Release. Warmup 300, mean of the next 300. A is `d0724f4` with a temporary parent recorder that was not committed. B is `62bd0cb`. A is 1.015 ms then 0.315 ms. B is 0.316 ms then 0.310 ms. The repeated pair is within 2%. The first A sample followed that commit's domain reload and ran near 9 fps; it does not repeat. There is no Record regression to attribute. On B, `RG.Compile.Place` is 0.054 ms and 0.053 ms, and `Mesh.DeclareList` is 0.080 ms and 0.077 ms at 13 calls. Place does not exist on A, and it does not account for a positive parent delta, so the nested scan was not replaced. `d0724f4` clears no rented array; the repeated parent times do not show that clear as a regression.

`RenderGraphCompileTests` 16 passed. `MeshDrawListLifetimeTests` 24 passed. The Scene_Spazon compiled graph is the same 41 passes before and after the insertion-point sort. `RasterDepth` stays merge group 0 and `RasterGBuffer` stays merge group 1. `ComputeMeshDrawList` stays immediately before `RasterCascadeShadow`. DisplayColor mean absolute difference is 0.000928, and 0.79% of pixels differ by more than 8/255, across two play sessions.

### Arena reuse — 2026-11-18 (macOS Metal, Unity 6000.6.0f1)

`RenderGraphCompileTests` 15 passed, including `RecordingArrays_TwoGraphsAndLengths_AllocateNothingAfterWarmup`: after warmup, 100 alternations of two graphs with lengths 3 and 7 allocated 0 bytes, and the recorded values were still there after Execute. `RenderGraphFailureTests` 6 passed. Static checks 0 errors.

Game view only, Scene_Spazon, MainCamera, GPU Indirect, camera still, Release. Warmup 300, mean of the next 300. `Inl_RecordRG` is 0.311 ms. The same window on `62bd0cb` before this change was 0.316 ms and 0.310 ms.

Scene view and Game view both rendering, Record 2.00 calls per frame: `frameAllocations` is 7, 7, 7. The count does not fall after warmup. The multi-camera 0 target is not met.

### B11d and B11e — 2026-11-17 (macOS Metal, Unity 6000.6.0f1)

Still Spazon after the caster cache: `Shadow.FitCascade` 0.053 ms, `Light.Registry` 0.0035 ms once, `Light.Pack` 0.011 ms, `Light.ZBin` 0.013 ms, `Light.Sort` 0.0001 ms, `Light.TileRect` 0 calls. One Game camera, so the light snapshot is not rebuilt per camera and was not hoisted. Pack, tile, sort and z-bin are not the head, so they stay managed. A cascade-fit reuse keyed on the camera matrices was compiled and measured: TAA changes the projection every frame, the key missed, and `Shadow.FitCascade` rose to 0.077 ms. That reuse was removed.

`Mesh.PrepareTransformVersion`, `Mesh.UploadColumns` and `Mesh.CanWriteBuffer` are 0 calls on Matrix and Spazon. The worlds are static, so `UpdateMeshWorldGPU` stays the residency gate (Matrix 0.008 ms, Spazon 0.020 ms in B11a). No Burst job and no upload merge.

### B11c caster cache — 2026-11-17 (macOS Metal, Unity 6000.6.0f1)

`Shadow.CollectCasters` on moving Matrix GPU Indirect is 0.0004 ms, from 0.022 ms in B11a. The caster index list follows `structuralRevision`. Bounds and local-frustum answers also follow `transformRevision` and the light, so a camera move with a static world does not walk entities. `ProcessLight` is 0.039 ms, from 0.058 ms, gap 6.3%. A mask-only entity desc change now advances `structuralRevision`. The miss path stays the managed loop: these scenes do not show a recalc cost, so it is not a Burst job. `ShadowCasterAllocationTests` 7 passed. `MeshWorldTests` 9 passed.

### B11b allocation — 2026-11-17 (macOS Metal, Unity 6000.6.0f1)

B11a’s moving-camera `GC.Alloc` samples were `new AtmosphereProduction` in `ComputeAtmosphericLUT`. The pipeline now keeps one observer and writes its fields each record. The recording arena and the CPU instance-buffer descriptor did not appear in those stacks, so they stay exact. After 300 warm-up frames and 300 sample frames, `frameAllocations` is 0, 0, 0 for Matrix GPU Indirect and CPU Direct with the camera moving, and for still Spazon. Matrix GPU Indirect `RecordRG` is 0.200 ms against the B11a 0.191 ms.

### B11a CPU marker baseline — 2026-11-17 (macOS Metal, Unity 6000.6.0f1)

Editor Code Optimization Release. Play mode, Game view only, one Game camera. Warmup 300 frames, mean of the next 300. Matrix (`BoxMatix_MeshPipeline`) moves the camera on a 40 m circle. Spazon keeps the camera still. `frameAllocations` is the mean `GC.Alloc` count inside `Render` for three 100-frame windows.

Direct-child coverage against the parent is under 10% on every run. Calls per frame are 1.00 for `Inl_RecordRG`, `Inl_ExecuteRG` and `Inl_ProcessLight`, so those stages run once per camera.

| Run | RecordRG | ExecuteRG | ProcessLight | UpdateMeshWorldGPU | frameAllocations |
|---|---|---|---|---|---|
| Matrix GPU Indirect | 0.191 ms, gap 1.9% | 0.375 ms, gap 0.6% | 0.058 ms, gap 3.8% | 0.008 ms, gap 4.0% | 1.00, 0.99, 1.00 |
| Matrix CPU Direct | 0.161 ms, gap 2.1% | 1.458 ms, gap 0.2% | 0.055 ms, gap 4.0% | 0.007 ms, gap 3.6% | 1.00, 0.99, 0.99 |
| Spazon GPU Indirect | 1.000 ms, gap 1.8% | 2.072 ms, gap 0.7% | 0.169 ms, gap 4.8% | 0.020 ms, gap 3.5% | 0, 0, 0 |

Matrix GPU Indirect, largest children: `RG.Execute.Submit` 0.236 ms, `Mesh.DeclareList` 0.044 ms (9 calls), `RG.Compile.Segments` 0.032 ms, `Record.RasterDepth` 0.026 ms, `Shadow.CollectCasters` 0.022 ms (1 call). `Light.TileRect` is 0 calls. `Mesh.PrepareTransformVersion`, `Mesh.UploadColumns`, `Mesh.CanWriteBuffer` and `Mesh.RecreateColumns` are 0 calls. CPU Direct moves the time into `RG.Execute.Submit` at 1.303 ms; `RG.Execute.Create` and `RG.Execute.Release` run 23 times. Spazon's largest record child is `Record.RasterCascadeShadow` at 0.137 ms, and `Shadow.FitCascade` is 0.067 ms.

One Matrix GPU Indirect frame with allocation call stacks attributes every `GC.Alloc` under `RecordCamera` to `AtmosphericLUTPass.cs:265` (`new AtmosphereProduction`), 868, 453, 289, 40, 17 and 17 bytes. Spazon, camera still, allocates nothing in `Render`.

### B10 final gate — 2026-11-17 (macOS Metal, Unity 6000.6.0f1)

Branch `refactor/convergence-b10`. Image baseline `1a357c0`. EditMode 882 tests: 870 passed, 11 failed (10 `ShaderStrippingTests` on `attempt to write a readonly database`, `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`), 1 skipped. Static checks 0 errors; 47 static-check tests pass. `frameAllocations` is 0 in three Play windows (2 s, 4 s, 6 s) after warmup, GPU indirect, Scene_Spazon.

A/B is four 300-frame runs, A = `1a357c0`, B = this branch. Frame CPU is 1.601 ms then 1.476 ms on A, and 1.822 ms then 1.516 ms on B (+13.8% and +2.7%). Pass GPU sum is +5.6% and +2.7%. `Inl_RecordRG` is 0.341 ms then 0.301 ms on A, and 0.401 ms then 0.339 ms on B (+17.7% and +12.6%). The return rule needs both frame pairs slower by more than 5% and the gap on this branch. The second frame pair is 2.7%. `1a357c0` is 24 commits before the branch point, so the `RecordRG` gap is not isolated to the arena.

Players are Mono, Scene_Spazon only, 1445×813. Development and release both succeed. The 432 build-report errors are `attempt to write a readonly database`; there are no script or shader errors. Release stripping keeps 10751 variants and 319 compute variants. The release player has no `Unity.RenderPipelines.Infinity.Validation.dll`. Its Runtime DLL has no `RenderCaptureService`, `ComputeDebugView`, `RenderFaultValidation`, `PipelineDiagnostics` or `InfinityDebugDisplaySettings`. With the player in front, release content means are 21.34 and 21.37, development 21.66 and 21.51, and the Editor Game crop is 20.94. Release against development is mean absolute difference 0.83 with 2.3% of pixels above 8. Editor against release is 1.74 and 4.1%, inside the Editor's own 8 s change (2.88 and 13.2%). `Player.log` has no thrown exception. An unfocused player pauses, so an early frame repeated at 15 s and 30 s is not a render result. D3D12 and Vulkan remain TODO(UNVERIFIED); they are not available on this Mac.

### Refactor final gate — 2026-11-15 (macOS Metal, Unity 6000.6.0f1)

Branch `claude/kind-knuth-d599st`, gate passed at `10d2360`; image baseline `1a357c0`. Results measured at `e818357` carry over because the later commits change only the release player paths and the Editor regression below.

- Compile and tests: Editor, `INFINITY_UNITY_CLI` and the release player script build have 0 errors. EditMode 846 tests: 834 passed, 11 failed (10 `ShaderStrippingTests` failing on `attempt to write a readonly database`, `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`), 1 skipped. Static checks 0 errors; 45 static-check tests pass.
- Images at 1445×813 against `1a357c0` (`e818357`): Scene_Spazon fixed, moving and TAA frame 30 have mean absolute difference 0.068, 0.021 and 0.180 and at most 0.04% of pixels differ by more than 8; Scene_BoxMatix 0.003. Per-feature results inside those frames are not isolated.
- Editor regression after the diagnostics fence (`10d2360`): Scene_Spazon Play frame mean 21.296 (21.16 before).
- Memory and time (`e818357`): `frameAllocations` 0 in three Play windows after 300 warm-up frames with GPU indirect; the first A/B pair measured B 3.0% faster than A, which rules out the slowdown criterion.
- Release player (Mono, Development off, Scene_Spazon only): builds with no script or shader errors, strips to 10751 kept variants and 319 compute variants, contains no `Unity.RenderPipelines.Infinity.Validation.dll` and no diagnostics type names, and renders the same frame as the Editor (mean 21.367 against 21.296). A Development player of the same scene matches it: window captures cropped the same way at 15 s and 30 s have means 21.19 and 21.21 against 21.27 and 21.30, with mean absolute difference about 1.0 and 2.6% of pixels differing by more than 8 (frame-to-frame noise; the Development Build watermark covers 0.04%). Its `Player.log` has no exception. Its 80 lines of Metal `xlatMtlMain` / `duplicated indices pair (<0, 0>)` come from the Example project's preloaded `ShaderVariantCollection` (Scriptable Render Pipeline pass type entries of `InfinityLit` warmed with one color attachment; a Development player with `logWhenShaderIsCompiled` matched them to the `RasterGBufferPass` fragment variants `<no keywords>`, `INSTANCING_ON`, `LIGHTMAP_ON` and `INSTANCING_ON LIGHTMAP_ON`), not from a frame draw. With the project's Preloaded Shaders emptied, a Development player logs 0 such lines and uploads only the frame's own GBuffer variants (`SHADOWS_SHADOWMASK`, `DIRLIGHTMAP_COMBINED LIGHTMAP_ON SHADOWS_SHADOWMASK`, and `InfinityLit-Instanced` without keywords); see the Manual's [getting started](../../Manual/getting-started.md) page.

### Sort key and record allocations — 2026-11-12 (macOS Metal)

Note: the two concrete gather jobs named here became one generic `MeshDrawGatherJob<TKey>`, registered for Burst with `RegisterGenericJobType` (2026-11-13, `79a397e`). The results below predate that change.

Three changes, one goal: the steady frame stops allocating just to name a failure or to carry a list that already exists.

**Sort.** Every mesh draw list orders by one `ulong`, then command, entity and fade. Opaque, depth, shadow and motion use `StateSortKey`: fading sits in bit 63, the command state sits below it. Shader rank uses 15 bits so bit 63 stays clear. Translucent color uses `TranslucentColorSortKey`: priority in the high 16 bits, distance from far to near in the low 32. T0, T1 and T2 stay on the CPU. Burst cannot enter a generic job, so the schedule is two concrete jobs, `MeshDrawGatherStateJob` and `MeshDrawGatherTranslucentColorJob`.

**Names and lists.** A resource check keeps a constant API name and builds the failure sentence only when the check fails. Preparing mesh lists does not allocate a `params` array. Compile clears one segment dictionary instead of creating a new one. A shadow moment atlas is sampled only when that camera produced it (`INFINITY_CASCADE_SHADOW_MOMENTS`, `INFINITY_LOCAL_SHADOW_MOMENTS`); a missing atlas is not bound and is not `Texture2D.blackTexture`. The four fixed ray-traced occlusion reasons are static strings. In-pass markers are `static readonly ProfilingSampler` fields or fixed tables (mesh draw, shadow group, color pyramid batch); a mesh pass cache resolves its sampler once.

**Uploads.** Cookie and probe uploads copy into one array on the atlas. The array doubles when it is too small and is reused by the next camera after this camera's queue observer runs. The two observers are created with the pipeline. A check of several compute kernels receives a static name array. A realtime probe is still filtered every frame it is visible. A cookie is still uploaded only when its texture changes.

**Checked.** Unity 6000.6.0f1, task mirror, Metal. Static check: 0 error, 0 warning. `dotnet build` of the runtime and test projects: 0 error.

- Sort: `MeshDrawListTests`, `MeshLodTests` and `SelectBackend_FollowsOverrideThenDebuggerThenAsset_AndRecordsCpuPassesOnCpu` were 17 passed and 0 failed after the translucent shader imported. The first run failed because `InfinityLit-Instanced` had not compiled `RasterTranslucentT0Pass` yet. The play log did not report the gather jobs as unknown Burst entry points.
- Names and lists: `RenderGraphCompileTests`, `FeatureRegistryTests`, `ShadowSamplingGpuTests`, `ShadowMomentGpuTests` and `DeferredLightLoopGpuTests` were 47 passed and 0 failed after import. The first import failed on missing shadow kernels. `FilterShadow` warned that a variable was uninitialized; that path is now `FilterShadowDepth` with one return. A later shader import exited 0 and printed no shader warning.
- Uploads: `LightCookieAtlasTests`, `LightCookieTransactionTests`, `ReflectionProbeTests` and `ReflectionProbeTransactionTests` were 35 passed and 0 failed, including both `TakeUploads_ReusesTheArray_AndMarksProduced` tests.

Play mode opened `Scene_Spazon`. The Game view showed the courtyard, about 1.0M triangles, 579 draw calls and 586 instances. The new console line was the one present-format log. A Unity search-indexer `ArgumentOutOfRangeException` was outside InfinityRP.

**Not checked.** `infinity_runtime_probe_start` did not run. `unity status` stayed `STATUS_PIPELINE_LOAD_PENDING`, so there is no 5/20/60 probe window, no `allocatedViewCount` and no separate CPU Direct and GPU Indirect probe. One Editor RSS sample taken around the sort change (1,472,416 KB to 1,477,952 KB over 60 seconds) is the whole Editor, not a probe live set. Player, iOS, D3D12 and Vulkan were not run. The owned Editor was not attached.

### State order and render-graph scratch — 2026-11-11 (macOS Metal)

`ESortMode` is gone. Opaque mesh passes submit commands in `stateKey` order on both backends and do not order instances by distance. Translucent color passes still order by priority then distance on the CPU. Render-graph compile scratch and in-pass markers are reused after the first camera instead of allocated every frame.

Unity 6000.6.0f1, task mirror, Null device. `MeshDrawListTests` and `MeshPassCacheTests`: 13 passed, 1 skipped (`Compute shaders with GPU readback are not supported on this device`). `StateBuild_MergesOneCommandAcrossDistances` and the translucent distance-order test passed. `dotnet build` of the runtime and test projects: 0 error. Static check: 0 error, 0 warning. The baseline on the same filter, before this change, was 5 passed, 1 failed (the translucent shader pass was not imported yet), 1 skipped.

TODO(UNVERIFIED): Game-window frames and 5/20/60 second memory. The owned Editor was not attached, and batchmode has no Game view for the runtime probe. Player, iOS, D3D12 and Vulkan were not run. Draw-call reduction is not a GPU-time result.

### Mesh preparation record order — 2026-11-11 (macOS Metal)

Note: these results predate `54062f7`, which replaced the per-view mesh names below with `MeshCullingContext` and one producer per draw list.

Recorded order is the execution order. Nonempty lists upload and compact after their culling pass and before the raster block. Marker names and the one verified `Scene_Spazon` frame are in [the record](../../Record/Evidence/MeshPrepOrder-2026-11-11/Results.md).

TODO(UNVERIFIED): continuous play, 5/20/60 second memory, and the residency readback. Player, iOS, D3D12 and Vulkan were not run. `HierarchicalRay_HitIsOneActualSurfaceTexel(True)` was not run and is not claimed.

### Mesh backend profiling and optimization — 2026-11-09/10 (macOS Metal)

Note: these results predate `54062f7`, which replaced the per-view mesh names below with `MeshCullingContext` and one producer per draw list.

Unity 6000.6.0f1 on an isolated Example mirror compiled and rendered CPU Direct
and GPU Indirect. Nonempty GPU lists use two dispatches; structurally empty
lists schedule no mesh preparation. Frame labels identify view, pass, phase and
list, and CPU markers separate schedule, Complete, upload and draw. Scene and 10,000
moving-instance probes completed at 5, 20 and 60 seconds for both backends;
`BoxMatix_MeshPipeline` rendered on both. A long-lived Editor showed a repeatable
slowdown that returned near baseline after a restart, with cause unproven.

TODO(UNVERIFIED): The 835/838 EditMode result includes two parent-baseline failures
and one skip. Partial-submit GPU cancellation, GPU timing, native Metal capture,
Player and other graphics APIs remain unverified. [Evidence, measurements and
limits](../../Record/Evidence/MeshBackend-2026-11-09/Results.md).

### Owner layout validation — 2026-11-09 (macOS Metal)

TODO(UNVERIFIED): The owner-layout candidate compiles and renders in an isolated
Unity 6000.6.0f1 Editor, but the EditMode gate has two reproducible failures and
the Editor emitted `attempt to write a readonly database`. The suite reported
819 passed, 12 failed and 1 skipped. Ten failures observed that database error;
the remaining failures reproduce individually in mesh submission and the
screen-space hierarchical-ray GPU contract. Both failures and the database
error also reproduce on the parent commit in the same mirror and Editor.
[Measurements and window captures](../../Record/Evidence/OwnerLayout-2026-11-09/Results.md).

This is the only document that states how far each feature is verified. Other documents describe design and use;
they link here instead of claiming "completed".

Latest bounded acceptance: [2026-10-26 Unity integration boundary receipt](../../Record/History/UnityIntegration-20261026.md).
Earlier dated sections below retain their original verification boundaries; the latest section supersedes them only for the cases it actually exercised.

### Mesh drawing pipeline — 2026-11-08 (macOS Metal)

Note: these results predate `54062f7`, which replaced the per-view mesh names below with `MeshCullingContext` and one producer per draw list. `MeshViewTests` is now `MeshCullingContextTests`.

Editor acceptance of the mesh drawing pipeline on an independent Example mirror. Unity 6000.6.0f1, Metal, Apple M3 Max. The user's Editor stayed on the original Example project. Numbers are in [History/MeshDrawing-2026-11-08/Results.md](../../Record/History/MeshDrawing-2026-11-08/Results.md).

`MeshWorld`, its pass caches, per-view LOD selection, CPU and GPU draw-list backends and the render-graph extension are the implementation in [ARCHITECTURE.md §7](../../../ARCHITECTURE.md#7-mesh-drawing-pipeline). `MeshComponent` draws a `MeshAsset`. The user Example's `Scene_Spazon`, `BoxMatix_MeshPipeline` and `MeshGroup_64000-1` hold `asset` on their drawn mesh components.

Compile on this Editor: zero C# errors. `Compute_MeshDrawPipeline` initially failed Metal compile because `MeshLOD.hlsl` indexed `float4` with a variable. `dbe6b62` selects the component explicitly. A following Play frame on GPU indirect did not log `Kernel at index is invalid`. `CS0414` on unused `m_Disposed` and `m_UpdateInit` was already present at the `a814a40` baseline.

In the user Example, Spazon assigns an asset on 8 of 8 components, `MeshGroup_64000-1` on 8000 of 64000 and `BoxMatix_MeshPipeline` on 8008 of 64008, whose 8000 prefab instances resolve the prefab's `asset`. Play mode on this Editor put 8 entities in the Spazon mesh world and 8008 in the Box mesh world.

EditMode `infinity_tests_start` at the candidate before the draw-list test fix: 813 passed, 15 failed, 1 skipped. `MeshDrawListTests` was rerun after `6509201` and passed 7/7. Ten `ShaderStrippingTests` failures are `attempt to write a readonly database` and already failed the same way at `a814a40` on this ExFAT mirror. `MeshResidencySubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs` and `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)` also failed at `a814a40`. `FindPassIndex_ShaderReplacement_ResolvesCurrentShaderIncludingMissingPass` failed on that first candidate (`material.FindPass` returned 0, `FindPassIndex` returned -1). The round 2 suite, below, passes it.

`Scene_Spazon` MainCamera, 1666×1754, both backends: Play window pairs differed, and the Game view shows the courtyard. DisplayColor `R16G16B16A16_SFloat` against the `a814a40` capture, without masking animation or newly visible translucent draws: CPU SSIM 0.986, GPU SSIM 0.985, changed-pixel fraction 0.0066. That misses SSIM ≥ 0.99 and changed pixels ≤ 0.5%. The captures were not time-frozen.

Probe, 100% moving, windows 5/20/60 s. Candidate 10k proxy p50 at 60 s is 0.387 ms CPU and 0.382 ms GPU, under 2 ms. Baseline 10k at `a814a40` was about 6.7 ms CPU and 6.5 ms GPU. Proxy `GC.Alloc` p50 is 0. ResourcePool texture estimate stayed 608,287,712 bytes in every window. Managed heap from 20 s to 60 s stays under 1 MiB at 0, 1k and 10k. CPU 50k grows 2.5 MiB (245,334,016 to 247,955,456). GPU 50k does not. A separate continuously moving camera was not probed.

Round 2, same day, Unity 6000.6.0f1, Metal, Apple M3 Max, mirror Editor only. Full EditMode is 816 passed, 12 failed, 1 skipped. `FindPassIndex` passes. The 12 failures are the stripping logs, the residency case and `HierarchicalRay_HitIsOneActualSurfaceTexel(True)`, and each of those filters fails the same way at `a814a40`.

A moving camera on Scene_Spazon, 10k moving objects, windows 5/20/60 s: `allocatedViewCount` stays 7 on CPU and 8 on GPU, the texture pool stays 608,287,712 bytes, and the heap from 20 s to 60 s is flat on CPU and +360,448 bytes on GPU. CPU 50k at 20/60/180 s still grows, by 2.70 MiB then 1.65 MiB, with proxy `GC.Alloc` p50 at 0 and the same 680 render-scope allocation count as the 10k CPU run. No mesh-proxy owner was changed.

Frozen Scene_Spazon DisplayColor is SSIM 0.9758 and 2.03% changed against `a814a40`. Two frames inside one candidate Play session are SSIM 0.9855 and 0.73% changed, so the 0.99 / 0.5% gate is below this capture’s jitter floor. The difference was not assigned to a pass.

LOD, on a mirror-only 3-level asset (`fadeWidth` 0.3, last bound 0.08): CPU and GPU, perspective and orthographic, switch red cube, green sphere, then blue cylinder, the last level is gone at the far pose, and the shadow follows the visible mesh. T0 and T2 overlap back-to-front at equal priority. T1 draws the higher priority first in the sort key, which is the comparer contract. Edit mode updates a static mesh for an undo-recorded move, a parent move, undo, redo and a layer hide. Play-mode hierarchy churn does not call `NotifyAllStaticPoses`. Two loaded main-stage scenes share one `GetSceneCullingMask`, so moving between them does not change the draw.

Round 3, same Editor and GPU. The reserved Mono heap is not the live set. After `GC.Collect` at each window, `Profiler.GetMonoUsedSizeLong` from 60 s to 600 s changes by -57 KiB on HEAD CPU 50k, -16 KiB on HEAD GPU 50k, -144 KiB on `a814a40` CPU 50k, and +184 KiB on `a814a40` GPU 50k. All are under 1 MiB. `GC Allocated In Frame` p50 is about 56 KiB at both 10k and 50k on HEAD CPU (680 allocations), and about 69 KiB at both counts on HEAD GPU (883 allocations). The baseline is about 53 KiB CPU and 62 KiB GPU at both counts. The bytes do not grow with object count. The earlier reserved-heap rise is allocator behavior. No leak.

Scene_Spazon was compared with time frozen, `Rotate` and `CameraMovement` disabled (`Rotate` turns every `Update` by a fixed angle and ignores `timeScale`), and `lockHistoryReset` so the captured frame has jitter 0 and frame index 0. Two baseline frames are SSIM 1 and 0 changed pixels. Two HEAD frames are the same. Against `a814a40`, raw SSIM is 0.97824 and 1.63% of pixels change. The first differing buffer is `Depth`. `Motion` is identical. Outside the screen bounds of Spazon's 8 `MeshComponent`s, SSIM is 0.999956 and no pixel exceeds the luma step. Those 8 components are drawn on HEAD and are absent from the baseline frame. That is the pipeline drawing them, not a depth or transform error in the rest of the scene. Spazon has no translucent `MeshComponent` and no LOD fade band in this view.

Moving an object between two loaded main-stage scenes stays closed. Both scenes share one `GetSceneCullingMask`. Pose, parent, undo, redo and layer were shown in round 2. Distinct masks are covered by `MeshViewTests`.

Naming convergence at `ccfdfb8`, same Editor and Metal, does not change that behavior. EditMode is 817 passed, 12 failed, 1 skipped, and the 12 failures are the stripping logs, the residency case and `HierarchicalRay_HitIsOneActualSurfaceTexel(True)`. Project `AssetBaseline.py compare` is 0 differences on 137 inputs. The Editor rewrote `ProjectSettings`, `TimeManager` and `project.slnx` while it was open; those files were restored from the pre-rename backup, and `Scene_Spazon` was not saved. Frozen DisplayColor against that backup's capture is SSIM 1 and 0 changed pixels on CPU (`final-cpu-8`) and GPU (`final-gpu`). A quiet 10k all-moving probe gives a 60 s proxy p50 of 0.390 ms CPU and 0.371 ms GPU, inside 10% of the round-2 0.387 / 0.382 ms, with proxy `GC.Alloc` p50 at 0. An earlier probe on a busy Editor sat outside that band; its per-frame minimum was already under the round-2 median. The rename table is [History/MeshDrawing-2026-11-08/Naming.md](../../Record/History/MeshDrawing-2026-11-08/Naming.md).

GPU culling and preparation naming, 2026-11-09, same mirror, Unity 6000.6.0f1, Metal, Apple M3 Max. The user's Editor stayed on the original Example project. EditMode is 819 passed, 12 failed, 1 skipped. The 12 failures are the same stripping logs, residency case and `HierarchicalRay_HitIsOneActualSurfaceTexel(True)`. The new log window has no new InfinityRP `InvalidOperationException`. On `Scene_Spazon`, a GPU frame with Hi-Z history has three `ComputeMeshCulling` events (prediction, one frustum batch, recovery) and `ComputeMeshCompaction` is not inside `RasterDepth`. The first frame, with no history, records one `ComputeMeshCulling`. At `69a0465` the same scene put compaction inside `RasterDepth`, `RasterGBuffer` and the cascade groups, and the frame tree listed six `ComputeMeshCulling` events. Frozen DisplayColor, 1666×1754, history reset, jitter 0: CPU is SSIM 1 and 0 changed pixels against `69a0465`. GPU is SSIM 0.9998815 and 31 pixels above 8/255, and those 31 pixels are the ones that also differ between two `69a0465` frames. A 10k all-moving probe with a camera path gives a 60 s proxy p50 of 0.428 ms CPU and 0.365 ms GPU, proxy `GC.Alloc` p50 0. The GPU figure is within 10% of the round-2 0.382 ms. The CPU figure is 10.5% above 0.387 ms; its per-frame minimum is 0.298 ms. `allocatedViewCount` stays 4 while that camera moves, and the texture-pool estimate stays 608,287,712 bytes. GPU timestamp queries were not available. The numbers are in [History/MeshDrawing-2026-11-09/GPUCulling.md](../../Record/History/MeshDrawing-2026-11-09/GPUCulling.md).

D3D12, Vulkan and Windows stay `TODO(UNVERIFIED)`.

### RenderGraph compiler — 2026-10-27

The compiler culls across resource types, batches a submission segment per queue run, and merges compatible raster passes into one native render pass. Handles carry a generation. Profiling scopes are pass ranges. History retirement waits on the frame fence. Unity has no GPU heap, so overlap is descriptor-level reuse by ResourcePool. Memoryless is requested only on Metal, only for an attachment that stays inside one native render pass and ends in `DontCare`, and not for a color UAV.

Verified on the owned InfinityExample Editor, Unity 6000.6.0f1, Metal, `Scene_Spazon` MainCamera at 1666×937. The post-refresh `Editor.log` window and the rest of this Editor session contain no InfinityRP `InvalidOperationException`, no `numAttachments` assert, and no memoryless/UAV warning.

EditMode via `infinity_tests_start`:

- `RenderGraph`: 57 tests, 56 passed, 0 failed, 1 skipped. The skip is `RenderGraphQueueGpuTests.CrossQueueHazardsAndPhysicalReuse_PreserveValues` because this GPU/API does not support async compute. `GraphicsQueueHazardsAndPhysicalReuse_PreserveValues` passed. The run includes `RenderGraphCompileTests` (including the merged-execute and split-segment profiling scope cases), `RenderGraphQueueHazardTests`, `RenderGraphFailureTests` and `RenderGraphViewerTests`.
- `MeshDrawListLifetimeTests`: 11 passed, 0 failed.
- `HistoryCacheCommitTests`: 6 passed, 0 failed.

The Play snapshot for MainCamera had 3 submission segments, 36 physical alias slots, and merged groups: RasterDepth with RasterGBuffer (subpasses 0 and 1), object motion with camera motion, and translucent T1 with T2. This frame culled no passes, so the viewer had no cull reason to show. Cross-type culling is covered by the passing EditMode tests.

Two window captures of the Scene_Spazon Editor differed after Play advanced from frame 121 to 459. The Game view shows the courtyard, columns, arches and banners. An earlier pair taken while the player loop was not advancing was identical and is not liveness evidence.

Frame Debugger export: 107 events for MainCamera. Draw scopes are labeled `(RP 0:0)` on RasterDepth and `(RP 0:1)` on RasterGBuffer, which is Unity's label for two subpasses of one native render pass. Cascade shadow draws stay in `(RP 2:0)`. The export has no separate `BeginRenderPass` event. This is not a Metal System Trace of on-chip memory.

Memoryless: that snapshot marked 0 resources. Sampled depth, GBuffer, lighting and history targets were not marked, so ordinary targets were not mis-tagged. No memoryless allocation was observed on this content.

Still open: D3D12 and Vulkan, a Metal capture of tile memory, and a Spazon frame that actually culls a pass.

### Unity integration ownership — 2026-10-26

Native Renderer content retains Unity culling/renderer-list/per-object ownership. Custom Mesh keeps Infinity scene,
CPU/GPU and history ownership, with no Lightmap or CPU per-object Probe data; world-space APV stays enabled.
The custom lighting cache and Renderer takeover are deleted with their consumers. Shadowmask uses visible native
Light.bakingOutput. URT native/custom instance identities and build acceptance, component registration, APV cleanup,
explicit terrain authoring and project-specific development tools are closed through their consumers.

Static: 0 errors / 0 warnings, 14 negative checks pass. The full 822-case run initially had five failures; all five
are resolved by traceable affected-class reruns. Including two new Terrain contracts, 823 unique cases pass and
one real cross-queue GPU case remains skipped. This is not a second full 824-case run. Final loaded production
ShaderUtil diagnostics: 25 Compute and 25 surface Shaders, zero messages; generated Lit/Unlit/Decal graphs draw.

Full Scene_Spazon matched 1666×937 Game frames retain the enabled chain, finite GPU results and byte-equivalent
four-cascade depth. Actual native Bake/reopen, APV bake/load and independent custom Mesh APV output pass.
RTAO actually dispatches on URT Compute. Native SkinnedMeshRenderer/Terrain on/off normal-camera readbacks prove
draw output. Complete supplement Graph/APV Frame Debugger export passes; a separate native payload timeout is
not counted as complete-tree acceptance. Frozen normal-frame GC counts are zero; original dynamic frames have
zero proxy/library-controlled allocations, with four allocations measured entirely inside native Submit.

The authorized source scene migration removes eight null obsolete fields, preserving 18,716 retained properties
and identities; save/reopen/second-save/no-op checks pass. Thirteen unrelated resource files retain their bytes.
Metal is verified within this scope. D3D12/Vulkan, true cross-queue and native Submit fault injection retain their
existing unverified boundaries; hardware RT and Player results are not inferred. See the linked receipt for details.

Cleanup: owned Editor PID 9896 exited; tests/captures/readbacks drained, Frame Debugger/Play/pause off and clean
Scene_Spazon restored. Exact task mirror `intermediate/unity-boundary-20261026` and its independent APFS Library
cache were removed and absence checked. Generated bake/graphs, raw captures, task helpers/log and temporary
source rollback bytes are gone. Only the concise receipt/preview and audit/inventory remain; source Library and
other sessions are preserved. This acceptance preceded the subsequent user-authorized commit and publication;
the delivery scope is this package, with the unrelated resource edits and Example repository changes preserved.

### Lighting-cache reload regression — 2026-10-26 (historical, superseded above)

The current Scene_Spazon Editor exposed stale LightmapData wrappers: all 11 cached
sets held destroyed textures while Unity's 11 live sets retained valid color,
direction and Shadowmask textures. The cached shadowmask-presence decision could
therefore disable the baked part of Mixed/Shadowmask lighting. All eight custom
Mesh SH samples matched fresh native interpolation at their cached positions;
this observation does not establish a separate Probe interpolation defect.

The bounded fix checks destroyed wrappers on cache access and invalidates on
Editor scene/open-close and Play transitions, retaining stable-frame reuse and
the explicit runtime lightmap-edit notification API. Only the cache implementation
and its targeted tests change. Unity Roslyn compilation of Runtime and Tests
passes without diagnostics; static checks and 12 negative checks pass. After the
user-authorized Editor restart cleared the Bee/Mono file-descriptor failure,
the actual candidate DLL loaded and all eight targeted tests passed (run
`4583eccc-d8f0-4aa8-a434-5a6616e826c7`), including destroyed color/direction/mask,
legal empty tables/slots, the Shadowmask consumer and calibrated zero-allocation
stable reads. All three destroyed-reference cases were also re-executed directly
against Scene_Spazon's actual 11-set table, and the compiled stable-read assertion
passed for 512 accesses to that same live table. The earlier old-DLL two-test run
is not used for acceptance.

Scene_Spazon MainCamera frame 80 rendered at 1666x937 on Metal with actual present.
BakedDiffuse, BakedOcclusion, Lighting, DisplayColor and CascadeShadow readbacks
were finite; all four cascades were valid and depth ranged from 0 to 0.800506.
The normal image was inspected; a [linear-to-sRGB readback preview](../../Record/History/LightmapCache-20261026.jpg)
is retained. After restoring Edit mode and original poses, all 11 cached map sets
matched Unity's live color/direction/mask references, Shadowmask detection was true,
279 probes remained loaded and all eight Mesh SH samples still matched native
interpolation exactly. Scene dirty/Play/pause were false and background rendering
returned to false. Existing 13 scene-independent serialized resource edits stayed
byte-identical; no rebake, asset save or shadow-bias change was made.

Unity Test Framework's bootstrap scene creation emitted native "attempt to write
a readonly database" diagnostics. They are retained in the user's Editor log and
are separate from the eight passing assertions and successful normal-frame GPU
readbacks; no database/cache repair or diagnostic suppression is part of this fix.
The Editor remains open. Task tests/capture/restore callbacks were drained and
the task-owned intermediate directory was removed; only this status and preview
are retained. Other graphics APIs were not revalidated for this bounded repair.

Columns:

- **Recorded**: the pass records in the RenderGraph when the feature is enabled.
- **EditMode**: covered by EditMode tests in `Tests/Editor` (CPU contracts or GPU numeric contracts).
- **Image**: a captured normal frame was inspected and accepted. `Open` means not yet captured or not accepted.
- **Perf**: GPU timing measured. `Open` means no accepted GPU measurement.
- **Platforms**: platforms where the Editor actually rendered it. D3D12 and Vulkan are unverified everywhere.

0.5.0 rows are marked `0.5.0` and stay `Open` in Image/Perf until verified on a real Editor.

## Geometry and lighting

Batches B5 to B7 of this branch passed their local gate at `2133e38` (commit `472e6b4`); the gate lists were deleted and their results are not recorded here, so a row keeps the date of its last recorded result. `TODO(UNVERIFIED)` in a Notes cell marks a change after that result.

| Feature | Recorded | EditMode | Image | Perf | Platforms | Notes |
|---|---|---|---|---|---|---|
| Mesh drawing pipeline (CPU + GPU backends, LOD, translucency) (0.5.0) | Yes | 2026-11-08 / Unity 6000.6.0f1 / Metal: full suite 816 passed, 12 failed, 1 skipped. `FindPassIndex` passed. The 12 failures match `a814a40`. | 2026-11-08 / Unity 6000.6.0f1 / macOS Metal: locked Scene_Spazon frames match themselves (SSIM 1). Outside the 8 MeshComponent bounds, baseline vs HEAD is SSIM 0.999956 and 0 changed pixels. | 2026-11-08 / Metal: post-GC live set is flat from 60 s to 600 s at 50k. Frame allocation bytes do not grow from 10k to 50k. | macOS Metal | D3D12, Vulkan and Windows TODO(UNVERIFIED). Reserved Mono heap is not the live set. See the 2026-11-08 section. Visibility moved into each draw list's producer (`MeshCullingContext`, occluder slot and remainder list, `54062f7`) and the generic gather job replaced two concrete jobs. Refactor final gate 2026-11-15 (top section): Scene_Spazon fixed, moving and TAA frame 30 match `1a357c0`, `frameAllocations` 0 in three Play windows, A/B busy time not slower. B10a 2026-11-16 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play DisplayColor 1678×1746 RGB mean 0.033, non-finite 0, history reset, jitter 0. `RasterDepth` and `RasterGBuffer` share native group 0 and their producers sit before that group; the motion producer sits before `RasterObjectMotion`; four shadow producers sit before `RasterCascadeShadow`, all on the graphics queue. A numeric diff against `1a357c0` is not this capture: the Game view is 1678×1746, not the gate's 1445×813. B10b same view after the pass-type merge: DisplayColor mean 0.275 including alpha, non-finite 0. B10c 2026-11-16 Play, pipeline-scope GC count 0 over 60 frames after 30 warm; mean frame ticks 40496 versus 44603 on the keyed array pool. |
| ThinGBuffer + Deferred shading | Yes | Yes (pack contract) | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play 1445×813 matches `1a357c0` (`mean abs diff` 0.025, 0.002% pixels > 8); toggling realtime local lights changes the `ComputeDeferredShading` lit region (`mean abs diff` 20.56, 50.44% pixels > 8). Earlier 2026-10-23 bounded eight normal fixtures / ground and marker controls. | Open | macOS Metal | Finite-plane horizon and marker shadow controls accepted; no atmosphere shader repair or arbitrary-scene guarantee. See closure picture report. Pass parameters use `DeferredShadingParameters` (`3f5a883`), camera matrices read `ViewUniform` (`0eb4f36`), and light list inputs read `LightLoopUniform` (verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Forward (forward-route materials) | Yes | Partial | Open | Open | macOS Metal | |
| Decal projection / RP replacement | Yes | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: 5 lifecycle cases | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: actual DBuffer overlays after replacement, disable/restore and second camera | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | One-shot context recovery; no source asset migration. Finite-plane horizon band traced to disabled planetary ground in a fixed-camera control, not an atmosphere algorithm repair. |
| Light shapes and record (0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: shape GPU 3, record contracts, actual Deferred weight GPU | 2026-10-22 / Unity 6000.6.0f1 / macOS Metal: all shapes, glossy/rough, radius, backface, barn-door and term controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Karis / Frostbite approximation. Fixed-input RGBA32F weight equations pass; strict normal RG16F half-ULP decomposition remains unproved. `LightRecordPack.TryPack` now reads transform, final color, range and spot angle from `VisibleLight` (`982eee7`). Refactor final gate 2026-11-15: with records packed from `VisibleLight`, Scene_Spazon matches `1a357c0`; the release player frame shows the same rect, tube and sun shapes as the Editor frame (shapes frozen by `LightShapeBuildProcessor`). 2026-11-24 / Unity 6000.6.0f1 / macOS Metal. LightShapeTests passed 49/49 in the Editor Test Runner. The Inspector was checked at normal and narrow widths, in both themes, for Directional and Point lights and their mixed selection. In Scene_Spazon Play Mode, a temperature drag changed 7000 K to 2925 K with visible warming. Entering 8400 K and Undo/Redo updated the light. A Point light accepted a source radius edit and the expanded Additional Light Data body was empty. The revised flat Inspector was checked at normal and narrow widths. In Scene_Spazon Play Mode, the right-hand intensity edit from 3.14 to 1.5 visibly dimmed the Game view, a Diffuse Weight drag reached 0.443, and the Specular Weight field accepted 1.25 above the slider range. The Pipeline listener failed to start, so CLI execution was unavailable. HDRP window comparison, Prefab partial Apply/Revert, reentrant Undo rollback and the first Scene handle shape Undo/Redo are TODO(UNVERIFIED). |
| Lightmapper delegate (0.5.0) | Editor bakes | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: shape/record/angular GPU contracts | 2026-10-21–23 / Unity 6000.6.0f1 / macOS Metal: bounded S6/S12 actual bakes | — | macOS Metal | Authored Disk uses native Rectangle carrier and Disc baking. Matched spot window/intensity controls accepted; original spatial/caster residuals remain separately reported. |
| Light cookies + IES atlas (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (5 allocator/state + 11 transaction tests) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (Editor whole-frame, 3×300 before/after) | macOS Metal | Normal-frame consumers, real IES prefab import, reimport and camera/queue recovery verified; timing is not isolated pass cost or causal speedup. Native Point clears a 2D cookie; the retained-invalid-reference Inspector warning is not reachable through that setter. See S11 report. Refactor final gate 2026-11-15: `INFINITY_LIGHT_COOKIES` gating; the cookie on/off frame-setting change matches `1a357c0` (about 1.0% of pixels change in both), no "property not set". |
| Clustered light list (log z-bins + tile bitmasks, 0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: binning 6, actual Deferred GPU 8; exact-index probe | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: disabling Play-mode local lights on Scene_Spazon drops `LocalLightList` production (`localLights` 2 → 0), removing tile contributions while keeping the directional sun light (mean 41.24 → 20.95, `mean abs diff` 20.56, 50.44% pixels > 8). Earlier 2026-10-22 point-light Deferred/Forward, inside/behind/far, multi-camera; S6-matched fog/cloud. | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Caller-owned out state; matched compute projection. Whole-frame area/local workload timing does not isolate clustering cost. Counts and tile parameters use the `LightLoopUniform` per-camera record (`3f5a883`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Cascade + local shadows (1–4 cascades with blending; tent PCF, PCSS, VSM, EVSM; every light shape, 0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (allocation, filtering, GPU moments, cache transactions, Shadowmask filtering) | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Play-mode directional `shadows` `Soft` → `None` → `Soft` removes and restores cascade shadows (BoxMatix_MeshPipeline mean 12.43 → 15.83, `mean abs diff` 3.637, 10.91% pixels > 8, restored 12.59 / `mean abs diff` 0.401; Scene_Spazon dynamic casters `mean abs diff` 0.502, 0.97% pixels > 8, restored 0.363); local spot `shadows` `Soft` → `None` → `Soft` removes and restores local shadows (mean 41.24 → 42.40, `mean abs diff` 1.518, 4.95% pixels > 8, restored 42.09). | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Normal frames and native atlas/dispatch/Inspector evidence; historical S9 requires the existing S12 matrix-alias repair. Small EVSM half residual remains reported. Shadowmask filtering accepted; bounded S12 GI/APV fixtures verified separately. Shadow constants use `ShadowUniform`, shadow casters project with `_ShadowCasterViewProj` (`0eb4f36`, `3f5a883`), and additional light data is resolved from the registry once per frame (`93a664a`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Screen-space traced (contact) shadows: sun + 24 local lights (0.5.0) | Yes | 2026-10-22 / Unity 6000.6.0f1 / Metal: allocation 2/2; capture 13/13 | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play with `ContactShadow.enable` (`Intensity` 1.0) and `EFrameSetting.ContactShadows` produces `ContactShadow` and darkens fine geometry (`mean abs diff` 0.439 / 0.81% pixels > 8 / 6.23% > 2 vs Volume `enable` off; `mean abs diff` 0.488 / 0.82% > 8 / 6.99% > 2 vs `ContactShadows` frame setting off, both dropping `ContactShadow` production). Earlier 2026-10-22 normal sun/local/Forward, R8/R32 outputs, 30-light allocation, off and missing-producer controls. | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Single visible-depth approximation; thin-caster positive controls and thick-caster misses retained. Low/Ultra native step counts verified; no quality or performance improvement inferred from different global RNG frames. Pass parameters use `ContactShadowParameters` (`3f5a883`) and camera matrices read `ViewUniform` (`0eb4f36`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Reflection probes in the clustered list + probe atlas (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (17 layout/priority/transaction tests; actual GPU filter contracts) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal: normal baked/custom/realtime, blend/priority/projection, matched Deferred/Forward, same-frame failure and reimport controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: composite Editor frame, 3×300 before/after (closure report) | macOS Metal | Axis-aligned and existing capacity limits remain. Three independent fresh-RP first/stable Game pairs pass; historical purple/black root cause unknown. Composite baked-environment/realtime-probe timing includes six faces, not APV/lightmap or isolated pass cost. Refactor final gate 2026-11-15: `INFINITY_REFLECTION_PROBES` gating; the probe on/off frame-setting change matches `1a357c0` (0.9% of pixels in both), no "property not set". |
| Shader Graph target: Lit / Unlit / Decal (0.5.0) | Graph import | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: generated graphs and contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: bounded Lit/Unlit/Decal, Forward, translucent and baked consumers | Open | macOS Metal | Actual generated/imported consumers remain accepted. Three cold probe-fixture first/stable pairs do not reproduce historical purple/black outputs; root cause unknown. Bounded final Display regression passes, not every graph configuration. Scene Depth outside decals and Fog node return defaults. |
| Atmosphere LUTs + sky IBL | Yes | Yes (parameters, cache) | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: disabling Scene_Spazon's `DirectionalLight` in Play (`dirLights` 1 → 0) invalidates the view and IBL atmosphere caches immediately without retaining the sunlit LUTs (frame mean 20.35 → 14.53, `mean abs diff` 5.999, 18.49% pixels > 8; `rawDepth == 0` sky with TAA reset 28.86 → 0.000000 sRGB, linear max 0.200 → 0.000000; 21076 unbaked lit pixels drop to exact 0.000000); `DirectionalLight` returns (`intensity` 3.14) upon leaving Play. | Open | macOS Metal | Refactor final gate 2026-11-15: halving the sun intensity in Play darkens sky and lit surfaces on the next frame (arch sky window 72.73 to 66.83). View and IBL cache keys include view direction/projection (`8ed87e3`) and sun illuminance (`1ee1046`), parameters bind as `AtmosphereConstants` (`afdcb52`, `3f5a883`), and `IntegrateAtmosphereRay` returns zero luminance when `Atmo_SunIlluminance` is zero so the unit-illuminance `MultiScatteringLUT` does not leak blue haze with no directional light (`e14ac4a`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| GTAO | Yes | Yes (numeric); 2026-11-14 Metal: record path `RenderGraphConstantsGpuTests` | 2026-11-14 Unity 6000.6.0f1 / macOS Metal, Scene_Spazon Play: AO DebugView mean about 0.93 (237–243 of 255) at y = −1.25 and +5; deferred indirect diffuse/specular 23.11 / 8.52 | Open | macOS Metal | Before `3f5a883` the pipeline's GTAO output was zero: in `2133e38` the AO DebugView, indirect diffuse and indirect specular were 0.00 at both heights while baked diffuse matched. Its parameters were then set member by member into the named cbuffer `CBV_OcclusionParameters`; they are now the bound `OcclusionParameters` record. The failure mechanism is not isolated: a standalone command-buffer test did fill a named cbuffer by name. Static check `cbuffermember` forbids the pattern. |
| RTAO (UnifiedRayTracing) | When URT is ready | Partial | Open | Open | Editor only | Player shader load unverified |
| SSR (stochastic) | Yes | 2026-10-30 Metal: numeric 10 (`e3aecb84-4ced-4edb-abb6-38a4885b536a`), including a dead history that must not cover the current hit. A history sample below half the current resolve is not reused, and a kept history is not darker than this frame. The blend weight remains the hit edge fade. Trace contract 7 (`e10ca20d-dd95-4b5e-85b9-8f9939d98340`). Earlier same day: Trace contract 7 (`44c8e982-d123-49d6-b5a4-6beb109e5447`): a grazing plane hits in both marches, and the planar test does not thicken a one-texel rod. Resolve stores the mean and deviation of the unpremultiplied hit colors. The blend weight is this frame's mean hit edge fade. A miss does not enter it, and temporal filtering does not replace it. | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play 1445×813 disabling either `ScreenSpaceReflection.enable` or `EFrameSetting.ScreenSpaceReflection` stops `SSR` production and removes floor reflections while keeping baked lightmaps (`mean abs diff` 2.238 / 12.19% pixels > 8 and 2.332 / 12.73% > 8; the two off paths agree within `mean abs diff` 0.200, 0.09% > 8). Earlier 2026-10-29/30 Scene view planar-step and history-rejection checks. | GPU time TODO(UNVERIFIED). D3D12/Vulkan TODO(UNVERIFIED) | macOS Metal | Empty trace samples record facing, edge, invalid color, or a direction into the receiver. Debug ray misses store an octahedron and an exit reason. A temporary 2% eye-depth capture was higher and was not kept. Pass parameters use `SSRParameters` (`3f5a883`) and camera matrices read `ViewUniform` (`0eb4f36`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| SSGI | Yes | 2026-10-29 Metal: numeric 17 in `65d90c15-33f9-4464-aa4c-7009d1342b48`. The trace uses the same depth step as SSR. Two temporal-then-world-space stages, 6 m then 3 m. | 2026-10-29 Scene view, same camera: hit rate floor/vault/wall 0.078/0.065/0.058 to 0.119/0.151/0.114. Mean trace alpha 0.063/0.032/0.032 to 0.100/0.101/0.074. Play frame TODO(UNVERIFIED). | GPU time TODO(UNVERIFIED). Other APIs TODO(UNVERIFIED) | macOS Metal | `F(viewZ)`, `F(variance)` and `F(AO)` are monotone stand-ins. The Metro slide does not give their bodies. Intensity Scale is 0 to 10. There is no Volume thickness. Refactor final gate 2026-11-15: with `SSGI_AMBIENT_OCCLUSION` gating, AO off raises the frame mean 21.21 to 22.99 against 21.17 to 22.95 on `1a357c0`. TODO(UNVERIFIED): pass parameters moved into a GPU-RECORD constant buffer (`3f5a883`) and camera matrices into `ViewUniform` (`0eb4f36`) after the result above. |
| Burley SSS | Yes | No | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play 1445×813 with an in-memory `DiffusionProfile` and lit subsurface sphere; toggling `SubsurfaceScattering.enable` produces/drops `SubsurfaceScattering` and scatters across the subsurface terminator (`mean abs diff` 0.417, 0.86% pixels > 8, 5.17% > 2). | Open | macOS Metal | No dedicated frame setting (`Volume` + `DiffusionProfile` gated). Pass parameters use `SubsurfaceParameters` (`3f5a883`) and camera matrices read `ViewUniform` (`0eb4f36`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Volumetric fog / cloud | Yes | Partial | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play 1445×813 toggling `VolumetricFog.enable` adds/removes sunlit courtyard fog (`VolumetricFog` produced vs not, mean 50.81 vs 21.93, `mean abs diff` 31.820, 95.56% pixels > 8); at camera `y = 6 m` looking through the courtyard roof, toggling `VolumetricCloud.enable` adds/removes the cloud layer (`VolumetricCloud` produced vs not, mean 169.53 vs 45.53, `mean abs diff` 124.094, 57.27% pixels > 8). | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: fog workload 3×300 composite Editor frames; cloud Open | macOS Metal | Pass parameters use `VolumetricFogParameters` / `VolumetricCloudParameters` (`3f5a883`) and camera matrices read `ViewUniform` (`0eb4f36`, verified on macOS Metal above). Cookie atlas compiles under `INFINITY_LIGHT_COOKIES`. D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Translucent T0 / T1 / T2 | Yes | Partial | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: bounded fixture regression and ground control | Open | macOS Metal | Prior band disposition accepted; inherited S15 consumers retained. No unrelated fog-node implementation claim. |
| Baked lighting: lightmap, Shadowmask / Distance Shadowmask, probes, LPPV, APV, terrain, baking sky (0.5.0) | Yes | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (CPU contracts and actual GPU readbacks) | 2026-10-22 Unity 6000.6.0f1 / macOS Metal (bounded task fixtures) | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: baked-environment/probe composite only; APV/lightmap timing Open | macOS Metal | Real bounded S12 lightmap/probe/APV/sky, Terrain and shadowmask evidence retained. Closure adds a durable native BakedLeft cubemap plus realtime-probe composite timing; it does not replace the invalid S12 GPU timing or measure APV/lightmap/baking cost. Meta holes, Player and other APIs remain unverified. |

## Temporal, post and output

| Feature | Recorded | EditMode | Image | Perf | Platforms | Notes |
|---|---|---|---|---|---|---|
| Fused TAA and display sharpening | Yes | Yes; see 2026-10-24 capacity/TAA receipt | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play 1445×813 (`enableSuperResolution` off → `ETemporalResolve.TAA`): frame 1 (`lockHistoryReset`) vs frame 30 confirms temporal accumulation (`mean abs diff` 2.821, 14.44% pixels > 8, 36.19% > 2); frame 30 matches `1a357c0` (`mean abs diff` 0.205, 0.54% pixels > 8 across jitter phase; fixed settled frame `mean abs diff` 0.025, 0.002% > 8). | No isolated GPU time | macOS Metal | Single tiled dispatch; unsharpened pending history, sharpened display only. Pass parameters use `TemporalAntiAliasingParameters` (`3f5a883`) and camera matrices and TAA jitter read `ViewUniform` (`0eb4f36`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Fixed-ratio super resolution | Game cameras | Partial | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: toggling `InfinityRenderPipelineAsset.enableSuperResolution` in memory (`m_RenderScale` 0.5, no asset save) switches Scene_Spazon Game camera to `ETemporalResolve.SuperResolution` (`internalSize` 723×407, `displaySize` 1445×813, `SuperResolution` produced) and returns to `ETemporalResolve.TAA` (`1445×813`, `TAA` produced) when disabled; half-resolution edges differ from full-resolution TAA (`mean abs diff` 1.886, 7.00% pixels > 8, 34.09% > 2) and match `1a357c0`'s SuperResolution (`mean abs diff` 0.097, 0.00% pixels > 8). | Open | macOS Metal | Pass parameters use `SuperResolutionParameters` (`3f5a883`) and camera matrices read `ViewUniform` (`0eb4f36`, verified on macOS Metal above). D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Exposure, Bloom, CombineLUT (ACES), Vignette, Film Grain | Yes | Yes | 2026-11-17 / Unity 6000.6.0f1 / macOS Metal: Scene_Spazon Play 1445×813 three isolated toggles — `Exposure` (`Auto` +1.2 EV vs off: mean 154.10 vs 21.22, `mean abs diff` 132.879, 99.98% pixels > 8), `Bloom.intensity` (1.8 vs 0: mean 73.54 vs 21.20, `mean abs diff` 52.347, 100.00% > 8), and `CombineLUT` (`ColorGrading` + `FilmTonemap` ACES vs neutral/None: mean 11.56 vs 25.43, `mean abs diff` 14.794, 72.04% > 8). | Open | macOS Metal | `ExposureParameters`, `BloomParameters` and `CombineLutParameterDescriptor` (`8cdf1b5`, `3f5a883`) verified on macOS Metal above. Vignette and Film Grain were not isolated in this pass (TODO(UNVERIFIED)); D3D12 and Vulkan TODO(UNVERIFIED) (unavailable on this macOS machine). |
| Present (transfer encoding) | Yes | 2026-10-25: Output cases plus native-linear primaries/paper-white GPU test | SDR and actual internal-XDR linear Rec.2020 HDR, native RGBA16Float attachment, UI/exposure and mode restoration | Open | macOS Metal Editor | HDR available/active/format/gamut validated; no absolute photometry, other HDR swapchains or Player acceptance. |
| SceneView sizing / UI overlay gate | Yes | Yes | Accepted 2026-09-13 | — | macOS Metal | |
| DebugView | Yes | Partial | Open | — | macOS Metal | Refactor final gate 2026-11-15: the table-driven pass draws AO, SSGI, IndirectDiffuse, CascadeShadowAtlas, LightComplexity and ReflectionProbeAtlas as on `1a357c0` (at most 1.0% of pixels differ by more than 8). |
| Rendering Debugger panels, debug views 19–29, Scene view Draw Modes, pass timings (0.5.0) | Yes | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: catalog and mode contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: 20 bounded native debug/Draw Mode views | — | macOS Metal | Current viewport and camera identity/restoration verified. Timings UI visibility does not provide valid GPU timing. See S15 report. Refactor final gate 2026-11-15: in the Editor SSRRayHit, TAAConfidence and AO draw and the Mesh and Lighting panels report counts; the release player's Runtime DLL contains none of the Rendering Debugger, Debug View or capture type names. |
| Render Graph Viewer (0.5.0) | While open | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: snapshot contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: camera/Scene switching and SSR off/on | — | macOS Metal | Imported-resource markers and CPU closed/open controls verified; no GPU speedup inferred. See S15 report. |
| Material conversion (Built-in / URP / HDRP → Infinity) and create menus (0.5.0) | Editor | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: three conversion contracts | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: three native conversions and 16 creation operations | — | macOS Metal | Bounded source cases, canonical map/color/route/pass state and Undo/Redo verified. Not all 17 registered providers were exercised. Masks, occlusion, detail, height and emission maps are dropped. See S15 report. |
| Image regression over the validation fixtures (0.5.0) | Normal Play frames | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: comparison16, request1, save ownership6; actual recovery controls | 2026-10-23 / Unity 6000.6.0f1 / macOS Metal: eight fixed-input cases compare Pass on main and repaired S16 | — | macOS Metal | Same accepted baselines reused; real light-change Fail/restored Pass, missing/corrupt baseline, state/metadata write failures, pending cancellation, foreign capture and dirty recovery verified. Known sky/dark defects remain Open in their feature rows; consistency is not complete image correctness. See [S16 report](../../Record/Evidence/Verification-0.5-2026-10-19/S16-report.md). TODO(UNVERIFIED): capture shaders moved into the `InfinityRenderPipelineCaptureShaders` Global Settings container and exist only in Editor and Development builds (`ef632e0`); the wizard Fix adds the container to existing assets (`d29fe57`). |

## Out of scope

Hardware ray tracing outside UnifiedRayTracing, depth of field, motion blur, other new post effects, XR, MSAA,
dynamic resolution and super-resolution upscalers beyond the fixed-ratio path.

S17 directory-layout verification (2026-10-23, Unity 6000.6.0f1 / macOS Metal): all 57 moved script/meta GUID bindings and 15 default Volume components resolve. Eight fixtures have no Missing Script and retain their expected Profile components. Repaired historical and final main candidates execute 1,826 EditMode cases (1,773 Passed, 0 Failed, 53 existing Skipped). Callbacks are complete after two reload/resume boundaries; native framework XML omits 33 actual checkpointed skips. The same 16 S16 baseline files remain byte-identical and all eight normal-frame comparisons pass. Two early fixture creators now persist their authored components; only two task-owned profiles were explicitly repaired. These checks do not close the image or performance Open cells above. See the S17 report for identities and evidence.

Final marker retirement (2026-10-23 / Unity 6000.6.0f1 / macOS Metal): the remaining 45 categorized contracts were actually Passed in the final full/category runs. Only their 29 Category attribute lines in nine files were removed. A subsequent cold compile and nine-class recheck pass 64/64 with all 45 exact identities preserved. No test body, assertion or parameter was removed, and image/performance boundaries are unchanged.

## Approved closure acceptance — 2026-10-23

Original InfinityExample, Unity 6000.6.0f1 / CoreRP 17.6 / Shader Graph 17.6 / Apple M3 Max / macOS Metal.
Current Infinity-only EditMode: 625 discovered, started and finished; 625 Passed, zero Failed/Skipped, framework and
callbacks complete. S1 combines its traceable historical related contracts/behavior with this result under the
explicitly approved revised scope. The historical repaired whole-Example run is waived, not represented as executed.
The 53 historical external SDK skips retain their actual reasons outside Infinity acceptance.

Eight normal Game regressions pass against the original 16 byte-identical S16 baseline files. No baseline or tolerance
was changed. Three independent cold Editors pass fresh-RP first Game and +120 accepted-submission stable frames;
necessary six-face Reflection preproduction is explicit. Historical purple/black causes remain unknown, currently not
reproduced. This does not assert every optional image/platform path is verified.

Seven fixed 1280×720 High workloads have valid unique positive CPU/GPU timestamps, 120 accepted warmup frames and
three groups of 300 samples per version. Timing is whole Editor with a fixed camera RenderTexture, without final
backbuffer Present/uGUI. Decal replacement restores missing draws, so its work differs. Same-code observation-state
controls explain the initially repeatable probe timing offset without establishing an OS cause or strict performance
equivalence. No stable regression attributable to the patch was found. See the
[closure report](../../Record/Evidence/Verification-0.5-2026-10-19/Closure-20261023/closure-report.md)
for bounded results, original failures, source identities and retained limitations. D3D12/Vulkan, Player, HDR and
unrelated Open cells are unchanged. Ready recommendation applies to the approved closure scope.

## Execution naming convergence — 2026-10-24

Baseline main 2e05bc2. Pass recording and GPU markers now distinguish Raster,
Compute, Copy and Upload. Ten custom LightModes and matching shader names gain
Raster; Unity ShadowCaster/Meta/SRPDefaultUnlit stay unchanged. RTAO marker comes
from the owned URT backend. Shader Graph generation, material routing, mesh pass
lookup, stripping and Example TerrainDependencyControl consumers were updated.
No old-name aliases, asset-load migration, branch, Player build or package build.

Unity 6000.6.0f1, macOS / Metal / Apple M3 Max. Final source recompile completed
with 0 errors / 0 warnings. StaticCheck: 0 errors / 0 warnings; offline tests 11/11.

| Final relevant test group | Passed | Run identity |
| --- | ---: | --- |
| MaterialRouteTests | 18 | a77ca655-be84-4d6f-91a4-e9dc51a73820 |
| MeshPassShaderUtilityTests | 3 | ba55a070-d0ae-4f01-ab96-942d2aad5e71 |
| ShaderStrippingTests | 16 | aae0202a-aa60-46d8-80ff-e5bcc825ad02 |
| ColorOutputGpuTests | 13 | c777177e-9e92-4bed-9cb4-d238cb218d3f |
| ShadowDepthCopyGpuTests | 1 | 88f36863-59e7-4c5a-97a3-abd8ba61dd09 |
| SceneViewOutputTests | 10 | 994203d5-3c15-4adb-b39b-c19736a21fec |
| PipelineSeamTests | 2 | d393b84b-67a2-4c42-b642-daa2738bbc9d |

All 63 selected tests passed with complete framework and callback accounting.
The final MeshPassShaderUtility rerun covers the additional RasterDefaultFullScreen
shader name. Actual generated/imported Lit/Unlit/Decal graphs had 11/1/1 passes,
no shader errors and successful RasterGBufferPass/RasterTranslucentT2Pass/
RasterDBufferPass lookup. Temporary graph assets and asmref verification bridge
were removed and the final source recompiled.

Scene_Spazon normal Game capture: five successful submissions, one DisplayColor
and Depth frame, zero pending readbacks. The inspected image contains the expected
atrium geometry; finite colors. This is a rendering smoke check, not a matched
before/after image regression. Frame Debugger exported 132 actual events, including
RasterDepthPass, RasterGBufferPass, RasterMotionPass, RasterForwardPass and
RasterPresent draws. Native ShadowCaster remains in the shadow route.

Transient Play-only RTAO Volume and RP clone: actual Compute backend, no blocking
reason, 70 acceleration instances; captured Occlusion finite with range [0,1].
Compiled graph records ComputeRayTracedOcclusion as RayTracing, as intended.
One additional scaled frame records RasterResolveDisplayDepth, RasterUIOverlay
and RasterPresent. This proves Overlay recording, not a UI pixel regression.
Cached-shadow copy passed its existing GPU test; a scene-driven cache save/load
cycle was not captured. Hardware RT and non-Metal graphics APIs remain
TODO(UNVERIFIED). Player RTAO shader loading is an existing separate gap.

Original failures remain distinct: initial URT test assembly reference omission
was corrected; three stripping fixture filename failures were fixed by moving
shader/meta together with GUIDs intact, then 16/16 passed. An initial test run
interrupted by script reload was cancelled and excluded. One cold Editor launch
exited before CLI connection; retry succeeded, cause unestablished. An ordinary
sandbox offline-test attempt lacked intermediate write access; the permitted
retry passed 11/11. No Library/DataStore changes were made. Auto-review rejected
a persistent preview image; that extra file was removed, and image inspection
used only task-owned intermediate output.

Cleanup: own Editor 53804 closed gracefully; Play/Frame Debugger off, original RP binding restored, original scene clean. Task intermediate root and task log absent; final deletion reclaimed at least 67826460 payload bytes / 123207680 allocated bytes (earlier partial cleanup not counted). AppleDouble sidecar disappearance required FileNotFound-only cleanup tolerance. No persistent preview or validation bridge remains.


## 2026-10-24 output and transparent attachment convergence

Implemented on `main` at baseline `2e05bc2`, preserving the preceding uncommitted
execution/tag migration. No branch, commit, push or Player build. Unity 6000.6.0f1,
macOS Editor / Metal / Apple M3 Max. This receipt supersedes the earlier output
and reactive-mask verification scope; earlier dated receipts remain historical.

Changed: raster final combination shares grading/exposure/Bloom/vignette/grain
HLSL with intermediate output. Direct output removes the post-combine UAV texture
and the extra final sample/draw. Hardware-sRGB SDR UI draws on the final target
with an explicit camera viewport. Linear consumers retain one intermediate and
an output-only encoding draw. Post bypass aliases read-only input; an overlay
writer first obtains an independent raster copy. Editor depth upsampling is
consumer-driven. Cached-shadow save/restore uses RasterCopyShadowDepthTiles.
ReactiveMask is TAA-only: first transparent color attachment Clear(0), subsequent
stages Load; no clear dispatch, and SR/non-TAA variants omit that MRT.
Post/fog kernels resolve cached names rather than depending on kernel order.
The root task ledger was retired to the single historical
[0.5.0 plan](../../Record/History/PLAN-0.5.0-completed.md); active tasks live in conversation.

StaticCheck: 0 errors / 0 warnings; offline negative controls 11/11;
`git diff --check` clean. Final source compilation completed without errors.
124 selected tests passed with complete framework/callback accounting:

| Group | Passed | Run identity |
| --- | ---: | --- |
| ColorOutputGpuTests | 13 | 648a7149-694e-47cc-a1a8-550b640f486b |
| OutputConvergenceTests | 17 | 157f70db-61ec-424c-8f61-e3cc74ea3a30 |
| FogTranslucentFeatureTests | 4 | 83cb08bf-1acf-432f-a598-06ed0fe3ac9c |
| TemporalResolveGpuTests | 10 | e48276ed-5c18-4a7c-9bb4-1a2970fb0bbe |
| SceneViewOutputTests | 10 | 2848b6af-a60d-413d-b277-5486d2edddbe |
| MaterialRouteTests | 18 | 09c8475d-8c35-4270-acdd-77d4b8b69045 |
| ShaderStrippingTests | 16 | 13ee89ba-0e2b-497a-98bc-767962ab148f |
| ShadowDepthCopyGpuTests | 1 | f886622e-97f7-422e-bb57-890b5322e70d |
| LocalShadowCacheTests | 19 | dd9272b0-156e-4330-924f-16c5944e39f1 |
| ExposureOutputTests | 16 | f4877225-24f7-42fc-8126-c0bb80560cd0 |

GPU output tests compare direct composition against RGBA16F intermediate plus
encoding for LinearTarget, SDR shader-sRGB, PQ, HLG and scRGB, using a nonuniform
source and active grading/exposure/vignette/grain. Hardware-sRGB UI blending is
compared with linear composition plus encoding. The partial-viewport test checks
12 changed pixels and 36 preserved destination pixels. Existing transfer tests
cover finite outputs and coverage; existing depth-tile GPU tests compare every
pixel, including neighboring tiles and unequal atlas sizes.
Actual generated/imported Lit, Unlit and Decal Shader Graphs found their new
RasterGBufferPass / RasterTranslucentT2Pass / RasterDBufferPass and warmed shader
variants, including reactive output enabled/disabled. This round does not claim
an exhaustive visual acceptance of every graph configuration.

Normal-frame evidence:

- Scene_Spazon / MainCamera, 1666x1756: baseline and final normal captures each
  completed 33 accepted frames (32 warmup + one readback), zero outstanding
  resources and no non-finite DisplayColor values. The inspected candidate image
  contains the expected atrium geometry. Scene animation phases differ across
  Play sessions, so the whole-image difference is not a matched regression gate.
- Actual SDR UI panel drew on direct output. Direct compiled graph has one
  RasterFinalOutput and RasterUIOverlay. An SR direct resource snapshot contains
  imported DisplayBackbuffer but no PostProcessTexture or ReactiveMask texture.
- Empty transparency produced an all-zero ReactiveMask. T1-only produced 0/128;
  T2-only produced 0/191 without T1 residue. T0/T1/T2 together produced
  0/64/128/191 with counts 220794/3331/2896/3379. Motion metadata remained finite.
  SR at 320x180 internal / 640x360 display recorded neither reactive allocation
  nor depth upsampling while overlays were off.
- The same scaled camera with Gizmos on recorded RasterPostCombine,
  RasterUpscaleDepthForEditorOverlays and RasterGizmos; its displayed scene and
  gizmo wires were inspected. Partial camera viewport preserved prior output
  outside the camera region. Equal-size depth aliases the existing attachment.
- Actual post-disabled SceneView captures completed with zero outstanding
  resources: read-only PostProcess capture has no RasterOutputCopy; enabling
  Gizmos adds that copy before the writer. Actual Albedo DebugView snapshot
  records RasterPostCombine -> ComputeDebugView -> RasterFinalOutput.
- Two OnDemand spot tiles: first frame rendered 2 tiles, cache-hit frames rendered
  0; an explicit request for one light rendered exactly 1, subsequent frames 0.
  Compiled resources record RasterCopyShadowDepthTiles. Exact neighboring depth
  preservation is independently covered by the GPU tile test.

Performance: baseline/direct/UI runs each used 32 warmup + 120 recorded frames at
1666x1756. GPU nanoseconds and GPU sample blocks were zero in all runs. CPU/Editor
samples are not GPU measurements and no millisecond speedup is claimed. The
verified direct-path structural saving is one RGBA16F full-size intermediate,
one separate final sample/draw and one post-combine dispatch; TAA also removes
one standalone reactive clear dispatch. At this resolution the removed color
allocation is 23,403,968 bytes and the nominal store+load traffic is 46,807,936
bytes/frame, before compression, tiling and cache effects. UI samples have no
matched pre-change UI baseline, so they establish execution, not a timing delta.

Tool failures are separate from rendering results: initial compile transition
errors were repaired and superseded by final successful compilation/captures.
The first Shader Graph synchronous CLI call exceeded its 5-second wait; a
compiled asynchronous bridge completed all three imports and warmups. A hidden
SceneView capture changed dimensions and failed; a bounded owned SceneView retry
completed both post-disabled paths. Frame Debugger full export failed first
on changing event count and again on unavailable event-0 payload timeout;
partial export is not accepted as a complete frame tree. Execution names and
resource ownership are instead verified by live compiled RenderGraph snapshots.
The fresh final-render log increment (starting at byte 1061611) contains no new
render exceptions, shader errors, invalid kernels or assertions.

Platform boundary: Metal Editor runtime and controlled RenderTextures verified.
D3D12, Vulkan, actual HDR display presentation and hardware DispatchRays remain
TODO(UNVERIFIED). Existing Player RTAO shader loading is an independent known gap;
no Player result is inferred from Editor or URT. These changes do not change
ray-tracing scheduling or atlas layout.

Cleanup: owned Editor PID 62747 closed gracefully. Play, pause and Frame Debugger
were off; original RP binding and clean Scene_Spazon restored, SceneView effects
on / Gizmos off. Temporary graphs, verification bridge and transient Play objects
are absent. All captures/tests/readbacks drained before shutdown. Exact task root
`intermediate/output-convergence/` deleted and absence verified, reclaiming
237,504,085 payload bytes / 312,999,936 allocated bytes. No persistent capture
archive or preview was created. Existing project/user changes were preserved.


## RT capacity, fused TAA and MeshDraw graph convergence — 2026-10-24

Continues main `2e05bc2` and the existing uncommitted naming/output/transparent
attachment changes. No branch, commit, push, serialized user setting, dynamic
resolution controller or Player build. Main Agent owned design, implementation,
review and Unity operations; Luna was used only for bounded source searches.
Conversation Todo tracked baseline, capacity/views/retention, TAA, MeshDraw,
documents and final validation/cleanup; no active PLAN.md was created.

Changed: explicit Internal/Display sizing, per-camera 64-texel capacity buckets,
12.5% growth, three-accepted-request peaks and 30-request/75%-area shrink;
field-equal physical compatibility including MSAA; one shared transient pool
with 400/64 MiB texture/buffer retention watermarks and fence-delayed eviction.
Texture binding metadata and owned screen-sampling helpers preserve logical
coordinates and clamp each mip's valid rectangle. Material sampling stays native.
History reads keep their accepted texture and previous view until a pending
writer commits; failed resized writers cannot replace accepted history. A change
only to physical capacity does not invalidate otherwise valid history.
TAA solves an 8x8 tile plus halo, half-quantizes shared RGB, and writes unsharpened
history/depth and sharpened display in one dispatch. The runtime Sharpen kernel
and two TAA history-copy passes are removed. MeshDraw compaction/upload are
explicit graph producers; disjoint batch ranges prevent later preparation from
overwriting earlier output. The unused CPU immediate-submit entry was removed;
callers submit through the mesh graph extension's registry. Neutral functional scopes and MeshDraw markers
leave Unity's own batching labels intact.

Unity 6000.6.0f1 / OSXEditor / Metal / Apple M3 Max. Final script recompile:
0 errors / 0 warnings. StaticCheck: 0 errors / 0 warnings. Offline negative and
registry tests: 12/12 passed. Final accepted relevant EditMode/GPU cases:
237 passed, 0 failed, 0 skipped (deduplicated groups below; this is not a full-suite run).

| Group | Passed | Run identity |
|---|---:|---|
| MeshSceneTests | 32 | `8a1026a9-96c7-4825-b35c-2a96bdcf33a9` |
| CompactContractTests | 3 | `e07ee794-8b11-4b97-a244-6cc53ac4e96c` |
| PipelineSeamTests | 2 | `088d9efb-4a73-4da5-bf1c-638da694d5bf` |
| TemporalResolveGpuTests | 14 | `aa6bb33e-ea6a-4569-8403-f2ba4e8f59cd` |
| OutputConvergenceTests | 17 | `d8458a7e-73f1-4e2b-a869-f81b9661d7a2` |
| ColorOutputGpuTests | 13 | `f8fd00fe-4da1-490a-98a6-079a6a7958bf` |
| MaterialRouteTests | 18 | `3cefacb8-3c17-408e-88c8-206ca244ccc6` |
| ShaderStrippingTests | 16 | `d4d8c461-f2fb-415e-af54-f4346b98c8ef` |
| ShadowDepthCopyGpuTests | 1 | `39d7ae64-7299-46e0-865b-c984d507f086` |
| TemporalAntiAliasingTests | 4 | `e0e028d9-9b5f-4a87-8c00-97aa399c1fd3` |
| GBufferContractTests | 11 | `4f5b5679-9df9-4c6e-ac68-9b12c40942ea` |
| VisualContractGpuTests | 12 | `a487e29c-11dc-4fea-a64f-9514dcd0fcb9` |
| ScreenSpaceModeTests | 5 | `7e1fbe5b-92c3-4795-9baf-fa6d6b1a9298` |
| ScreenSpaceDenoiseTests | 6 | `6f19b6a5-a947-44a3-9b0d-eefe62b31d75` |
| StochasticSSGINumericTests | 4 | `4a34a4b8-ae83-4ffe-b81e-efc5b542f035` |
| SSGITemporalResponseGpuTests | 20 | `e89a80b8-4922-4f4c-a86e-41fe984bbb01` |
| StochasticSSRNumericTests | 5 | `bcc6eb4c-a5ae-4ca6-bc40-3ed3f9781dd5` |
| MeshDrawBatchGpuTests | 1 | `d1353b19-02d3-405c-9bfc-34df7d3fff90` |
| TextureCapacityTests | 8 | `f9c54b52-e915-429b-a531-7c3b558f1a63` |
| HistoryCacheCommitTests | 6 | `67cd9328-f6dd-4e52-8c3c-c3e51aea88ca` |
| RenderGraphFailureTests | 6 | `15db7149-9a6f-4da7-ad18-b4db57b10d7c` |
| SceneViewOutputTests | 10 | `d7e4e264-0d07-4592-b589-395d62da3b71` |
| MeshDrawListLifetimeTests | 10 | `666f7b5c-71df-4d50-9f4b-18b0c016a9ab` |
| RenderCaptureTests | 13 | `24be5558-3beb-42f6-9c07-16e073ad2367` |

Normal-frame and resource evidence:

- Scene_Spazon, frozen animation/time, fixed MainCamera. Normal 640x360 capture
  completed after 64 successful warmup submissions. Display, accumulation,
  previous color, current/previous depth, ReactiveMask and confidence were finite;
  all readbacks reached terminal retirement. History sample age reached 32.
- Final 115-frame resize sequence covered bucket-local oscillation, 1280x720,
  stable 320x180, portrait 360x640, and return oscillation. Bucket-local frames
  1–19 kept texture allocation count 78; the final 48 frames kept 173.
  Capacity shrank from 1280x768 to 320x192 after the history/hysteresis window.
  Portrait reset to 384x640. Idle eviction count reached 23; pending retirement
  drained without a CPU wait. Counters estimate pooled texture storage only,
  excluding histories, driver overhead and unrelated GPU allocations. They are
  not process VRAM measurements or a hard working-set limit.
- A zero-warmup capture started in the same main-thread invocation as changing
  640x360 to 961x541. Current accumulation/depth/display were 961x541 while the
  captured previous color/depth remained 640x360. Reset was true, confidence age
  was 1, and all buffers were finite. This proves distinct current/previous views
  on an actual resize frame, beyond the rollback unit tests.
- Odd 641x361 output with offset viewport and transient SDR overlay UI completed
  finite color/TAA/reactive captures. SR ran 320x180 Internal to 640x360 Display.
  Temporary RGBA16F camera RenderTexture plus Albedo DebugView produced finite
  output and depth. These controlled tests do not accept a real HDR display.
- Two isolated Preview renders requested 193x129 and 241x173 GUI points; Retina
  targets were 386x258 and 482x346. GPU readbacks were finite. Preview cleanup
  left only Main and the owned SceneView state; Main retained its own capacity.
  Owned SceneView, aligned to the fixed camera, visibly rendered geometry,
  generated graph materials and Gizmos; normal color/depth capture was finite.
  An initial misaligned empty SceneView was excluded from image acceptance.
- Lit, Unlit and Decal graphs were generated and synchronously imported with
  RasterGBufferPass / RasterTranslucentT2Pass / RasterDBufferPass found and no
  shader errors. Actual transient scene draws populated GBuffer/DBuffer and the
  transparent ReactiveMask reached 1. Transparent reactive-on/off variants were
  warmed where declared. Temporary assets and bridge were then removed.
- Live RenderGraph Viewer showed ComputeMeshDrawCompact as a Graphics-queue
  Compute node reading visibility/candidates/metadata and writing indices/counts/
  indirect args before RasterDepth. CPU/no-consumer/shared-list cases are covered
  by compiled graph tests; the GPU two-batch test verifies disjoint args/indices.
  Actual Game snapshot has one ComputeAntiAliasing and no temporal Sharpen or
  TAA color/depth CopyHistory nodes. The two-stage numerical oracle is test-only.
- Poisoned padding/odd mips and point/bilinear/trilinear numeric tests passed.
  Filter coordinate precision is bounded at 1/512; fractional mip selection uses
  an independent logical filter oracle because native Metal LOD quantization
  need not be bit-identical. Fused temporal comparisons allow half-storage ULPs.

Performance: baseline, candidate and UI candidate each used 32 warmup plus 120
samples at 640x360. GPU nanoseconds and GPU block counts were zero throughout;
no millisecond GPU speedup is claimed and CPU samples are not substituted.
Verified changes remove one sharpen dispatch and two TAA history transfers, and
stabilize pooled allocation during bucket-local resize. Explicit MeshDraw nodes
make existing work visible; their extra graph nodes are not extra compaction.
Halo solving adds arithmetic, so its net GPU benefit remains unmeasured.

Superseded failures are distinct from acceptance: the first async-queue fence
could not be queried on this Metal device and was replaced by CPU-synchronization
fences; initial mip/native-reference assumptions were corrected with an independent
filter oracle; direct-output unit fixtures needed explicit exact-view metadata;
initial temporary Shader Graph keyword and CLI helper calls were corrected.
The last history audit repaired resize read replacement and added tests plus a
fresh real resize capture. Final rendering log from byte 14806752 has zero new render
exceptions, shader errors, invalid kernels or assertions before restoration.

Platform boundaries: Metal Editor and controlled RTs verified. D3D12, Vulkan,
actual HDR display and hardware DispatchRays remain TODO(UNVERIFIED). The pipeline
requires graphics fences at construction, so retention eviction runs on every
supported backend.
Existing independent Player RTAO shader loading remains out of scope. The current
normal-frame fixture has no new claim of exhaustive optional-feature, multi-camera
compositing or all-platform image equivalence.


Cleanup completed: owned Editor PID 78645 exited gracefully. Original clean
Scene_Spazon, source RP binding and 1666x1756 camera viewport were restored;
Play, pause and Frame Debugger were off, and the temporary camera target was
null. All capture requests were Completed with zero outstanding readbacks;
no tests or sampling callbacks remained active. The task-owned graphs, bridge,
SceneView, transient objects and Python cache are absent. Exact task directory
`intermediate/rt-taa-mesh/` and its sibling OS sidecar were deleted and absence
verified: 140,735,307 payload bytes / 338,165,760 allocated bytes reclaimed.
Only this receipt and source/tests/design documentation remain. Earlier user
changes are preserved; HEAD remains main 2e05bc2.

Shutdown tooling note: after Mono/UDS cleanup the Unity Pipeline HTTP server
logged one ObjectDisposedException while replying to its own quit request.
The CLI independently confirmed graceful process exit. This happened during
server disposal after rendering stopped; it is not a render/capture failure.

### Mesh profiling domains and hierarchy — 2026-10-24

Note: these results predate `54062f7`, which replaced the per-view mesh names below with `MeshCullingContext` and one producer per draw list.

Baseline: clean `main / 590f470`. This change only adjusts Mesh profiler names,
their internal view context and consumers. Shader contracts, algorithms, graph
dependencies, queues and resource lifetimes are unchanged. No branch, commit,
push, Player build or system shutdown was performed.

- `MeshDrawProfiling` owns seven cached Compute/Upload samplers and four Draw
  names. `EMeshViewKind` selects RenderLoop versus Shadows; Timings uses the same
  sampler instances. RendererList submits directly without an extra wrapper.
- Static gate: 0 errors / 0 warnings; all 12 static-check regression/negative
  tests passed. Unity 6000.6.0f1 script compilation: 0 errors / 0 warnings.
- EditMode: 23 passed, 0 failed, 0 skipped. MeshDrawProfilingTests 7
  (`389f1380-9b61-45ff-b21b-1bda437c8d52`), MeshDrawListLifetimeTests 11
  (`70cd9bba-ef20-4720-8793-408939e1ca64`), CompactContractTests 3
  (`4962cc6a-7f3f-4aa9-b325-6de02b90dd74`), PipelineSeamTests 2
  (`10f3f408-f7f4-4b60-8c3c-152782c8c284`). Coverage includes Auto, CPU/GPU,
  shared/culled DrawLists, view-domain names, cached identity and submit seams.
- Original Example was occupied by another session. All CLI operations targeted
  a task-owned minimal host under `intermediate/mesh-marker/run01/host`, PID
  13241 / port 7801, using the working package and a disposable MarkerScene.
  Native and Mesh cubes, ground, directional cascades and a local spot shadow
  provided real geometry. No original scene, camera or Editor was operated.
- Metal / Apple M3 Max: Auto, CPU Direct and GPU Indirect each completed one
  640x360 normal color/depth capture after eight warmup frames. All six readbacks
  were finite, with zero outstanding requests. A normal-frame color preview
  visibly showed both colored cubes, ground and shadows. CPU compiled graph had
  zero Mesh culling/compaction nodes; GPU/Auto kept preparation before Raster.
- GPU Frame Debugger export completed: 62/62 event payloads ready. Depth,
  GBuffer and ObjectMotion contain RenderLoop.DrawMeshIndirect beside the native
  RenderLoop.DrawSRPBatcher. Four CascadeSlice regions and LocalShadowSlice0
  contain Shadows.DrawMeshIndirect beside Shadows.DrawSRPBatcher. Each owned
  indirect event reports one draw call; Unity reports zero vertex/instance
  counts for these indirect events, so those counters are not an instance proof.
  ClearCounts / CompactIndices / BuildIndirectArgs remain under the view-domain
  compaction node, outside Raster. No RendererList wrapper remains.
- CPU Frame Debugger topology: 32 events with the corresponding Direct names,
  native siblings and preserved shadow regions. The first Mesh depth event had
  a ready payload (24 vertices, one instance, one draw). Full payload export
  timed out twice on native SRPBatcher event 1; later payloads remain unverified,
  rather than being accepted from their placeholder zero counters. Normal CPU
  rendering and graph tests passed independently. Export restoration reported
  no cleanup error; no further retries or exporter changes were made.

The controlled host used Gamma color space; this is a marker/hierarchy smoke
test, not acceptance of the original Scene_Spazon image or Linear/HDR output.
D3D12 and Vulkan remain TODO(UNVERIFIED). No GPU performance benefit is claimed.
Cold shader import emitted existing HLSL uninitialized-value warnings, with no
shader errors. The marked rendering log had no invalid kernels or assertions;
its two exceptions were the explicitly reported CPU debugger payload timeouts.
Initial host/test setup errors were corrected before the accepted runs above;
the static negative tests were rerun with task-directory write permission.

Cleanup: backend override cleared, time scale restored, Play and pause off;
capture Completed with zero outstanding readbacks and debugger export inactive.
Owned Editor PID 13241 exited gracefully. The exact `intermediate/mesh-marker/`
directory and sibling OS sidecar were deleted and absence verified, reclaiming
320,207,846 payload bytes / 1,275,068,416 allocated bytes (including the disposable
host Library). Original user scenes/settings were never modified. Unity emitted
thread-finalization / readonly-database shutdown messages after stopping; CLI
independently confirmed process exit. Only source, tests and this receipt remain.

### Mesh preparation sampler unification — 2026-10-24 follow-up

Note: these results predate `54062f7`, which replaced the per-view mesh names below with `MeshCullingContext` and one producer per draw list.

This follow-up supersedes the seven preparation samplers in the preceding receipt.
There are exactly three cached preparation samplers: `ComputeMeshCulling`,
`ComputeMeshCompaction` and `UploadMeshData`. Main/cascade/local views share
sampler identity while retaining separate graph nodes and parent scopes. Only
Draw names retain RenderLoop / Shadows domains. Obsolete view parameters and
domain-specific preparation samplers are removed; current rules and consumers
are updated. Existing earlier working-tree changes are preserved.

- Static gate: 0 errors / 0 warnings; 12 static regression/negative tests passed.
  Unity 6000.6.0f1 recompile reported up-to-date, 0 errors / 0 warnings.
- EditMode: 23 passed, 0 failed, 0 skipped. Profiling 7
  (`8742c443-aef1-404a-af82-e0efa9d6f234`), lifecycle 11
  (`7f1d4734-e4b5-4b95-a772-e8a3c70c3ecb`), compact 3
  (`6213a1a7-b5c9-4467-b6f2-a2355f7bb684`), seam 2
  (`66c5717a-42dd-4fdb-99cc-b56e5339e7d1`). The main/cascade/local culling test
  verifies three distinct nodes with the same name; shared/dead DrawList and
  CPU/GPU/Auto preparation tests remain passing.
- Metal / M3 Max: one normal GPU Indirect Scene_Spazon MainCamera frame at
  640x360 after eight warmup frames completed, with finite color/depth readbacks
  and zero outstanding requests. Main Agent inspected its normal color preview.
  The actual compiled graph contains five ComputeMeshCulling, seven
  ComputeMeshCompaction and seven UploadMeshData nodes; no RenderLoop/Shadows
  preparation prefixes remain. No new rendering exceptions, shader errors,
  invalid kernels or assertions occurred after the pre-Play log mark.
- No new Frame Debugger payload export or performance benchmark was performed;
  the preceding export limitation remains explicit. D3D12/Vulkan remain
  TODO(UNVERIFIED). No performance improvement is claimed from renaming.

Validation used a task-owned Editor PID 51500 on the idle original project.
The first CLI-launched process exited before readiness; the managed retry
provided all accepted results. An unsupported combined test filter matched zero
tests and was superseded by the four passing class runs. Capture setup requests
missing fixture/ROI were rejected before starting and corrected explicitly.

Cleanup verified: Play off, time scale restored, clean Scene_Spazon still active,
MainCamera back to 1666x1756, normal capture Completed with zero outstanding
readbacks. Owned Editor PID 51500 exited gracefully. The task directory
`intermediate/mesh-sampler-unify/` and its sibling OS sidecar are absent;
3,716,994 payload bytes / 23,068,672 allocated bytes were reclaimed. No source
scene/settings save, branch, commit, push or shutdown was performed.

### Metal Shader defined outputs — 2026-10-24

The reported SSGI LoadSurface, TAA depth conversion, SSR reprojection, texture UV
and sphere/disk illuminance diagnostics were reproduced on Metal. The original
six compute shaders additionally reported the same diagnostic on directly
coupled trace, atmosphere, light, shadow and probe helpers (47 flagged symbols,
including out parameters). Actual consumption later exposed ContactShadowFromMask
and the sky's GGX prefilter; these are included in the repair.

Helpers now initialize meaningful results/out values before guarded work and
converge to one return. Ray traversal retains its original order and formulas,
using found state and break for termination. Rejected surfaces preserve raw
depth; their geometry stays zero. Shared SSGI surface loading lives in one HLSL
include used by production and focused GPU tests. The two-dispatch TAA oracle
remains test-only. No warning suppression, compiler-backend switch, feature
disable, resource fallback, queue change or serialized schema change was added.

Verification:

- Static gate: 0 errors / 0 warnings; 12 static regression/negative tests passed.
  Unity 6000.6.0f1 script recompile: up-to-date, 0 errors / 0 warnings.
- DXC: 41 kernel/variant combinations passed across the six affected compute
  shaders, CubeFilter, the new contract fixture and the temporal reference.
  This is an offline syntax/binding gate, not D3D12 hardware acceptance.
- Metal / Apple M3 Max: 86 unique EditMode tests passed, 0 failed / skipped.
  ShaderInitializationGpuTests 14 (`8f4ceca7-4abf-40d9-801a-cdcabc7ccf89`),
  StochasticSSGINumericTests 4 (`a163afb3-02d1-4f95-83e9-1c6e5061749e`),
  StochasticSSRNumericTests 5 (`5e671fba-ab56-4966-a792-80f54f3cfcf6`),
  TemporalResolveGpuTests 14 (`c96e4c8d-3991-4d22-a77b-8a5561e50aa8`),
  TextureCapacityTests 8 (`06c80b91-d5d9-4b35-ba19-41cff408fa59`),
  LightShapeGpuTests 3 (`c40870eb-92ea-40b6-9322-ad450ef94edd`),
  ShadowSamplingGpuTests 16 (`4bac9227-139e-4008-8ed8-b2f22601fa6a`),
  CubemapArraySamplingGpuTests 2 (`3e099006-cf4e-4f57-9afc-8dcb6e1ff589`),
  SSGITemporalResponseGpuTests 20 (`5d05b323-dc87-4fbd-802e-1255480cabee`).
- New contracts cover perspective/orthographic and both depth directions,
  each rejected projection, horizon-clipped sphere/disk illuminance, rejected
  surface metadata, Hi-Z/linear hits and misses, zero-length rejection, and
  contact-mask selection. The CPU-double irradiance oracle allows max(2e-6,
  2e-4 * expected) for Metal transcendental approximations; formulas are unchanged.
- Six bounded 320x180 normal frames completed in an owned linear-color fixture.
  Four covered actual Deferred AO/contact keyword combinations; two repeated
  the contact combinations after the final helper repair. All color/depth/SSR/
  SSGI readbacks were finite and drained. Final SSR RGB max was 0.540527 and SSGI
  RGB max 0.102478, with 4173 / 7726 nonzero RGB pixels respectively. Actual graphs
  contained deferred, atmosphere composite, opaque/color pyramids, SSR, SSGI and
  TAA producers; Main ran normally and MainDebug ran in the temporal GPU tests.
- After final dispatch, public ShaderUtil diagnostics reported zero uninitialized
  warnings and zero errors on all six targets plus CubeFilter. The final marked
  rendering log had zero new uninitialized warnings, shader errors, exceptions,
  invalid kernels or assertions. Existing Deferred X4714/register-pressure and
  signed-divide/modulus advisories were also present in the original baseline;
  GTAO retains its unrelated truncation/pow diagnostics. These are not accepted
  as a generic warning-free pipeline. No GPU performance benefit is claimed.

Main Agent inspected the normal color preview: geometry, reflections and lit
ground were visible. The fixture also shows the already-listed horizon dark band;
this task neither repairs that independent image issue nor establishes a matched
Scene_Spazon image baseline. D3D12/Vulkan remain TODO(UNVERIFIED); no Player built.

The original project Editor was occupied, so only disposable hosts were operated.
The first host required uGUI/TMP for existing tools; package Search indexing then
stalled the host main thread (confirmed by a bounded sample). A fresh host with
package indexing disabled provided accepted results. Early host startup failures,
fixture type/pragma import failures, oracle tolerance failures and non-idle test
requests are superseded by the final runs, rather than counted as acceptance.
The user confirmed concurrent default-resource serialization changes belong to
another session; those assets were not reverted or included in this repair.

Cleanup verified: all six capture sessions were Completed with zero outstanding
requests; every submitted buffer readback was terminal and retired. The owned
host's command channel was lost during its final reload/restore, so clean
in-Editor restoration was not claimed. Graceful quit was unreachable; CLI closed
only owned PID 60527 with its explicit project path and --force. No owned Unity
producer remained. The exact `intermediate/metal-init/` tree and OS sidecar are
absent, reclaiming 349,086,031 payload bytes / 1,557,659,648 allocated bytes.
The Test Framework's forced DefaultCompany/host/TestResults.xml mirror was
identified as this task's final 14-case run and removed. Original user scenes
were never loaded; disposable host assets/settings were deleted with the host.
No branch, commit, push, Player build or system shutdown was performed.


## InfinityExample full-chain Metal warning closure — 2026-10-24

This supersedes the earlier six-target/320x180 fixture-only warning verdict for
current Example acceptance. Naming, routing, serialized settings and the user's
existing uncommitted resources are preserved. Main Agent used the explicitly
authorized open Editor, PID 53421: Unity 6000.6.0f1 / Metal / Apple M3 Max,
Scene_Spazon, MainCamera 1666x1756, Quality 0,
Assets/Profile/RenderPipelineAsset.asset.

The initial eight loaded compute assets exposed 34 diagnostic entries. The
broader raster inventory exposed another 11 entries. Repairs:

| Diagnostic | Repair |
| --- | --- |
| Cookie SelectChannels; VSM/EVSM moment outputs | Initialized result, guarded assignment, one return; channel/warp formulas preserved. |
| Cloud scene limit, sphere roots, shell intervals and LightMarch | Explicit result/control flow; stable quadratic roots, interval ordering, ground rejection and Beer/powder integration retained. |
| Volumetric shadow receiver divide-by-zero | Defined reciprocal length for a degenerate tangent; zero-normal/degenerate projected planes keep zero gradient. |
| GTAO truncation and negative pow domain | Explicit motion `.xy` and AO `.r`; saturate the physical AO base before pow, without abs. |
| Cascade signed modulus/division | Unsigned slice bit operations retain the 2x2 atlas layout. |
| Deferred X4714 | 8x8 shading workgroup; named kernel and queried group dimensions frozen at recording; clustered tiles remain 16x16. |
| Deprecated LUT pragma; signed fixture comparison | Current debug-symbol pragma and explicit unsigned case bound. |
| Probe outputs; Terrain matrix macro redefinitions | Initialized probe-local outputs and one return retain APV/LPPV/SH precedence; ShaderVariables is the sole previous-matrix macro owner. |

Validation completed:

- Static gate: 0 errors / 0 warnings; 12 static regression/negative tests passed.
  Unity script compilation: 0 errors / 0 warnings. DXC was not rerun in this
  round; Metal production compilation/execution is the platform evidence.
- 85 unique EditMode tests passed, zero failed/skipped in their final runs:
  ShadowSamplingGpuTests 17, LightCookieProductionGpuTests 6,
  DeferredLightLoopGpuTests 13, CloudShellGpuTests 3,
  ShadowMomentProductionGpuTests 13, GBufferContractTests 11,
  ShaderInitializationGpuTests 14, BakedGITests 8.
  Test receipts respectively: `7892c360-865b-4b82-972c-1b72e5a9af55`,
  `5ae11caf-7a48-41d3-b5d9-a7b881b62111`,
  `85361a50-b5da-40c6-b872-4fbcbd67ef3b`,
  `79491c60-ea66-4aa5-be99-ffeb99553be1`,
  `676133c7-f89f-4a79-a012-1c4da4bb470f`,
  `398a08e7-e69f-4b8b-a05e-5a11bd2fa47f`,
  `c2bd5cdb-0236-4b2c-8895-214abf4f8b1e`,
  `cfa0c855-e758-4b49-bcab-ea4a2cc426d0`.
- Original full Scene_Spazon before/after normal frames used the original
  resolution, camera and quality. Depth, GBuffer, motion, HiZ, SSR, SSGI,
  Deferred, TAA, both color pyramids, post-processing and display all reported
  requested/supported/produced. DisplayColor, depth, SSR and SSGI readbacks
  completed with zero nonfinite values; actual geometry/materials, illumination,
  shadows and output were visible in Main Agent's preview inspection.
- A runtime-only additive supplement enabled GTAO, contact shadows, cloud/fog,
  2D/cube cookies and local VSM/EVSM lights while the original full chain stayed
  active. All those feature states reported produced. The accepted fog history
  was read through HistoryCache: logical 209x220x64, capacity 216x224x64;
  all effective texels were finite (11,770,880 nonzero components).
- MainCamera is below the cloud model's ground. A separate temporary 1666x1756
  camera at altitude 1000 exercised nonempty cloud integration in the loaded
  original world: 3179 cloud pixels, transmittance minimum 0.000262737,
  RGB maximum 88.125; cloud/display/depth readbacks were finite. The original
  camera transform was not moved.
- After production execution, public ShaderUtil reported zero warnings/errors
  across 27 distinct loaded compute assets and 26 loaded package/Example raster
  shaders. No Shader Graph was used in the current scene/inventory, so this is
  not an all-Graph/all-variant acceptance claim. The final live log window
  (PlayerLoop frame 5091 through 7441) contained zero new shader warnings/errors,
  uninitialized/divide-by-zero messages, invalid kernels, assertions or exceptions.
- The before/after display component mean absolute difference was 0.001939.
  These are visual/finite-value regression frames, not a bit-exact oracle:
  temporal phases and initial game times (0.02 versus 0 seconds) differ. The
  production numeric tests provide the branch/numeric contracts. No GPU timing
  or performance gain is claimed.

Superseded attempts are not passes: two initial Deferred fixture cases could
not create a readable UInt Texture2D on Metal; the final fixture uses the
production UInt RenderTexture layout and all four AO/contact combinations pass.
An invalid fog capture semantic and an additive-camera scene selector were
rejected before capture. The first cloud request timed out because background
Game View repaints did not advance the PlayerLoop; after the authorized Editor
was focused, actual frame progression and nonempty production cloud execution
were verified. Unity's unrelated Curl certificate error is infrastructure noise,
not counted as shader acceptance or hidden by a warning filter.

Platform limits: Metal current-chain acceptance only. D3D12/Vulkan and real HDR
displays remain TODO(UNVERIFIED). RTAO is disabled on the original RP Asset;
its existing Player-loading gap remains independent. APV/LPPV data-driven image
acceptance and unused Shader Graph variants are not established by this run.
No Player build, branch, commit, push or shutdown was performed.

Restoration verified: stopped/unpaused, timeScale 1, Frame Debugger off, sole
Scene_Spazon clean, original MainCamera position/rotation and 1666x1756,
Quality 0 and original RP Asset. User Editor stays open. All 642 baseline
source asset/ProjectSettings/resource hashes match; no source asset was saved.
Four successful single-frame capture groups used 292,549,600 raw bytes; total
intermediate data was under 283 MiB. Task data and owned forced test-result
copies are removed only after producer/readback drain; final cleanup is recorded
below after checking their absence.

Cleanup complete: tests inactive; capture finished with zero outstanding requests;
all owned transient objects absent. The project task directory
`intermediate/example-warning-closure` and the byte-identical framework copy
`~/Library/Application Support/Infinity/MeshBatch/TestResults.xml` were removed
and checked absent. An unrelated DefaultCompany test result did not match any
task receipt and was preserved. DataDisk AppleDouble removal raced with directory
cleanup; only already-missing sidecars were tolerated, followed by an explicit
absence check. No Library/ShaderCache cleanup occurred.


## Visibility / shared history / effect-chain upgrade — 2026-10-25

Implemented and exercised on Unity 6000.6.0f1 / Metal / Apple M3 Max in one
independent task-owned mirror of the complete Scene_Spazon, Quality 0,
MainCamera 1666x1756. The source checkout stayed on main / 2485580 with the
candidate uncommitted. See [delivery receipt](../../Record/History/Visibility-20261025/README.md)
and its compact machine-readable evidence; historical sections above describe
older implementations and do not override this section.

- RenderGraph handles RAW/WAR/WAW, physical import aliases and queue-aware reuse.
  ResourcePool owns recording leases, individual pending submission epochs,
  submitted completion and uncertain outcomes. CPU uploads acquire writable
  storage or publish independent replacements; same-queue ordered reuse remains.
- Mesh transforms use scene-owned immutable matrix/generation versions. Views
  retain accepted/pending references; 64-slot dirty patches and GPU copy/patch
  preserve sparse-view history without per-camera pose arrays. Native renderers
  retain Unity's own motion history.
- Closest/Furthest half-base R32 HiZ, early Furthest and conservative two-stage
  GPU visibility are integrated. The native-occluder recovery control observed
  first/deferred/recovered/final visibility 0/1/1/1 and real recovered depth.
- SSR alone keeps full Resolve/Temporal/Spatial/history, with optional half Trace.
  SSGI has no Resolve: half Trace/Temporal/world-space Spatial/history reconstruct
  at full resolution. Ultra uses full work resolution; Custom keeps HalfResolution.
  Both use accepted unsharpened TAA/SR color mips and previous-object depth metadata.
  SSGI reconstruction respects all four rotating quad phases and odd last intervals.
- GTAO accumulates linear visibility with normal/depth/age; artistic shaping is
  applied after reconstruction. SSGI retains filtered recurrent history; SSR
  retains pre-spatial history.
- Half-resolution Burley SSS filters a separate optional diffuse attachment and
  recomposes main minus original diffuse plus filtered diffuse. Specular/emission
  stay unscattered. Material profile indices own diffusion parameters; Volume
  activation no longer overrides all surfaces with profile zero.
- AO/contact and previous-color effects precede independent shadow rasterization.
  Graphics history copies follow that window. Early recovery, late SSS, TAA/SR
  and post stay on Graphics where no independent raster window exists.

Verification receipts include 12 resource intervals, four actual Metal delayed/
incomplete submission cases, six submission outcomes, 45 RG passes plus one
hardware-async skip, 92 Mesh passes, nine dual-HiZ cases, 13 shared temporal-color
cases, four SR history cases, 18 GTAO cases, 16 stochastic SSR/SSGI cases,
21 SSGI response cases, nine SSS GPU cases, 11 camera history cases, 22 quality
cases, 13 Deferred GPU cases and five feature-registry cases. Counts describe
individual selected batches, not a deduplicated whole-repository test total.

Final original-settings capture at game frame 9533 completed 32 warmup submissions
plus one frame, with zero outstanding readbacks and zero nonfinite values across
12 buffers. SSR Resolve/Temporal/meta/output were 1666x1756; SSGI Trace/Temporal/
meta/hit distance were 833x878 and final output was 1666x1756. Previous color was
accepted UnsharpenedTAA. Main Agent inspected the presented image. A separate
runtime-only supplement retained SSR/SSGI/TAA while producing GTAO, SSS, cloud,
fog, Cookie, EVSM/contact and generated Lit/Unlit/Decal Graph draws. Eight output
readbacks were finite. Cloud output was neutral in this camera: visible-cloud
quality is not accepted by that result.

Generated Lit/Unlit/Decal Graph assets imported and their 13 passes plus the Lit
Forward SSS variant compiled on Metal. Final public ShaderUtil diagnostics were
empty for 26 loaded package ComputeShaders and 28 package/generated Shaders;
the restarted candidate rendering log had no shader errors/warnings, invalid
kernels, unbound properties or assertions. A pre-existing cold C# compile warning
in the LPPV validation-scene fixture remains separately recorded (CS0618).

Matched baseline/candidate static, dynamic and two-camera cases each used 32
warmup plus 120 accepted main-camera submissions, with animation frozen and the
same fixed camera/effects. Allocations stayed constant within every sampling
window. Static candidate transform productions/uploads stayed at 1/640 total;
dynamic single/two-camera cases each produced 119 versions and 85,680 patch bytes
across 119 sample intervals. Per-camera pose arrays/uploads were removed. This
small scene has nine transform slots: dirty-page patch uploads can cost more than
a single old per-view matrix upload, so no universal bandwidth reduction is
claimed. Estimated pooled texture bytes rose from 522,615,920 to 558,742,496 in
the main-view case; the extra HiZ, geometric metadata and history mip work is not
reported as memory savings. These are format estimates, not driver residency.
GPU timing was unavailable/zero; no acceleration claim is made.

**Resource-pool whole lifecycle remains PARTIAL.** This host reports
supportsAsyncCompute=false. Hardware cross-queue execution/overlap and real native
Submit-failure recovery remain TODO(UNVERIFIED), even though Metal delayed,
cross-camera, cancelled and submitted-but-incomplete tests passed. Unknown native
submission outcomes stay quarantined. D3D12, Vulkan, real HDR display and unrun RT
backends remain TODO(UNVERIFIED); the existing independent Player RTAO loading gap
is unchanged. Arbitrary-scene quality and a high-sample reference sweep are not
implied by these bounded normal frames and numeric controls.

Restoration and final cleanup evidence is maintained in the delivery receipt.
The 642 protected source files matched the initial hashes except the explicitly
removed SSGI ResolveSamples/ResolveRadius entries in the package default Profile.
No source scene/material/LUT was saved or overwritten for validation.

## Main-thread / GC / memory / SSGI binning / native HDR — 2026-10-25

The [delivery receipt](../../Record/History/MainThread-20261025/Report.md) and
[structured evidence](../../Record/History/MainThread-20261025/Evidence.json) record the latest
full Scene_Spazon comparison on a single owned Metal Editor. The source scene,
1666x1756 main camera, Quality 0 and enabled SSR/SSGI/TAA/Deferred/post chain were
preserved. Generated Lit/Unlit/Decal Graphs actually drew; runtime-only supplements
produced SSS, fog/cloud, Cookie/EVSM and DBuffer data. Main cloud RGB was empty;
a cloud-layer camera separately produced nonempty finite RGB, and finite fog
history contained nonzero scattering. These controls do not accept all content.

- Final selected batches: 458 passed executions, zero failed, one hardware Async
  skip; 443 distinct passing names and one distinct skipped name. The latest
  Mesh batch has 96 cases. Final import/render diagnostics were empty for 27
  loaded package ComputeShaders and 15 Shaders/Graphs. Static gate and 12 negative
  checks passed. No Library/ShaderCache deletion was used.
- Calibrated native GC.Alloc counters replaced an invalid Mono byte counter.
  Fixed-size, prewarmed normal library Proxy scopes recorded zero allocations
  in stationary and moving 1k/10k/50k windows. Complete Render still recorded four
  Unity-native Submit GUILayout allocations per frame; these are reported rather
  than subtracted from the total. One earlier nonzero CPU window was repeated
  successfully and remains documented. Active RTAO, setup/growth and diagnostics
  are outside this zero-GC acceptance.
- At 10k, Proxy p50 fell from approximately 64 ms stationary / 190–194 ms fully
  moving to 0.10 ms / 21–22 ms. Fully moving 50k still costs about 105 ms Proxy CPU.
  These are CPU measurements; no GPU speedup is claimed. Static scenes upload
  zero transform/attribute bytes; existing safe attribute version uploads remain
  measurable during motion and are not claimed as a bandwidth optimization.
- Pool texture estimates are 497.421 MiB versus 532.858 MiB at this round's
  `a4f016c` baseline and 498.405 MiB at matched `2485580`. Pool+history+other created
  texture estimates fell 63.985 MiB against `2485580`; this is not an ownership
  transfer or driver-residency measurement. Full physical ledgers are retained.
- Native internal-XDR HDR was available and active with no transition pending:
  linear Rec.2020, R16G16B16A16_SFloat, 160-nit paper white. The actual Metal color
  attachment was RGBA16Float. UI steps, primaries/reference-white GPU controls,
  exposure and SDR restoration passed. Absolute photometric accuracy, other HDR
  encodings/displays, Player, D3D12 and Vulkan remain unverified.

**Resource-pool whole lifecycle remains PARTIAL.** This host has no exposed Async
Compute queue. Metal delayed/cancelled/incomplete/cross-camera tests pass, but
actual hardware cross-queue execution and native driver Submit-failure recovery
remain unverified. The independent Player RTAO loading gap and unexecuted RT
backends are unchanged. Cleanup/restoration proof belongs to the receipt; it does
not broaden these platform results.
