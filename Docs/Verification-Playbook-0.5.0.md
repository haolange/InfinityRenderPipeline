# 0.5.0 verification playbook

0.5.0 was originally written without a Unity Editor. Its original offline gates were differential C# compilation,
DXC shader compilation and `Tools/StaticCheck/static_check.py`. Subsequent Editor evidence is recorded in
`Docs/FeatureStatus.md` and `Documentation~/Evidence/Verification-0.5-2026-10-19/`. For historical replay, verify
stages **in commit order**: each stage's first commit subject starts with `SNN:`.

The approved 2026-10-23 remaining-acceptance closure reuses completed historical evidence rather than replaying
S0–S17. S1 acceptance combines its historical related contracts/behavior with the final current Infinity-package
suite; the missing historical whole-Example run is explicitly waived, not represented as executed. The 53 external
SDK skips retain their actual reasons and outcomes outside Infinity acceptance. Current picture defects, required
whole-frame performance comparisons, three cold-start transient checks and final normal-image regressions remain
separate gates in the conversation task list.

```
git log --reverse --format='%h %s' origin/main..claude/infinityrp-0.5
```

## How to verify one stage

1. Check out the last commit of the stage.
2. Open the Example project and wait for the import to finish. Record the first Console error, if any.
3. Run the stage's steps below.
4. Fill in the report template at the end and send it back.

If a stage fails to compile, stop there: later stages build on it.

The shared `infinity_tests_start` command accepts `outputDirectory` (a new owned task directory), `filter` (literal
name substring), `category` (exact category name), and `allAssemblies` (default false: Infinity tests only).
The pending-verification category is `Unverified_0_5`. Earlier commits used `Unverified-0.5`, which NUnit rejects
because category names cannot contain `-`; the affected tests become Failed:Invalid before their bodies execute.
When replaying S2 or later, carry the literal-name correction and retain the original failed XML. This preserves
pending status and every test/assertion; it does not accept a feature or remove its verification marker. Match the
same test identities before/after the rename and require a nonzero exact-category run with executed test bodies.
Name, category and assembly selections are combined with AND. Run stage filters first, then run with
`allAssemblies=true` and no name/category filter for the whole Example EditMode suite. Check discovery and final
XML counts; a zero-match run, cancellation or incomplete reload is not a pass. `run.json` records counts and
reload identity. `reload-checkpoint.json` stores the original UniqueName selection and actual completed leaf XML;
`progress.jsonl` retains callback progress, with failed-test XML saved separately. Across reload the runner
reattaches to the same TestRunner run without executing it again.
Missing, malformed, duplicate, unexpected or mismatched outcomes fail the evidence gate.

`results.xml` is the unchanged framework result. UTF 1.8.0 can omit already-finished skipped leaves when rebuilding
its result tree after a script reload. In that case `callback-results.xml` is a separately named derived report:
it may restore only exact, completed, checkpointed skipped XML nodes. Check `frameworkComplete`,
`callbacksComplete`, `frameworkMissingSkipped` and both roots. A Skipped/Ignored root stays Skipped/Ignored;
a completed command or zero failed leaves does not imply every discovered test passed. Preserve skip reasons.
For runner regression checks, retain hashes of a completed run's JSON/XML, execute a second run into a fresh
directory, and confirm the first run's artifacts stay byte-identical (including zero-match and failure results).
Before starting, use an idle Editor with clean scenes: a native save-dialog cancellation before RunStarted has
zero discovered/started tests and cannot be accepted as a completed test run.

## Report template

```
Stage: SNN   Commit: <hash>   Unity: 6000.6.x   Platform: macOS Metal / Windows D3D12 / ...
Compile: OK | first error (file:line + message)
EditMode: <passed>/<total>; failures (test name + message)
Images: scene → OK | problem (screenshot)
Notes:
```

## S0 — offline gate

Nothing to verify in the Editor. Run `python3 Tools/StaticCheck/static_check.py --repo .` and the offline regression
tests (`python3 -B Tools/StaticCheck/test_static_check.py -v`, with `TMPDIR` set to the owned task directory).
Hidden macOS sidecars and commented-out LightMode tags are not assets or live passes. Real orphan metadata,
duplicate GUIDs and unknown live tags must still fail.

When replaying S0 before historical reports were archived, the eight missing historical links may be reported as
explicit warnings using `--historical-link-receipt Documentation~/Evidence/Verification-0.5-2026-10-19/S0-historical-links.json`.
The receipt binds each document path, full SHA-256 and exact link. A changed document or an unlisted broken link
still fails. Retain the original failed check; these missing historical images are not current image evidence.

## S1 — documents and hygiene

1. Compile. The deleted tools in `Editor/Tools/Validation` had no entry points; any compile error there is a missed consumer.
2. Add a Camera to a scene: `InfinityAdditionalCameraData` appears, and Undo removes both.
3. A FilmTonemap Volume component shows mode **ACES** where it showed **Film**. Compare the same scene,
   camera dimensions, exposure, grading and serialized mode value 1 on the pre-S1 and S1 endpoints. Capture normal
   advancing Game frames after warmup; compare both pre-post input and display-linear output. Record a mode None
   control, then restore it without saving the shared Profile. Retain actual Inspector and frame evidence.
4. Rendering Debugger → Infinity Temporal panel is gone; other panels are unchanged.
5. Run `ShaderStrippingTests`: native and empty LightModes in Infinity-tagged shaders are kept; untagged system
   shaders keep TerrainAdd/SystemLUT; undrawn Infinity LightModes and exclusively foreign shaders are stripped.
   Native tag identity, rather than the spelling returned by `ShaderTagId.name`, determines the result. The tests
   load real single-pass shader fixtures without modifying or destroying their assets and invoke the production callback; they do not prove build-time callback
   scheduling or Player output. Do not start a Player build in the staged Editor-only verification run.
6. Optional Player build outside this Editor-only run: the Console prints an "InfinityRP shader stripping" summary that now includes LightMode-stripped variants
   (Infinity-tagged passes whose LightMode the pipeline never draws). Expected count is 0 for the shipped shaders.

## S2 — contract defects

1. Compile; run EditMode tests (new: `DebugViewModeTests`, `ShaderStrippingTests.ActualShaderCallback_StripsUnusedLightModeInInfinitySubShader`, `CommandTableTests.CommittedEdit_ChangesBuildSerial`, `CommandTableTests.SwitchingMaterial_RebuildsCommands`).
2. **Reflection probes**: bake a reflection probe in a lit scene. Expected change: the probe stores HDR scene-linear radiance (no TAA ghosting between faces, no exposure, tonemapping or display encoding).
3. **Material / Inspector previews** render without jitter shimmer; SSR, SSGI, GTAO and volumetrics are off in previews. Record actual multi-frame camera jitter (including committed frames), then rotate the preview. Place a custom Mesh in the open scene within the preview camera frustum: it must not leak into the preview. A custom Mesh inside the PreviewScene must still draw. Verify native and custom Mesh scene eligibility agree for Game additive scenes, SceneView, Reflection and explicit camera scene/override masks. Move a static Mesh between scenes without calling `MarkDirty` or enabling Update Proxy; CPU/GPU visibility and shadow views must update. Run `CameraFrameStateTests` and `MeshViewTests`, including high-bit masks, empty masks, rollback and GPU upload controls.
4. **Disc light** in a scene: the camera keeps rendering. During historical S2 replay, Disc is unsupported: Skipped Light Shapes counts only unsupported lights actually returned by Unity's visible-light cull. Record both the native visible-light list and the counter; a Disc omitted by Unity leaves the counter at zero and does not exercise the unsupported-record path. Keep a separate mapping test for that path. S6 adds supported Disc shading, so the final version must not count it as unsupported.
5. **RP Asset → Lighting & Shadows**: Cascade Splits and Max Shadowed Local Lights are visible. Expand Diffusion Profiles alongside the other fields; no nested Foldout Header error appears. An invalid split order shows an error box and fails rendering with a clear message; restoring valid values resumes rendering. Shadow culling follows Shadow Distance (previously fixed at 128 m): compare two distances with the same camera/light/casters and record the resulting cascade distances and culling spheres. Use an unsaved RP Asset copy for the Inspector negative control, and restore any temporary live values without saving the original asset.
6. **Unified Ray Tracing off** on the RP Asset: RTAO does not record; the Debugger reason says ray tracing is disabled.
7. **GTAO off**: no HalfResDownsample pass in the Frame Debugger; GTAO on: it is present.
8. Rendering Debugger → Infinity Rendering → Debug View is an enum popup; open it and select an option through native input. Check every view on advancing normal Game frames. AO/SSR/SSGI need both their enabled, valid-texture path and their disabled missing-texture indicator. A programmatic model setter proves mode behavior separately and does not prove the popup's input interaction.
9. Mesh GPU path: change a MeshComponent's material at runtime and remove/re-add instances; no stale draws or flicker.

## S3 — RenderGraph and feature registry

1. Compile; run `FeatureRegistryTests`.
2. Inspect GlobalSettings → RuntimeShaders → `Utility Shader`. Existing assets may retain a null slot after an upgrade; ordinary reload is not an automatic completion guarantee. The resource is `Shaders/Utility/Compute_Utility.compute` in this package. Do not use whole-container Reset to fill one field: Reset can overwrite valid custom references.
   During historical replay, explicitly bind only that missing reference in memory, check its GUID/local ID and both kernels, and leave the source asset clean and unsaved. Record all container fields/references before and after, verify the binding survives any test domain reload, then restore the exact original configuration and source/meta bytes before exiting.
   A complete JSON comparison under the current schema cannot prove preservation of native fields that schema no longer declares. Before a persistent upgrade, inventory old and new serialized fields and identify explicitly allowed removals/replacements; only then perform targeted authoring with a small current-byte preimage, stable asset/meta identity and references, Undo/Redo, a byte-identical second save and a cold-reopen no-op. Do not save an existing GlobalSettings from a different schema merely to fill one new slot.
   Run both utility kernels on the GPU with bounds/sentinel controls. Exercise `DispatchComputeIndirect` on imported ComputeBuffer and GraphicsBuffer argument refs, including a nonzero byte offset and zero dispatch. Also verify a real graph execution with declared argument reads/output writes, `RGComputeEncoder` dispatch, terminal readbacks and safe resource retirement; distinguish it from command-wrapper-only checks.
