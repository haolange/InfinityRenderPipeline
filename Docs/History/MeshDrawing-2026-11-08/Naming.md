# Mesh drawing naming acceptance

Recorded 2026-11-08 on `ccfdfb8`. The three rename commits and the include fix are `6df8f8f`, `824e82e`, `9d4f691` and `ccfdfb8`, on `main`. `Docs/History` bodies and `Documentation~/Evidence` were not rewritten. The one history rename is `MeshPipeline-Design-v2.md` to `MeshDrawingPipeline-Design.md`, with its `.meta`. No `DESIGN.md`.

Submitting a mesh draw list records draw calls on the raster encoder. Native submit and a submission segment remain command-buffer submission.

## N1 draw API

```csharp
var desc = new MeshDrawListDesc(meshView, InfinityPassIDs.RasterGBufferPass);
RGMeshDrawListHandle meshDrawList = m_RGBuilder.CreateMeshDrawList(desc);
passData.meshDrawList = pass.UseMeshDrawList(meshDrawList);
cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
```

`MeshDrawListDesc` stores `MeshView view` and `ShaderTagId passTag`. Record checks the tag against the nine `InfinityPassIDs` mesh passes and still checks `MeshLODProjection`. Queue, sort, entity flags and previous transforms stay on `MeshPassTable`. The world builds those nine caches at construction. An unknown tag throws `ArgumentException` from `CreateMeshDrawList`. There is no 64-cache cap and no first-request intern.

| Before | After |
|---|---|
| `CreateDrawList(view, MeshPassPresets.X)` | `CreateMeshDrawList(new MeshDrawListDesc(view, InfinityPassIDs.X))` |
| `RGDrawListHandle` | `RGMeshDrawListHandle` |
| `UseDrawList` | `UseMeshDrawList` |
| encoder `Draw(handle)` | `SubmitMeshDrawList` |
| `EnsureMeshGpuCull` | `RequestMeshGPUCulling` |
| `RecordMeshOcclusionFirst` | `RecordMeshOcclusionPrediction` |
| `EMeshVisibilityPhase.First` | `EMeshVisibilityPhase.Prediction` (same ordinal) |
| `MeshCpuDrawBackend` / `MeshGpuDrawBackend` | `MeshDrawCPUBackend` / `MeshDrawGPUBackend` |
| `EMeshBackendPolicy` `Auto=0`, `CpuDirect=1`, `GpuIndirect=2` | `EMeshDrawBackendPolicy` `Auto=0`, `CPUDirect=1`, `GPUIndirect=2` |
| `MeshGpuVisibilityCache` | `MeshGPUVisibilityCache` |
| `MeshPassPresets` | internal `MeshPassTable` |
| `MeshPassDesc` | internal |
| `RGDrawListContext` | `MeshDrawListRegistry` |
| `RGDrawListRecord` | `MeshDrawListRecord` |
| `ERGDrawListCompileState` | `EMeshDrawListState` |
| `MeshDrawItem` / `MeshDrawItemComparer` | `MeshDrawInstance` / `MeshDrawInstanceComparer` |
| `MeshInstancedDraw` | `MeshDrawCall` |
| pass field `draws` | `meshDrawList` / `meshDrawLists` / `sliceMeshDrawLists` |
| `MeshWorldGpu` / `MeshWorldGpuTables` | `MeshWorldGPU` / `MeshWorldGPUTables` |
| `MeshLod*` types and `MeshLod.hlsl` | `MeshLOD*` and `MeshLOD.hlsl` |

The serialized field `meshBackendPolicy` is still the integers 0, 1 and 2. `MeshDrawListBuild.draws` is still the `NativeList` of `MeshDrawCall`. GUID, kernel names, shader property strings and profiling markers are unchanged.

Names the token pass did not change, and why:

