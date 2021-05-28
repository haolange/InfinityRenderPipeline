using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class GraphicsUtility
    {
        private static Material m_BlitMaterial;

        internal static Material BlitMaterial
        {
            get
            {
                if (m_BlitMaterial != null) { return m_BlitMaterial; }

                m_BlitMaterial = new Material(Shader.Find("InfinityPipeline/Utility/DrawFullScreen"));
                return m_BlitMaterial;
            }
        }

        private static Mesh m_FullScreenMesh;

        internal static Mesh FullScreenMesh
        {
            get
            {
                if (m_FullScreenMesh != null) { return m_FullScreenMesh; }

                m_FullScreenMesh = new Mesh { name = "FullScreen Mesh" };
                m_FullScreenMesh.vertices = new Vector3[] {new Vector3(-1f, -1f, 0f), new Vector3(-1f,  3f, 0f), new Vector3( 3f, -1f, 0f)};
                m_FullScreenMesh.SetIndices(new int[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
                m_FullScreenMesh.UploadMeshData(false);
                return m_FullScreenMesh;
            }
        }

        public static void SetRenderTarget(CommandBuffer cmdBuffer, RenderTargetIdentifier colorAttachment, RenderBufferLoadAction colorLoadAction, RenderBufferStoreAction colorStoreAction, ClearFlag clearFlags, Color clearColor, TextureDimension dimension)
        {
            if (dimension == TextureDimension.Tex2DArray) {
                CoreUtils.SetRenderTarget(cmdBuffer, colorAttachment, clearFlags, clearColor, 0, CubemapFace.Unknown, -1);
            } else {
                CoreUtils.SetRenderTarget(cmdBuffer, colorAttachment, colorLoadAction, colorStoreAction, clearFlags, clearColor);
            }   
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RTHandle src, RenderTargetIdentifier dsc)
        {
            cmdBuffer.SetRenderTarget(dsc);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, src);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, 1);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RTHandle src, RenderTargetIdentifier dsc, MaterialPropertyBlock materialPropertyBlock = null)
        {
            cmdBuffer.SetRenderTarget(dsc);
            materialPropertyBlock.SetTexture(InfinityShaderIDs.RT_MainTexture, src);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, 1, materialPropertyBlock);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier src, RenderTargetIdentifier dsc)
        {
            cmdBuffer.SetRenderTarget(dsc);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, src);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, 0);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier src, RenderTargetIdentifier dsc, int passIndex)
        {
            cmdBuffer.SetRenderTarget(dsc);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, src);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, passIndex);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, Rect viewport, RenderTargetIdentifier src, RenderTargetIdentifier dsc, int passIndex)
        {
            cmdBuffer.SetRenderTarget(dsc);
            cmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, src);
            cmdBuffer.SetViewport(viewport);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, passIndex);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier colorBuffer, Material material, int passIndex)
        {
            cmdBuffer.SetRenderTarget(colorBuffer);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier colorBuffer, Material material, int passIndex, MaterialPropertyBlock materialPropertyBlock = null)
        {
            cmdBuffer.SetRenderTarget(colorBuffer);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex, materialPropertyBlock);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthBuffer, Material material, int passIndex)
        {
            cmdBuffer.SetRenderTarget(colorBuffer, depthBuffer);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthBuffer, Material material, int passIndex, MaterialPropertyBlock materialPropertyBlock = null)
        {
            cmdBuffer.SetRenderTarget(colorBuffer, depthBuffer);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex, materialPropertyBlock);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier[] colorBuffers, RenderTargetIdentifier depthBuffer, Material material, int passIndex)
        {
            cmdBuffer.SetRenderTarget(colorBuffers, depthBuffer);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex);
        }

        public static void DrawFullScreen(this CommandBuffer cmdBuffer, RenderTargetIdentifier[] colorBuffers, RenderTargetIdentifier depthBuffer, Material material, int passIndex, MaterialPropertyBlock materialPropertyBlock = null)
        {
            cmdBuffer.SetRenderTarget(colorBuffers, depthBuffer);
            cmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex, materialPropertyBlock);
        }

        public static Rect GetViewport(Camera camera)
        {
            return new Rect(camera.pixelRect.x, camera.pixelRect.y, camera.pixelWidth, camera.pixelHeight);
        }
    }
}
