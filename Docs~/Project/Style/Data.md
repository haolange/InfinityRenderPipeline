# Data

Group fields, pass data, shader IDs, and GPU records by column. Put the same kind of member in one group, and align the assignments of that group.

A GPU record keeps its `// GPU-RECORD: <HlslStruct> <hlsl file>` comment on the C# type. The path in that comment is the file path. The HLSL struct name matches the C# type name.

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
