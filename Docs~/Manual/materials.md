# Materials

InfinityLit groups its inspector into Surface Options, Surface Inputs, Baked Emission, and Advanced / Render State. `ValidateMaterial` only checks the material. It does not write pass enables. Pass enables change when you edit the material or assign the shader.

**Surface Route** is Deferred or Forward. Both routes draw the GBuffer. The route also writes the stencil bit that lets deferred shading touch that pixel.

Pipeline stencil uses three bits:

| Bit | Meaning |
|---|---|
| 0 | Object motion |
| 1 | Deferred shading |
| 2 | Subsurface scattering |

Bits 3–7 are free for a custom pass. The pipeline does not read or write them. There is no custom depth target. A pass that only needs to mark pixels uses those five stencil bits.

A Forward material saved before the stencil reference existed still carries the deferred default. Run **Window > Infinity > Materials > Apply Surface Route State To Project Materials** once and save the project. Until then that pixel is shaded twice, first deferred and then forward, and the Surface Stencil view shows it as deferred.

Deferred opaque InfinityLit draws `RasterEmissivePass` when any channel of `_EmissionColor` is above zero. The same menu writes that enable. A script that changes `_EmissionColor` at runtime calls `MaterialRouteUtility.ApplyPassState`. Shader Graph Lit has no `_EmissionColor`. A deferred opaque graph keeps the pass on, and a black emission output adds nothing. The pass tests stencil bit 1, so a stale pass enable cannot add emission to a forward pixel.

## Terrain

Use `InfinityPipeline/TerrainLit` on a Unity Terrain. Its GBuffer pass and distant basemap write depth with the hidden `_ZTest` (LEqual, 4) and `_ZWrite` (On, 1) settings. Additional splat layers use that depth.

To enable a terrain depth prepass, (1) add a pass tagged `LightMode = RasterDepthPass` to the main and basemap shaders and use the same geometry and holes as the GBuffer, (2) switch the GBuffer defaults to `_ZTest = 3` (Equal) and `_ZWrite = 0`, and (3) use the existing native depth renderer list, which already selects that tag. Unity submits native Terrain through that list, outside `MaterialRouteUtility` and the `MeshComponent` pass table. See [Decals](decals.md) for the effects of writing terrain depth in the GBuffer.

## Skin

`InfinityPipeline/InfinitySkin` is the only material with subsurface scattering. It is always Forward opaque and has no route or translucent stage to edit. Inspector groups:

- Surface Inputs: base color texture and color, tile, normal texture and tile, occlusion texture and strength.
- Specular: roughness, specular level (0.35 is the skin reflectance 0.028), Second Lobe Mix and Second Lobe Roughness Scale of the dual-lobe Beckmann specular.
- Subsurface: Diffusion Profile Index 0..14, a slot of the pipeline asset's Diffusion Profile list. The inspector warns when the slot is outside the list or empty; such a skin is shaded but not scattered.

Skin shades completely in the forward pass, reflections included. Scattering then blurs only its diffuse light within the same profile. InfinitySkin does not receive clustered decals. A material that used the removed InfinityLit subsurface controls becomes InfinitySkin: assign the shader and set its profile index.

`InfinityUnlit` and `SRPDefaultUnlit` / untagged passes are accepted in the T2 translucent RendererList.

## Converting materials

`Window > Infinity > Material Conversion Wizard` lists the project's (or the selection's) Built-in Standard / Legacy /
Unlit, URP Lit / Complex Lit / Simple Lit / Unlit and HDRP Lit / Unlit materials and converts the checked ones in place
(Undo-able; references stay valid). `Edit > Rendering > Materials > Convert ... to Current SRP` uses the same converters.

- Base color and its map, normal map, metallic (→ Reflectance), smoothness (→ Roughness = 1 − smoothness), base map
  tiling (x → Base Color Tile) and the emission color (when emission is on) carry over.
- Specular-workflow sources become dielectric with Specular Level from the specular color.
- Transparent sources become translucent stage T0.
- Metallic / mask, occlusion, detail, height and emission maps have no InfinityLit input; the wizard lists them as
  dropped. Skin sources are not converted to InfinitySkin automatically.
