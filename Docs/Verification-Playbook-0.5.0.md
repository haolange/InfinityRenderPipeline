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
6. More than 24 contact lights: the 24 largest on screen keep contact shadows.

## S11 — cookies and IES

1. Compile; run `LightCookieAtlasTests`.
2. A spot light with a 2D cookie (any RGB texture): the pattern is projected inside the cone, colored. An Alpha8 cookie
   (Unity's default cookie import) projects its alpha as gray. Rendering Debugger → Debug View = Light Cookie Atlas
   shows the texture in its block.
3. A point light with a Cubemap cookie: the pattern surrounds the light; a 2D cookie on a point light shows the inspector
   warning and no pattern.
4. A directional light with a cookie and Cookie Size (10, 10): the pattern tiles every 10 m on the ground.
5. A rect light with a cookie: glossy reflections of the rect show the texture; diffuse lighting takes its average
   color.
6. Import an .ies file (Assets → Import): the asset's prefab has an Infinity Additional Light Data with IES Profile set;
   drop it in the scene and compare with the profile's shape. Assign the Cube texture of the .ies asset to a spot light:
   the profile applies by direction.
7. Volumetric fog with a cookie spot: the shafts carry the pattern.
8. Change a cookie texture's import settings: the atlas block re-uploads once (Frame Debugger `ComputeLightCookies`
   appears for one frame).

## S12 — baked GI

0. Window/Infinity → Append Missing Default Volume Components (adds Adaptive Probe Volumes Options).
1. Compile; run `BakedGITests`.
2. Shadows: with a cascade and a spot light, casters now appear in the shadow maps from the light's view (Debug View
   Cascade / Local Shadow Atlas). If shadows looked camera-aligned or missing before, this was the cause.
3. Lightmaps: Window/Infinity/Lighting/Use Atmosphere As Baking Sky, then Generate Lighting. Static InfinityLit objects
   get lightmaps; sky color in the bake matches the realtime sky (blue fill on the shadow side). Check
   Assets/InfinityBakingSky/<scene>_BakingSkyFaces.asset in the Inspector preview: six faces, sky up on +Y.
4. Shadowmask: Mixed lights + Shadowmask: static shadows beyond the shadow distance come from the mask; Distance
   Shadowmask uses realtime shadows inside the distance.
5. Terrain: mark the terrain Contribute GI and bake: the terrain receives lightmaps and casts realtime shadows
   (ShadowCaster pass).
6. Light probes: a dynamic MeshComponent moving through a probe group changes its ambient; a static one keeps its
   sample until the next bake (Profiler: one CalculateInterpolatedLightAndOcclusionProbes call per frame at most).
7. LPPV: a large dynamic renderer with a Light Probe Proxy Volume (Use Proxy Volume) shows a probe gradient across it.
8. APV: RP Asset → Light Probe System = Adaptive Probe Volumes; add an Adaptive Probe Volume, bake from Lighting →
   Adaptive Probe Volumes. Unlightmapped objects show the APV lighting; Rendering Debugger → Probe Volume shows the
   probes. Switch the band count L1 / L2.

## S13 — reflection probes

1. Compile; run `ReflectionProbeTests`, `AtmosphereCacheTests`.
2. Sky IBL is unchanged: with no probe in the scene, glossy and rough spheres look as before (the sky prefilter moved
   to `Compute_CubeFilter`; Frame Debugger `ComputeAtmosphericLUT` dispatches `FilterArrayToArray` once per mip).
3. Add a Reflection Probe (Baked) around an indoor room and bake: glossy surfaces inside show the room instead of the
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
9. RP Asset Max Reflection Probes = 0: probes disappear, only the sky remains; `ComputeReflectionProbes` is gone.

## S14 — SSGI on the SSR core

1. Compile; run `QualityPresetTests`, `StochasticSSGINumericTests` (GPU) and `StochasticSSRNumericTests` (SSR now uses
   the shared `ScreenSpaceTraceTiles.hlsl`; its results must not change). The 0.4.0 SSGI kernels are gone from
   `ScreenSpaceNumericContract_ProductionKernels`.
2. Screen Space Indirect Diffuse Volume: removed parameters `NumRays`, `SpatialRadius`, `SpatialSample`,
   `BilateralSample`, `BilateralColorWeight`, `BilateralDepthWeight`, `BilateralNormalWeight` (their values are
   dropped). Kept names: `MaxDistance`, `Thickness`, `IntensityScale`, `NumSteps` (now the Hi-Z budget, range 4–64),
   `TemporalWeight`, `TemporalScale` (now the variance-clip width). New: `quality`, `HalfResolution`,
   `ResolveSamples`, `ResolveRadius`, `SpatialPasses`.
3. A room with a bright red wall and a white floor, SSGI enabled: the floor near the wall picks up red bounce light;
   Debug View **SSGI** shows it. Looking away from the wall (wall off screen) the bounce fades back to the baked / probe
   ambient instead of turning black.
4. Orbit slowly: no crawling noise after ~16 frames; disocclusions settle within a few frames. Report ghosting behind
   moving objects.
5. Frame Debugger: `SSGI_Classify`, `SSGI_Trace` (two indirect dispatches), `SSGI_Resolve`, `SSGI_Temporal`,
   `SSGI_Spatial`, then `CopyHistorySSGI` copies two textures. Quality Low → Ultra trades noise for cost.

## S15a — Shader Graph target

1. Compile. The Shader Graph package compiles the `Editor/ShaderGraph` asmref into `Unity.ShaderGraph.Editor`; record
   any Console error from that assembly first.
2. Create → Shader Graph → (any) graph, Graph Settings → Active Targets → Infinity. Material: Lit, Unlit, Decal appear.
   Lit blocks: Base Color, Normal (Tangent Space), Metallic, Roughness, Specular Level, Emission (+ Alpha with clipping or
   a translucent stage). Save: the shader compiles without errors (Inspector of the graph asset → no error list).
