using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DebugViewPassUtilityData
    {
        internal static readonly int DV_ResolutionID = Shader.PropertyToID("DV_Resolution");
        internal static readonly int DV_ModeID = Shader.PropertyToID("DV_Mode");
        internal static readonly int DV_ParamsID = Shader.PropertyToID("DV_Params");
        internal static readonly int DV_FarDepthID = Shader.PropertyToID("DV_FarDepth");
        internal static readonly int SRV_ProbeAtlasTextureID = Shader.PropertyToID("SRV_ProbeAtlasTexture");
        internal static readonly int SRV_OptionalTextureID = Shader.PropertyToID("SRV_OptionalTexture");
        internal static readonly int SRV_ShadowAtlasTextureID = Shader.PropertyToID("SRV_ShadowAtlasTexture");
        internal static readonly int UAV_PostProcessTextureID = Shader.PropertyToID("UAV_PostProcessTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct DebugViewPassData
        {
            public int2 resolution;
            public int mode;
            public ComputeKernel kernel;
            public int bindSet;
            // x = first probe-atlas slice, y = complexity heat-map range.
            public Vector4 parameters;
            public LightLoopInputs lightLoop;
            public RGTextureHandle depthTexture;
            public ComputeShader debugViewShader;
            public RGTextureHandle gBufferA;
            public RGTextureHandle gBufferB;
            public RGTextureHandle gBufferC;
            public RGTextureHandle motionTexture;
            public RGTextureHandle sceneColorTexture;
            public RGTextureHandle optionalTexture;
            public RGTextureHandle postProcessTexture;
        }

        void ComputeDebugView(in CameraRecordContext context)
        {
            EDebugView view = context.debugView;
            if (view == EDebugView.None)
            {
                return;
            }

            RGTextureHandle postProcessTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            ComputeKernel debugKernel;
            int bindSet;
            bool needGBuffer = false;
            bool needMotion = false;
            bool needSceneColor = false;
            bool needOptional = false;
            bool needLightLoop = false;
            Vector4 parameters = Vector4.zero;
            InfinityDebugDisplaySettingsLighting lightingDebug = InfinityDebugDisplaySettings.current.lighting;
            RGTextureHandle gBufferA = default;
            RGTextureHandle gBufferB = default;
            RGTextureHandle gBufferC = default;
            RGTextureHandle motionTexture = default;
            RGTextureHandle sceneColorTexture = default;
            RGTextureHandle optionalTexture = default;

            switch (view)
            {
                case EDebugView.Albedo:
                case EDebugView.Normal:
                case EDebugView.Roughness:
                case EDebugView.Metallic:
                case EDebugView.Specular:
                case EDebugView.ShadingModel:
                case EDebugView.RenderingLayers:
                    debugKernel = kernels.debugViewGBuffer;
                    bindSet = 0;
                    needGBuffer = true;
                    gBufferA = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
                    gBufferB = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
                    gBufferC = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC);
                    break;
                case EDebugView.AO:
                    if (m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.SSR:
                    if (m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.SSRBuffer, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.SSRTileClass:
                case EDebugView.SSRRayHit:
                case EDebugView.SSRConfidence:
                case EDebugView.SSRHistoryAge:
                    if (m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.SSRDebugBuffer, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.SSGI:
                    if (m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.SSGIBuffer, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.MotionMagnitude:
                    debugKernel = kernels.debugViewMotion;
                    bindSet = 1;
                    needMotion = true;
                    motionTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
                    break;
                case EDebugView.TAAConfidence:
                    if (!m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.TAAConfidenceBuffer, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    break;
                case EDebugView.PreTonemapLuma:
                    debugKernel = kernels.debugViewSceneColor;
                    bindSet = 2;
                    needSceneColor = true;
                    sceneColorTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ResolvedSceneColor);
                    break;
                case EDebugView.CascadeShadowAtlas:
                case EDebugView.LocalShadowAtlas:
                case EDebugView.CachedShadowAtlas:
                case EDebugView.ShadowMoments:
                    bool hasAtlas = view == EDebugView.CascadeShadowAtlas ? m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.CascadeShadowMap, out optionalTexture)
                        : view == EDebugView.LocalShadowAtlas ? m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.LocalShadowMap, out optionalTexture)
                        : view == EDebugView.CachedShadowAtlas ? m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.CachedLocalShadowMap, out optionalTexture)
                        : m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.CascadeShadowMoments, out optionalTexture)
                            || m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.LocalShadowMoments, out optionalTexture);
                    if (hasAtlas)
                    {
                        debugKernel = kernels.debugViewShadow;
                        bindSet = 5;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.LightCookieAtlas:
                    if (m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.LightCookieAtlas, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.BakedDiffuse:
                case EDebugView.IndirectDiffuse:
                case EDebugView.IndirectSpecular:
                case EDebugView.ContactShadows:
                    int optionalId = view == EDebugView.BakedDiffuse ? InfinityShaderIDs.BakedDiffuseBuffer
                        : view == EDebugView.IndirectDiffuse ? InfinityShaderIDs.IndirectDiffuseBuffer
                        : view == EDebugView.IndirectSpecular ? InfinityShaderIDs.IndirectSpecularBuffer
                        : InfinityShaderIDs.ContactShadowBuffer;
                    if (m_RGBuilder.scoper.TryQueryTexture(optionalId, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewOptional;
                        bindSet = 3;
                        needOptional = true;
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                case EDebugView.LightComplexity:
                case EDebugView.ReflectionProbeComplexity:
                    debugKernel = kernels.debugViewLightLoop;
                    bindSet = 6;
                    needLightLoop = true;
                    parameters.y = Mathf.Max(1, lightingDebug.complexityRange);
                    break;
                case EDebugView.ReflectionProbeAtlas:
                    int probeCount = renderContext.lightContext.ReflectionProbeCount;
                    if (probeCount > 0 && m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.ReflectionProbeAtlas, out optionalTexture))
                    {
                        debugKernel = kernels.debugViewProbeAtlas;
                        bindSet = 7;
                        needOptional = true;
                        // Six faces per probe; the atlas slot of list entry i is its first slice (record parameters.y).
                        parameters.x = renderContext.lightContext.ReflectionProbeFirstSlice(Mathf.Clamp(lightingDebug.reflectionProbeAtlasIndex, 0, probeCount - 1));
                    }
                    else
                    {
                        debugKernel = kernels.debugViewMissing;
                        bindSet = 4;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"InfinityRP: DebugView '{view}' is not implemented.");
            }

            var debugDescriptor = m_RGBuilder.GetTextureDescriptor(postProcessTexture);
            debugDescriptor.name = "DebugDisplayTexture";
            debugDescriptor.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            debugDescriptor.enableRandomWrite = true;
            m_RGBuilder.scoper.MoveTexture(InfinityShaderIDs.PostProcessBuffer, InfinityShaderIDs.PreDebugColorBuffer);
            postProcessTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, debugDescriptor);

            using (var passRef = m_RGBuilder.AddComputePass<DebugViewPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDebugView)))
            {
                ref DebugViewPassData passData = ref passRef.GetPassData();
                passData.resolution = new int2(context.dimensions.displaySize.x, context.dimensions.displaySize.y);
                passData.mode = (int)view;
                passData.kernel = debugKernel;
                passData.bindSet = bindSet;
                passData.parameters = parameters;
                passData.debugViewShader = shaders.debugViewShader;
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

                if (needLightLoop)
                {
                    LightLoopInputs lightLoop = QueryLightLoop(context);
                    lightLoop.Read(passRef);
                    passData.lightLoop = lightLoop;
                    passData.depthTexture = passRef.ReadTexture(depthTexture);
                }

                passData.postProcessTexture = passRef.WriteTexture(postProcessTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DebugViewPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.debugViewShader;
                    int kernel = passData.kernel.index;
                    cmdEncoder.SetComputeVectorParam(shader, DebugViewPassUtilityData.DV_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(shader, DebugViewPassUtilityData.DV_ModeID, passData.mode);
                    cmdEncoder.SetComputeVectorParam(shader, DebugViewPassUtilityData.DV_ParamsID, passData.parameters);

                    if (passData.bindSet == 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferA);
                        cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferB);
                        cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferC);
                    }
                    else if (passData.bindSet == 1)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
                    }
                    else if (passData.bindSet == 2)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTexture);
                    }
                    else if (passData.bindSet == 3)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_OptionalTextureID, passData.optionalTexture);
                    }
                    else if (passData.bindSet == 5)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ShadowAtlasTextureID, passData.optionalTexture);
                    }
                    else if (passData.bindSet == 6)
                    {
                        cmdEncoder.SetComputeFloatParam(shader, DebugViewPassUtilityData.DV_FarDepthID, GraphicsConventions.SampledFarDepth);
                        cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                        passData.lightLoop.Bind(cmdEncoder, shader, kernel);
                    }
                    else if (passData.bindSet == 7)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ProbeAtlasTextureID, passData.optionalTexture);
                    }

                    cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.UAV_PostProcessTextureID, passData.postProcessTexture);
                    cmdEncoder.DispatchCompute(shader, kernel, passData.kernel.GroupsX(passData.resolution.x), passData.kernel.GroupsY(passData.resolution.y), 1);
                });
            }
        }
    }
}
