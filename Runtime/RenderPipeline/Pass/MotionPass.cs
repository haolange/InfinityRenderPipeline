using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
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
            public RendererList rendererList;
            public RGDrawListRef draws;
        }

        struct CameraMotionPassData
        {
            public RGTextureRef depthTexture;
        }

        void RenderMotion(RenderContext renderContext, Camera camera, MeshVisibilityHandle visibility, in CullingResults cullingResults)
        {
            camera.depthTextureMode |= DepthTextureMode.MotionVectors | DepthTextureMode.Depth;

            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor motionTextureDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                motionTextureDsc.name = MotionPassUtilityData.MotionTextureName;
                motionTextureDsc.dimension = TextureDimension.Tex2D;
                motionTextureDsc.colorFormat = GraphicsFormat.R16G16_SFloat;
                motionTextureDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef motionTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.MotionBuffer, motionTextureDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.MotionPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                rendererListDesc.renderingLayerMask = (uint)ERenderingLayer.Everything;
                rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList motionRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            MeshFilterProgram motionFilter = BuiltinMeshesPasses.Motion.defaultFilter;
            motionFilter.layerMask = camera.cullingMask;
            motionFilter.renderingLayerMask = (uint)ERenderingLayer.Everything;
            var motionRequest = new MeshDrawRequest
            {
                filter = motionFilter,
                sort = BuiltinMeshesPasses.Motion.defaultSort,
                backendPolicy = EMeshBackendPolicy.Auto,
                shaderPassIndex = BuiltinMeshesPasses.Motion.shaderPassIndex,
                viewPosition = camera.transform.position,
                renderingLayerMask = motionFilter.renderingLayerMask,
                viewKey = UnityEntityId.ToUInt64(camera)
            };
            RGDrawListRef motionDraws = m_RGBuilder.DeclareDrawList(m_MotionMeshProcessor, motionRequest, visibility, m_VisibilityShare);

            //Add ObjectMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ObjectMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderObjectMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                // Write marks the depth-stencil resource (stencil 5). Shader ZWrite Off keeps depth values.
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref ObjectMotionPassData passData = ref passRef.GetPassData<ObjectMotionPassData>();
                {
                    passData.rendererList = motionRendererList;
                    passData.draws = passRef.UseDrawList(motionDraws);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ObjectMotionPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    cmdEncoder.Draw(passData.draws);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            //Add CameraMotionPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<CameraMotionPassData>(ProfilingSampler.Get(CustomSamplerId.RenderCameraMotion)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(motionTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);

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
        }

    }
}
