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
