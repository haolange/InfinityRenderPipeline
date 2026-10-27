# Volumes

Infinity Volume components declare `[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]`.

Optional features implement `IsActive()`. Screen-space reflection, screen-space indirect diffuse, horizon ambient occlusion, ray-traced ambient occlusion, volumetric fog, volumetric clouds, contact shadows and subsurface scattering each have an enable flag. Bloom, vignette and grain are active when intensity is greater than zero. Film tonemap is active when the mode is ACES. The supported modes are None and ACES.

`overrideState` marks a scene override. It does not turn a feature on, and it does not gate required exposure. The default profile contains every consumed component type. Window > Infinity > Validate Default Volume Profile checks that profile and does not rewrite a complete one. Window > Infinity > Append Missing Default Volume Components adds absent types and keeps existing components, identities and values. Reset to packaged defaults is a confirmed action on the Global Settings default-profile drawer. It preserves subasset identities and unrelated components.

Atmosphere is not a Volume. It is an `AtmosphericalProfile` on the pipeline asset.

## Quality

Screen-space reflection, screen-space indirect diffuse, horizon ambient occlusion, ray-traced ambient occlusion, contact shadows, volumetric fog and volumetric clouds have a Quality parameter.

- Low, Medium, High and Ultra read a preset in `Runtime/RenderPipeline/Quality/QualityPresets.<Feature>.cs`. The inspector shows those values read-only. Copy Preset To Custom writes them onto the Volume.
- Custom, the default, uses the values on the Volume. Profiles authored before that control keep their values.

Intensity, radius, density, color and distance always come from the Volume. Passes read the resolved settings struct and do not branch on the quality level.

## Screen-space reflection

1. Classify each 8×8 trace tile by its smallest roughness. Tiles with no reflective deferred pixel are skipped. Tiles whose smoothest pixel is below Linear Roughness use the Hi-Z walk. The others use a linear march. Forward-route pixels, and pixels rougher than Max Roughness, are not traced.
2. Trace one GGX visible-normal sample per trace pixel. Half Resolution traces one pixel of each 2×2 quad and rotates through the quad. BRDF Bias trims the lobe.
3. Resolve at full resolution reuses neighboring rays inside Resolve Radius, weighted by the pixel BRDF over each ray's density, and samples the lit color pyramid at the mip of the ray cone. Pixels smoother than Mirror Roughness keep their own mirror ray. Screen edges, back faces and the last 10% of Max Distance reduce confidence.
4. Temporal accumulation reprojects the surface and the reflection, clips history to the neighborhood, and tracks age up to Temporal Weight.
5. An optional spatial pass filters rough reflections.

The reflected color is the current frame's opaque lighting. Debug views show tile class, ray hit, confidence and history age.

## Screen-space indirect diffuse

This component has no resolve-sample count and no resolve radius. Those controls belonged to an earlier ray-reuse path and are not read.

1. Trace one cosine-distributed ray per work pixel with the Hi-Z walk. The ray samples the accepted unsharpened color pyramid from the previous temporal resolve. Half Resolution, when set, traces one pixel of each 2×2 quad.
2. Temporal accumulation follows the surface on that work grid and clips history with Temporal Scale, up to Temporal Weight.
3. Spatial Passes runs a world-space filter on the same grid. The radius comes from the hit distance and the pixel footprint.
4. Reconstruction writes full resolution from the rotating quad representatives, including the shorter last interval on an odd edge.

Missed rays add no radiance. That portion of the pixel keeps its lightmap, probe or sky ambient. History stores the filtered work-grid result, not a pre-spatial buffer. Surface history is an integer texture: exact depth bits, a packed octahedral normal, age and a validity bit. Invalid and off-screen history contribute nothing.

The gathered light is one bounce from the accepted previous color. It does not read the current frame's lighting buffer.
