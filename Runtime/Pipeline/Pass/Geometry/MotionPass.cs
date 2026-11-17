using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class MotionPassUtilityData
    {
        internal static readonly string MotionTextureName = "MotionTexture";
        internal static readonly string DepthTextureName = "MotionDepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ObjectMotionPassData
        {
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
        }

        struct CameraMotionPassData
        {
            public RGTextureHandle depthTextureHandle;
            public int shaderPass;
        }

        void RasterMotion(in CameraRecordContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.Motion))
            {
                return;
            }
            RGMeshDrawListHandle motionMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(context.mainCulling, InfinityPassIDs.RasterMotionPass));

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor motionTextureDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                motionTextureDsc.name = MotionPassUtilityData.MotionTextureName;
                motionTextureDsc.dimension = TextureDimension.Tex2D;
                motionTextureDsc.colorFormat = GraphicsFormat.R16G16_SFloat;
            }
            RGTextureHandle motionTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionTextureDsc);
            motionTextureDsc.name = "MotionMetadata";
            motionTextureDsc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            motionTextureDsc.clearColor = Color.clear;
            RGTextureHandle motionMetadataHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionMetadataBuffer, motionTextureDsc);

            RGRendererListHandle motionRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterMotionPass,
                new RenderQueueRange(0, 2999), SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, m_Settings.enableInstanceBatch));

            //Add ObjectMotionPass
            {
                RGRasterPassHandle<ObjectMotionPassData> passHandle = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterObjectMotion));
                //Setup Phase
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(motionTextureHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(motionMetadataHandle, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                // Write marks the depth-stencil resource (stencil 5). Shader ZWrite Off keeps depth values.
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref ObjectMotionPassData passData = ref passHandle.GetPassData();
                {
                    passData.meshDrawListHandle = passHandle.UseMeshDrawList(motionMeshDrawListHandle);
                    passData.rendererListHandle = passHandle.UseRendererList(motionRendererListHandle);
                }

                //Execute Phase
                passHandle.SetExecuteFunc((in ObjectMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }

            //Add CameraMotionPass
            {
                RGRasterPassHandle<CameraMotionPassData> passHandle = m_RGBuilder.AddRasterPass<CameraMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCameraMotion));
                //Setup Phase
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(motionTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(motionMetadataHandle, 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref CameraMotionPassData passData = ref passHandle.GetPassData();
                {
                    passData.depthTextureHandle = depthTextureHandle;
                    passData.shaderPass = blitPasses.cameraMotion;
                }

                //Execute Phase
                passHandle.SetExecuteFunc((in CameraMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.depthTextureHandle);
                    cmdEncoder.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.shaderPass);
                });
            }

            context.features.MarkProduced(EFrameFeature.Motion);
        }

    }
}