3. Rendering Debugger → Infinity Rendering → Frame Features: each feature shows Off / Records / Blocked with a reason. Disable SSR on the Volume (Off); re-enable it before temporarily clearing the SSR compute shader slot (Blocked: not assigned), then restore the original reference (Records). Use transient Volume overrides, do not save the negative-control null reference, and keep advancing normal-frame and native Debugger evidence for all three states.
4. Images are unchanged from S2.
5. An `RGScoper` error now names the missing resource (for example `_HiZTexture`) instead of a number.

## S4 — quality presets

1. Compile; run `QualityPresetTests`.
2. Open a Volume with GTAO, RTAO, Volumetric Fog or Volumetric Cloud: a Quality section shows `Quality = Custom` and the existing values (images unchanged).
3. Switch Quality to Low/Medium/High/Ultra: the quality fields become read-only and show the preset values; the image changes accordingly. Artistic fields (intensity, radius, density, ...) stay editable.
4. "Copy Preset To Custom" copies the preset into the parameters and switches to Custom; Undo restores the previous state.
5. Run `CloudShellGpuTests`: the production shell helper must match the independent double-precision boundary oracle
   for both planet radii, below/inside/above the layer, tangents, both grazing segments, planet blocking and depth clips.
6. In a task-owned cloud fixture, inspect the actual `ComputeVolumetricCloud` dispatch in Frame Debugger, then resume
   normal frames. Compare Low/Medium/High/Ultra/Custom with sky and opaque foreground visible; clouds must remain above
   the layer bottom and in front of the scene depth. Check sky beyond camera far plane, an observer inside the layer,
   a grazing view, and directional/local light paths. Capture before/after, preserve the original failure, and confirm
   normal capture retirement and original source/configuration restoration. Frame completion alone is not image acceptance.


## S5 — stochastic SSR

1. Compile; run `QualityPresetTests` and `StochasticSSRNumericTests` (GPU; three plane cases, rough-plane skip and constant spatial).
   The 0.4.0 SSR kernels are gone from `ScreenSpaceNumericContract_ProductionKernels`.
2. The three textures in `Runtime/Resources/Textures/MonteCarlo/` (RankingTile256SPP, ScramblingTile256SPP,
   ScrambledNoise_Owen) reimport as uncompressed single-channel. GlobalSettings → RuntimeTextures shows the three
   new blue-noise slots filled.
3. Screen Space Reflection Volume: removed parameters `NumRays`, `SpatialRadius`, `SpatialSample`, `BilateralSample`,
   `BilateralColorWeight`, `BilateralDepthWeight`, `BilateralNormalWeight` (their values are dropped). Kept names:
   `MaxDistance`, `Thickness`, `MaxRoughness`, `fade`, `NumSteps` (now the Hi-Z budget), `BrdfBias`, `TemporalScale`
   (now the variance-clip width), `TemporalWeight`. New: `quality`, `HalfResolution`, `MirrorRoughness`,
   `LinearRoughness`, `LinearSteps`, `SpatialPasses`. The removed ray-reuse controls `ResolveSamples` / `ResolveRadius` have no replacement or load-time migration: SSGI Trace samples accepted previous color directly. Preserve all other serialized parameters when explicitly resaving an existing Profile.
4. Scene with a glossy floor and a rough wall, SSR enabled:
   - Debug View **SSR Tile Class**: floor tiles blue (Hi-Z), rough tiles orange (linear), sky and forward materials gray.
   - **SSR Ray Hit**: green where reflected objects are on screen, red elsewhere; with Half Resolution the pattern is
     a 2x2 rotating checker.
   - **SSR Confidence** fades at screen edges and near Max Distance; **SSR History Age** goes white within ~16 frames
     on a still camera. On disocclusion a valid surface restarts at age 1 (near black), while invalid pixels have age 0.
     Use Dynamic MeshComponents for moving-object controls and confirm the actual Depth change before judging rejection.
   - Final image: mirror reflections sharp and stable while orbiting; rough reflections blurred without fireflies.
     Report ghosting on moving objects and any reflection of an object's back side.
5. Compare Quality Low → Ultra for noise, blur and coverage; measure frame cost separately rather than assuming every
   scene improves monotonically. Frame Debugger shows `SSR_Classify`, `SSR_Trace`
   (three indirect dispatches), `SSR_Resolve`, `SSR_Temporal` and, below Ultra, `SSR_Spatial`.

6. For passive numerical evidence with Debug View still None, normal capture accepts `SSRTileClass` (R32 UInt tile
   values), `SSRMeta` (RGBA16F normal/depth/history age), `SSRRayHit`, `SSRResolved`, `SSRTemporal` and `SSR`.
   Classification is graph-owned only on the explicitly requested capture frame. Verify successful readback/retirement
   and compare matched normal output with/without these diagnostics. Debug View captures remain rejected by the
   normal-beauty guard; inspect those modes separately in the actual window.

## S6 — light data model

1. Compile; run `LightShapeTests`, `LightRecordLayoutTests`, `LightRecordTests`.
2. Open an existing scene: loading does not change native Light fields. On the same fixed code, adopting the native
   shape in the Light inspector leaves rendering and native fields unchanged, creates one dirty/Undo operation, and
   Undo/Redo restores the shape. Separately record the intentional S6 correction from sRGB to linear light color;
   gray/colored light intensity can differ from earlier stages. On a task-owned legacy light, Undo restores the
   un-authored Additional JSON and Redo restores adoption; native Light values stay identical throughout.
3. Light inspector: **Type** offers Directional / Point / Spot / Rect / Disk. Changing it or a shape value updates the
   native type (Point ↔ Tube by Source Length, Spot ↔ Pyramid / Box by Spot Shape) and Unity's gizmo; Undo reverts both
   the Infinity shape and the native Light in one step. Authored Disk uses a native Rectangle culling carrier:
   Unity's native Disc is omitted from realtime culling and resets Realtime/Mixed to Baked on Undo deserialization.
   Additional data remains the Disk identity; the lightmapper must still receive GI Disc with the authored radius/mode.
   Check all three bake modes, shape edit and mode-only edit on an adopted native Disc, Undo/Redo, and save/reopen.
   Plain inspector adoption/loading does not convert an existing native Disc. The native carrier gizmo is rectangular;
   the Infinity Disk handle is circular. Check the actual window and keep these observations separate.
4. New shapes render: pyramid and box spots (hard-edged frustum / box), disk (as a square of equal area), sphere and
   tube (at their center for now). Rect lights no longer light surfaces behind them.
5. Maximum Distance: put the camera beyond a point light's Maximum Distance: it fades out over Distance Fade and
   Rendering Debugger → Infinity Lighting → Lights Beyond Max Distance counts it.
6. Bake a scene with a Mixed spot (inner angle 20°, outer 60°), a Baked disk and a Baked sun with Source Angle 2°: the
   baked spot has the same inner cone as the realtime one; the baked sun shadow penumbra widens with Source Angle;
   baked intensity matches realtime (compare a Mixed light in Baked Indirect with its realtime direct lighting). Report
   if baked lights are now about π times darker or brighter than before. Before the bake, compare realtime radiance with
   public `LightmapperUtils.Extract` for white, middle gray and colored light, with temperature off/on. Authored Unity color
   must be converted to linear before temperature and intensity; record color-space differences separately from pi factors.
   Run `LightAngularGpuTests`: use real packed records, transition windows 0.25/0.5/0.75, transformed lights and
   Point/Pyramid/Box controls. For the bake/direct-light comparison, set the task receiver's `_SpecularLevel` to zero
   and verify the material/GBuffer identity so realtime GGX is not counted as a diffuse intensity discrepancy.
   Record original failures separately. Read each generated lightmap's actual format, atlas ST and loaded mip before
   reading texels. If streaming prevents full-resolution analysis, only a task-owned generated lightmap may be imported
   with streaming disabled after bake/capture/readback stop; record its GUID and importer delta, keep global quality
   unchanged, and use the same configuration for both cases. This is an analysis configuration, not a streaming fix.
   Keep angular shape, absolute intensity scale and spatial registration as separate measurements. Compare actual
   realtime diffuse against fixed-parameter Lambert and integrate native baked irradiance against the same intensity;
   do not fit a brightness multiplier. Retain the original world-aligned pixel error and native atlas ST/centroid.
   A diagnostic position shift may explain registration, but must not replace original evidence or modify production UV.
7. Sun direction is unchanged (directional lights now store the emission axis; contact shadows, fog and clouds read
   `-forward`).
8. Run the pipeline global-state lifecycle tests: a pre-existing custom Lightmapper delegate is restored on disposal,
   independently of replacement feature flags; a later delegate replacement is preserved. Use task-owned sentinels,
   never a user's baking callback as the negative control.

## S7 — clustered light list v2

1. Compile; run `LightBinningTests`, `LightResourceLifetimeTests`, `LightRecordTests`. GlobalSettings → RuntimeShaders
   shows `Light Tile Mask Shader` filled (the old `Z Binning Shader` slot is gone).
2. `CreateLocalLightsValidationScene` (or any scene with many point / spot lights): lighting matches S6 in Deferred.
   Forward-route materials now receive local lights through the list: compare a Forward and a Deferred sphere side by
   side; they should match.
3. Put the camera inside a point light's range: the light still lights everything (it covers every tile). Lights
   behind the camera no longer light pixels in front (the old mirrored-tile bug).
   Also move a small light above and below the image center. Both influence regions must follow the light in
   Deferred and Forward. CPU rectangles use `GraphicsUtility.GetComputeGpuProjection`, matching the actual depth
   consumer; a self-consistent test that projects both its oracle and sphere with the old `true` matrix is insufficient.
4. Far scenes: local lights 100 m+ away no longer flicker on and off between bins.
5. Volumetric fog / clouds with local lights: same scattering as S6. Match the actual camera matrices and jitter.
   Clouds also sample `Time.frameCount & 15`; compare all 16 global phases with matching jittered inverse VP and
   history-reset state, including transmittance alpha. A camera cut resets history validity but does not restart
   the committed jitter counter. Preserve unmatched-phase captures as controls; do not fit exposure or overwrite a baseline.
6. Frame Debugger: `ComputeLightTileMask` replaces `ComputeZBinningLightList` (one dispatch); no ZBin overflow
   capture remains.
