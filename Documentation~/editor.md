# Editor tools

| Tool | Where | Purpose |
|---|---|---|
| Render Pipeline Wizard | Window → Infinity → Render Pipeline Wizard | Checks color space, graphics API, pipeline assignment, GlobalSettings, default Volume profile, SRP Batcher, mixed lighting, baking sky, light shape sync, realtime-only tube lights and convertible scene materials; Fix / Fix All. |
| Frame Settings | RP Asset (cameras, reflection probe captures); Camera → Custom Frame Settings | Per-feature switches (decals, AO, RTAO, contact shadows, SSR, SSGI, fog, clouds, reflection probes, cookies). The Rendering Debugger shows each effective value and its source. |
| Light Explorer | Window → Rendering → Light Explorer | Infinity Lights, Infinity Probes and Volumes tabs next to Unity's. |
| Lighting window | Window → Rendering → Lighting → Environment | Atmosphere profile, baking sky, ambient and reflection intensity. |
| Inspectors | Light, Reflection Probe, Decal Projector, Camera, RP Asset | Foldout sections, Additional Properties, Scene view handles for every light shape, probe sphere radius and decal box. |
| Custom terrain authoring | Terrain Component → Rebuild Sector From Unity Terrain | Explicitly imports the attached native TerrainData; supports Undo and multi-object editing. Saving a scene never rebuilds sectors. Disable/re-enable releases/recreates runtime storage while retaining authored sectors. |
| Rendering Debugger | Window → Analysis → Rendering Debugger | See [Rendering Debugger](rendering-debugger.md); Scene view Draw Modes per panel. |
| Render Graph Viewer | Window → Analysis → Infinity Render Graph Viewer | Passes, queues, culling and resource lifetimes of each camera's graph. |
| Material Conversion Wizard | Window → Infinity → Material Conversion Wizard | Built-in / URP / HDRP materials to InfinityLit / InfinityUnlit (see [Materials](materials.md)). |
| Create menus | GameObject → Light → Infinity, GameObject → Rendering → Infinity, Assets → Create → Rendering → Infinity, Assets → Create → Shader Graph → Infinity | Light shapes, volumes, decal projector, probes, light probe group; pipeline asset, profiles, Global Settings, graphs. |
| Validation fixtures | Window → Infinity → Create … Fixture | Test scenes for area lights, local lights, shadows, cookies, reflection probes, baked lighting, screen space, decals, translucency, temporal, output, volumes, UI. |
| Image regression | Window → Infinity → Image Regression | Record Baselines / Run Comparison over the fixtures (DisplayColor of each fixture camera in Play mode). Baselines live in `ImageBaselines/<graphics API>/` next to `Assets`; reports in `Library/InfinityImageRegression/`. |
