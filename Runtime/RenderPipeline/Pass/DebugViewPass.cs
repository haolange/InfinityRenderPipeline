using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DebugViewPassUtilityData
    {
        internal static readonly string[] RequiredKernels =
        {
            "DebugViewGBuffer",
            "DebugViewMotion",
            "DebugViewSceneColor",
            "DebugViewOptional",
            "DebugViewMissing"
        };

        internal static int DV_ResolutionID = Shader.PropertyToID("DV_Resolution");
        internal static int DV_ModeID = Shader.PropertyToID("DV_Mode");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static int SRV_OptionalTextureID = Shader.PropertyToID("SRV_OptionalTexture");
        internal static int UAV_PostProcessTextureID = Shader.PropertyToID("UAV_PostProcessTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct DebugViewPassData
        {
            public int2 resolution;
            public int mode;
            public int kernelIndex;
            public int bindSet;
            public ComputeShader debugViewShader;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef motionTexture;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef optionalTexture;
            public RGTextureRef postProcessTexture;
        }

        void ComputeDebugView(Camera camera)
        {
            EDebugView view = pipelineAsset.debugView;
            if (view == EDebugView.None)
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.debugViewShader, DebugViewPassUtilityData.RequiredKernels))
            {
                throw new InvalidOperationException("InfinityRP: DebugView is active but debugViewShader is missing required kernels (DebugViewGBuffer/Motion/SceneColor/Optional/Missing).");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.PostProcessBuffer, out RGTextureRef postProcessTexture))
            {
                throw new InvalidOperationException("InfinityRP: DebugView requires PostProcessBuffer, which was never registered this frame.");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.DepthBuffer, out _))
            {
                throw new InvalidOperationException("InfinityRP: DebugView requires DepthBuffer, which was never registered this frame.");
            }

            string kernelName;
            int bindSet;
            bool needGBuffer = false;
            bool needMotion = false;
            bool needSceneColor = false;
            bool needOptional = false;
            RGTextureRef gBufferA = default;
            RGTextureRef gBufferB = default;
            RGTextureRef gBufferC = default;
            RGTextureRef motionTexture = default;
            RGTextureRef sceneColorTexture = default;
            RGTextureRef optionalTexture = default;

            switch (view)
            {
                case EDebugView.Albedo:
                case EDebugView.Normal:
                case EDebugView.Roughness:
                    kernelName = "DebugViewGBuffer";
                    bindSet = 0;
                    needGBuffer = true;
                    RequireTexture(InfinityShaderIDs.GBufferA, "GBufferA", view, out gBufferA);
                    RequireTexture(InfinityShaderIDs.GBufferB, "GBufferB", view, out gBufferB);
                    RequireTexture(InfinityShaderIDs.GBufferC, "GBufferC", view, out gBufferC);
                    break;
                case EDebugView.AO:
                    if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out optionalTexture))
                    {
                        kernelName = "DebugViewOptional";
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    break;
                case EDebugView.SSR:
                    if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.SSRBuffer, out optionalTexture))
                    {
                        kernelName = "DebugViewOptional";
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    break;
                case EDebugView.SSGI:
                    if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.SSGIBuffer, out optionalTexture))
                    {
                        kernelName = "DebugViewOptional";
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    break;
                case EDebugView.MotionMagnitude:
                    kernelName = "DebugViewMotion";
                    bindSet = 1;
                    needMotion = true;
                    RequireTexture(InfinityShaderIDs.MotionBuffer, "MotionBuffer", view, out motionTexture);
                    break;
                case EDebugView.TAAConfidence:
                    if (pipelineAsset.enableSuperResolution)
                    {
                        throw new InvalidOperationException("InfinityRP: DebugView TAAConfidence requires the TAA path (enableSuperResolution is on).");
                    }

                    kernelName = "DebugViewOptional";
                    bindSet = 3;
                    needOptional = true;
                    RequireTexture(InfinityShaderIDs.TAAConfidenceBuffer, "TAAConfidenceBuffer", view, out optionalTexture);
                    break;
                case EDebugView.PreTonemapLuma:
                    kernelName = "DebugViewSceneColor";
                    bindSet = 2;
                    needSceneColor = true;
                    if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.AntiAliasingBuffer, out sceneColorTexture) &&
                        !m_RGScoper.TryQueryTexture(InfinityShaderIDs.SuperResolutionBuffer, out sceneColorTexture) &&
                        !m_RGScoper.TryQueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer, out sceneColorTexture))
                    {
                        throw new InvalidOperationException("InfinityRP: DebugView PreTonemapLuma requires AntiAliasingBuffer/SuperResolutionBuffer/FoggedSceneColorBuffer, none of which were registered this frame.");
                    }
                    break;
                default:
                    throw new InvalidOperationException($"InfinityRP: DebugView '{view}' is not implemented.");
            }

            int kernelIndex = pipelineAsset.debugViewShader.FindKernel(kernelName);
            if (kernelIndex < 0)
            {
                throw new InvalidOperationException($"InfinityRP: DebugView is active but FindKernel({kernelName}) failed.");
            }

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<DebugViewPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDebugView)))
            {
                ref DebugViewPassData passData = ref passRef.GetPassData<DebugViewPassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.mode = (int)view;
                passData.kernelIndex = kernelIndex;
                passData.bindSet = bindSet;
                passData.debugViewShader = pipelineAsset.debugViewShader;
                if (needGBuffer)
                {
                    passData.gBufferA = passRef.ReadTexture(gBufferA);
                    passData.gBufferB = passRef.ReadTexture(gBufferB);
                    passData.gBufferC = passRef.ReadTexture(gBufferC);
                }

                if (needMotion)
                {
                    passData.motionTexture = passRef.ReadTexture(motionTexture);
                }

                if (needSceneColor)
                {
                    passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                }

                if (needOptional)
                {
                    passData.optionalTexture = passRef.ReadTexture(optionalTexture);
                }

                passRef.ReadTexture(postProcessTexture);
                passData.postProcessTexture = passRef.WriteTexture(postProcessTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DebugViewPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.debugViewShader;
                    int kernel = passData.kernelIndex;
                    cmdEncoder.SetComputeVectorParam(shader, DebugViewPassUtilityData.DV_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(shader, DebugViewPassUtilityData.DV_ModeID, passData.mode);

                    if (passData.bindSet == 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    }
                    else if (passData.bindSet == 1)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_MotionTextureID, passData.motionTexture);
                    }
                    else if (passData.bindSet == 2)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    }
                    else if (passData.bindSet == 3)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_OptionalTextureID, passData.optionalTexture);
                    }

                    cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.UAV_PostProcessTextureID, passData.postProcessTexture);
                    cmdEncoder.DispatchCompute(shader, kernel, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }

        void RequireTexture(int id, string idName, EDebugView view, out RGTextureRef texture)
        {
            if (!m_RGScoper.TryQueryTexture(id, out texture))
            {
                throw new InvalidOperationException($"InfinityRP: DebugView '{view}' requires {idName}, which was never registered this frame.");
            }
        }
    }
}
