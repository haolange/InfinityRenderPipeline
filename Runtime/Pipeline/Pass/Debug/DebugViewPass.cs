#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_DebugViewParameters Shaders/Pass/Debug/Compute_DebugView.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct DebugViewParameters
    {
        public Vector4 resolution;
        // x = first probe-atlas slice, y = complexity heat-map range.
        public Vector4 parameters;
        public int mode;
        public float farDepth;
        public int padding0;
        public int padding1;
    }

    internal static class DebugViewPassUtilityData
    {
        internal static readonly int CBV_DebugViewParametersID = Shader.PropertyToID("CBV_DebugViewParameters");
        internal static readonly int SRV_ProbeAtlasTextureID = Shader.PropertyToID("SRV_ProbeAtlasTexture");
        internal static readonly int SRV_OptionalTextureID = Shader.PropertyToID("SRV_OptionalTexture");
        internal static readonly int SRV_ShadowAtlasTextureID = Shader.PropertyToID("SRV_ShadowAtlasTexture");
        internal static readonly int UAV_PostProcessTextureID = Shader.PropertyToID("UAV_PostProcessTexture");

        // One entry per EDebugView, indexed by its value. A view with inputs reads the first registered one.
        static readonly DebugViewBinding[] s_Bindings = BuildBindings();

        internal static DebugViewBinding BindingOf(EDebugView view)
        {
            int index = (int)view;
            if ((uint)index >= (uint)s_Bindings.Length || s_Bindings[index].inputs == null)
            {
                throw new InvalidOperationException($"InfinityRP: DebugView '{view}' is not implemented.");
            }

            return s_Bindings[index];
        }

        static DebugViewBinding[] BuildBindings()
        {
            var table = new DebugViewBinding[Enum.GetValues(typeof(EDebugView)).Length];
            void Set(EDebugView view, EDebugViewKernel kernel, params RGResourceKey[] inputs) => table[(int)view] = new DebugViewBinding(kernel, inputs);

            Set(EDebugView.Albedo, EDebugViewKernel.GBuffer);
            Set(EDebugView.Normal, EDebugViewKernel.GBuffer);
            Set(EDebugView.Roughness, EDebugViewKernel.GBuffer);
            Set(EDebugView.Metallic, EDebugViewKernel.GBuffer);
            Set(EDebugView.Specular, EDebugViewKernel.GBuffer);
            Set(EDebugView.SurfaceStencil, EDebugViewKernel.GBuffer);
            Set(EDebugView.MotionMagnitude, EDebugViewKernel.Motion);
            Set(EDebugView.PreTonemapLuma, EDebugViewKernel.SceneColor);
            Set(EDebugView.LightComplexity, EDebugViewKernel.LightLoop);
            Set(EDebugView.ReflectionProbeComplexity, EDebugViewKernel.LightLoop);

            Set(EDebugView.AO, EDebugViewKernel.Optional, InfinityShaderIDs.OcclusionBuffer);
            Set(EDebugView.SSR, EDebugViewKernel.ScreenSpaceRadiance, InfinityShaderIDs.SSRBuffer);
            Set(EDebugView.SSRTileClass, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSRRayHit, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSRConfidence, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSRHistoryAge, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSGI, EDebugViewKernel.Optional, InfinityShaderIDs.SSGIBuffer);
            Set(EDebugView.TAAConfidence, EDebugViewKernel.Optional, InfinityShaderIDs.TAAConfidenceBuffer);
            Set(EDebugView.LightCookieAtlas, EDebugViewKernel.Optional, InfinityShaderIDs.LightCookieAtlas);
            // The sideband exists only while one of its readers records; without it these views show Missing.
            Set(EDebugView.RenderingLayers, EDebugViewKernel.Optional, InfinityShaderIDs.SidebandBuffer);
            Set(EDebugView.DiffuseRatio, EDebugViewKernel.Optional, InfinityShaderIDs.SidebandBuffer);
            Set(EDebugView.MixedLightVisibility, EDebugViewKernel.Optional, InfinityShaderIDs.SidebandBuffer);
            Set(EDebugView.SubsurfaceProfile, EDebugViewKernel.Optional, InfinityShaderIDs.SidebandBuffer);
            Set(EDebugView.ContactShadows, EDebugViewKernel.Optional, InfinityShaderIDs.ContactShadowBuffer);

            Set(EDebugView.CascadeShadowAtlas, EDebugViewKernel.Shadow, InfinityShaderIDs.CascadeShadowMap);
            Set(EDebugView.LocalShadowAtlas, EDebugViewKernel.Shadow, InfinityShaderIDs.LocalShadowMap);
            Set(EDebugView.CachedShadowAtlas, EDebugViewKernel.Shadow, InfinityShaderIDs.CachedLocalShadowMap);
            Set(EDebugView.ShadowMoments, EDebugViewKernel.Shadow, InfinityShaderIDs.CascadeShadowMoments, InfinityShaderIDs.LocalShadowMoments);
            Set(EDebugView.ReflectionProbeAtlas, EDebugViewKernel.ProbeAtlas, InfinityShaderIDs.ReflectionProbeAtlas);
            return table;
        }
    }

    // The kernel a view records with. It also fixes which resources the pass reads and binds, so it is the bind set.
    // Values index PipelineKernels.debugView.
    internal enum EDebugViewKernel
    {
        GBuffer,
        Motion,
        SceneColor,
        Optional,
        Missing,
        Shadow,
        LightLoop,
        ProbeAtlas,
        ScreenSpaceRadiance
    }

    // The kernel a view uses when its inputs exist; a view with inputs falls back to Missing when none is registered.
    internal readonly struct DebugViewBinding
    {
        internal readonly EDebugViewKernel kernel;
        internal readonly RGResourceKey[] inputs;

        internal DebugViewBinding(EDebugViewKernel kernel, RGResourceKey[] inputs)
        {
            this.kernel = kernel;
            this.inputs = inputs;
        }
    }

    internal sealed class DebugViewPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;

        internal DebugViewPass(RGBuilder graph, InfinityRenderPipelineResources resources)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
        }

        struct DebugViewPassData
        {
            public RGConstantsHandle parametersHandle;
            public int2 resolution;
            public ComputeKernel kernel;
            public EDebugViewKernel kind;
            public LightLoopInputs lightLoop;
            public ReflectionInputs reflections;
            public RGTextureHandle depthTextureHandle;
            public ComputeShader debugViewShader;
            public RGTextureHandle gBufferAHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle motionTextureHandle;
            public RGTextureHandle sceneColorTextureHandle;
            public RGTextureHandle optionalTextureHandle;
            public RGTextureHandle debugViewTextureHandle;
        }

        internal void ComputeDebugView(in ViewContext context)
        {
            EDebugView view = context.debugView;
            if (view == EDebugView.None)
            {
                return;
            }

            RGTextureHandle postProcessTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.PostProcessBuffer);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            // A view whose optional input is not registered, or a probe atlas view with no probe, shows the Missing kernel.
            DebugViewBinding binding = DebugViewPassUtilityData.BindingOf(view);
            EDebugViewKernel kind = binding.kernel;
            int probeCount = context.reflections.ProbeCount;
            RGTextureHandle optionalTextureHandle = default;
            if (binding.inputs.Length > 0)
            {
                bool found = false;
                if (kind != EDebugViewKernel.ProbeAtlas || probeCount > 0)
                {
                    foreach (RGResourceKey input in binding.inputs)
                    {
                        if (m_RGBuilder.scoper.TryQueryTexture(input, out optionalTextureHandle))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    kind = EDebugViewKernel.Missing;
                }
            }

            Vector4 parameters = Vector4.zero;
            InfinityDebugDisplaySettingsLighting lightingDebug = InfinityDebugDisplaySettings.current.lighting;
            RGTextureHandle gBufferAHandle = default;
            RGTextureHandle gBufferBHandle = default;
            RGTextureHandle motionTextureHandle = default;
            RGTextureHandle sceneColorTextureHandle = default;
            switch (kind)
            {
                case EDebugViewKernel.GBuffer:
                    gBufferAHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
                    gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
                    break;
                case EDebugViewKernel.Motion:
                    motionTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
                    break;
                case EDebugViewKernel.SceneColor:
                    sceneColorTextureHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.resolved);
                    break;
                case EDebugViewKernel.LightLoop:
                    parameters.y = Mathf.Max(1, lightingDebug.complexityRange);
                    break;
                case EDebugViewKernel.ProbeAtlas:
                    // Six faces per probe; the atlas slot of list entry i is its first slice (record parameters.y).
                    parameters.x = context.reflections.FirstSlice(Mathf.Clamp(lightingDebug.reflectionProbeAtlasIndex, 0, probeCount - 1));
                    break;
            }

            var debugDescriptor = m_RGBuilder.GetTextureDescriptor(postProcessTextureHandle);
            debugDescriptor.name = "DebugDisplayTexture";
            debugDescriptor.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            debugDescriptor.enableRandomWrite = true;
            // The image under the debug view keeps its stage key; the view writes its own (SceneColorKeys.display).
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.PreDebugColorBuffer, postProcessTextureHandle);
            RGTextureHandle debugViewTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.DebugViewBuffer, debugDescriptor);

            {
                RGComputePassHandle<DebugViewPassData> passHandle = m_RGBuilder.AddComputePass<DebugViewPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDebugView));
                ref DebugViewPassData passData = ref passHandle.GetPassData();
                passData.resolution = new int2(context.dimensions.displaySize.x, context.dimensions.displaySize.y);
                passData.kernel = m_Resources.kernels.debugView[(int)kind];
                passData.kind = kind;
                passData.parametersHandle = passHandle.WriteConstants(new DebugViewParameters
                {
                    resolution = new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y),
                    parameters = parameters,
                    mode = (int)view,
                    farDepth = GraphicsConventions.SampledFarDepth
                });
                passData.debugViewShader = m_Resources.shaders.debugViewShader;
                switch (kind)
                {
                    case EDebugViewKernel.GBuffer:
                        passData.gBufferAHandle = passHandle.ReadTexture(gBufferAHandle);
                        passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                        // Depth marks the empty pixels; the stencil plane of the same target holds the admission bits.
                        passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                        break;
                    case EDebugViewKernel.Motion:
                        passData.motionTextureHandle = passHandle.ReadTexture(motionTextureHandle);
                        break;
                    case EDebugViewKernel.SceneColor:
                        passData.sceneColorTextureHandle = passHandle.ReadTexture(sceneColorTextureHandle);
                        break;
                    case EDebugViewKernel.Optional:
                    case EDebugViewKernel.ScreenSpaceRadiance:
                    case EDebugViewKernel.Shadow:
                    case EDebugViewKernel.ProbeAtlas:
                        passData.optionalTextureHandle = passHandle.ReadTexture(optionalTextureHandle);
                        break;
                    case EDebugViewKernel.LightLoop:
                        LightLoopInputs lightLoop = LightTileMaskPass.QueryLightLoop(m_RGBuilder, context);
                        lightLoop.Read(passHandle);
                        passData.lightLoop = lightLoop;
                        ReflectionInputs reflections = ReflectionInputs.Query(m_RGBuilder, context);
                        reflections.Read(passHandle);
                        passData.reflections = reflections;
                        passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                        break;
                }

                passData.debugViewTextureHandle = passHandle.WriteTexture(debugViewTextureHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in DebugViewPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.debugViewShader;
                    int kernel = passData.kernel.index;
                    cmdEncoder.SetComputeConstantBufferParam(shader, DebugViewPassUtilityData.CBV_DebugViewParametersID, passData.parametersHandle);

                    switch (passData.kind)
                    {
                        case EDebugViewKernel.GBuffer:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferAHandle);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_StencilTexture, passData.depthTextureHandle, 0, RenderTextureSubElement.Stencil);
                            break;
                        case EDebugViewKernel.Motion:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTextureHandle);
                            break;
                        case EDebugViewKernel.SceneColor:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTextureHandle);
                            break;
                        case EDebugViewKernel.Optional:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_OptionalTextureID, passData.optionalTextureHandle);
                            break;
                        case EDebugViewKernel.ScreenSpaceRadiance:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SSRTexture, passData.optionalTextureHandle);
                            break;
                        case EDebugViewKernel.Shadow:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ShadowAtlasTextureID, passData.optionalTextureHandle);
                            break;
                        case EDebugViewKernel.LightLoop:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                            passData.lightLoop.Bind(cmdEncoder, shader, kernel);
                            passData.reflections.Bind(cmdEncoder, shader, kernel);
                            break;
                        case EDebugViewKernel.ProbeAtlas:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ProbeAtlasTextureID, passData.optionalTextureHandle);
                            break;
                    }

                    cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.UAV_PostProcessTextureID, passData.debugViewTextureHandle);
                    cmdEncoder.DispatchCompute(shader, kernel, passData.kernel.GroupsX(passData.resolution.x), passData.kernel.GroupsY(passData.resolution.y), 1);
                });
            }
        }
    }
}
#endif