- Test methods `LodChain*`, `LodSelection_*`, `GpuLodSelection_AgreesWithCpuSelection`, `GpuCulling_IsRecordedOncePerVisibilityKey`, `CpuVisibility_*`, `GpuFrustumCulling_AgreesWithCpuCulling`, `GpuCompaction_MatchesTheCpuBuildPerCommandRange`. They are NUnit names, not the draw API.
- `MeshLodTests` is still the class in `MeshLODTests.cs`. `MeshOcclusionGpuTests` and `MeshTransformVersionGpuTests` keep `Gpu` in the class name.
- `MeshPassCache.LodShift` is a public const, not a type or method.
- Outside the mesh pipeline, test types such as `LightTileMaskGpuTests`, `LightShapeGpuTests` and `DeferredLightLoopGpuTests`, and `LightBinningTests.GpuProjection`, were not renamed.

## N2 type prefix

Living C# and HLSL types no longer use an `F` prefix. The HLSL struct uses the same name, and `GPU-RECORD` comments follow it. None of these types are `MonoBehaviour` or `ScriptableObject`, and none are `SerializeReference`. YAML field names are unchanged. A binary scene typetree stores the nested struct type name, so saving `Scene_Spazon` after the rename rewrote `FFrameSettings`, `FFrameSettingsOverride` and `FLightShape` at the same file length. That save was discarded. Reloading the original file with the new scripts leaves the scene clean. `m_Shape` still loads.

Collisions, where dropping `F` was the wrong name:

| Before | After | Why |
|---|---|---|
| `FBound` | `BoundingBox` | `Bound` is not the quantity |
| `FAABB` | `MinMaxBox` | not an AABB type name |
| `FPlane` | `PlaneEquation` | used by `TerrainUtility.IntersectAABBFrustum` and `Geometry.IntersectAABBFrustum` |
| `FGraphics` | `RenderTaskQueue` | not a graphics device |
| `FGraphicsTask` | `RenderTask` | the queue's task |
| `FGPUResourceCache` | `GPUResourceCache` | the `F` was the only prefix |
| `FLightLoop` | `LightLoopState` | `BeginLightLoop` already uses the word loop |
| `FGBufferData` | `GBufferData` | |
| `FInfinitySurface` | `InfinitySurface` | |
| `FImageComparison` | `ImageComparisonResult` | the file already has static class `ImageComparison` |
| `FLightShape` | `LightSourceShape` | `UnityEngine.LightShape` |
| `FLocalShadowFrustum` | `LocalShadowViewProjection` | `ShadowAllocator` already has method `LocalShadowFrustum` |

`FSphere` existed only as its own definition and was deleted. `SortFactory.cs` (`FSortFactory`, `FSortRange`, `FMergeSort`, `FQuicksortJob`) had no external caller and was deleted. `ccfdfb8` puts the include path back to `Lighting/LightShapes.hlsl` after a substring replace had pointed shaders at `LightSourceShapes.hlsl`.

Every other declared `F` type dropped the prefix: `CascadeShadowSlice`, `CatmullRomSamples`, `ContactShadowSettings`, `CookieKey`, `CookieUpload`, `DiffusionProfileRecord`, `FrameSettings`, `FrameSettingsOverride`, `GTAOSettings`, `HDROutputState`, `LightCookie`, `LightCookieGPU`, `LightLoopInputs`, `LightRecord`, `LightRecordFlags`, `LightRecordPack`, `LightShapeSample`, `LocalShadowSlice`, `LocalShadowSliceGPU`, `MeshTransformPatch`, `NeighborhoodMoments`, `OutputPlan`, `PostComputeKernels`, `ProbeCubeKey`, `ProbeCubeUpload`, `RGResourceMap`, `RTAOSettings`, `ReconstructInput`, `ReflectionProbeRecord`, `ReflectionSample`, `ResolvedFrameSettings`, `SSGISettings`, `SSGISurface`, `SSRSettings`, `SSRSurface`, `ScreenSpaceHit`, `ScreenSpaceRay`, `SectionKey`, `ShaderState`, `ShadowAllocatorSettings`, `ShadowCacheKey`, `ShadowFilterSettings`, `ShadowInputs`, `SphericalHarmonicOneBasis`, `SphericalHarmonicOneTable`, `SphericalHarmonicTwoBasis`, `SphericalHarmonicTwoTable`, `TemporalColorView`, `TextureCapacityContext`, `TextureView`, `TransformData`, `VolumetricCloudSettings`, `VolumetricFogSettings`. `FMeshLodChain` became `MeshLODChain` because N1 uppercased LOD first. There is no `.shadergraph` in the package or the Example Assets, so the Shader Graph reimport was a no-op. `check_sgstrings` passed.

