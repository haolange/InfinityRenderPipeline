using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Core
{
    // Device conventions every rendering layer reads: depth clear and sampled far values, memoryless support, the compute projection.
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

        // Compute UAV / sampled RT y=0 is the top of the image. Reconstructing with
        // GetGPUProjectionMatrix(..., renderIntoTexture: true) maps that row to NDC y = -1,
        // which this GPU projection treats as the bottom of the view. High-elevation sky
        // then samples below-horizon SkyView texels (black). Scene and Game both write
        // depth in the unflipped convention the raster attachments actually store.
        public static Matrix4x4 GetComputeGpuProjection(Camera camera)
        {
            return GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
        }
    }
}
