using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class MotionPassUtilityData
    {
        internal static string MotionTextureName = "MotionTexture";
        internal static string DepthTextureName = "MotionDepthTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ObjectMotionPassData
        {
            public RGDrawListHandle draws;
            public RGRendererListHandle rendererList;
        }

        struct CameraMotionPassData
        {
            public RGTextureHandle depthTexture;
        }

        void RasterMotion(RenderContext renderContext, Camera camera, in MeshView view, in CullingResults cullingResults)
        {
            if (!ShouldRecordFeature(EFrameFeature.Motion))
            {
                return;
            }

            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var previousTransforms = m_ActiveFrameState.meshMotionHistory.pendingBinding;

            TextureDescriptor motionTextureDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                motionTextureDsc.name = MotionPassUtilityData.MotionTextureName;
                motionTextureDsc.dimension = TextureDimension.Tex2D;
                motionTextureDsc.colorFormat = GraphicsFormat.R16G16_SFloat;
                motionTextureDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureHandle motionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionTextureDsc);
            motionTextureDsc.name = "MotionMetadata";
            motionTextureDsc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            motionTextureDsc.clearColor = Color.clear;
            RGTextureHandle motionMetadata = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionMetadataBuffer, motionTextureDsc);

            m_MeshWorld.BindPreviousTransforms(view.viewKey, previousTransforms);
            RGDrawListHandle motionDraws = m_RGBuilder.CreateDrawList(view, MeshPassId.Motion);
            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.RasterMotionPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListHandle motionRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            //Add ObjectMotionPass
            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterObjectMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionMetadata, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                // Write marks the depth-stencil resource (stencil 5). Shader ZWrite Off keeps depth values.
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref ObjectMotionPassData passData = ref passRef.GetPassData<ObjectMotionPassData>();
                {
                    passData.draws = passRef.UseDrawList(motionDraws);
                    passData.rendererList = passRef.UseRendererList(motionRendererList);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ObjectMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.Draw(passData.draws);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            //Add CameraMotionPass
            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<CameraMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCameraMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionMetadata, 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref CameraMotionPassData passData = ref passRef.GetPassData<CameraMotionPassData>();
                {
                    passData.depthTexture = depthTexture;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in CameraMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.depthTexture);
                    cmdEncoder.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 2);
                });
            }

            MarkFeatureProduced(EFrameFeature.Motion);
        }

    }
}
