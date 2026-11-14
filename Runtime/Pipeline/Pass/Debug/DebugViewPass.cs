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
            Set(EDebugView.ShadingModel, EDebugViewKernel.GBuffer);
            Set(EDebugView.RenderingLayers, EDebugViewKernel.GBuffer);
            Set(EDebugView.MotionMagnitude, EDebugViewKernel.Motion);
            Set(EDebugView.PreTonemapLuma, EDebugViewKernel.SceneColor);
            Set(EDebugView.LightComplexity, EDebugViewKernel.LightLoop);
            Set(EDebugView.ReflectionProbeComplexity, EDebugViewKernel.LightLoop);

            Set(EDebugView.AO, EDebugViewKernel.Optional, InfinityShaderIDs.OcclusionBuffer);
            Set(EDebugView.SSR, EDebugViewKernel.Optional, InfinityShaderIDs.SSRBuffer);
            Set(EDebugView.SSRTileClass, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSRRayHit, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSRConfidence, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSRHistoryAge, EDebugViewKernel.Optional, InfinityShaderIDs.SSRDebugBuffer);
            Set(EDebugView.SSGI, EDebugViewKernel.Optional, InfinityShaderIDs.SSGIBuffer);
            Set(EDebugView.TAAConfidence, EDebugViewKernel.Optional, InfinityShaderIDs.TAAConfidenceBuffer);
            Set(EDebugView.LightCookieAtlas, EDebugViewKernel.Optional, InfinityShaderIDs.LightCookieAtlas);
            Set(EDebugView.BakedDiffuse, EDebugViewKernel.Optional, InfinityShaderIDs.BakedDiffuseBuffer);
            Set(EDebugView.IndirectDiffuse, EDebugViewKernel.Optional, InfinityShaderIDs.IndirectDiffuseBuffer);
            Set(EDebugView.IndirectSpecular, EDebugViewKernel.Optional, InfinityShaderIDs.IndirectSpecularBuffer);
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
        ProbeAtlas
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

    public partial class InfinityRenderPipeline
    {
        struct DebugViewPassData
        {
            public RGConstantsHandle parameters;
            public int2 resolution;
            public ComputeKernel kernel;
            public EDebugViewKernel kind;
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

            // A view whose optional input is not registered, or a probe atlas view with no probe, shows the Missing kernel.
            DebugViewBinding binding = DebugViewPassUtilityData.BindingOf(view);
            EDebugViewKernel kind = binding.kernel;
            int probeCount = renderContext.lightContext.ReflectionProbeCount;
            RGTextureHandle optionalTexture = default;
            if (binding.inputs.Length > 0)
            {
                bool found = false;
                if (kind != EDebugViewKernel.ProbeAtlas || probeCount > 0)
                {
                    foreach (RGResourceKey input in binding.inputs)
                    {
                        if (m_RGBuilder.scoper.TryQueryTexture(input, out optionalTexture))
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
            RGTextureHandle gBufferA = default;
            RGTextureHandle gBufferB = default;
            RGTextureHandle gBufferC = default;
            RGTextureHandle motionTexture = default;
            RGTextureHandle sceneColorTexture = default;
            switch (kind)
            {
                case EDebugViewKernel.GBuffer:
                    gBufferA = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
                    gBufferB = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
                    gBufferC = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC);
                    break;
                case EDebugViewKernel.Motion:
                    motionTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);
                    break;
                case EDebugViewKernel.SceneColor:
                    sceneColorTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ResolvedSceneColor);
                    break;
                case EDebugViewKernel.LightLoop:
                    parameters.y = Mathf.Max(1, lightingDebug.complexityRange);
                    break;
                case EDebugViewKernel.ProbeAtlas:
                    // Six faces per probe; the atlas slot of list entry i is its first slice (record parameters.y).
                    parameters.x = renderContext.lightContext.ReflectionProbeFirstSlice(Mathf.Clamp(lightingDebug.reflectionProbeAtlasIndex, 0, probeCount - 1));
                    break;
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
                passData.kernel = kernels.debugView[(int)kind];
                passData.kind = kind;
                passData.parameters = passRef.WriteConstants(new DebugViewParameters
                {
                    resolution = new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y),
                    parameters = parameters,
                    mode = (int)view,
                    farDepth = GraphicsConventions.SampledFarDepth
                });
                passData.debugViewShader = shaders.debugViewShader;
                switch (kind)
                {
                    case EDebugViewKernel.GBuffer:
                        passData.gBufferA = passRef.ReadTexture(gBufferA);
                        passData.gBufferB = passRef.ReadTexture(gBufferB);
                        passData.gBufferC = passRef.ReadTexture(gBufferC);
                        break;
                    case EDebugViewKernel.Motion:
                        passData.motionTexture = passRef.ReadTexture(motionTexture);
                        break;
                    case EDebugViewKernel.SceneColor:
                        passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                        break;
                    case EDebugViewKernel.Optional:
                    case EDebugViewKernel.Shadow:
                    case EDebugViewKernel.ProbeAtlas:
                        passData.optionalTexture = passRef.ReadTexture(optionalTexture);
                        break;
                    case EDebugViewKernel.LightLoop:
                        LightLoopInputs lightLoop = QueryLightLoop(context);
                        lightLoop.Read(passRef);
                        passData.lightLoop = lightLoop;
                        passData.depthTexture = passRef.ReadTexture(depthTexture);
                        break;
                }

                passData.postProcessTexture = passRef.WriteTexture(postProcessTexture);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DebugViewPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.debugViewShader;
                    int kernel = passData.kernel.index;
                    cmdEncoder.SetComputeConstantBufferParam(shader, DebugViewPassUtilityData.CBV_DebugViewParametersID, passData.parameters);

                    switch (passData.kind)
                    {
                        case EDebugViewKernel.GBuffer:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferA);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferB);
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureC, passData.gBufferC);
                            break;
                        case EDebugViewKernel.Motion:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_MotionTexture, passData.motionTexture);
                            break;
                        case EDebugViewKernel.SceneColor:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTexture);
                            break;
                        case EDebugViewKernel.Optional:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_OptionalTextureID, passData.optionalTexture);
                            break;
                        case EDebugViewKernel.Shadow:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ShadowAtlasTextureID, passData.optionalTexture);
                            break;
                        case EDebugViewKernel.LightLoop:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTexture);
                            passData.lightLoop.Bind(cmdEncoder, shader, kernel);
                            break;
                        case EDebugViewKernel.ProbeAtlas:
                            cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.SRV_ProbeAtlasTextureID, passData.optionalTexture);
                            break;
                    }

                    cmdEncoder.SetComputeTextureParam(shader, kernel, DebugViewPassUtilityData.UAV_PostProcessTextureID, passData.postProcessTexture);
                    cmdEncoder.DispatchCompute(shader, kernel, passData.kernel.GroupsX(passData.resolution.x), passData.kernel.GroupsY(passData.resolution.y), 1);
                });
            }
        }
    }
}
#endif
