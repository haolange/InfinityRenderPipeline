# Rendering Debugger

Window > Analysis > Rendering Debugger is Unity's window. Infinity registers these panels when the pipeline is active.

| Panel | Contents |
|---|---|
| Infinity Rendering | Debug View for the whole `EDebugView` enumeration, the Rendering subset, Frame Features of the last camera, Frame Settings of the last camera |
| Infinity Material | Material views: albedo, normal, roughness, metallic, specular, shading model, rendering layers. Shading model draws the forward route, subsurface and the default lit model in different colors |
| Infinity Lighting | Lighting views, Reflection Probe Index, Complexity Range, Volume selection, and the last camera's directional, local, probe, invalid-shape, distance-culled, cached-shadow and ray-tracing counts |
| Infinity Screen Space | Ambient occlusion, reflection, indirect diffuse, and the reflection tile, ray, confidence and history views |
| Volume | Core RP camera, component and contribution panel |
| Infinity Timings | Record Pass Timings. When recording is on, the previous frame's total and a Passes foldout with one row per pass sampler |
| Infinity Mesh | Matrix Duplicate Ratio, Override Mesh Backend, the backend enum when override is on, requested backend, actual backend, and the backend reason |

Choosing a view in one panel clears the other panels' views. A view whose producer did not run is magenta. The debug view is not serialized on the pipeline asset. Probe and preview cameras do not take a debug view. Game, Scene and VR cameras can.

Light and reflection-probe complexity count the lights and probes the clustered loop visits for that pixel's tile and depth bin. They do not count lights that pass the pixel's lighting test. Red is the top of Complexity Range.

## Scene view Draw Modes

The Scene view menu has one section per Infinity panel: Infinity Material, Infinity Lighting, Infinity Screen Space and Infinity Rendering. There is no Volume section. A mode replaces the debugger view for that Scene view. Shaded restores the debugger view. While Infinity is active, built-in modes the pipeline does not render, including shadow cascades, overdraw and the deferred validation modes, are disabled.

## Render Graph Viewer

The viewer is a separate window. Its layout, colors and selection fields are in [Editor tools](editor.md).
