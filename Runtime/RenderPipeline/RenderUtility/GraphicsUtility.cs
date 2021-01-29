using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    public static class GraphicsUtility
    {
        public static Material BlitMaterial
        {
            get
            {
                return new Material(Shader.Find("InfinityPipeline/Utility/InfinityDrawFullScreen"));
            }
        }

        public static Mesh FullScreenMeshOrigin;

        public static Mesh FullScreenMesh
        {
            get
            {
                if (FullScreenMeshOrigin != null) {
                    return FullScreenMeshOrigin;
                }

                FullScreenMeshOrigin = new Mesh { name = "FullScreen Mesh" };

                FullScreenMeshOrigin.vertices = new Vector3[] {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(-1f,  3f, 0f),
                    new Vector3( 3f, -1f, 0f)
                };

                FullScreenMeshOrigin.SetIndices(new int[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
                FullScreenMeshOrigin.UploadMeshData(false);
                return FullScreenMeshOrigin;
            }
        }

        public static void SetRenderTarget(CommandBuffer cmd, RenderTargetIdentifier colorAttachment, RenderBufferLoadAction colorLoadAction, RenderBufferStoreAction colorStoreAction, ClearFlag clearFlags, Color clearColor, TextureDimension dimension)
        {
            if (dimension == TextureDimension.Tex2DArray) {
                CoreUtils.SetRenderTarget(cmd, colorAttachment, clearFlags, clearColor, 0, CubemapFace.Unknown, -1);
            } else {
                CoreUtils.SetRenderTarget(cmd, colorAttachment, colorLoadAction, colorStoreAction, clearFlags, clearColor);
            }   
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RTHandle Source, RenderTargetIdentifier Desc)
        {
            CmdBuffer.SetRenderTarget(Desc);
            CmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, Source);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, 1);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RTHandle Source, RenderTargetIdentifier Desc, MaterialPropertyBlock MaterialPropertyBlock = null)
        {
            CmdBuffer.SetRenderTarget(Desc);
            MaterialPropertyBlock.SetTexture(InfinityShaderIDs.RT_MainTexture, Source);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, 1, MaterialPropertyBlock);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier Source, RenderTargetIdentifier Desc)
        {
            CmdBuffer.SetRenderTarget(Desc);
            CmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, Source);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, 0);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier Source, RenderTargetIdentifier Desc, int DrawPass)
        {
            CmdBuffer.SetRenderTarget(Desc);
            CmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, Source);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, DrawPass);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, Rect Viewport, RenderTargetIdentifier Source, RenderTargetIdentifier Desc, int DrawPass)
        {
            CmdBuffer.SetRenderTarget(Desc);
            CmdBuffer.SetGlobalTexture(InfinityShaderIDs.RT_MainTexture, Source);
            CmdBuffer.SetViewport(Viewport);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, BlitMaterial, 0, DrawPass);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier ColorBuffer, Material DrawMaterial, int DrawPass)
        {
            CmdBuffer.SetRenderTarget(ColorBuffer);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, DrawMaterial, 0, DrawPass);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier ColorBuffer, Material DrawMaterial, int DrawPass, MaterialPropertyBlock MaterialPropertyBlock = null)
        {
            CmdBuffer.SetRenderTarget(ColorBuffer);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, DrawMaterial, 0, DrawPass, MaterialPropertyBlock);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier ColorBuffer, RenderTargetIdentifier DepthBuffer, Material DrawMaterial, int DrawPass)
        {
            CmdBuffer.SetRenderTarget(ColorBuffer, DepthBuffer);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, DrawMaterial, 0, DrawPass);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier ColorBuffer, RenderTargetIdentifier DepthBuffer, Material DrawMaterial, int DrawPass, MaterialPropertyBlock MaterialPropertyBlock = null)
        {
            CmdBuffer.SetRenderTarget(ColorBuffer, DepthBuffer);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, DrawMaterial, 0, DrawPass, MaterialPropertyBlock);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier[] ColorBuffers, RenderTargetIdentifier DepthBuffer, Material DrawMaterial, int DrawPass)
        {
            CmdBuffer.SetRenderTarget(ColorBuffers, DepthBuffer);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, DrawMaterial, 0, DrawPass);
        }

        public static void DrawFullScreen(this CommandBuffer CmdBuffer, RenderTargetIdentifier[] ColorBuffers, RenderTargetIdentifier DepthBuffer, Material DrawMaterial, int DrawPass, MaterialPropertyBlock MaterialPropertyBlock = null)
        {
            CmdBuffer.SetRenderTarget(ColorBuffers, DepthBuffer);
            CmdBuffer.DrawMesh(FullScreenMesh, Matrix4x4.identity, DrawMaterial, 0, DrawPass, MaterialPropertyBlock);
        }

        public static Rect GetViewport(Camera RenderCamera)
        {
            return new Rect(RenderCamera.pixelRect.x, RenderCamera.pixelRect.y, RenderCamera.pixelWidth, RenderCamera.pixelHeight);
        }
    }
}