## N3 generation names

Living comments and headings no longer call the light record, the clustered light list or the mesh drawing pipeline a generation. `CHANGELOG.md` 0.5.0 names the API. `static_check.py` has `fprefix` and `generationname`, with positive and negative tests. Data versions stay: `MeshTransformVersion`, atlas content version, `historyVersion`, `shadowCacheVersion`, the playbook cookie sentence `committed v1 → queued v2 → unqueued failed v3 → request v1`, semver, and ACES `v1.0` / `v1.5`. `uv0` and `v0` are not generation names. `0.4.0` and earlier changelog text is unchanged.

## Checks

Mirror: `/Volumes/DataDisk/Projects/Unity/InfinityExample/intermediate/mesh-naming/20261108-2256/project`. Unity 6000.6.0f1, Metal, Apple M3 Max. The user Example Editor was not driven. `python3 -B Tools/StaticCheck/static_check.py --repo .` reported 0 errors before this note. `test_static_check.py` was 24 passed.

| Check | Result |
|---|---|
| C# / shader compile | The mirror Editor reloaded the renamed runtime with no `error CS`. The broken `LightSourceShapes.hlsl` include was corrected in `ccfdfb8` before the frames below. No new InfinityRP warning was the acceptance signal; the frame and the suite were. |
| EditMode `Unity.RenderPipelines.Infinity.Tests` | 830 tests: 817 passed, 12 failed, 1 skipped. Failures: ten `ShaderStrippingTests` (`attempt to write a readonly database`), `MeshResidencySubmissionTests.FailedGraph_ThenGrowthBeforeSubmit_PreservesQueuedInputs`, `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`. Those 12 already fail at `a814a40`. |
| `AssetBaseline.py compare` project scope | PASS. 137 inputs, 0 differences, after restoring `ProjectSettings.asset`, `TimeManager.asset` and `project.slnx` from the pre-rename backup. `Scene_Spazon.unity` matched that backup without a restore. |
| Scene_Spazon DisplayColor, 1666×1754, history reset, jitter 0, game time 0 | CPU `final-cpu-8` vs pre-rename CPU: SSIM 1, 0 pixels above 8/255, max channel delta 0.0029. GPU `final-gpu` vs pre-rename GPU: SSIM 1, 0 pixels, max channel delta 0.0029. |
| 10k all-moving probe, windows 5/20/60 s | Quiet rerun. CPU 60 s proxy p50 0.3895 ms (2455 frames). GPU 60 s proxy p50 0.3712 ms (2297 frames). Round 2 was 0.387 and 0.382. Both are inside ±10%. Proxy `GC.Alloc` p50 is 0 in every window. `actualBackend` matched the request. GPU evidence says `restored: true`. |

A probe taken while the Editor was still compiling and capturing was outside the band (CPU 0.574 ms, GPU 0.545 ms at 60 s). The minimum of that CPU window was 0.306 ms, under the round-2 median, so the median moved with the tail. The quiet rerun is the number above.

Searches outside `Docs/History` and `Documentation~/Evidence` found no old mesh draw API names, no `F` type declarations, and no generation heading or table row.

D3D12, Vulkan and Windows stay `TODO(UNVERIFIED)`.
