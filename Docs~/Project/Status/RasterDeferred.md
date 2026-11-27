# Raster deferred shading

This page explains the opaque color path as it stands on 2026-11-27. The rules live in [ARCHITECTURE.md](../../../ARCHITECTURE.md) §8 and §16. The pass and fail counts live in [Feature status](FeatureStatus.md). If a number here and a number there disagree, Feature status is the record.

Authoring steps are in [Materials](../../Manual/materials.md), [Decals](../../Manual/decals.md) and [Baked lighting](../../Manual/baked-gi.md).

## In one pass

A deferred pixel is no longer lit by a compute shader that splits the color into indirect and emission.

The geometry draw stores indirect light only. A full-screen triangle then adds direct light, reflections and screen-space indirect on pixels marked deferred. Emission is a later additive draw. Forward materials, skin included, still write their whole color in one draw.

## What happens to one pixel

1. The depth prepass clears stencil to 0.
2. DBuffer decals, then any direct decal aimed at the DBuffer, paint the surface before the GBuffer exists. Baked light and forward materials see that paint.
3. The GBuffer draw writes the surface and the indirect color. Clustered decals are applied in that draw, before the indirect color is stored. The draw also marks stencil bit 1 for a deferred material and bit 2 for skin.
4. A direct decal aimed at the GBuffer changes the surface after that indirect color exists.
5. Object motion marks stencil bit 0.
6. Ambient occlusion, shadows, screen-space reflection and screen-space indirect diffuse run. They do not write the final color yet.
7. Deferred shading draws one triangle over pixels whose stencil bit 1 is set.
8. `RasterEmissivePass` adds emission on deferred materials that have some.
9. Forward materials, including InfinitySkin, replace their pixels with a complete shade and record how much of that shade is diffuse.
10. Skin scattering blurs that diffuse part on pixels whose stencil bit 2 is set.

Empty passes do not appear in the Frame Debugger. On Scene_Spazon the emissive pass and the forward pass recorded no draws: no deferred material had a nonzero emission color, and that scene produced no forward draw.

## What is stored

| Buffer | What it holds after the geometry draw |
|---|---|
| GBuffer A | Albedo and roughness |
| GBuffer B | Octahedral normal, metallic, specular level |
| Scene Color | Indirect diffuse only: diffuse color × lightmap, probes or sky × material occlusion. No emission. |
| Sideband R | 0 on a deferred pixel. Forward shading later writes the diffuse share, which only skin scattering reads. |
| Sideband G and B | Baked visibility of the first two mixed directional lights |
| Sideband A | Four light channels, and the skin profile |
| Stencil | Bit 0 motion, bit 1 deferred, bit 2 subsurface. Bits 3–7 are left for a user pass. |

The sideband exists only when something reads it: rendering layers, a mixed light with baked visibility, or subsurface scattering. Without it, readers assume full visibility, every light channel, no profile and a diffuse share of 0.

Scene Color stays a load/store texture. The atmosphere still reads and writes that same texture in place. Deferred shading does not. It blends onto the color already stored there.

## How the deferred blend works

The shader does not sample Scene Color. The hardware blend does the mix.

The triangle outputs:

- RGB: direct light, plus environment reflections, plus albedo × screen-space indirect.
- Alpha: occlusion × (1 − screen-space indirect confidence).

Occlusion is GTAO or RTAO, times analytic indirect occlusion. With screen-space indirect off, its confidence is 0, so alpha is the occlusion. The blend then keeps `indirect × occlusion` and adds the direct light and the reflection. An alpha of 0 would throw the stored indirect light away. The pass does not do that.

Screen-space reflection replaces the environment term by its own confidence. Pixels without stencil bit 1 are not drawn, so a forward pixel is left for the forward pass.

Emission is the next pass, additive, depth-equal, and only for a deferred material that should emit. Ambient occlusion and screen-space indirect never scale it. A pure unlit emissive material stays on the forward or unlit path. Forward shading still includes its own emission in the same draw.

InfinityLit turns the emissive pass on when any channel of `_EmissionColor` is above zero. Shader Graph Lit has no such color, so a deferred opaque graph keeps the pass on and a black output adds nothing. Terrain does not draw this pass. Its emission color is for the lightmapper only.

A newly added shader pass starts enabled. Forward materials would add emission twice until **Window > Infinity > Materials > Apply Surface Route State To Project Materials** runs. A script that changes `_EmissionColor` calls `MaterialRouteUtility.ApplyPassState`.

## Decals

Two mechanisms stay separate.

