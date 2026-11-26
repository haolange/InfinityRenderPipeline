# Shader Graph

Create a graph from Assets > Create > Shader Graph > Infinity (Lit, Unlit, Decal), or add the Infinity target to an existing graph. The target is compiled into the Shader Graph editor assembly. Runtime code does not reference Shader Graph internals.

## Lit

Blocks are Base Color, Normal (tangent space), Metallic, Roughness, Specular Level, Emission, and Alpha and Alpha Clip Threshold when those features are used. Specular Level 0.5 is 4% reflectance. Target settings:

- Surface Route: Deferred (default, GBuffer) or Forward. Screen-space reflection and indirect diffuse do not trace forward pixels.
- Translucent Stage: None, T0 (lit, before the refraction pyramid), T1 (refraction) or T2 (after refraction).
- Blending Mode on translucent stages, Render Face, Alpha Clipping, Custom Editor GUI.

Lit graphs use the InfinityLit passes: depth, GBuffer, emission, forward, motion, shadows, the translucent stages, and Meta for baking. A deferred opaque graph keeps `RasterEmissivePass` enabled, because emission is the graph output rather than `_EmissionColor`; a black output adds nothing. Lightmaps, shadowmask, light probes, light-probe proxy volumes, Adaptive Probe Volumes and decals apply to native renderer draws of those graphs. The graph material GUI edits routing. The Occlusion block scales the graph's baked or sky indirect diffuse; it never darkens emission or direct light. Lit graphs do not scatter: skin is the hand-written InfinitySkin shader.

The hand-written InfinityLit shader does not have a metallic map. The graph Metallic block is a graph input. The runtime lit buffer stores reflectance.

## Unlit and Decal

Unlit draws in translucent stage T2. Decal graphs project through the box: Base Color, Normal, Metallic, Roughness, Occlusion and Alpha. `_DecalTarget` selects the DBuffer pass or the GBuffer blend; Replace Normal clip-replaces the GBuffer sample. Assign the material to a Decal Component.

## Limits

Scene Depth is available in decal graphs only. Scene Color is available in T1 only. The Fog node returns no fog. Motion vectors assume rigid object motion. Vertex animation in the graph does not reach the previous position.
