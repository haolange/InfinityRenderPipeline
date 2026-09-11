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
        static readonly ProfilingSampler s_UploadNativeMotion = new ProfilingSampler("UploadNativeMotionVertices");
        static readonly ProfilingSampler s_UploadViewMotion = new ProfilingSampler("UploadViewMotionTransforms");
        struct ViewMotionUploadData
        {
            public ComputeBuffer buffer;
            public Unity.Mathematics.float4x4[] previous;
        }

        struct NativeMotionUploadData
        {
            public RGBufferRef buffer;
            public Vector3[] vertices;
        }

        struct ObjectMotionPassData
        {
            public RendererList rendererList;
            public RGDrawListRef draws;
            public RGBufferRef nativeVertices;
        }

        struct CameraMotionPassData
        {
            public RGTextureRef depthTexture;
        }

        void RenderMotion(RenderContext renderContext, Camera camera, MeshVisibilityHandle visibility, in CullingResults cullingResults)
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


            var nativeVertices = m_ActiveFrameState.nativeMotionHistory.PreviousVertices;
            RGBufferRef nativeVertexInput = m_RGScoper.CreateBuffer(InfinityShaderIDs.NativePreviousVertices,
                new BufferDescriptor(nativeVertices.Length, 12));
            using (RGTransferPassRef upload = m_RGBuilder.AddTransferPass<NativeMotionUploadData>(s_UploadNativeMotion))
            {
                ref NativeMotionUploadData data = ref upload.GetPassData<NativeMotionUploadData>();
                data.buffer = upload.WriteBuffer(nativeVertexInput); data.vertices = nativeVertices;
                upload.SetExecuteFunc((in NativeMotionUploadData data, in RGTransferEncoder commands, RGObjectPool pool) =>
                    commands.SetBufferData((ComputeBuffer)data.buffer, data.vertices, 0, 0, data.vertices.Length));
            }

            TextureDescriptor motionTextureDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
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

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.MotionPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.CommonOpaque;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = InfinityDebugDisplaySettings.current.temporal.preferNativeMotionVectors
                    ? PerObjectData.MotionVectors
                    : PerObjectData.None;
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
                backendPolicy = RenderCaptureService.BackendFor(camera),
                shaderPassIndex = BuiltinMeshesPasses.Motion.shaderPassIndex,
                lightModeTag = BuiltinMeshesPasses.Motion.lightModeTag,
                viewPosition = camera.transform.position,
                viewKey = UnityEntityId.ToUInt64(camera),
                previousTransforms = previousTransforms.buffer
            };
            RGDrawListRef motionDraws = m_RGBuilder.DeclareDrawList(m_MotionMeshProcessor, motionRequest, visibility, m_VisibilityShare);

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
                    passData.rendererList = motionRendererList;
                    passData.draws = passRef.UseDrawList(motionDraws);
                    passData.nativeVertices = passRef.ReadBuffer(nativeVertexInput);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ObjectMotionPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    //MeshDrawPipeline
                    cmdEncoder.Draw(passData.draws);

                    //UnityDrawPipeline
                    cmdEncoder.SetGlobalBuffer(InfinityShaderIDs.NativePreviousVertices, passData.nativeVertices);
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
