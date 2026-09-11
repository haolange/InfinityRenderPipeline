# Materials

InfinityLit inspector groups: Surface Options, Surface Inputs, Subsurface, Advanced / Render State. `ValidateMaterial` is read-only. Pass state is applied only on user edit or shader assign.

Renamed shader properties (explicit migration on the Mac):

- `_NomralTexture` → `_NormalTexture`
- `_PixelDepthOffsetVaule` → `_PixelDepthOffset`

`InfinityUnlit` and `SRPDefaultUnlit` / untagged passes are accepted in the T2 translucent RendererList.
