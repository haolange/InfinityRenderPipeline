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