7. For a missing-light diagnosis, request `DeferredLighting`, `Lighting` and `LightTileMask` on normal Game frames.
   `DeferredLighting` is copied immediately after Deferred, before Forward/atmosphere. The mask capture is a
   diagnostic RGBA32F texture: each channel stores one exact byte (0–255), low to high, of a production uint word.
   Its width is tile-count-X × words-per-tile and its height is tile-count-Y; without local lights it is the existing
   one-word empty mask. Reconstruct `R | G<<8 | B<<16 | A<<24` and compare with the same frame's CPU rectangles
   and bin range. Its texture producer is the diagnostic conversion pass; its source is the actual shared light-list
   buffer. Check both with and without diagnostics because the extra reader extends the mask's graph lifetime.
   Exercise 0/1/31/32/33/63/64/65 lights, empty bins and nonzero directional offsets; each local record must be
   visited exactly once when its tile bit and bin range both include it. Shared consumers initialize `FLightLoop`
   via the caller-owned `out` argument; do not restore a return-by-value wrapper for any consumer.

## S8 — area light shapes

1. After a fresh script reload/cold startup, verify a live pipeline and initialized VolumeManager. On current main, where the S15 Volume panel is present, verify that the Rendering Debugger Volume panel is populated; repeated registration must not duplicate panels/widgets. Historical snapshots check only their available panels. Preserve the first startup error if any. Compile; run `LightShapeGpuTests` and `DeferredLightLoopGpuTests` (GPU). The weight case invokes the actual Deferred kernel with six fixed-input controls in RGBA32F and nonzero diffuse/specular terms.
2. Window/Infinity/Create Area Lights Fixture: on the glossy floor each light's highlight has the light's shape
   (round for the sphere, a streak for the line and tube, a rectangle / ellipse for rect and disk, a soft disk for the
   sun with Source Angle 2°). On the rough floor the highlights widen without becoming brighter than their sources.
3. Change Source Radius of the sphere from 0 to 0.5: the overall brightness on the rough floor stays nearly the same;
   the highlight on the glossy floor grows and its peak drops. Fix the ROI, exposure and tolerance before capture. Match current/previous camera jitter and actual matrices; compare peak and integrated energy separately. Verify roughness in GBufferA and specular level in GBufferB.
4. Rect lights now light diffuse surfaces (they only produced a highlight before) and do not light behind the emitter.
   Barn Door Length 0 → 0.4 at 45° narrows the lit area. Report the brightness threshold: the faint outer support may shrink while the bright core stays unchanged. For a backface control, keep the same enabled emitter and compare against both weights set to zero.
5. Expected global changes: brighter, unclamped specular highlights on glossy materials (bloom may react); punctual
   lights with a Diffuse or Specular weight below 1 now scale only that term. Keep production RGBA16F frame measurements separate from the fixed-input RGBA32F numeric oracle; multiple storage/composite stages prevent a final-readback-only half-ULP bound from proving every normal-frame equation.

## S9a — shadow settings, PCF / PCSS, shadows for every shape

1. Compile; run `LocalShadowFrustumTests`, `LightRecordTests` and `ShadowSamplingGpuTests`. The latter calls production
   PCSS with analytic sloped planes, a separate closer-plane negative control, local/directional projections,
   nonzero atlas rectangles with closer outside sentinels, and near/far half-plane penumbra profiles. Do not increase
   bias to hide a same-plane failure; gradient correction must use the actual sampled texel center. Local blocker
   search must cover the possible near-plane emitter footprint, rather than only its receiver-plane radius.
2. RP Asset → Lighting & Shadows: new Cascade Count, Cascade Blend Width and Shadow Filter Quality. Cascade Count 1–4
   changes the number of cascades in Frame Debugger (`RasterCascadeShadow`); with Blend Width 0.1 the cascade seams on a
   long floor are no longer visible as a hard line.
3. Shadow Filter Quality Low → Ultra: PCF edges grow from 3×3 to 7×7 texels. Custom shows editable fields; Copy Preset
   To Custom copies the current preset. Seed all seven Custom fields with different legal values before copying; native Undo must restore that complete struct and quality, and Redo must restore the copied state. Use a transient clone so source assets remain unchanged.
4. Light inspector → Shadow Filter = PCSS on the sun with Source Angle 2°: contact-hardening shadows (sharp at the
   contact, softer with distance). On a sphere point light, Source Radius 0 → 0.3 widens the penumbra.
5. Pyramid spot, box spot, rect and disk lights now cast shadows (they cast none before). Box spots give parallel
   shadows. A point light with Source Length > 0 (line / tube) still casts six-face shadows.
6. Expected changes: shadow edges are softer at Medium (5×5 tent replaces the 3×3 box); Hard keeps one-tap edges.
7. Run `ShadowCasterAllocationTests`. With native renderers disabled, a MeshComponent with Cast Shadow = Dynamic
   and a valid Shadow pass must allocate and populate cascade/local tiles. Compare native-only and mixed casters,
   including a custom caster outside native bounds and an offscreen caster upstream of a visible receiver. All Off,
   mismatched light/rendering/scene layers and Shadowmask-static exclusions must not allocate custom-only tiles.
8. Check cascade counts 1–4, orthographic and perspective cameras, translated/rotated lights, and near planes crossing
   the first two split distances. Valid native culling split indices must be contiguous even when atlas tile indices
   have gaps. Move a native/custom caster through the blend band; both draw paths must retain the shared caster.
9. Set the local shadow light budget to one. A higher-priority spot with only a caster behind its actual frustum
   must not displace a lower-priority spot with an eligible caster. Repeat for pyramid/box/planar shapes.

## S9b — VSM / EVSM

1. Compile; run `ShadowMomentGpuTests` and `ShadowMomentProductionGpuTests` (GPU). The production test executes the
   real FromDepth/H/V kernels: orthographic and perspective clear depth, every constant texel, bit-preserving constant
   blur, and a nonconstant offset tile against an independent double-precision Gaussian (one half-ULP bound per pass)
   with unchanged neighbours. For a scene-level numerical comparison also record exact depth/moment bindings; a
   rounded Frame Debugger screenshot alone does not supply a precise CPU oracle.
   The producer→RGBA16F→visibility cases must keep equal-depth surfaces lit and independent receivers behind them
   shadowed; a float-only moment helper does not cover storage precision. Receiver comparison accounts for the
   half-float mean's conservative representation bound, without changing the authored shadow bias or atlas format.
2. Sun Shadow Filter = EVSM: Frame Debugger shows `ComputeShadowMoments` after `RasterLocalShadow`, with one
   `MomentFromDepth` + two blur dispatches per cascade; Rendering Debugger → Frame Features lists ShadowMoments as
   Records. Confirm actual dispatches separately; the feature state alone does not prove production. Shadows are smooth; contact areas stay dark.
3. Switch to VSM: overlapping occluders at different depths show light bleeding (a lighter halo inside the far
   shadow); Shadow Filter Quality Low → Ultra lowers it. EVSM shows much less.
   Run `VsmPresets_HigherQualityDoesNotIncreaseMultiDepthLightBleeding` against the real half-float production chain.
   Use the same scene, exposure and unshadowed control for every preset; check finite visibility, clear regions,
   contact darkness and EVSM edges as well as the overlap. Presets hold blur radius at one texel and increase
   bleeding suppression; Custom still controls both parameters directly.
4. Set every light back to PCF: `ComputeShadowMoments` disappears from Frame Debugger.
5. A spot light with EVSM next to a point light with PCF: only the spot's tile is written in `LocalShadowMoments`.
6. Volumetric fog with a shadowed sun: compare normal frames with fog on/off and shadowing on/off. In ScatterDensity, verify `_CascadeShadowMapSize` uses the complete cascade atlas dimensions and their reciprocals, matching the bound texture. The pre-S9 binding used one cascade resolution and twice the correct texel size. An isolated old-binding control may reveal small local differences; do not infer a measured sharpness improvement from a difference image. Restore the control before final tests.

## S9c — cached local shadows

1. Compile; run `LocalShadowCacheTests` and `ShadowDepthCopyGpuTests` (actual depth-tile GPU round trip). Check nonzero source/destination offsets, different atlas sizes, every neighbour pixel and the independent legal R32 color-copy reference. Depth-only atlases use separate save/load Raster passes; no region `CopyTexture` is allowed. A green slot-state test alone does not prove cached depth contents.
2. A shadowed spot light → Shadow Update Mode = OnEnable. Rendering Debugger → Infinity Lighting: Cached Shadow Tiles 1,
   Cached Shadow Re-renders 1 on the first frame, then 0. Frame Debugger: `RasterCopyShadowDepthTiles` appears and the
   spot's `LocalShadowSlice` draw disappears from `RasterLocalShadow` on later frames.
3. Move a cube under the spot: its shadow stays where it was (cached). Move or rotate the light: one re-render.
   Disable and re-enable the Infinity component: one re-render.
4. OnDemand on a point light: six cached tiles; moving the light does not re-render; the inspector button
   Request Shadow Map Rendering re-renders all six once. Capture the six depth tiles, matrices, depth decode,
   inverse far and shadow origin together: moving the light, changing range or changing its shape before a request
   keeps the complete produced snapshot. Current light position/radiance still changes direct illumination. A request
   updates depth and sampling state together; changing a point to a spot takes one tile only after the request.
   Check PCF → EVSM on a hit: moments are produced from the cached depth with its cached decode, while filter choice
   remains current. Missing/mixed-generation cube faces invalidate the complete group; insufficient cache capacity
   renders the whole cube normally. Repeat after a failed/unqueued production and with two cameras in one frame.
   Run `LocalShadowCacheTests` and `ShadowSamplingGpuTests`, including the six production `SampleLocalShadow` origin
   cases. A zero Cached Shadow Re-renders counter with zero cached tiles does not mean there are no shadow draws.
5. Cached Shadow Atlas Width 0: every light renders every frame again (Cached Shadow Tiles 0).
6. Two Game views / Scene + Game: a stale tile is rendered once per frame, not once per camera.

## S9d — Shadowmask casters

1. Compile; run `ShadowmaskCasterFilterTests`.
2. Project Settings → Quality → Shadowmask Mode = Shadowmask; Lighting → Mixed Lighting = Shadowmask; a Mixed sun and a
   Mixed spot; a static cube (Contribute GI, lightmapped) and a dynamic sphere. Bake.
3. Frame Debugger → `RasterCascadeShadow` / `RasterLocalShadow`: the static cube is not drawn; the sphere is. On
   screen the cube's shadow still appears (from the shadowmask), the sphere's shadow is realtime.
