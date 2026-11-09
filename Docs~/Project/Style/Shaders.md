# Shaders

Shader files use the same sections as Page. Leave one blank line before a section. The first line is a short label comment. Write one statement on a line. The branch of an `if`, an `else`, and a `switch` each occupies its own line.

Do not use a C# `#region` in a shader. The shader compiler does not remove it before compilation.

`Shader "InfinityPipeline/..."` is the shader name. A file move does not change that name. An `#include` uses the file path after the move. A `GPU-RECORD` comment uses that same file path.

A multi-line macro invocation or a signature that cannot fit one line without breaking the language stays broken after a parameter. That break is still one statement. Do not reorder declarations or change a kernel's thread count for layout.

```hlsl
// Reconstruct

float2 uv = (id.xy + 0.5) * texelSize;
float depth = DepthTexture.Load(int3(id.xy, 0)).r;

if (depth == 0.0)
{
    return;
}
```
