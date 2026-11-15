# Data

Group fields, pass data, shader IDs, and GPU records by column. Put the same kind of member in one group, and align the assignments of that group.

A GPU record keeps its `// GPU-RECORD: <HlslStruct> <hlsl file>` comment on the C# type. The path in that comment is the file path. The HLSL struct or cbuffer name matches the C# type name; a pass constant buffer adds `CBV_` (`cbuffer CBV_FogCompositeParameters` for `FogCompositeParameters`). Order record fields so no vector crosses a 16-byte register and the size is a multiple of 16.

Serialized field names stay as they are. Alignment and grouping do not rename a field or change its type.

```csharp
internal struct ShadowInputs
{
    // Atlases

    internal RGTextureHandle directional;
    internal RGTextureHandle local;

    // Shader IDs

    internal static readonly int DirectionalID = Shader.PropertyToID("_DirectionalShadow");
    internal static readonly int LocalID       = Shader.PropertyToID("_LocalShadow");
}
```