4. Switch Shadowmask Mode to Distance Shadowmask: the cube is drawn into the shadow maps again.
5. Custom Mesh instances have no baked Renderer source. Test them independently: they remain realtime casters in both Shadowmask modes; only native lightmapped static renderers use Unity's baked-caster exclusion.
6. Disable the static cubes and then the dynamic spheres in both native and MeshComponent variants. No disabled
   geometry may remain in depth/lighting or realtime shadow atlases; baked static shadows remain in the receiver's
   shadowmask. Include enable → disable before the deferred registration drains, existing-instance re-enable and
   disabled Static ↔ Dynamic changes. Run `MeshComponentRegistrationTests`: final disabled state has zero instances,
   transforms/draws/world members; final enabled state has exactly one of each and the correct mobility membership.

## S9e — shadow debug views

1. Run `DebugViewModeTests`.
2. Rendering Debugger → Debug View = Cascade Shadow Atlas: a 2x2 grid (only the first Cascade Count tiles filled),
   casters bright, background black. Local Shadow Atlas: one tile per spot / rect / disk, six per point.
3. Cached Shadow Atlas with an OnEnable light: its tile is visible; with no cached light the view is magenta.
4. Shadow Moments with an EVSM sun: a blurred, half-resolution version of the cascade atlas; magenta without any
   VSM / EVSM light.

## S10 — screen-space traced shadows

1. Compile; run `ContactShadowAllocationTests`.
2. Contact Shadow Volume on, Max Distance 0.3, a sun with Contact Shadows on: small crevices and contact points get
   dark lines (as before). Quality Low → Ultra raises the sun steps 8 → 32; existing profiles stay Custom and now use
   Sun Num Steps 16 for the sun (it used Num Steps 8 before).
3. A point light with Contact Shadows on near a small object on a floor: the object's contact shadow toward the point
   light appears; turning the light's Contact Shadows off removes it. Frame Debugger: `ComputeContactShadow` writes
   `ContactShadowSun` and `ContactShadowMask`.
4. Turn Contact Shadows off on every light: `ComputeContactShadow` is no longer recorded.
5. A forward-route material (InfinityLit with the Forward surface route): it now receives contact shadows like the
   deferred floor.
6. More than 24 contact lights: the 24 largest on screen keep contact shadows. Confirm 30 actual collected records with varied tile areas; independently sort by area and record-index tie-break, then verify 24 unique bits and six unassigned records.
7. Capture the explicit normal-frame `ContactShadowSun` and `ContactShadowMask` outputs only when their producer records. Verify R8 visibility and raw R32 UInt bits against the actual producer frame, depth, matrices, light records and effective per-camera Volume settings. A feature label or dispatch alone does not prove pixel contents. Capture must not enable contact shadows or substitute an absent resource. Explicitly request each output with no producer: keep the Volume enabled but turn off all light contact flags for the sun output, then keep a light flag enabled and disable the Volume for the mask output. Expect the corresponding QueryTexture failure and zero outstanding readbacks; restore, then verify healthy normal frames.
8. Contact noise uses global `Time.frameCount`; matching camera Halton phases does not match this noise. Use each actual frame for the screen-depth ray oracle, and select a visible receiver ROI. A thick occluder can intersect a real ray but remain undetectable in the single visible depth layer with a short trace; use a thin occluder for the positive control and retain the thick one as a limitation control. Do not claim that a per-frame Low/Ultra pixel difference proves quality improvement.

## S11 — cookies and IES

1. Compile; run `LightCookieAtlasTests`.
2. A spot light with a 2D cookie (any RGB texture): the pattern is projected inside the cone, colored. An Alpha8 cookie
   (Unity's default cookie import) projects its alpha as gray. Rendering Debugger → Debug View = Light Cookie Atlas
   shows the texture in its block.
3. A point light with a Cubemap cookie: the pattern surrounds the light. On Unity 6000.6 a 2D cookie is cleared by the native setter and SerializedObject submission; verify a null cookie, no cookie record and no pattern. The Infinity Inspector warning for a retained wrong-type reference is conditional on Unity allowing that reference; do not claim that unreachable warning branch was tested.
4. A directional light with a cookie and Cookie Size (10, 10): the pattern tiles every 10 m on the ground.
5. A rect light with a cookie: glossy reflections of the rect show the texture; diffuse lighting takes its average
   color.
6. Import an .ies file (Assets → Import): the asset's prefab has an Infinity Additional Light Data with IES Profile set;
   drop it in the scene and compare with the profile's shape. Assign the Cube texture of the .ies asset to a spot light:
   the profile applies by direction.
7. Volumetric fog with a cookie spot: the shafts carry the pattern.
8. Change an owned RGB cookie from linear to sRGB in its TextureImporter. Record GUID, EntityID, updateCount, GraphicsFormat and full imageContentsHash before and after: reimport can preserve identity and updateCount. The atlas must upload once for the new contents, then reuse it on stable frames. Check `ComputeLightCookies` in the graph/Frame Debugger and verify the actual lighting changes to sRGB-decoded values; a completed capture alone is not a pass.
9. With two cameras sharing a cookie, first submit a red version, then change that same texture to green. Fail camera A after it records the new upload but before queue acceptance (an explicit capture of an absent producer covers this point). Preserve A's Failed result. Camera B in the same global frame must upload and sample green; its successful submission does not erase A's error. Verify a healthy 33-frame recovery, ordinary two-camera reuse, and two lights sharing one upload. Also cover committed v1 → queued v2 → unqueued failed v3 → request v1: the overwritten v1 block must upload again. Run `LightCookieTransactionTests`; no substitute texture or early production marker is allowed.

## S12 — baked GI

0. Window/Infinity → Append Missing Default Volume Components (adds Adaptive Probe Volumes Options).
1. Compile; run `BakedGITests`, `ShaderStrippingTests`, `DefaultVolumeOutputTests` and all eight `GBufferContractTests` GPU cases. Confirm the three silhouette ValueSource cases are actually discovered; a zero-match or interrupted run is not a pass. A readback timeout fails while retaining that fixture's queued resources until terminal callbacks; never replace this with `WaitAllRequests` or free in-flight targets. CoreRP APV Options remain active sampling settings while the RP Asset selects the probe system; their activity is not an effect enable switch. Confirm the stripper accepts native/system passes without shader-local keyword assertions. This Editor callback check does not verify Player stripping.
2. Shadows: with a cascade and a spot light, casters now appear in the shadow maps from the light's view (Debug View
   Cascade / Local Shadow Atlas). Compare the original pre-0.5 base, the pre-S12 endpoint and S12 with the same native/custom casters, fixed lights, camera poses and exposure. Re-enable task MeshComponents after any historical pipeline recreation before claiming matched caster coverage. Inspect the actual draw's `Matrix_ViewFlipYProj`; moving only the observer must not change a fixed spotlight's local atlas. Cascades fit the camera, so do not use cross-version cascade pixel equality as an oracle. Queue a task GPU matrix readback after normal camera rendering to check both aliases are restored for two distinct cameras; do not substitute a CPU `Shader.GetGlobalMatrix` read.
3. Lightmaps: Window/Infinity/Lighting/Use Atmosphere As Baking Sky, then Generate Lighting. Static InfinityLit objects
   get lightmaps; sky color in the bake matches the realtime sky (blue fill on the shadow side). Check
   Assets/InfinityBakingSky/<saved-scene-GUID>_BakingSkyFaces.asset in the Inspector preview: six faces, sky up on +Y.
   For an actual baked blue-fill control, bake a white cube/plane with a fixed baked white sun once with the atmosphere sky and once with a black environment. Match actual camera jitter matrices, reconstruct the shadowed floor from depth and compare normal-frame BakedDiffuse. Keep the two completed native bake receipts: identical output paths do not mean unchanged lightmap bytes. The scene must be saved first. Two saved scenes with the same name must use different output assets. Keep an unrelated task material dirty while running the command and check immediately that its disk bytes and dirty state are unchanged. Resize the task faces and refresh: GUID/local ID and material references must survive. Reject unsaved scenes and occupied output paths without overwriting assets. If the current atmosphere is unavailable or a scene uses another scene's material (including the earlier name-based output), baking must not proceed; explicitly run the command for that scene before retrying. Exercise the actual Generate Lighting / BakeAsync startup path and confirm it refuses to start or stops. Record whether the job had entered its running state: a helper exception alone does not prove native cancellation, and startup rejection is distinct from cancelling an already-running job.
4. Shadowmask: Mixed lights + Shadowmask: static shadows beyond the shadow distance come from the mask; Distance
   Shadowmask uses realtime shadows inside the distance. Use actual baked lightmap/mask resources and query their identities per control. Hold geometry fixed and independently switch only native `shadowCastingMode` or Mesh `castShadow`: Shadowmask must keep its realtime cascade unchanged for a static baked caster; DistanceShadowmask must show that caster's contribution. Match raster matrices/splits to exclude cascade-refit changes. Separately compare the receiving plane at all eight actual jitter phases: beyond the distance both modes agree; strength zero removes the baked shadow darkening. Object names containing "Custom" do not prove a MeshComponent draw path.
5. Terrain: mark the terrain Contribute GI and bake: the terrain receives lightmaps and casts realtime shadows
   (ShadowCaster pass). Check that the native bake does not remove it as an instance with no materials, its lightmap index is valid, and BaseMapShader / BaseMapGenShader dependencies resolve. Prove realtime casting with an actual ShadowCaster draw or normal-frame atlas on/off control. Static baked Terrain is excluded in Shadowmask, so use Distance Shadowmask or a realtime sun for this control. Caster toggles can change depth fitting: accept only cascades with identical raster/culling matrices, splits and bias, or explicitly decode the differing depth conventions. Compare a two-color layer blend near and beyond the terrain basemap distance; verify baked albedo against the same blend.
6. Legacy light probes: move a native Unity Renderer through a Probe Group and verify its Unity per-object lighting. Change task-owned probe data, complete tetrahedralization and verify the native result updates. Restore the task probe positions and drain completion before unloading. Custom Mesh has no legacy Probe interpolation, anchor or uploaded SH/occlusion record; it must not participate in this test. Validate its world-space APV path separately in step 8.
7. LPPV: a large dynamic renderer with a Light Probe Proxy Volume (Use Proxy Volume) shows a probe gradient across it.
8. APV: RP Asset → Light Probe System = Adaptive Probe Volumes; add an Adaptive Probe Volume, bake from Lighting →
   Adaptive Probe Volumes. Unlightmapped objects show the APV lighting; Rendering Debugger → Probe Volume shows the
   probes. Switch the band count L1 / L2.

## S13 — reflection probes