3. A Lit graph with a texture into Base Color and a normal map, on a cube next to an InfinityLit cube with the same
   inputs: both look the same in Deferred; switch the material's Surface Route to Forward: still the same, Frame
   Debugger draws it in `ForwardPass`.
4. Translucent Stage T1 on the material: refraction through the color pyramid; T0: fog applies; T2: plain blend. Motion
   vectors: Debug View Motion shows the moving object.
5. Bake lighting with a static Lit-graph object: it receives the lightmap (Meta pass gives the right albedo / emission).
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
4. Edit a light's native Type through the Debug Inspector → "Light shapes synced" fails; Fix restores it (Undo works).
5. Scene with Baked GI and the default skybox → "Baking sky" fails; Fix runs Use Atmosphere As Baking Sky.

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
2. Window/Rendering/Lighting → Environment: Atmosphere Profile field edits the RP Asset; Baking Sky shows the scene
   skybox until Use Atmosphere As Baking Sky is pressed, then "Atmosphere".

## S15e — Inspectors and handles

1. Run `DecalProjectionTests`.
2. Select a point light with Source Radius 0.2 and Source Length 1: the Scene view shows the range sphere, the source
   sphere and the tube line with an end handle; dragging changes the inspector values, Undo restores them, and the native
   Light (Debug Inspector) follows (Type Tube, Area Size x = length).
3. Spot cone / pyramid: outline at the range with a rim handle for the outer angle; rect: a size box; disk: radius handle.
4. Light inspector Additional Properties shows soft source radius / angle, barn doors, shadow near plane and biases,
   weights and distance fade; off hides them.
5. Decal: drag a face of the box handle; the opposite face stays. UV Scale 2 tiles the texture; Start / End Angle Fade
   30 / 60 removes the decal from vertical walls under a downward projector.
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
6. Timings: Record Pass Timings on; the Passes foldout fills with GPU / CPU ms (previous frame, summed over cameras).
7. Scene view Draw Mode menu: sections Infinity Material / Lighting / Screen Space / Rendering. Choosing one changes
   only that Scene view (the Game view keeps the debugger's view); Shaded returns it to the debugger's view. Shadow
   Cascades, Overdraw, Mipmaps and the Deferred modes are disabled.
8. Bake a Reflection Probe with a debug view active: the probe stays normally shaded.

## S15g — Render Graph Viewer

1. Run `RenderGraphViewerTests`.
2. Window/Analysis/Infinity Render Graph Viewer with the Game view playing: the camera popup lists the Game and Scene
   cameras; columns show every pass (orange = async compute such as the light tile mask, gray = culled). Hovering a
   header names the pass; clicking shows its queue, wait / signal passes and resource lists.
3. Rows: GBufferA/B/C are written by RenderGBuffer and read by DeferredShading; the blue band spans from the
   allocating pass to the releasing pass. Imported rows (•) such as history textures have no band.
4. Turn SSR off in the Volume: its passes disappear (not recorded). Pause freezes the capture; Copy puts the graph as
   text on the clipboard.
5. Close the window: no further capture (no allocations from the viewer in the Profiler).

## S15h — Material conversion and create menus

1. Run `MaterialUpgraderTests`.
2. Import a few Standard materials (e.g. from an Asset Store pack) and URP Lit materials if available; Window/Infinity/
   Material Conversion Wizard → Scan Project lists them with their source shader and dropped maps. Convert Selected:
   the materials switch to InfinityLit with the same color, map and tiling; glossy stays glossy (roughness = 1 −
   smoothness); a Standard Fade material becomes translucent T0. Undo restores the source shader.
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

Each creator replaces `Assets/Scene/Validation/Validation_<Name>.unity` and its asset folder; run it once after
updating, then use the fixture for the stage it names.

1. Create Shadows Fixture: five poles under spots labeled by filter. Hard has aliased edges; PCF smooth constant
   width; PCSS sharp at the pole base and wider at the tip; VSM / EVSM smooth with possible faint light bleeding where
   shadows overlap. Sphere Light PCSS casts soft cube-map shadows; Spot OnDemand renders once (move the box: the shadow
   stays until `RequestShadowMapRendering`). Pebbles show contact shadows from the sun and the Contact Point light;
   disabling ContactShadow in the Volume removes them.
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
   `ImageComparisonTests`. Report every failure of the `Unverified-0.5` category with its message.
2. Create the fixtures you verified visually (Window → Infinity → Create … Fixture). Then Window → Infinity → Image
   Regression → Record Baselines: the Editor opens each fixture, enters Play mode, captures and leaves Play mode
   (a few seconds per fixture). The Console ends with "Completed: N case(s)"; `ImageBaselines/<API>/` next to `Assets`
   holds one `.bin` + `.json` per fixture. Commit that folder in the Example project if you want to keep them.
3. Image Regression → Run Comparison right after: every case logs **Pass** (rmse ≈ 0). Change one light's intensity in a
   fixture, save, run again: that case logs **Fail** with the rmse; the report is
   `Library/InfinityImageRegression/<run>/report.json`.
4. Documentation~ links resolve (index → every page); the pages match what you see in the inspectors.

## S17 — folder layout

1. Pull and let Unity reimport: no "missing script" warnings on Volume profiles, cameras or lights (the moved scripts
   keep their GUIDs). The default Volume profile and every fixture profile still show their components.
2. Compile; run the full EditMode suite (`NativeViewMotionHistoryTests` and `ScreenSpaceDenoiseTests` read pass sources
   from the new paths).
3. Render any fixture: identical to the S16 image (Image Regression → Run Comparison passes against S16 baselines).