**Clustered** decals are a volume lookup inside Lit, Shader Graph Lit and terrain. They run while the GBuffer stores indirect light, and again in forward Lit before that shade. Indirect light follows the decaled albedo. InfinitySkin does not receive them.

**Direct** decals are draws. The decal material's `_DecalTarget` picks the pass:

- DBuffer, before the GBuffer. Lightmaps, per-object probes and forward materials follow the decal.
- GBuffer, after the GBuffer. Hardware can blend albedo and metallic. It cannot also blend roughness or specular, because those live in the same alpha channel as coverage. A separate clip pass, used when Replace Normal is on, replaces the whole sample including the normal. Soft normal edges belong on the DBuffer target. A GBuffer-target decal leaves the baked indirect color of the surface underneath. It does not retint that color.

A clustered decal component turns its MeshRenderer off, so a shared material is not also drawn as a direct decal.

## One lit shader

`InfinityLit` and `InfinityLit-Instanced` are one shader. Mesh entities draw with procedural instancing (`SetupInfinityInstance`): the instance table supplies the matrices, the previous matrix, the light channels and the LOD fade, and indirect light comes from Adaptive Probe Volumes. An ordinary Renderer stays on the SRP Batcher, with lightmaps, light probes and the renderer motion path. Its material keeps GPU instancing off. Turning instancing on would run the procedural setup and break that draw.

The instanced shader file is gone.

## What is not built

These are absent. They are not a hidden mode.

- **Custom depth.** No extra depth target is allocated. A user pass that only needs to mark pixels uses stencil bits 3–7. The pipeline's own stencil masks stay inside bits 0–2. A mask left off a stencil block is treated as 255 and fails the static check, because that would write the user bits.
- **A project irradiance mode.** The asset has no Baked / APV / SSGI / ray-traced switch, no half-resolution irradiance target, and no keyword that strips the geometry write. Geometry indirect light is the lightmap, the probes or the sky. Screen-space indirect diffuse is a shading effect on top of that term. It is not a project mode, and it does not turn lightmaps off.

## What was checked

Unity 6000.6.0f1, Apple M3 Max, Metal. The counts are in [Feature status](FeatureStatus.md).

Passed: static checks, C# compile, the deferred and lit and decal shaders, the deferred GPU tests, the route and decal tests, the Scene_Spazon Game view, and the frame tree. The frame tree shows `RasterDeferredShading` as a three-vertex draw immediately after screen-space indirect diffuse. `ComputeDeferredShading` and `InfinityLit-Instanced` are absent.

The full EditMode suite is not green. After the decal-test fix, the remaining failures are `ShaderStrippingTests`, and they stop because `ShaderUtil.ShaderHasError` logs `attempt to write a readonly database`. The project Library is on exFAT (`/Volumes/DataDisk`). No assertion inside those tests failed. The same suite will fail the same way until the project is on a filesystem Unity's database can write. That is not a product change to paper over.

GPU time against `46139510` was not measured.

D3D12 and Vulkan were not run. This machine does not have them.

## What landed

Package `main`, oldest first:

| Commit | What it did |
|---|---|
| `071a3423` | Reserved stencil bits 3–7 for user passes |
| `579f397f` | Drew mesh entities through procedural instancing on InfinityLit |
| `becbbbc5` | Applied clustered decals while the GBuffer stores indirect diffuse |
| `cb7b7c54` | Selected the decal draw pass from the material target |
| `95b5f375` | Shaded deferred pixels with a stencil blend and added emission afterwards |
| `a9651337` | Destroyed decal test materials before their GameObjects |
| `da5118a5` | Recorded the verification and the current color path |
| `05e7189a` | Recorded the Spazon frame tree and Game view |

Example project `main`:

| Commit | What it did |
|---|---|
| `8919d96` | Clamped Scene_Spazon light channels from `0x21` to `0x01` and saved only that scene |
| `5b65a36` | Pointed mesh-instance materials at InfinityLit |
| `c66ee7d` | Pointed Global Settings `deferredShadingShader` at `Shaders/Pass/Lighting/DeferredShading.shader` |

The open Editor also applied pass state, including the emissive pass, to the routed materials under `Assets/Material` and `Assets/Material/Spazon`, plus `Assets/Animation/Anim_MAT.mat`. Those files were already locally modified before this work, so they were not committed. A domain reload without that save would leave the new emissive pass enabled on forward materials and add emission twice. Other dirty files in the Example project (`AtmosphericalProfile`, `RenderPipelineAsset`, and two ProjectSettings assets) were already dirty and were not part of this change.