1. Compile; run `ReflectionProbeTests`, `ReflectionProbeTransactionTests`, `AtmosphereCacheTests`. Full-suite Present GPU tests use asynchronous draw-ordered readback: fixture0/1/2 retain the original1×1/17×13/1919×1079 sizes and all five transfer policies; verify all three run and their named GPU resources and retained owner set return to zero. Historical Category labels use the valid `Unverified_0_5` spelling so NUnit executes the test bodies. The GPU record parser strips HLSL comments before checking every field count/order/type and stride; comment prose with semicolons is not a declaration.
2. Before normal captures, disable Frame Debugger and verify `EditorApplication.isPaused == false`; disabling the debugger alone can leave Play paused. Wait for the replacement pipeline to own its asset before starting a capture; a cancelled/undrained session is not acceptance. Confirm the actual Game camera collects a visible baked/custom probe with `NeedsReflectionProbes`; a configured asset or probe object alone is not collection evidence. Sky IBL is unchanged: with no probe in the scene, glossy and rough spheres look as before (the sky prefilter moved
   to `Compute_CubeFilter`; Frame Debugger `ComputeAtmosphericLUT` dispatches `FilterArrayToArray` once per mip).
3. Add a Reflection Probe (Baked) around an indoor room; mark the intended room renderers Reflection Probe Static, then bake: glossy surfaces inside show the room instead of the
   sky; Frame Debugger shows `ComputeReflectionProbes` for one frame (and again after a re-bake), then
   `ComputeProbeTileMask` every frame.
4. Blend Distance 1 m: walking out of the probe, the reflection fades to the sky over 1 m. Box Projection on: a mirror
   floor's reflection lines up with the room walls.
5. Two overlapping probes: the higher Importance wins; at equal importance the smaller one wins inside it.
6. Add Infinity Additional Reflection Probe Data, Influence = Sphere: the influence becomes a sphere (radius = Sphere
   Radius, or the largest box half extent when 0).
7. A Realtime probe (Every Frame): a moving object appears in the reflection; `ComputeReflectionProbes` runs every
   frame once, also with two cameras.
8. A Forward-route material inside the probe reflects the same as a deferred one.
9. Reimport a task-owned cubemap from Linear to sRGB while preserving GUID and identity. Its sampling format can change without updateCount or image hash changing; the first normal frame must filter the new contents, then stable frames must reuse it.
   Run `CubemapArraySamplingGpuTests`: constant colors, asymmetric face gradients, all edges and corners, fractional
   LOD, the 1x1 mip and nonzero atlas slices must remain finite and continuous. The array sampler uses a symmetric
   three-face vertex convention; compare native Cube only when the effective filter footprint has no corner tap,
   and use the independent three-face/full-1x1 oracle elsewhere. Test both actual cube-filter input kernels as well.
   For image comparison, bind the same task-owned baked cubemaps, freeze marker rotation and match camera matrices.
   Compare original/candidate/repeat normal frames: Depth/GBuffer and source GUID/hashes stay identical, and cube-face
   boundaries on rough reflections disappear. Other dark regions require separate diagnosis; this is not a blanket
   acceptance of every black area or a performance claim.
10. RP Asset Max Reflection Probes = 0: probes disappear, only the sky remains; `ComputeReflectionProbes` is gone.
11. Commit a red custom probe, change its source to green, then fail camera A after filter recording but before queue acceptance. Camera B in the same frame must record `ComputeReflectionProbes` and its normal Lighting capture must be green. Preserve the intended A failure and drain all readbacks. Also verify accepted production deduplicates across cameras, a later unqueued update cannot revive an overwritten version, and uncertain queue acceptance invalidates contents at frame completion.

## S14 — SSGI on the SSR core

1. Compile; run `QualityPresetTests`, `StochasticSSGINumericTests` (GPU) and `StochasticSSRNumericTests` (SSR now uses
   the shared `ScreenSpaceTraceTiles.hlsl`; its results must not change). Run `SSGITemporalResponseGpuTests` for
   current-sample reset, premultiplied RGBA, valid EMA, invalid history and spatial normalization. These deterministic
   kernel controls do not replace the normal-frame visual gate. The 0.4.0 SSGI kernels are gone from
   `ScreenSpaceNumericContract_ProductionKernels`.
2. Screen Space Indirect Diffuse Volume: removed parameters `NumRays`, `SpatialRadius`, `SpatialSample`,
   `BilateralSample`, `BilateralColorWeight`, `BilateralDepthWeight`, `BilateralNormalWeight` (their values are
   dropped). Kept names: `MaxDistance`, `Thickness`, `IntensityScale`, `NumSteps` (now the Hi-Z budget, range 4–64),
   `TemporalWeight`, `TemporalScale` (now the variance-clip width). New: `quality`, `HalfResolution`,
   `SpatialPasses`. The removed ray-reuse controls `ResolveSamples` / `ResolveRadius` have no replacement or load-time migration: SSGI Trace samples accepted previous color directly. Preserve all other serialized parameters when explicitly resaving an existing Profile.
3. A room with a bright red wall and a white floor, SSGI enabled: the floor near the wall picks up red bounce light;
   Debug View **SSGI** shows it. Looking away from the wall (wall off screen) the bounce fades back to the baked / probe
   ambient instead of turning black.
4. Orbit slowly in normal rendering with Debug View None: after ~16 warmup frames there must be no persistent
   visible crawling noise, and newly revealed regions must visually recover within a few frames. Report ghosting
   behind moving objects. Low-amplitude numerical residuals may last longer; this is not a zero-stochastic-noise
   requirement. Capture final `DisplayColor`, actual matrices and Depth
   for moving-occluder and settled-reference sequences. Inspect both visible recovery and remaining numeric residuals;
   a noisy reference is not a noise-free truth. Raw SSGI Debug View and pre-TAA `SceneColor` are separate intermediate
   signals and cannot establish a final-display failure by themselves. `nativeDisplays` records attachment descriptors,
   not native framebuffer pixels. Drain all readbacks and restore camera/Volume state before changing fixtures.
5. Frame Debugger: `SSGI_Classify`, `SSGI_Trace` (skip clear and cosine trace), `SSGI_Temporal`,
   `SSGI_Spatial`, then `SSGIUpsample` reconstructs the half-resolution working signal at full resolution. `CopyHistorySSGI` accepts the filtered working radiance and geometric metadata. Validate all four quad phases and odd last-texel intervals with a nonconstant signal. SSR retains full-resolution Resolve/Temporal/Spatial with optional half-resolution Trace. Quality Low → Ultra trades noise for cost.

## S15a — Shader Graph target

1. Compile. The Shader Graph package compiles the `Editor/ShaderGraph` asmref into `Unity.ShaderGraph.Editor`; record
   any Console error from that assembly first.
2. Create → Shader Graph → (any) graph, Graph Settings → Active Targets → Infinity. Material: Lit, Unlit, Decal appear.
   Lit blocks: Base Color, Normal (Tangent Space), Metallic, Roughness, Specular Level, Emission (+ Alpha with clipping or
   a translucent stage). Save: the shader compiles without errors (Inspector of the graph asset → no error list).
3. A Lit graph with a texture into Base Color and a normal map, on a cube next to an InfinityLit cube with the same
   inputs: both look the same in Deferred; switch the material's Surface Route to Forward: still the same, Frame
   Debugger draws it in `RasterForwardPass`.
   After changing target-generation code, reimport the task graph and inspect its actual keyword space before frame
   acceptance; an already loaded graph can retain its prior generated shader across a script reload.
   Preserve a black-output control even when later frames recover. Confirm the current normal-frame result again after
   a cold restart without a preceding Frame Debugger/import/CompilePass sequence; a later healthy draw cannot identify
   the earlier state failure or retrospectively validate its captured pixels.
   Exercise native Lit, Instanced Lit through MeshComponent, and generated Lit Graph Forward materials with Contact Shadows off and on.
   With no contact producer, Metal must report no uint mask/default color texture mismatch. With the producer active,
   retain the sun and local contact result; run two Game cameras with opposite overrides in both depth orders.
4. Translucent Stage T1 on the material: refraction through the color pyramid; T0: fog applies; T2: plain blend. Motion
   vectors: Debug View Motion shows the moving object.
   Use a patterned opaque background and a tilted surface. Compare zero and nonzero Refraction Strength on both native
   Lit and generated Lit Graph; a recorded pyramid alone does not prove refraction. Inspect the actual T1 draw in Frame
   Debugger for `_REFRACTION_PYRAMID`. Compare T0 with fog on/off against the same T2 control, keeping opaque lighting
   and depth fixed. T2 must clear fog/aerial/refraction keywords after T0/T1; repeat toggles and opposite camera overrides
   to check state isolation. Read pre-temporal TAAInput as well as final output to distinguish history from shading.
5. Bake lighting with a static Lit-graph object: it receives the lightmap (Meta pass gives the right albedo / emission).
   For an emitting graph, explicitly set the material GI control to Baked; keep a None/black control and a native
   InfinityLit emitter with the same effective linear HDR emission, geometry, sky and bake settings. A Graph HDR color
   constant is linear; the native ShaderLab Color property stores a Gamma-space color, so matching stored numeric
   literals is not a matched shader input. Record both stored and effective values. Compare actual lightmaps and normal
   Baked Diffuse frames; a visible bright surface or a lightmap index alone does not prove emitted bounce. Inspector
   repaint must preserve existing GI choices, including None; Undo restores explicit changes.
6. Unlit graph: drawn in T2 with alpha. Decal graph on a Decal Projector: the projected texture shows on the GBuffer
   surfaces, its normal follows the projector.
7. A Scene Color node in a T1 graph samples the opaque scene; Scene Depth outside decal graphs returns Shader Graph's constant 1
   (known gap).

## S15b — Render Pipeline Wizard

1. Run `RenderPipelineWizardTests`.
2. Window/Infinity/Render Pipeline Wizard on the Example project: every Project check passes (green) except settings you
   changed on purpose. Set Color Space to Gamma → the check turns yellow, Fix restores Linear.
3. Remove one component from the default Volume profile → "Default Volume profile complete" fails; Window/Infinity/Append
   Missing Default Volume Components (or Fix) adds it back with packaged values, others unchanged; Undo removes it.
   Redo restores the concrete component type (including APV `ProbeVolumesOptions`), not only its serialized class-name
   text. Save/reimport must retain that type and a second completion must add zero. Run the persistent completion
   Undo/Redo contract in `RenderPipelineWizardTests`; record exact local IDs as invariant strings in JSON receipts.
