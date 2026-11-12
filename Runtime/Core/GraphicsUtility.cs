using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class GraphicsUtility
    {
        // Unity normalizes the depth argument of ClearRenderTarget / AttachmentDescriptor.clearDepth:
        // 1.0 always means far plane and the backend flips it on reversed-Z platforms.
        internal const float ClearDepthFar = 1.0f;

        // Value the depth buffer actually holds at the far plane when sampled from a shader.
        internal static float SampledFarDepth => SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f;

        // Unity exposes no dedicated "supports memoryless" query. Metal is the tile backend that honors
        // RenderTextureMemoryless; other devices keep an ordinary allocation.
        internal static bool SupportsMemorylessRenderTargets => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;

        internal static bool VolumeComponentActive(VolumeComponent component)
        {
            if (component == null)
            {
                return false;
            }

            if (component is InfinityTech.Rendering.Volume.IInfinityVolumeActivity activity)
            {
                return activity.IsActive();
            }

            return component.active;
        }

        internal static bool HasRequiredKernels(ComputeShader shader, string kernelName) => shader != null && shader.HasKernel(kernelName);

        internal static bool HasRequiredKernels(ComputeShader shader, string[] kernelNames)
        {
            if (shader == null)
            {
                return false;
            }

            if (kernelNames == null || kernelNames.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < kernelNames.Length; ++i)
            {
                if (!shader.HasKernel(kernelNames[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static Material m_BlitMaterial;

        internal static Material BlitMaterial
        {
            get
            {
                return m_BlitMaterial;
            }
        }

        private static Mesh m_FullScreenMesh;

        internal static Mesh FullScreenMesh
        {
            get
            {
                if (m_FullScreenMesh != null)
                {
                    return m_FullScreenMesh;
                }

                m_FullScreenMesh = new Mesh { name = "FullScreen Mesh" };
                m_FullScreenMesh.vertices = new Vector3[] {new Vector3(-1f, -1f, 0f), new Vector3(-1f,  3f, 0f), new Vector3( 3f, -1f, 0f)};
                m_FullScreenMesh.SetIndices(new int[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
                m_FullScreenMesh.UploadMeshData(false);
                return m_FullScreenMesh;
            }
        }

        // Compute UAV / sampled RT y=0 is the top of the image. Reconstructing with
        // GetGPUProjectionMatrix(..., renderIntoTexture: true) maps that row to NDC y = -1,
        // which this GPU projection treats as the bottom of the view. High-elevation sky
        // then samples below-horizon SkyView texels (black). Scene and Game both write
        // depth in the unflipped convention the raster attachments actually store.
        internal static Matrix4x4 GetComputeGpuProjection(Camera camera)
        {
            return GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
        }

        internal static Matrix4x4 GetComputeInvViewProj(Camera camera)
        {
            return (GetComputeGpuProjection(camera) * camera.worldToCameraMatrix).inverse;
        }
    }
}
