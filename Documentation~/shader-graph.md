# Shader Graph

Create graphs from Assets → Create → Shader Graph → Infinity (Lit, Unlit, Decal), or add the **Infinity** target to an
existing graph.

## Lit

Blocks: Base Color, Normal (Tangent Space), Metallic, **Roughness** and **Specular Level** (Unreal form; 0.5 = 4%
reflectance), Emission, Alpha and Alpha Clip Threshold when used. Target settings:

- **Surface Route**: Deferred (GBuffer, default) or Forward (shaded in the forward pass; SSR / SSGI skip it).
- **Translucent Stage**: None (opaque), T0 (lit, before the refraction pyramid), T1 (refraction), T2 (after).
- **Blending Mode** (translucent), **Render Face**, **Alpha Clipping**, **Custom Editor GUI**.

Lit graphs render through every InfinityLit pass (depth, GBuffer, forward, motion, shadows, translucent stages, Meta for
baking) with lightmaps, shadowmask, light probes, LPPV, APV and decals. Materials use the Infinity graph material GUI
(routing, subsurface profile and thickness).

## Unlit and Decal

Unlit draws in translucent stage T2. Decal graphs draw into the DBuffer through the projector box (Base Color, Normal,
Metallic, Roughness, Occlusion, Alpha); assign the material to a Decal Projector.

## Limits

Scene Depth is available in decal graphs only, Scene Color in T1 only; the Fog node returns no fog; motion vectors use
rigid object motion (no vertex animation history).
