using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Core
{
    // Device conventions every rendering layer reads: depth clear and sampled far values, memoryless support.
    public static class GraphicsConventions
    {
        // Unity normalizes the depth argument of ClearRenderTarget / AttachmentDescriptor.clearDepth:
        // 1.0 always means far plane and the backend flips it on reversed-Z platforms.
        public const float ClearDepthFar = 1.0f;

        // Value the depth buffer actually holds at the far plane when sampled from a shader.
        public static float SampledFarDepth => SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f;

        // Unity exposes no dedicated "supports memoryless" query. Metal is the tile backend that honors
        // RenderTextureMemoryless; other devices keep an ordinary allocation.
        public static bool SupportsMemorylessRenderTargets => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;
    }
}
