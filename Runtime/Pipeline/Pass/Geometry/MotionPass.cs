using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class MotionPassUtilityData
    {
        internal static readonly string MotionTextureName = "MotionTexture";
        internal static readonly string DepthTextureName = "MotionDepthTexture";
    }

    internal sealed class MotionPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly MeshPassId m_MotionMeshPass;

        internal MotionPass(RGBuilder graph, InfinityRenderPipelineResources resources, in InfinityRenderPipelineAsset.Settings settings, MeshPassId motionMeshPass)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_Settings = settings;
            m_MotionMeshPass = motionMeshPass;
        }

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

        // viewHandle carries the camera's mesh motion, which the motion pass requires.
        internal void RasterMotion(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            if (!context.features.ShouldRecord(EFrameFeature.Motion))
            {
                return;
            }
            RGMeshDrawListHandle motionMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_MotionMeshPass);

            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor motionTextureDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                motionTextureDsc.name = MotionPassUtilityData.MotionTextureName;
                motionTextureDsc.dimension = TextureDimension.Tex2D;
                motionTextureDsc.colorFormat = EffectivePipelineConfiguration.MotionFormat;
            }
            RGTextureHandle motionTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionTextureDsc);

            RGRendererListHandle motionRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterMotionPass,
                new RenderQueueRange(0, 2999), SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, m_Settings.enableInstanceBatch));

            //Add ObjectMotionPass
            {
                RGRasterPassHandle<ObjectMotionPassData> passHandle = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RasterObjectMotion));
                //Setup Phase
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(motionTextureHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                // Shader ZWrite Off keeps depth values while writing stencil bit 0 (ObjectMotion).
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnlyDepth | EDepthAccess.WriteStencil);

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
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref CameraMotionPassData passData = ref passHandle.GetPassData();
                {
                    passData.depthTextureHandle = depthTextureHandle;
                    passData.shaderPass = m_Resources.blitPasses.cameraMotion;
                }

                //Execute Phase
                passHandle.SetExecuteFunc((in CameraMotionPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.MainTexture, passData.depthTextureHandle);
                    cmdEncoder.DrawProcedural(Matrix4x4.identity, GraphicsUtility.BlitMaterial, passData.shaderPass, MeshTopology.Triangles, 3);
                });
            }

            context.features.MarkProduced(EFrameFeature.Motion);
        }

    }
}
