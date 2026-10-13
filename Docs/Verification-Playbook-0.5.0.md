# 0.5.0 verification playbook

0.5.0 was written without a Unity Editor. Every stage passed the offline gate (differential C# compile against
UnityCsReference + CoreRP sources, DXC shader compile, `Tools/StaticCheck/static_check.py`), but nothing has been
compiled by Unity, rendered, or measured. Verify the stages **in commit order**: each stage's first commit subject
starts with `SNN:`.

```
git log --reverse --format='%h %s' origin/main..claude/infinityrp-0.5
```

## How to verify one stage

1. Check out the last commit of the stage.
2. Open the Example project and wait for the import to finish. Record the first Console error, if any.
3. Run the stage's steps below.
4. Fill in the report template at the end and send it back.

If a stage fails to compile, stop there: later stages build on it.

## Report template

```
Stage: SNN   Commit: <hash>   Unity: 6000.6.x   Platform: macOS Metal / Windows D3D12 / ...
Compile: OK | first error (file:line + message)
EditMode: <passed>/<total>; failures (test name + message)
Images: scene → OK | problem (screenshot)
Notes:
```

## S0 — offline gate

Nothing to verify in the Editor. Optional: `python3 Tools/StaticCheck/static_check.py --repo .` prints 0 errors.

## S1 — documents and hygiene

1. Compile. The deleted tools in `Editor/Tools/Validation` had no entry points; any compile error there is a missed consumer.
2. Add a Camera to a scene: `InfinityAdditionalCameraData` appears, and Undo removes both.
3. A FilmTonemap Volume component shows mode **ACES** where it showed **Film**; images are unchanged.
4. Rendering Debugger → Infinity Temporal panel is gone; other panels are unchanged.
5. Optional Player build: the Console prints an "InfinityRP shader stripping" summary that now includes LightMode-stripped variants
   (Infinity-tagged passes whose LightMode the pipeline never draws). Expected count is 0 for the shipped shaders.

## S2 — contract defects

1. Compile; run EditMode tests (new: `DebugViewModeTests`, `ShaderStrippingTests.ActualShaderCallback_StripsUnusedLightModeInInfinitySubShader`, `CommandTableTests.CommittedEdit_ChangesBuildSerial`, `CommandTableTests.SwitchingMaterial_RebuildsCommands`).
2. **Reflection probes**: bake a reflection probe in a lit scene. Expected change: the probe stores HDR scene-linear radiance (no TAA ghosting between faces, no exposure, tonemapping or display encoding).
3. **Material / Inspector previews** render without jitter shimmer; SSR, SSGI, GTAO and volumetrics are off in previews.
4. **Disc light** in a scene: the camera keeps rendering (it used to throw); Rendering Debugger → Infinity Lighting → Skipped Light Shapes shows 1.
5. **RP Asset → Lighting & Shadows**: new Cascade Splits and Max Shadowed Local Lights fields; an invalid split order shows an error box and fails rendering with a clear message. Shadow culling follows Shadow Distance (previously fixed at 128 m).
6. **Unified Ray Tracing off** on the RP Asset: RTAO does not record; the Debugger reason says ray tracing is disabled.
7. **GTAO off**: no HalfResDownsample pass in the Frame Debugger; GTAO on: it is present.
8. Rendering Debugger → Infinity Rendering → Debug View is an enum popup; every view still works.
9. Mesh GPU path: change a MeshComponent's material at runtime and remove/re-add instances; no stale draws or flicker.

## S3 — RenderGraph and feature registry

1. Compile; run `FeatureRegistryTests`.
2. GlobalSettings → RuntimeShaders shows the new `Utility Shader` slot filled automatically (Unity reloads null `[ResourcePath]` fields). If it is empty, use the GlobalSettings inspector reset for that container.
3. Rendering Debugger → Infinity Rendering → Frame Features: each feature shows Off / Records / Blocked with a reason. Disable SSR on the Volume (Off), then clear the SSR compute shader slot (Blocked: not assigned) and restore it.
4. Images are unchanged from S2.
5. An `RGScoper` error now names the missing resource (for example `_HiZTexture`) instead of a number.

## S4 — quality presets

1. Compile; run `QualityPresetTests`.
2. Open a Volume with GTAO, RTAO, Volumetric Fog or Volumetric Cloud: a Quality section shows `Quality = Custom` and the existing values (images unchanged).
3. Switch Quality to Low/Medium/High/Ultra: the quality fields become read-only and show the preset values; the image changes accordingly. Artistic fields (intensity, radius, density, ...) stay editable.
4. "Copy Preset To Custom" copies the preset into the parameters and switches to Custom; Undo restores the previous state.

## S5 — stochastic SSR

1. Compile; run `QualityPresetTests` and `StochasticSSRNumericTests` (GPU; three plane cases plus constant spatial).
   The 0.4.0 SSR kernels are gone from `ScreenSpaceNumericContract_ProductionKernels`.
