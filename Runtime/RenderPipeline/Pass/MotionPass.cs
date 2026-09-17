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
        static readonly ProfilingSampler s_UploadViewMotion = new ProfilingSampler("UploadViewMotionTransforms");
        struct ViewMotionUploadData
        {
            public ComputeBuffer buffer;
            public Unity.Mathematics.float4x4[] previous;
        }

        struct ObjectMotionPassData
        {
            public RGDrawListRef draws;
            public RGRendererListRef rendererList;
        }

        struct CameraMotionPassData
        {
            public RGTextureRef depthTexture;
        }

        void RenderMotion(RenderContext renderContext, Camera camera, in MeshView view, in CullingResults cullingResults)
        {
            if (!ShouldRecordFeature(EFrameFeature.Motion))
            {
                return;
            }

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            var previousMatrices = m_ActiveFrameState.meshMotionHistory.Prepare(renderContext.GetMeshScene());
            FBufferRef previousTransforms = m_ActiveFrameState.historyCache.GetBuffer(InfinityShaderIDs.PreviousTransformBuffer,
                new BufferDescriptor(previousMatrices.Length, 64));
            RGBufferRef previousTransformInput;
            using (RGTransferPassRef upload = m_RGBuilder.AddTransferPass<ViewMotionUploadData>(s_UploadViewMotion))
            {
                previousTransformInput = upload.WriteBuffer(m_RGBuilder.ImportBuffer(previousTransforms));
                ref ViewMotionUploadData data = ref upload.GetPassData<ViewMotionUploadData>();
                data.buffer = previousTransforms.buffer; data.previous = previousMatrices;
                upload.SetExecuteFunc((in ViewMotionUploadData data, in RGTransferEncoder commands, RGObjectPool pool) =>
                    commands.SetBufferData(data.buffer, data.previous, 0, 0, data.previous.Length));
            }

            TextureDescriptor motionTextureDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y);
            {
                motionTextureDsc.name = MotionPassUtilityData.MotionTextureName;
                motionTextureDsc.dimension = TextureDimension.Tex2D;
                motionTextureDsc.colorFormat = GraphicsFormat.R16G16_SFloat;
                motionTextureDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef motionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionTextureDsc);
            motionTextureDsc.name = "MotionMetadata";
            motionTextureDsc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            motionTextureDsc.clearColor = Color.clear;
            RGTextureRef motionMetadata = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionMetadataBuffer, motionTextureDsc);

            m_MeshWorld.BindPreviousTransforms(view.viewKey, previousTransforms.buffer);
            RGDrawListRef motionDraws = m_RGBuilder.CreateDrawList(view, MeshPassId.Motion);
            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.MotionPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListRef motionRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderObjectMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.ReadBuffer(previousTransformInput);
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
                passRef.SetExecuteFunc((in ObjectMotionPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.Draw(passData.draws);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            //Add CameraMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<CameraMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderCameraMotion)))
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
                passRef.SetExecuteFunc((in CameraMotionPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.depthTexture);
                    cmdEncoder.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, 2);
                });
            }

            MarkFeatureProduced(EFrameFeature.Motion);
        }

    }
}
