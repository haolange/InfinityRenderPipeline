# Baked lighting

## Sources and priority

Each pixel takes its baked diffuse lighting from the first available source:

1. **Lightmap** (static renderers with Contribute GI, including terrain).
2. **Adaptive Probe Volumes** when the RP Asset's **Light Probe System** is Adaptive Probe Volumes.
3. **Light Probe Proxy Volume** (renderer Light Probes = Use Proxy Volume).
4. **Light probes** (interpolated per renderer; MeshComponent instances re-interpolate in one batch when they move).
5. **Sky** ambient (the atmosphere's SH).

Shadowmask and probe occlusion come from the same source.

## Mixed lighting

Lighting Settings → Mixed Lighting supports **Baked Indirect** and **Shadowmask** (Quality → Shadowmask Mode chooses
Shadowmask or Distance Shadowmask). Subtractive is not supported (the Render Pipeline Wizard flags it).

## Adaptive Probe Volumes

RP Asset → Lighting: Light Probe System = Adaptive Probe Volumes, SH Bands (L1 / L2), memory budgets, GPU / disk streaming,
lighting scenarios and scenario blending. Add Probe Volumes to the scene and bake from the Lighting window. The
**Probe Volumes Options** Volume component adjusts sampling bias and leak reduction at runtime.

## Baking sky

Save the scene, then use Window → Infinity → Lighting → Use Atmosphere As Baking Sky. The command reads the six
atmosphere faces into a Texture2DArray and assigns a skybox material, so baked ambient and reflection probes use that
sky. Outputs under `Assets/InfinityBakingSky/` use the saved scene GUID; equal scene names do not share assets.
Only those output assets are saved by the command. Refresh validates the GPU readback before updating the existing
texture, including when its size changes. Baking stops if refresh fails or the material belongs to a different
scene. For an earlier name-based output, explicitly run the command again for that scene; existing assets are not
deleted or migrated on load. The Lighting window's Environment section shows the state.

## Lightmapper

While Infinity is active its lightmapper delegate sends the Infinity light shapes (sphere radius as baked soft
shadow radius, sun angle, pyramid and box spots, inverse-square falloff, inner spot angle) to Unity's lightmapper.
Tube and line lights are realtime only.

## Debugging

Rendering Debugger → Infinity Lighting: Baked Diffuse, Indirect Diffuse, Indirect Specular. Fixture: Window → Infinity
→ Create Baked Lighting Fixture, then Generate Lighting.