2. The three textures in `Runtime/Resources/Textures/MonteCarlo/` (RankingTile256SPP, ScramblingTile256SPP,
   ScrambledNoise_Owen) reimport as uncompressed single-channel. GlobalSettings → RuntimeTextures shows the three
   new blue-noise slots filled.
3. Screen Space Reflection Volume: removed parameters `NumRays`, `SpatialRadius`, `SpatialSample`, `BilateralSample`,
   `BilateralColorWeight`, `BilateralDepthWeight`, `BilateralNormalWeight` (their values are dropped). Kept names:
   `MaxDistance`, `Thickness`, `MaxRoughness`, `fade`, `NumSteps` (now the Hi-Z budget), `BrdfBias`, `TemporalScale`
   (now the variance-clip width), `TemporalWeight`. New: `quality`, `HalfResolution`, `MirrorRoughness`,
   `LinearRoughness`, `LinearSteps`, `ResolveSamples`, `ResolveRadius`, `SpatialPasses`.
4. Scene with a glossy floor and a rough wall, SSR enabled:
   - Debug View **SSR Tile Class**: floor tiles blue (Hi-Z), rough tiles orange (linear), sky and forward materials gray.
   - **SSR Ray Hit**: green where reflected objects are on screen, red elsewhere; with Half Resolution the pattern is
     a 2x2 rotating checker.
   - **SSR Confidence** fades at screen edges and near Max Distance; **SSR History Age** goes white within ~16 frames
     on a still camera and drops to black on disocclusion.
   - Final image: mirror reflections sharp and stable while orbiting; rough reflections blurred without fireflies.
     Report ghosting on moving objects and any reflection of an object's back side.
5. Quality Low → Ultra visibly trades noise and blur for cost; Frame Debugger shows `SSR_Classify`, `SSR_Trace`
   (three indirect dispatches), `SSR_Resolve`, `SSR_Temporal` and, below Ultra, `SSR_Spatial`.

## S6 — light data model

1. Compile; run `LightShapeTests`, `LightRecordLayoutTests`, `LightRecordTests`.
2. Open an existing scene: lights render exactly as before (no shape is authored until the Light inspector opens; then
   the native shape is adopted and the scene becomes dirty once).
3. Light inspector: **Type** offers Directional / Point / Spot / Rect / Disk. Changing it or a shape value updates the
   native type (Point ↔ Tube by Source Length, Spot ↔ Pyramid / Box by Spot Shape) and Unity's gizmo; Undo reverts both
   the Infinity shape and the native Light in one step.
4. New shapes render: pyramid and box spots (hard-edged frustum / box), disk (as a square of equal area), sphere and
   tube (at their center for now). Rect lights no longer light surfaces behind them.
5. Maximum Distance: put the camera beyond a point light's Maximum Distance: it fades out over Distance Fade and
   Rendering Debugger → Infinity Lighting → Lights Beyond Max Distance counts it.
6. Bake a scene with a Mixed spot (inner angle 20°, outer 60°), a Baked disk and a Baked sun with Source Angle 2°: the
   baked spot has the same inner cone as the realtime one; the baked sun shadow penumbra widens with Source Angle;
   baked intensity matches realtime (compare a Mixed light in Baked Indirect with its realtime direct lighting). Report
   if baked lights are now about π times darker or brighter than before.
7. Sun direction is unchanged (directional lights now store the emission axis; contact shadows, fog and clouds read
   `-forward`).

## S7 — clustered light list v2

1. Compile; run `LightBinningTests`, `LightResourceLifetimeTests`, `LightRecordTests`. GlobalSettings → RuntimeShaders
   shows `Light Tile Mask Shader` filled (the old `Z Binning Shader` slot is gone).
2. `CreateLocalLightsValidationScene` (or any scene with many point / spot lights): lighting matches S6 in Deferred.
   Forward-route materials now receive local lights through the list: compare a Forward and a Deferred sphere side by
   side; they should match.
3. Put the camera inside a point light's range: the light still lights everything (it covers every tile). Lights
   behind the camera no longer light pixels in front (the old mirrored-tile bug).
4. Far scenes: local lights 100 m+ away no longer flicker on and off between bins.
5. Volumetric fog / clouds with local lights: same scattering as S6.
6. Frame Debugger: `ComputeLightTileMask` replaces `ComputeZBinningLightList` (one dispatch); no ZBin overflow
   capture remains.

## S8 — area light shapes

1. Compile; run `LightShapeGpuTests` (GPU).
2. Window/Infinity/Create Area Lights Fixture: on the glossy floor each light's highlight has the light's shape
   (round for the sphere, a streak for the line and tube, a rectangle / ellipse for rect and disk, a soft disk for the
   sun with Source Angle 2°). On the rough floor the highlights widen without becoming brighter than their sources.
3. Change Source Radius of the sphere from 0 to 0.5: the overall brightness on the rough floor stays nearly the same;
   the highlight on the glossy floor grows and its peak drops.
