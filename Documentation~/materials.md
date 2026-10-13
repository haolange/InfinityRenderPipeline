# Materials

InfinityLit inspector groups: Surface Options, Surface Inputs, Subsurface, Advanced / Render State. `ValidateMaterial` is read-only. Pass state is applied only on user edit or shader assign.

Renamed shader properties. Assigning the InfinityLit shader to a material migrates the old names:

- `_NomralTexture` → `_NormalTexture`
- `_PixelDepthOffsetVaule` → `_PixelDepthOffset`

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
  dropped.
