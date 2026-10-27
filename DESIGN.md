# InfinityRP — Editor UI

Architecture is in [ARCHITECTURE.md](ARCHITECTURE.md). This note only describes surfaces that exist in the Editor.

## Render Graph Viewer

`Window > Analysis > Infinity Render Graph Viewer` shows the last compiled graph of each camera while the window is open. The toolbar picks the camera, and has Pause, Culled, Imported, Unused, a resource-name filter, Copy and Clear.

The summary line shows the frame, pass count, culled count, async count, segment count, resource count and peak alias bytes.

Columns are passes in record order. A header is blue for the graphics queue, orange for async compute, and gray when the pass is culled. The tooltip names the pass, its type, queue, cull state, submission segment, and native-pass group plus subpass when the pass was merged. Rows are resources. An imported resource is marked with a dot. A cell is green for a read, red for a write, and yellow for both. A blue band behind the cells is the pooled lifetime from the allocating pass to the releasing pass.

Selecting a pass shows type, queue, segment, native pass (group and subpass), whether it was culled and why, or that it is cullable or has culling disabled. It also lists waits, signals, reads, writes, allocations and releases. Selecting a resource shows kind, imported or pooled, size, physical slot (alias group, slot, and memoryless when set), first and last use, allocating pass, releasing pass, writers and readers.

## Rendering Debugger

Window > Analysis > Rendering Debugger, panels registered by InfinityRP:

| Panel | What it shows |
|---|---|
| Infinity Rendering | Debug View, the Rendering subset, Frame Features and Frame Settings of the last camera |
| Infinity Material | Material debug views |
| Infinity Lighting | Lighting debug views, Reflection Probe Index, Complexity Range, and the last camera's directional, local, probe, invalid-shape, distance-culled, cached-shadow and ray-tracing counts |
| Infinity Screen Space | Screen-space debug views |
| Volume | CoreRP's camera, component and contribution panel |
| Infinity Timings | Record Pass Timings, the previous frame's total, and one row per pass sampler |
| Infinity Mesh | Matrix Duplicate Ratio, Override Mesh Backend, the requested and actual backend, and the backend reason |

Scene view Draw Modes use the sections Infinity Material, Infinity Lighting, Infinity Screen Space and Infinity Rendering. A mode on that view replaces the Debugger view for that camera. Probe and preview cameras do not take a debug view.

## Inspectors

These inspectors are active when an Infinity Render Pipeline asset is assigned.

- **Camera.** Projection, Rendering, Environment (Volume Mask, Volume Trigger, Super Resolution), Frame Settings, Output (Target Texture, Viewport Rect).
- **Light.** General, Shape, Emission, Baking, Cookie & IES, Shadows (including Filter, Update Mode, Request Shadow Map Rendering and Contact Shadows), Volumetric, Layers & Distance. Additional Properties reveals the less common fields. Scene view handles edit range, angles and the source shape.
- **Mesh.** State (Mobility), Mesh, Lighting, Rendering.
- **Decal.** General, Projection.
- **Volume Profile.** The CoreRP component list.
- **Pipeline Asset.** Shadow filter quality, Rendering, Lighting & Shadows, Post-processing & Output, Frame Settings (Cameras and Reflection Probe Captures), Default Materials. Runtime shaders are resolved from Project Settings > Graphics > Infinity RP.
- **Atmospherical Profile.** Planet, Scatter, Quality.
- **Diffusion Profile.** Scatter.
- **InfinityLit.** Surface Options, Surface Inputs, Subsurface when enabled, Baked Emission, Advanced / Render State.
