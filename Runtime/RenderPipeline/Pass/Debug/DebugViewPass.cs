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
            "DebugViewMissing",
            "DebugViewShadow",
            "DebugViewLightLoop",
            "DebugViewProbeAtlas"
        };

        internal static int DV_ResolutionID = Shader.PropertyToID("DV_Resolution");
        internal static int DV_ModeID = Shader.PropertyToID("DV_Mode");
        internal static int DV_ParamsID = Shader.PropertyToID("DV_Params");
        internal static int DV_InvProjID = Shader.PropertyToID("DV_InvProj");
        internal static int DV_FarDepthID = Shader.PropertyToID("DV_FarDepth");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_ProbeAtlasTextureID = Shader.PropertyToID("SRV_ProbeAtlasTexture");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_MotionTextureID = Shader.PropertyToID("SRV_MotionTexture");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static int SRV_OptionalTextureID = Shader.PropertyToID("SRV_OptionalTexture");
        internal static int SRV_ShadowAtlasTextureID = Shader.PropertyToID("SRV_ShadowAtlasTexture");
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
            // x = first probe-atlas slice, y = complexity heat-map range.
            public Vector4 parameters;
            public Matrix4x4 invProj;
            public FLightLoopInputs lightLoop;
            public RGTextureRef depthTexture;
            public ComputeShader debugViewShader;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef motionTexture;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef optionalTexture;
            public RGTextureRef postProcessTexture;
        }

        void ComputeDebugView(RenderContext renderContext, Camera camera)
        {
            EDebugView view = m_ActiveFrameState.debugView;
            if (view == EDebugView.None)
            {
                return;
            }

            if (!GraphicsUtility.HasRequiredKernels(shaders.debugViewShader, DebugViewPassUtilityData.RequiredKernels))
            {
                throw new InvalidOperationException("InfinityRP: DebugView is active but debugViewShader is missing required kernels (DebugViewGBuffer/Motion/SceneColor/Optional/Missing/Shadow/LightLoop/ProbeAtlas).");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.PostProcessBuffer, out RGTextureRef postProcessTexture))
            {
                throw new InvalidOperationException("InfinityRP: DebugView requires PostProcessBuffer, which was never registered this frame.");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.DepthBuffer, out RGTextureRef depthTexture))
            {
                throw new InvalidOperationException("InfinityRP: DebugView requires DepthBuffer, which was never registered this frame.");
            }

            string kernelName;
            int bindSet;
            bool needGBuffer = false;
            bool needMotion = false;
            bool needSceneColor = false;
            bool needOptional = false;
            bool needLightLoop = false;
            Vector4 parameters = Vector4.zero;
            InfinityDebugDisplaySettingsLighting lightingDebug = InfinityDebugDisplaySettings.current.lighting;
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
                case EDebugView.Metallic:
                case EDebugView.Specular:
                case EDebugView.ShadingModel:
                case EDebugView.RenderingLayers:
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
                case EDebugView.SSRTileClass:
                case EDebugView.SSRRayHit:
                case EDebugView.SSRConfidence:
                case EDebugView.SSRHistoryAge:
                    if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.SSRDebugBuffer, out optionalTexture))
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
                    if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.TAAConfidenceBuffer, out optionalTexture))
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    else
                    {
                        kernelName = "DebugViewOptional";
                        bindSet = 3;
                        needOptional = true;
                    }
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
                case EDebugView.CascadeShadowAtlas:
                case EDebugView.LocalShadowAtlas:
                case EDebugView.CachedShadowAtlas:
                case EDebugView.ShadowMoments:
                    bool hasAtlas = view == EDebugView.CascadeShadowAtlas ? m_RGScoper.TryQueryTexture(InfinityShaderIDs.CascadeShadowMap, out optionalTexture)
                        : view == EDebugView.LocalShadowAtlas ? m_RGScoper.TryQueryTexture(InfinityShaderIDs.LocalShadowMap, out optionalTexture)
                        : view == EDebugView.CachedShadowAtlas ? m_RGScoper.TryQueryTexture(InfinityShaderIDs.CachedLocalShadowMap, out optionalTexture)
                        : m_RGScoper.TryQueryTexture(InfinityShaderIDs.CascadeShadowMoments, out optionalTexture)
                            || m_RGScoper.TryQueryTexture(InfinityShaderIDs.LocalShadowMoments, out optionalTexture);
                    if (hasAtlas)
                    {
                        kernelName = "DebugViewShadow";
                        bindSet = 5;
                        needOptional = true;
                    }
                    else
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    break;
                case EDebugView.LightCookieAtlas:
                    if (m_RGScoper.TryQueryTexture(InfinityShaderIDs.LightCookieAtlas, out optionalTexture))
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
                case EDebugView.BakedDiffuse:
                case EDebugView.IndirectDiffuse:
                case EDebugView.IndirectSpecular:
                case EDebugView.ContactShadows:
                    int optionalId = view == EDebugView.BakedDiffuse ? InfinityShaderIDs.BakedDiffuseBuffer
                        : view == EDebugView.IndirectDiffuse ? InfinityShaderIDs.IndirectDiffuseBuffer
                        : view == EDebugView.IndirectSpecular ? InfinityShaderIDs.IndirectSpecularBuffer
                        : InfinityShaderIDs.ContactShadowBuffer;
                    if (m_RGScoper.TryQueryTexture(optionalId, out optionalTexture))
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
                case EDebugView.LightComplexity:
                case EDebugView.ReflectionProbeComplexity:
                    if (renderContext?.lightContext != null)
                    {
                        kernelName = "DebugViewLightLoop";
                        bindSet = 6;
                        needLightLoop = true;
                        parameters.y = Mathf.Max(1, lightingDebug.complexityRange);
                    }
                    else
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    break;
                case EDebugView.ReflectionProbeAtlas:
                    int probeCount = renderContext?.lightContext != null ? renderContext.lightContext.ReflectionProbeCount : 0;
                    if (probeCount > 0 && m_RGScoper.TryQueryTexture(InfinityShaderIDs.ReflectionProbeAtlas, out optionalTexture))
                    {
                        kernelName = "DebugViewProbeAtlas";
                        bindSet = 7;
                        needOptional = true;
                        // Six faces per probe; the atlas slot of list entry i is its first slice (record parameters.y).
                        parameters.x = renderContext.lightContext.ReflectionProbeFirstSlice(Mathf.Clamp(lightingDebug.reflectionProbeAtlasIndex, 0, probeCount - 1));
                    }
                    else
                    {
                        kernelName = "DebugViewMissing";
                        bindSet = 4;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"InfinityRP: DebugView '{view}' is not implemented.");
            }

            int kernelIndex = shaders.debugViewShader.FindKernel(kernelName);
            if (kernelIndex < 0)
            {
                throw new InvalidOperationException($"InfinityRP: DebugView is active but FindKernel({kernelName}) failed.");
            }

            var debugDescriptor = m_RGBuilder.GetTextureDescriptor(postProcessTexture);
            debugDescriptor.name = "DebugDisplayTexture";
            debugDescriptor.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            debugDescriptor.enableRandomWrite = true;
            m_RGScoper.MoveTexture(InfinityShaderIDs.PostProcessBuffer, InfinityShaderIDs.PreDebugColorBuffer);
            postProcessTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, debugDescriptor);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<DebugViewPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDebugView)))
            {
                ref DebugViewPassData passData = ref passRef.GetPassData<DebugViewPassData>();
                passData.resolution = new int2(m_ActiveFrameState.dimensions.displaySize.x, m_ActiveFrameState.dimensions.displaySize.y);
                passData.mode = (int)view;
                passData.kernelIndex = kernelIndex;
                passData.bindSet = bindSet;
                passData.parameters = parameters;
                passData.invProj = m_CameraUniform.matrix_InvFlipYJitterProj;
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
                    FLightLoopInputs lightLoop = QueryLightLoop(renderContext);
                    lightLoop.Read(passRef);
                    passData.lightLoop = lightLoop;
                    passData.depthTexture = passRef.ReadTexture(depthTexture);
                }

                passData.postProcessTexture = passRef.WriteTexture(postProcessTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DebugViewPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.debugViewShader;
                    int kernel = passData.kernelIndex;
                    cmdEncoder.SetComputeVectorParam(shader, DebugViewPassUtilityData.DV_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(shader, DebugViewPassUtilityData.DV_ModeID, passData.mode);
                    cmdEncoder.SetComputeVectorParam(shader, DebugViewPassUtilityData.DV_ParamsID, passData.parameters);

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
                    else if (passData.bindSet == 5)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ShadowAtlasTextureID, passData.optionalTexture);
                    }
                    else if (passData.bindSet == 6)
                    {
                        cmdEncoder.SetComputeMatrixParam(shader, DebugViewPassUtilityData.DV_InvProjID, passData.invProj);
                        cmdEncoder.SetComputeFloatParam(shader, DebugViewPassUtilityData.DV_FarDepthID, GraphicsUtility.SampledFarDepth);
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                        passData.lightLoop.Bind(cmdEncoder, shader, kernel);
                    }
                    else if (passData.bindSet == 7)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ProbeAtlasTextureID, passData.optionalTexture);
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
