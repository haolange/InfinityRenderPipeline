# Materials

InfinityLit inspector groups: Surface Options, Surface Inputs, Subsurface, Advanced / Render State. `ValidateMaterial` is read-only. Pass state is applied only on user edit or shader assign.

Renamed shader properties. Assigning the InfinityLit shader to a material migrates the old names:

- `_NomralTexture` → `_NormalTexture`
- `_PixelDepthOffsetVaule` → `_PixelDepthOffset`

`InfinityUnlit` and `SRPDefaultUnlit` / untagged passes are accepted in the T2 translucent RendererList.
