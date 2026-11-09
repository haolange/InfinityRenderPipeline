# U02 protective baseline

Harness: CLI `infinity_normalization_baseline` writes Volume / package / Unity receipts. The CLI reports Infinity `IsActive` through `GraphicsUtility.VolumeComponentActive`, not a `VolumeComponent.IsActive()` call.

Still required on the Mac worker against InfinityExample (no second Editor):

- Spazon + Validation_* normal-frame captures
- LUT descriptor hash
- EditMode XML
- Editor.log mark
- Frame Debugger tree

Until those exist, later frame gates stay `TODO(UNVERIFIED)` against this missing baseline.