4. Edit a light's native Type through the Debug Inspector → "Light shapes synced" fails; Fix restores it (Undo works).
5. Scene with Baked GI and the default skybox → "Baking sky" fails; Fix runs Use Atmosphere As Baking Sky.
6. Keep an unrelated task-owned native asset dirty while applying one Fix and Fix All: its bytes and dirty state stay
   unchanged. Profile completion and mixed-lighting fixes persist only their own edited asset; scene edits retain Undo.

## S15c — Frame Settings

1. Run `FrameSettingsTests`.
2. RP Asset → Frame Settings: all camera settings on; reflection probe captures have AO, RTAO, Contact Shadows, SSR and
   SSGI off. Existing assets show the same (new fields).
3. Camera → Custom Frame Settings → override Screen Space Reflection off: SSR disappears for that camera only
   (Frame Debugger has no `ComputeScreenSpaceReflection`); Rendering Debugger → Infinity Rendering → Frame Settings shows
   "Off (CameraOverride)".
4. Override Reflection Probes off: reflections fall back to the sky. Light Cookies off: cookie patterns vanish.
5. Bake a Reflection Probe with SSR enabled in the Volume: the probe capture does not record SSR (ReflectionDefault).

## S15d — Light Explorer and Lighting window

1. Window/Rendering/Light Explorer: tabs Lights, Infinity Lights, Reflection Probes, Infinity Probes, Light Probes,
   Volumes, Static Emissives. Editing Shadow Filter / Contact Shadows / Volume Priority in the table changes the object
   (Undo works); the Infinity Lights Shape column is read-only text (Sphere, Tube, Cone Spot, Rect, ...).
   Lights shows read-only Native Type and effective Shape; Tube, Pyramid and Box must display without unsupported-type
   errors. Edit Type/Shape through the Light inspector so authored and native fields share one Undo transaction.
   Leave Lights open while closing a task-owned scene containing those lights: stale rows must disappear without
   NullReferenceException or GUILayout errors. Reopen the scene and verify that all rows return.
   In Volumes, toggle Global and edit Priority, then Undo/Redo each change. All columns must continue drawing without
   a null SerializedProperty error; Global uses CoreRP's serialized `m_IsGlobal` field.
2. Window/Rendering/Lighting → Environment: Atmosphere Profile edits the current effective RP Asset.
   If the scene already uses an Infinity baking sky, Baking Sky shows "Atmosphere (in sync on every bake)" and the
   conversion button is absent. Otherwise it shows "Scene skybox (<name or none>)" and Use Atmosphere As Baking Sky.
   Exercise both initial states on task-owned assets; explicit conversion changes the label to Atmosphere. The label
   identifies the sky material type; verify actual baking output separately from this UI state.

## S15e — Inspectors and handles

1. Run `DecalProjectionTests`.
2. Select a point light with Source Radius 0.2 and Source Length 1: the Scene view shows the range sphere, the source
   sphere and the tube line with an end handle; dragging changes the inspector values, Undo restores them, and the native
   Light (Debug Inspector) follows (Type Tube, Area Size x = length).
3. Spot cone / pyramid: outline at the range with a rim handle for the outer angle; rect: a size box; disk: radius handle.
   Start the disk at Source Radius 0 and drag its center handle outward, then repeat from a nonzero radius. Both edits
   update the native area size; Undo/Redo restores the authored radius and native fields together.
4. Light inspector Additional Properties shows soft source radius / angle, barn doors, shadow near plane and biases,
   weights and distance fade; off hides them.
   Select a Point and Directional light together: General shows a mixed Type, Shape is hidden, and the following
   sections draw without a GUILayout error. Select each separately: Shape reappears with the corresponding fields.
5. Decal: drag a face of the box handle; the opposite face stays. UV Scale 2 tiles the texture; Start / End Angle Fade
   30 / 60 removes the decal from vertical walls under a downward projector while retaining the horizontal floor.
   Compare against a no-decal frame: pixels outside the projector and fully clipped walls preserve the receiver
   GBuffer. Use a nonsymmetric camera and projector placement to expose Y-mirrored depth reconstruction. Run
   `DBufferCompositeGpuTests` for zero, half, full and fractional coverage plus two ordered overlapping decals;
   DBuffer RGB already contains coverage, so it must not be multiplied by alpha again when applied.
   Capture DBufferA/B/C to check actual MRT coverage and premultiplied values, including overlapping and partially
   faded decals. Run two Game cameras with opposite Decals overrides in both depth orders; compare GBuffer and
   Lighting for order independence and verify that only the enabled camera records RasterDBuffer.
6. Reflection probe with Infinity data Influence = Sphere: a sphere handle edits Sphere Radius.

## S15f — Rendering Debugger panels and Draw Modes

1. Run `DebugViewCatalogTests` and `DebugViewModeTests`.
2. Window/Analysis/Rendering Debugger: panels Infinity Material, Infinity Lighting, Infinity Screen Space, Infinity
   Rendering, Volume, Infinity Timings, Infinity Mesh. Picking a view in one panel resets the others to None.
3. Material: Metallic / Specular are grayscale; Shading Model shows forward-route objects blue, subsurface orange,
   default lit gray, sky black; Rendering Layers colors objects by their layer mask.
4. Lighting: Light Complexity is dark where no local light reaches the tile and warms toward red at Complexity Range;
   it changes with depth along a wall crossing a light's range (z-bins). Reflection Probe Complexity counts probes.
   Baked Diffuse shows lightmap / probe irradiance; Indirect Diffuse and Indirect Specular show the deferred terms
   (forward pixels black). Reflection Probe Atlas shows six faces; Reflection Probe Index switches probes. Contact
   Shadows shows the sun trace (white = lit). Missing producers show magenta.
5. Volume: select the Game camera and a component (e.g. Exposure); the table lists each influencing volume.
   Run `VolumeCameraSelector_TracksEnabledInfinityGameCamera`: CoreRP's actual selector must include enabled Infinity
   Game cameras and remove/reinclude them on disable/reenable. A menu listing only SceneCamera fails this gate.
