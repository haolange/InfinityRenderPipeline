# Rendering Debugger

`Window > Analysis > Rendering Debugger` shows the Infinity panels:

- **Infinity Rendering**: the master Debug View (every view), Motion Magnitude / TAA Confidence / Pre-Tonemap Luma,
  the Frame Features and Frame Settings of the last camera.
- **Infinity Material**: Albedo, Normal, Roughness, Metallic, Specular, Shading Model (blue = forward route, orange =
  subsurface, gray = default lit), Rendering Layers.
- **Infinity Lighting**: Light Complexity and Reflection Probe Complexity heat maps (the lights / probes a pixel's
  clustered loop visits; red at **Complexity Range**), Baked Diffuse, Indirect Diffuse, Indirect Specular, the shadow,
  moment and cookie atlases, Reflection Probe Atlas (six faces of the probe at **Reflection Probe Index**), Contact
  Shadows; light, probe and cache counters.
- **Infinity Screen Space**: AO, SSR, SSGI and the SSR tile / ray / confidence / history views.
- **Volume**: CoreRP's Volume panel (camera, component, interpolated values, each volume's influence).
- **Infinity Timings**: **Record Pass Timings** turns on the profiler recorders of every pass and lists their GPU and
  CPU time (previous frame, summed over cameras).
- **Infinity Mesh**: MeshScene matrix duplicate ratio.

Every panel drives one debug view: choosing a view in one panel sets the others to None. A view whose producer did not
run this frame is magenta. The debug view is never serialized on the RP Asset, and reflection probe captures and
previews ignore it.

## Scene view Draw Modes

The Scene view Draw Mode menu has a section per panel (Infinity Material, Infinity Lighting, Infinity Screen Space,
Infinity Rendering). A mode applies to that Scene view only; Shaded returns it to the debugger's view. Built-in modes
the pipeline does not render (Shadow Cascades, Overdraw, Mipmaps, Deferred buffers, validation modes) are disabled.

## Render Graph Viewer

`Window > Analysis > Infinity Render Graph Viewer` shows the last compiled render graph of each camera: passes in
record order as columns (orange = async compute, gray = culled), resources as rows, the pass reading (green), writing
(red) or both (yellow), and the pool lifetime of each resource from its allocating to its releasing pass. Click a pass
header or a resource for details; **Pause** freezes the capture, **Copy** puts the graph on the clipboard as text. The
graph is captured only while the window is open.
