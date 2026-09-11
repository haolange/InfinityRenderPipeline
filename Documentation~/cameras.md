# Cameras

`InfinityAdditionalCameraData` is the only per-camera Infinity storage:

- Volume layer mask and trigger
- Super Resolution override (`UseAsset` / `Off`)

`InfinityCameraEditor` draws Camera under Infinity. Preview cameras use defaults only (`mask = 0`). Scene View inherits the unique active Game camera's additional data when exactly one exists; otherwise mask `~0` and the Scene View transform.