6. Timings: Record Pass Timings on; the Passes foldout fills with GPU / CPU ms (previous frame, summed over cameras).
7. Scene view Draw Mode menu: sections Infinity Material / Lighting / Screen Space / Rendering. Choosing one changes
   only that Scene view (the Game view keeps the debugger's view); Shaded returns it to the debugger's view. Shadow
   Cascades, Overdraw, Mipmaps and the Deferred modes are disabled.
8. Bake a Reflection Probe with a debug view active: the probe stays normally shaded.
   Preserve and verify the original global lightmap array and mode around temporary additive Editor scenes. In the
   tested 6000.6.0f1 Editor, creating an empty additive scene clears the global array even after making the original
   scene active again. Restore the captured references before baking and after closing the temporary scene; check
   the original scene remains clean and its LightingData/texture identities are unchanged. Do not treat a renderer's
   lightmap index alone as proof that its lightmap is bound, or silently save/rebake assets to repair a fixture.

## S15g — Render Graph Viewer

1. Run `RenderGraphViewerTests`. Its compiled-snapshot control stops at publication before any pass executes and
   preserves the previous debug session; this is not a real GPU submission or native Viewer interaction check.
2. Window/Analysis/Infinity Render Graph Viewer with the Game view playing: the camera popup lists the Game and Scene
   cameras; columns show every pass (orange = async compute such as the light tile mask, gray = culled). Hovering a
   header names the pass; clicking shows its queue, wait / signal passes and resource lists.
3. Rows: GBufferA/B/C are written by RasterGBuffer and read by DeferredShading; the blue band spans from the
   allocating pass to the releasing pass. Imported rows (•) such as history textures have no band.
   Toggle Imported and select an imported row: read/write cells remain visible while pool allocation/release are absent.
4. Turn SSR off in the Volume: its passes disappear (not recorded). Pause freezes the capture; Copy puts the graph as
   text on the clipboard.
5. Close the window: no further capture (no allocations from the viewer in the Profiler).

## S15h — Material conversion and create menus

1. Run `MaterialUpgraderTests`. Color preservation compares exactly with an independent target Material's native
   setter/getter round trip from the source's stored color; do not use literal float-bit equality or infer an image pass.
2. Import a few Standard materials (e.g. from an Asset Store pack) and URP Lit materials if available; Window/Infinity/
   Material Conversion Wizard → Scan Project lists them with their source shader and dropped maps. Convert Selected:
   the materials switch to InfinityLit with the same color, map and tiling; glossy stays glossy (roughness = 1 −
   smoothness); a Standard Fade material becomes translucent T0. Undo restores the source shader. Keep an unrelated
   task-owned native asset dirty: conversion persists only converted materials, preserves GUIDs, and leaves that
   asset's bytes and dirty state unchanged. Run the conversion save-boundary/Undo test in `MaterialUpgraderTests`.
3. Edit/Rendering/Materials/Convert Selected Built-In Materials to Current SRP converts with the same result.
4. Render Pipeline Wizard: a scene with a Standard material fails "Scene materials use Infinity shaders"; Fix opens the
   conversion window with those materials.
5. GameObject/Light/Infinity: each entry creates the named shape (Inspector Shape section agrees; Tube shows a line
   handle). GameObject/Rendering/Infinity: Global / Local Volume, Decal Projector (projects a gray box; the material
   `Assets/InfinityDefaultDecal.mat` is created once), Reflection Probe (Box / Sphere), Light Probe Group. Undo removes
   each object.
6. Assets/Create/Rendering/Infinity/Volume Profile (Defaults) creates a profile with Exposure, Tonemap, Color Grading
   and the optional components; Assets/Create/Shader Graph/Infinity/Lit Shader Graph opens with Base Color, Normal,
   Metallic, Roughness, Specular Level and Emission blocks.

## S15i — Validation fixtures

For Decal lifecycle recovery, leave enabled projectors in a task-owned loaded scene, replace the RP with a memory-only
clone carrying identical settings, and wait for the new pipeline to render. The colored DBuffer overlays must remain
visible without toggling components or reopening the scene. Verify disable/enable and scene unload afterward, then
restore the original RP binding without saving user assets. Run `DecalContextRegistrationTests` for pending enable,
pending disable, rapid toggles, context replacement and unload. Keep script-reload-cancelled captures separate from
completed evidence.

Newly created fixtures keep the rotating liveness cube inside a small viewport corner and the camera clip range.
Check that it remains visible and moves in normal Game frames, including a narrow/orthographic camera and a larger
near plane. Existing saved markers are not moved on load. For fixed-image feature comparisons, freeze only the
task-owned marker's rotation in the Play copy, retain its Renderer, and restore its prior rotation/enabled state
afterward. Do not compare different rotating-marker poses as if their depth or temporal histories were identical.
Metal raw readbacks use texture row order; apply the recorded Present scale/bias when making window-oriented preview
images. Keep raw values unchanged for numerical comparisons.

Each menu creator replaces `Assets/Scene/Validation/Validation_<Name>.unity` and its asset folder; run it once after
updating, then use the fixture for the stage it names. For verification beside existing user fixtures, invoke the same
internal `CreateInDirectory` logic with a new task-owned Assets subdirectory; record that path explicitly. Confirm a clean
current scene and preserve its setup before the Single-scene switch. Save only the generated scene and fixture-owned
assets. Unity SaveScene can implicitly save unrelated dirty native assets, so fixture Save rejects those edits before
calling it; the refusal must leave their bytes and dirty state intact. Resolve only explicitly intended edits before retrying.
Run `ValidationFixtureSaveTests` through UTF EditMode (its isolated bootstrap scene), then verify the outer saved scene
was restored; do not directly invoke that integration test on a user scene.

1. Create Shadows Fixture: five poles under spots labeled by filter. Hard has aliased edges; PCF smooth constant
   width; PCSS sharp at the pole base and wider at the tip; VSM / EVSM smooth with possible faint light bleeding where
   shadows overlap. Sphere Light PCSS casts soft cube-map shadows; Spot OnDemand renders once (move the box: the shadow
   stays until `RequestShadowMapRendering`). Pebbles show contact shadows from the sun and the Contact Point light;
   disabling ContactShadow in the Volume removes them.
   The generated profile sets Thickness to 0.03 m: at the fixture's 10-12 m eye depths, 0.01 m is below the trace's
   relative bias and cannot demonstrate pebble grounding. Keep that authored value for this fixture; do not change
   the runtime default. Compare matching camera jitter/Depth and inspect Sun/local masks beside each pebble.
   When capturing `CachedShadow` on a cache-hit frame, verify `sourceQueue=External` and an `Imported:` producer:
   those contents were produced before this graph. A save frame reports its actual writer instead.
   Check `producerFrame=-1` (unknown original production time) and the current `recordedFrame`; a cache-hit capture
   does not identify the original save frame. Imports must retain the cache's authoritative texture descriptor.
   Run `CaptureProvenance_DistinguishesPersistentImportFromMissingProducer`; graph-owned textures without a writer must
   still fail. Do not interpret an external import as a current-frame shadow re-render.
   Unallocated cache slots have undefined contents. A full-atlas diagnostic may correctly fail its finite-data gate;
   retain that failure, analyze only a valid slot identified by its light/face/scene-mask entry and sampling production,
   and obtain a separate successful normal-frame capture. Never derive valid slots from the finite-pixel bounds.
2. Create Cookies Fixture: checker on the spot and (stretched 2:1) on the pyramid; tiled 4 m checker from the sun;
   colored stripes around the point light; rings from the IES light (Console warns if the IES import produced
   nothing).
3. Create Reflection Probes Fixture: bake Probe Left (Lighting window or the probe inspector). Spheres reflect red on
   the left and blue on the right, blending over 1 m; the middle sphere shows the sphere probe; roughness ramps blur.
   Debug View Reflection Probe Complexity counts 1–3.
4. Create Baked Lighting Fixture: Generate Lighting. Red / green bounce on the floor and pillar, the sun patch through
   the slot with shadowmask shadows, the dynamic sphere tinted by probes, the capsule lit per LPPV cell, the baked
   reflection probe on glossy surfaces. Debug View Baked Diffuse shows the lightmap.
5. Create Screen Space Fixture: the mirror strip reflects the blocks (SSR Tile Class: mirror tiles one class, rough
   strip another); SSGI tints the white walls near the blocks and the emissive bar; SSR Confidence falls at screen
   edges.

## S16 — tests, image regression, documentation

1. Run the whole EditMode suite; new in S16: `ShadowBudgetTests`, `CubeFilterGpuTests`, `LightTileMaskGpuTests`,
   `ImageComparisonTests`. Report every failure of the `Unverified_0_5` category with its message.
2. Create each fixture explicitly and inspect its normal Game-camera output first. Record Baselines never
   creates or replaces fixture source assets. The menu uses the eight `Assets/Scene/Validation/Validation_*.unity`
   scenes; validation can pass explicit task-owned fixture paths to the same internal runner. Save original scenes
   first, leave Play mode and Frame Debugger, and select Debug View None. Capture uses 640×400 DisplayColor,
   90 warm-up frames, one normal submitted frame and a full-size ROI; markers keep their renderer while their
   Play-copy pose is fixed. No `Camera.Render()` is used.
3. Record Baselines writes immutable SHA256-named `.bin` payloads and atomically commits each case's `.json`
   metadata under `ImageBaselines/<API>/`. Old metadata/payload remain valid if the new metadata commit fails.
   Run Comparison must report exactly eight **Pass** results and outcome **Passed**; capture completion alone,
   `NoBaseline`, `Error`, invalid dimensions/lengths and non-finite pixels never count as Pass. Compare the recorded
   format, dimensions, color semantics and SHA, without applying an extra transfer to DisplayColor.
   Increase one task-owned light's intensity: the corresponding case must **Fail**. Restore it and require Pass
   again without recording replacement baselines. Also exercise missing/corrupt baseline, cancellation with
   outstanding readback, state/metadata write failures and Play reload. Cancellation retires the runner's own
   capture before leaving Play; an independent capture is never cancelled. Original SceneSetup and Selection
   are restored after retirement; unexpected unsaved edits stop recovery as **NeedsAttention**. Cancel retries
   recovery after those edits are resolved. State/report and captures live only under the project's
   `intermediate/image-regression/<run>/` (or an explicit task-owned intermediate directory). Report outcome,
   exact case count and individual verdicts, not the final Console message alone. Eight cases do not cover
   Baked GI, APV/LPPV or Shader Graph; retain their separate stage evidence and unresolved image defects.
4. Documentation~ links resolve (index → every page); the pages match what you see in the inspectors.

## S17 — folder layout

1. Pull and let Unity reimport: no "missing script" warnings on Volume profiles, cameras or lights (the moved scripts
   keep their GUIDs). The default Volume profile and every fixture profile still show their components.
2. Compile; run the full EditMode suite. `NativeViewMotionHistoryTests`, `ScreenSpaceDenoiseTests` and
   `PipelineSeamTests` read canonical pass paths. The seam test includes Geometry/DepthPass, GBufferPass and
   MotionPass, Lighting/ForwardPass, and Shadow/CascadeShadowPass and LocalShadowPass. Keep every file-existence
   and forbidden-token assertion; no recursive search or old-path fallback. Check actual GUID-to-path bindings,
   default/fixture Volume components and scene Missing Scripts. The original move preserves57script/meta pairs;
   sixteen new folder meta and removal of Runtime/PostProcess.meta are separate from script GUID preservation.
   LocalLights must retain two components (Fog and Cloud, both inactive), and Translucent one Fog component
   (density0.04, height8, distance64, feature enablefalse) after disk reimport. The early creators explicitly add
   their components as Profile subassets. Check persistent file membership, GUID/local ID and independently
   imported asset bytes; IsSubAsset alone can return false in Unity6000.6. Existing profiles are not migrated on
   load. Repair only explicitly owned profiles with byte backups, stable GUID/references, a byte-stable second
   save and a separate no-op check; keep accepted image baselines unchanged.
3. Render any fixture: identical to the S16 image (Image Regression → Run Comparison passes against S16 baselines).

Final verified-marker note (2026-10-23): historical snapshots can retain `Unverified_0_5` categories. The validated main test contracts have had their executed markers retired. A zero-match category run is still a failure to select tests, never a pass; use the stage classes or exact names recorded in the stage receipts, then the full suite. Marker retirement changes metadata only and does not upgrade unrelated image/performance cells.

## Approved remaining-acceptance result (2026-10-23)

The closure report in `Documentation~/Evidence/Verification-0.5-2026-10-19/Closure-20261023/` records the approved
S1/SDK scope adjustment, 625/625 current Infinity tests, eight unchanged-baseline comparisons, three independent cold
first/stable pairs and seven composite performance loads. The old full-Example historical instruction above remains
historical; it is not an extra requirement of this approved closure. Retain original failures and reasons.

For the probe cold check, arm capture before the first Game submission of a newly created RP in each independently
started Editor. Record accepted Game count zero, necessary Reflection six-face preproduction, first capture index1
and stable index121, history reset true/false, actual Present/null target, finite buffers and zero pending readbacks.
Do not relabel this as the absolute first render before any Reflection preparation. Three current successes classify
an unreproduced historical transient without proving its root cause fixed.

For performance, use fixed1280×720 High, independent SSR/SSGI loads, accepted Game warmup120 and3×300 unique valid
CPU/GPU timestamps. Preserve all diagnostics, invalid samples and observation state; compare like targets. A fixed
RenderTexture omits final backbuffer Present/overlay; whole-Editor timing is not pass cost. A repeated probe offset
requires same-code observation-state controls before attributing it to a code regression. A repaired missing DBuffer
adds real work and is not an equal-image speed comparison. APV/lightmap/bake-process cost remains separate from baked
environment input sampling. See performance-report.md for exact exclusions and retained original groups.

## Output convergence verification

Use bounded normal-frame captures and exact camera/viewport identities. Compare direct
and linear-intermediate output with SDR UI, HDR transfer policies on controlled RTs,
partial viewports, multiple cameras, post bypass and DebugView. Verify no encoded
target is exposed as DisplayColor. Validate TAA reactive initialization with zero draws
and T0/T1/T2 layers, then switch to SR and confirm the attachment/output is absent.
Verify Editor overlays request display depth only when recorded; exercise cached-shadow
first save, cache hit, explicit invalidation and neighboring tile preservation. Record
GPU timing only if the counters provide positive, credible samples.


## Capacity, fused TAA and MeshDraw verification

Use a fixed camera and frozen animation for baseline/candidate captures. Drive
bounded size sequences within a bucket, across growth boundaries, through
landscape/portrait and back to a smaller stable size. Observe accepted-request
capacity, pool allocation/reuse/eviction estimates and pending retirement; repeated
size cycles must stabilize after warmup. Exercise Main, SceneView, Preview, SR,
RenderTexture, UI, DebugView and camera disposal without changing saved assets.
Poison unused texture regions; validate valid edges and fractional/odd mip samples
against exact-sized textures. Test rollback without advancing extent history.
Run TextureCapacityTests, TemporalResolveGpuTests, MeshDrawBatchGpuTests,
RGDrawListLifetimeTests, MeshSceneTests and related graph/history/output contracts.
Read accepted history before requesting a resized pending target; roll back and
verify that its old logical/physical view survives. Also change only capacity and
verify that history remains valid. Capture a zero-warmup resize frame to prove
current and previous dimensions remain separate.
Verify one fused TAA dispatch and unsharpened pending history. Compare both history
and sharpened display with the test-only two-stage reference at half precision.
Verify shared live DrawLists prepare once, dead lists do not prepare, CPU Direct
has no Compact producer, and later GPU batches preserve earlier output ranges.
Inspect normal frames and graph resource dependencies; report incomplete debugger
exports separately. Use 32 warmup plus 120 timing samples per representative path,
and report GPU timing only when actually available. Stop/drain all producers,
restore the owned Editor and remove task-generated data at completion.


### Mesh profiling domain and hierarchy checks

Run MeshDrawProfilingTests, RGDrawListLifetimeTests, CompactContractTests and
PipelineSeamTests. Verify normal/SceneView/Preview draws use RenderLoop.* and
cascade/local shadow draws use Shadows.*. All views share exactly three cached
preparation samplers: ComputeMeshCulling, ComputeMeshCompaction and UploadMeshData.
Timings lists each once; shared sampler identity must not merge per-view nodes.
In a task-owned Editor with actual native and Mesh draws, inspect
CPU Direct, GPU Indirect and Auto frames. Raster scopes contain native SRPBatcher
and DrawMeshDirect/Indirect as siblings, with no RendererList wrapper and no
compute/upload preparation inside Raster. Preserve shadow Slice/Face and Unity
(RP ...) labels. Inspect shared and culled DrawLists for one/zero preparation.
Do not infer GPU speedups from a naming change. Restore the owned scene/settings
and delete the task intermediate directory after capture and producer drain.

### Metal Shader initialization diagnostics

Use a task-owned Editor and a fresh log mark. Reimport the affected SSGI, SSR,
TAA, color-pyramid, atmosphere and deferred compute shaders, then execute their
production kernels; import/FindKernel can report no diagnostics before actual
Metal compilation. Read public ShaderUtil compute diagnostics after dispatch.
Run ShaderInitializationGpuTests, StochasticSSGINumericTests,
StochasticSSRNumericTests, TemporalResolveGpuTests, TextureCapacityTests,
LightShapeGpuTests, ShadowSamplingGpuTests, CubemapArraySamplingGpuTests and
SSGITemporalResponseGpuTests. Include TAA Main/MainDebug and the consumed local
keyword variants. Verify normal-frame color/depth are finite with active SSR/SSGI,
sky, deferred light and color-pyramid producers. Check the new log separately
from superseded test/import failures. A CPU-double oracle permits documented
Metal transcendental precision bounds; it never substitutes for GPU execution.
Do not delete the original project's Library/ShaderCache or operate other Editors.


### Current Example warning acceptance

For full-chain warning repairs, record the actual Editor/project identity,
source-scene dirty state, camera/quality, effective Volumes and source-asset
hashes before testing. An explicitly authorized existing Editor stays open and
retains its original Play/pause state. Never replace its current chain with a
small fixture and call that Example acceptance.

Inventory public GetComputeShaderMessages and GetShaderMessages after real
production dispatch/draw, including loaded hand shaders and used Shader Graphs.
Preserve existing Console history; compare a fresh log byte range. Check
requested/supported/produced for every current camera feature, graph consumers,
normal color/depth and temporal resource finite values. A background repaint
can submit rendering while Time.frameCount stays unchanged; newly created
runtime cameras also require a real PlayerLoop. Verify frame progression.

Run ShadowSamplingGpuTests, LightCookieProductionGpuTests,
DeferredLightLoopGpuTests, CloudShellGpuTests, ShadowMomentProductionGpuTests,
GBufferContractTests, ShaderInitializationGpuTests and BakedGITests. Cover
odd-sized Deferred AO/contact combinations, 2D/cube cookie channels, VSM/EVSM
and degenerate receiver planes. An unsupported readable integer Texture2D is a
fixture failure, not production evidence; use the actual integer RenderTexture
layout for contact masks.

Optional paths can use runtime-only additive content while the original full
scene stays loaded. Verify they really produce; a below-ground cloud camera
only covers rejection. A separate owned high-altitude camera covers nonempty
integration without moving the original. The normal capture interface accepts
2D resources only: inspect accepted 3D fog history read-only via HistoryCache,
using AsyncGPUReadback.GetData per layer and the logical extent within capacity.
Do not broaden capture APIs to hide a mismatched request.

After drain, remove owned transient content and task data, verify asset hashes
and original camera/settings/scene state, and report the precise loaded-variant
scope. Separate Metal from D3D12/Vulkan/HDR/APV/unused Graph limits. Keep old
attempt failures and infrastructure errors distinct from final passing receipts.

### Resource-pool recording and submission intervals

Run `ResourcePoolLifecycleTests`, `GpuSceneSubmissionTests`, the RenderGraph lifecycle/queue tests and Mesh consumers on the same candidate. Imports must protect physical allocations before Execute. Test buffer/texture return without an explicit fence, two independent camera recordings, cancellation of each recording, and disposal before cancellation.

The GPU fixture queues the first camera without Submit, updates dirty GPU Scene tables (including growth after a partially queued failure), then queues a second camera. Probe the returned old descriptor through the CPU acquisition path and verify both camera results by readback. Record whether the completion fence is actually incomplete immediately after Submit; CPU writes must remain prohibited until completion. A separate fixture disposes the pool while submitted work is incomplete and checks that physical destruction happens only after completion.

Also update dirty tables after the first camera is recorded but before Execute; both cameras must retain their own values. Return and grow a Mesh indirect payload while an old buffer is still imported; the old physical buffer must survive until the recording is cancelled or its submission completes. Run `FrameSubmissionTests` to distinguish final-fence queue failure, native Submit failure, and a post-Submit observer exception. A completion fence that failed to queue must never acknowledge resource use.

Confirm that same-queue command reuse still aliases compatible storage. Opposite-queue reuse requires a genuinely queued fence. A GPU wait never permits immediate CPU SetData. Completing batch B must not acknowledge unsubmitted batch A; camera failure within one frame must not silently create a new submission batch. Unknown native Submit failure stays quarantined unless its own submission/cancellation is proven; a later frame is insufficient. Known submitted work awaiting an async join may be acknowledged only after recovery joins it and the recovering submission succeeds.

Metal topology/state tests do not prove hardware async execution. Keep the whole lifecycle gate open until actual cross-queue and native failure recovery are covered on supporting APIs; report observed submitted-but-incomplete and disposal results separately.

### Main-thread, GC, compact surface history and native HDR convergence

Use one owned full Scene_Spazon mirror and APFS Library. Freeze the camera, every original local pose and animation state for matched comparisons. Run 32 warmup and 120 samples at 1k/10k, 0/1/100% motion and CPU/GPU policy; separately stress 50k stationary and moving content. Report Proxy transform/probe/apply sections and complete library Render scope as CPU time, never isolated GPU time.

Calibrate GC measurement with a known allocation. On this Unity Mono runtime `GC.GetAllocatedBytesForCurrentThread` returns zero even for a 1 MiB allocation and is not an acceptance counter. Unity's GC.Alloc ProfilerRecorder reports marker time and allocation **count**, not byte size. A zero count proves zero bytes in that measured main-thread scope; a nonzero count must be investigated and cannot be printed as zero bytes. Keep observer/eval/Viewer allocations outside the measured scope.

Run generation-lease copy/expiry/rollback, registration interleaving, shared route notifications and parent-transform cases. Re-run graph/pool delayed-submit/cancel/CPU-write tests after staging reuse changes. Do not broaden these Metal checks into proof of real hardware cross-queue execution or native driver Submit failure recovery.

Record physical texture IDs, full physical descriptors and logical aliases for ResourcePool, HistoryCache and other persistent storage. Re-run 2485580 using identical scene/assets/effects; preserve the frozen current profile instead of counting feature removal as memory savings. Capture production metadata through a diagnostic GPU decode because Metal AsyncGPUReadback cannot directly read RG32UInt. Verify exact raw-depth bits, age 0..64, oct normal error <=0.1 degrees, sparse/empty/odd tile bins and all quad phases. Verify Trace snapshots are taken before aliased Spatial overwrites.

For native HDR, enable support only in the owned mirror, request mode and wait for available+active with no pending transition. Record actual format/gamut/paper white and the native attachment from a normal frame. Native linear Rec.2020 must use its own encoding; a float RT or HDR availability flag does not prove activation. Check highlight/gray/color ramps, exposure and UI on the actual XDR screen, then restore mode/automatic tonemapping/settings. Separately report numeric RT evidence and visible presentation; screenshots do not measure display luminance.

After drain, restore Play/pause, scenes/camera/Asset/Volume/HDR, remove transient Graphs and fixtures, close only the owned Editor, unmount its exact APFS image and delete the task directory. Verify source asset hashes and absence of task paths; retain only a compact report.

## Unity integration ownership acceptance

Use one task-owned full Example mirror. Native Renderer and custom Mesh paths must remain independent through creation, enable/disable, scene changes and pipeline recreation. Check NativeLightingContractTests, RayTracingInputContractTests, MeshComponentRegistrationTests, RGRendererListLifetimeTests, native motion, resource lifecycle and GlobalSettings/Volume suites before normal-frame acceptance.

Record a full Scene_Spazon baseline and candidate with identical camera/quality. Native Lightmap/Directional/Shadowmask output must remain valid after actual Bake, scene reopen and Play transitions with and without Domain Reload. Custom Mesh has no baked/legacy Probe data; validate world-position APV separately and document the intentional removal of per-instance SH. Include native/custom shared-Mesh ray tracing instances, moving native renderers, inactive roots and persistent-scene content.

Inspect graph/draw provenance and finite GPU readbacks; import generated Lit/Unlit/Decal graphs. Run bounded zero-GC checks after warmup, and keep real Async/native fault/HDR platform limits distinct. The audit inventory is a file coverage receipt, not evidence of GPU behavior. Restore settings, drain all producers and remove the owned mirror/cache.
