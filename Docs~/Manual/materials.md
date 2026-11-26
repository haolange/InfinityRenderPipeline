# Materials

InfinityLit inspector groups: Surface Options, Surface Inputs, Baked Emission, Advanced / Render State. `ValidateMaterial` is read-only. Pass state is applied only on user edit or shader assign. Surface Route selects Deferred or Forward; both draw the GBuffer, and the material writes the stencil bit that admits a deferred pixel to deferred shading (applied with the route).

## Skin

`InfinityPipeline/InfinitySkin` is the only material with subsurface scattering. It is always Forward opaque and has no route or translucent stage to edit. Inspector groups:

- Surface Inputs: base color texture and color, tile, normal texture and tile, occlusion texture and strength.
- Specular: roughness, specular level (0.35 is the skin reflectance 0.028), Second Lobe Mix and Second Lobe Roughness Scale of the dual-lobe Beckmann specular.
- Subsurface: Diffusion Profile Index 0..14, a slot of the pipeline asset's Diffusion Profile list. The inspector warns when the slot is outside the list or empty; such a skin is shaded but not scattered.

Skin shades completely in the forward pass, reflections included; scattering then blurs only its diffuse light within the same profile. A material that used the removed InfinityLit subsurface controls becomes InfinitySkin: assign the shader and set its profile index.

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
