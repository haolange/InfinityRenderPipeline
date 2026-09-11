# Upgrade 0.3 to 0.4

1. Upgrade the Editor to 6000.6 and CoreRP 17.6.
2. Open the project so Global Settings auto-create and resources reload.
3. Run `Window > Infinity > Migrate > Pipeline Asset Resources To GlobalSettings`.
4. Run `Window > Infinity > Validate Default Volume Profile`.
5. Re-save Example RP assets with the backup / exact-delta / second-save / no-op protocol.
6. Migrate Lit materials (`Window > Infinity > Migrate > Lit Material Property Names`).
7. Replace any remaining `CameraComponent` / `LightComponent` references — script GUIDs were reused, so scenes should reconnect.
8. Optional Volumes now require `enable` or intensity; existing override-only stacks will go inactive until those flags are set.
