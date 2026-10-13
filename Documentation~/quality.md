# Quality

## Volume quality levels

SSR, SSGI, GTAO, RTAO, contact shadows, volumetric fog and volumetric clouds have a **Quality** parameter:

- **Low / Medium / High / Ultra** pick a preset hardcoded in `Runtime/RenderPipeline/Quality/QualityPresets.<Feature>.cs`.
  The inspector shows the preset values read-only; **Copy Preset To Custom** copies them into the Volume.
- **Custom** (default) uses the values authored on the Volume.

Artistic parameters (intensity, distance, thickness, colors) always come from the Volume. Passes read only the resolved
settings, so changing a preset is a one-line code change.

## Pipeline Asset

- **Shadow Filter Quality**: the same four presets or Custom for the shadow filters (see [Shadows](shadows.md)).
- **Render Scale** (Game cameras, fixed-ratio super resolution), shadow resolutions and budgets, reflection probe
  resolution and count, cookie atlas size.
- **Frame Settings**: which features cameras and reflection probe captures render (see [Editor tools](editor.md)).

Several RP Assets can be assigned to Quality levels; each asset may also carry a quality Volume profile that
VolumeManager applies on top of the default profile.
