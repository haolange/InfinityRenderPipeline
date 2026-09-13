# Upgrade 0.3 to 0.4

One-off `Window/Infinity/Migrate/*` menus are retired after the Example receipts (RP Asset schema, default Volume registry, Lit property names). Remaining steps:

1. Use Unity **6000.6** with CoreRP / Shader Graph / VFX **17.6**.
2. Open the project so Global Settings bind and `[ResourcePath]` resources load.
3. Confirm Graphics / Quality use an `InfinityRenderPipelineAsset`, and Global Settings owns the default Volume profile.
4. Run `Window > Infinity > Validate Default Volume Profile` if you need a no-op registry check. It does not rewrite a complete profile.
5. Example RP assets and Lit materials already follow the backup / exact-delta / second-save / no-op protocol on the Mac worker. Do not re-run retired migrate menus.
6. `CameraComponent` / `LightComponent` were GUID-preserving renames; scenes reconnect to additional-data types.
7. Optional Volumes require `enable` or a documented intensity/mode threshold. `overrideState` is not a feature gate.
8. Out-of-range atmosphere fields are restored from the AtmosphericalProfile Inspector, not a migrate menu.