4. Rect lights now light diffuse surfaces (they only produced a highlight before) and do not light behind the emitter.
   Barn Door Length 0 → 0.4 at 45° narrows the lit area.
5. Expected global changes: brighter, unclamped specular highlights on glossy materials (bloom may react); punctual
   lights with a Diffuse or Specular weight below 1 now scale only that term.

## S9a — shadow settings, PCF / PCSS, shadows for every shape

1. Compile; run `LocalShadowFrustumTests` and `LightRecordTests`.
2. RP Asset → Lighting & Shadows: new Cascade Count, Cascade Blend Width and Shadow Filter Quality. Cascade Count 1–4
   changes the number of cascades in Frame Debugger (`RenderCascadeShadow`); with Blend Width 0.1 the cascade seams on a
   long floor are no longer visible as a hard line.
3. Shadow Filter Quality Low → Ultra: PCF edges grow from 3×3 to 7×7 texels. Custom shows editable fields; Copy Preset
   To Custom copies the current preset.
4. Light inspector → Shadow Filter = PCSS on the sun with Source Angle 2°: contact-hardening shadows (sharp at the
   contact, softer with distance). On a sphere point light, Source Radius 0 → 0.3 widens the penumbra.
5. Pyramid spot, box spot, rect and disk lights now cast shadows (they cast none before). Box spots give parallel
   shadows. A point light with Source Length > 0 (line / tube) still casts six-face shadows.
6. Expected changes: shadow edges are softer at Medium (5×5 tent replaces the 3×3 box); Hard keeps one-tap edges.

## S9b — VSM / EVSM

1. Compile; run `ShadowMomentGpuTests` (GPU).
2. Sun Shadow Filter = EVSM: Frame Debugger shows `ComputeShadowMoments` after `RenderLocalShadow`, with one
   `MomentFromDepth` + two blur dispatches per cascade; Rendering Debugger → Frame Features lists ShadowMoments as
   produced. Shadows are smooth and wide; contact areas stay dark.
3. Switch to VSM: overlapping occluders at different depths show light bleeding (a lighter halo inside the far
   shadow); Shadow Filter Quality Low → Ultra lowers it. EVSM shows much less.
4. Set every light back to PCF: `ComputeShadowMoments` disappears from Frame Debugger.
5. A spot light with EVSM next to a point light with PCF: only the spot's tile is written in `LocalShadowMoments`.
6. Volumetric fog with a shadowed sun: shafts look slightly sharper than before (the fog used twice the texel size).

## S9c — cached local shadows

1. Compile; run `LocalShadowCacheTests`.
2. A shadowed spot light → Shadow Update Mode = OnEnable. Rendering Debugger → Infinity Lighting: Cached Shadow Tiles 1,
   Cached Shadow Re-renders 1 on the first frame, then 0. Frame Debugger: `CopyCachedLocalShadow` appears and the
   spot's `LocalShadowSlice` draw disappears from `RenderLocalShadow` on later frames.
3. Move a cube under the spot: its shadow stays where it was (cached). Move or rotate the light: one re-render.
   Disable and re-enable the Infinity component: one re-render.
4. OnDemand on a point light: six cached tiles; moving the light does not re-render; the inspector button
   Request Shadow Map Rendering re-renders all six once.
5. Cached Shadow Atlas Width 0: every light renders every frame again (Cached Shadow Tiles 0).
6. Two Game views / Scene + Game: a stale tile is rendered once per frame, not once per camera.

## S9d — Shadowmask casters

1. Compile; run `ShadowmaskCasterFilterTests`.
2. Project Settings → Quality → Shadowmask Mode = Shadowmask; Lighting → Mixed Lighting = Shadowmask; a Mixed sun and a
   Mixed spot; a static cube (Contribute GI, lightmapped) and a dynamic sphere. Bake.
3. Frame Debugger → `RenderCascadeShadow` / `RenderLocalShadow`: the static cube is not drawn; the sphere is. On
   screen the cube's shadow still appears (from the shadowmask), the sphere's shadow is realtime.
4. Switch Shadowmask Mode to Distance Shadowmask: the cube is drawn into the shadow maps again.
5. With a MeshComponent (Static mobility) using a lightmapped renderer as its baked source: same result as step 3.

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
6. More than 24 contact lights: the 24 largest on screen keep contact shadows.

## S9e — shadow debug views

1. Run `DebugViewModeTests`.
2. Rendering Debugger → Debug View = Cascade Shadow Atlas: a 2x2 grid (only the first Cascade Count tiles filled),
   casters bright, background black. Local Shadow Atlas: one tile per spot / rect / disk, six per point.
3. Cached Shadow Atlas with an OnEnable light: its tile is visible; with no cached light the view is magenta.
4. Shadow Moments with an EVSM sun: a blurred, half-resolution version of the cascade atlas; magenta without any
   VSM / EVSM light.

