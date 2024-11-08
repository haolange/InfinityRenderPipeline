﻿using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RendererUtils;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfinityTech.Rendering.Pipeline
{
    public partial class InfinityRenderPipeline
    {
#if UNITY_EDITOR
        // WireOverlay Graph
        struct WireOverlayPassData
        {
            public RendererList rendererList;
        }
        
        void RenderWireOverlay(RenderContext renderContext, Camera camera)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

            RendererList wireOverlayRendererList = renderContext.scriptableRenderContext.CreateWireOverlayRendererList(camera);

            // Add WireOverlayPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<WireOverlayPassData>(ProfilingSampler.Get(CustomSamplerId.RenderWireOverlay)))
            {
                //Setup Phase
                passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);

                ref WireOverlayPassData passData = ref passRef.GetPassData<WireOverlayPassData>();
                {
                    passData.rendererList = wireOverlayRendererList;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in WireOverlayPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
        
        // Gizmos Graph
        struct GizmosPassData
        {
            public RendererList rendererList;
        }

        void RenderGizmos(RenderContext renderContext, Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
                RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

                RendererList gizmosRendererList = renderContext.scriptableRenderContext.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);

                // Add GizmosPass
                using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<GizmosPassData>(ProfilingSampler.Get(CustomSamplerId.RenderGizmos)))
                {
                    //Setup Phase
                    passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);

                    ref GizmosPassData passData = ref passRef.GetPassData<GizmosPassData>();
                    {
                        passData.rendererList = gizmosRendererList;
                    }

                    //Execute Phase
                    passRef.SetExecuteFunc((in GizmosPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                    {
                        cmdEncoder.DrawRendererList(passData.rendererList);
                    });
                }
            }
        }
#endif

        // SkyBox Graph
        struct SkyBoxPassData
        {
            public RendererList rendererList;
        }

        void RenderSkyBox(RenderContext renderContext, Camera camera)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef colorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);

            RendererList skyRendererList = renderContext.scriptableRenderContext.CreateSkyboxRendererList(camera);

            // Add SkyBoxPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<SkyBoxPassData>(ProfilingSampler.Get(CustomSamplerId.RenderSkyBox)))
            {
                //Setup Phase
                passRef.SetColorAttachment(colorTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);

                ref SkyBoxPassData passData = ref passRef.GetPassData<SkyBoxPassData>();
                {
                    passData.rendererList = skyRendererList;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in SkyBoxPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }

        // Present Graph
        struct PresentPassData
        {
            public Camera camera;
            public RGTextureRef srcTexture;
        }

        void RenderPresent(RenderContext renderContext, Camera camera)
        {
            RGTextureRef srcTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.AntiAliasingBuffer);

            // Add PresentPass
            using (RGTransferPassRef passRef = m_RGBuilder.AddTransferPass<PresentPassData>(ProfilingSampler.Get(CustomSamplerId.Present)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);

                ref PresentPassData passData = ref passRef.GetPassData<PresentPassData>();
                {
                    passData.camera = camera;
                    passData.srcTexture = passRef.ReadTexture(srcTexture);
                }

                //Execute Phase
                passRef.SetExecuteFunc((in PresentPassData passData, in RGTransferEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    RenderTexture srcBuffer = passData.srcTexture;
                    float4 scaleBias = new float4(passData.camera.pixelWidth / (float)srcBuffer.width, passData.camera.pixelHeight / (float)srcBuffer.height, 0.0f, 0.0f);
                    if (!passData.camera.targetTexture)
                    {
                        scaleBias.w = scaleBias.y;
                        scaleBias.y *= -1;
                    }
                    cmdEncoder.Present(passData.camera.cameraType != CameraType.SceneView, GraphicsUtility.GetViewport(passData.camera), scaleBias, srcBuffer);
                });
            }
        }
    }
}
