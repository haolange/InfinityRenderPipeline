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
            public RGMeshDrawListHandle meshDrawList;
            public RGRendererListHandle rendererList;
        }

        struct CameraMotionPassData
        {
            public RGTextureHandle depthTexture;
            public int shaderPass;
        }

        void RasterMotion(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle motionMeshDrawList)
        {
            if (!context.features.ShouldRecord(EFrameFeature.Motion))
            {
                return;
            }

            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var previousTransforms = context.state.meshMotionHistory.pendingBinding;

            TextureDescriptor motionTextureDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                motionTextureDsc.name = MotionPassUtilityData.MotionTextureName;
                motionTextureDsc.dimension = TextureDimension.Tex2D;
                motionTextureDsc.colorFormat = GraphicsFormat.R16G16_SFloat;
                motionTextureDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureHandle motionTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionTextureDsc);
            motionTextureDsc.name = "MotionMetadata";
            motionTextureDsc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            motionTextureDsc.clearColor = Color.clear;
            RGTextureHandle motionMetadata = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionMetadataBuffer, motionTextureDsc);

            renderContext.meshWorld.BindPreviousTransforms(view.viewKey, previousTransforms);
            RGRendererListHandle motionRendererList = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterMotionPass,
                new RenderQueueRange(0, 2999), SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, pipelineAsset));

            //Add ObjectMotionPass
            using (var passRef = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterObjectMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionMetadata, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                // Write marks the depth-stencil resource (stencil 5). Shader ZWrite Off keeps depth values.
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref ObjectMotionPassData passData = ref passRef.GetPassData();
                {
                    passData.meshDrawList = passRef.UseMeshDrawList(motionMeshDrawList);
                    passData.rendererList = passRef.UseRendererList(motionRendererList);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ObjectMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            //Add CameraMotionPass
            using (var passRef = m_RGBuilder.AddRasterPass<CameraMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCameraMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionMetadata, 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref CameraMotionPassData passData = ref passRef.GetPassData();
                {
                    passData.depthTexture = depthTexture;
                    passData.shaderPass = blitPasses.cameraMotion;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in CameraMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.depthTexture);
                    cmdEncoder.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.shaderPass);
                });
            }

            context.features.MarkProduced(EFrameFeature.Motion);
        }

    }
}
