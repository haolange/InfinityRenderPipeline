using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_DeferredShadingParameters Shaders/Pass/Lighting/Compute_DeferredShading.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeferredShadingParameters
    {
        public Vector4 resolution;
        public float atmosphereIBLMaxMip;
        public int hasRenderingLayers;
        public int padding0;
        public int padding1;
    }

    internal static class DeferredShadingPassUtilityData
    {
        internal static readonly int CBV_DeferredShadingParametersID = Shader.PropertyToID("CBV_DeferredShadingParameters");
        internal static readonly int SRV_RasterReflectionID = Shader.PropertyToID("SRV_RasterReflection");
    }

    // Completes the shading of deferred pixels in place: Scene Color arrives as emission plus indirect diffuse from the
    // geometry draw and leaves as the whole shading, and the sideband's diffuse share is rewritten. The stencil plane
    // admits only pixels with the deferred bit.
    internal sealed class DeferredShadingPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;

        internal DeferredShadingPass(RGBuilder graph, InfinityRenderPipelineResources resources, in InfinityRenderPipelineAsset.Settings settings)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_Settings = settings;
        }

        struct DeferredShadingPassData
        {
            public ComputeShader shader;
            public int kernel;
            public int2 groupCount;
            public RGConstantsHandle parametersHandle;
            public LightLoopInputs lightLoop;
            public ReflectionInputs reflections;
            public AnalyticShadowInputs analyticShadows;
            public ShadowInputs shadows;
            public RGBufferHandle lightRecordBufferHandle;
            public bool hasSideband;
            public bool hasOcclusion;
            public bool hasIndirectDiffuse;
            public bool hasReflection;
            public bool hasContactShadow;
            public bool hasRasterReflection;
            public bool hasAnalyticShadow;
            public LocalKeyword sidebandKeyword;
            public LocalKeyword occlusionKeyword;
            public LocalKeyword indirectDiffuseKeyword;
            public LocalKeyword reflectionKeyword;
            public LocalKeyword contactShadowKeyword;
            public LocalKeyword rasterReflectionKeyword;
            public LocalKeyword analyticShadowKeyword;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle sidebandHandle;
            public RGTextureHandle gBufferAHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle occlusionTextureHandle;
            public RGTextureHandle ssgiTextureHandle;
            public RGTextureHandle ssrTextureHandle;
            public RGTextureHandle contactShadowTextureHandle;
            public RGTextureHandle contactShadowMaskHandle;
            public RGTextureHandle rasterReflectionHandle;
            public RGTextureHandle atmosphereGGXPrefilterHandle;
        }

        internal void ComputeDeferredShading(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            ComputeKernel kernel = m_Resources.kernels.deferredShading;
            PipelineKernels kernels = m_Resources.kernels;

            // The feature set decides which optional inputs exist; a recorded producer that did not register its output throws.
            bool hasSideband = context.sideband;
            bool hasOcclusion = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
            bool hasIndirectDiffuse = context.features.ShouldRecord(EFrameFeature.SSGI);
            bool hasReflection = context.features.ShouldRecord(EFrameFeature.SSR);
            bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);
            bool hasRasterReflection = context.reflections.RasterReflectionCount > 0 && context.features.ShouldRecord(EFrameFeature.ReflectionProbes);

            RGComputePassHandle<DeferredShadingPassData> passHandle = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading));
            ref DeferredShadingPassData passData = ref passHandle.GetPassData();
            passData.shader = kernel.shader;
            passData.kernel = kernel.index;
            passData.groupCount = new int2(kernel.GroupsX(width), kernel.GroupsY(height));
            passData.parametersHandle = passHandle.WriteConstants(new DeferredShadingParameters
            {
                resolution = new Vector4(width, height, 1.0f / width, 1.0f / height),
                atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(context.atmosphere.cubemapSize) - 1,
                hasRenderingLayers = m_Settings.config.enableRenderingLayers ? 1 : 0
            });

            // In place: Scene Color and the sideband are read and written by the same lane.
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            passHandle.ReadTexture(lightingTextureHandle);
            passData.lightingTextureHandle = passHandle.WriteTexture(lightingTextureHandle);
            passData.hasSideband = hasSideband;
            if (hasSideband)
            {
                RGTextureHandle sidebandHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SidebandBuffer);
                passHandle.ReadTexture(sidebandHandle);
                passData.sidebandHandle = passHandle.WriteTexture(sidebandHandle);
            }
            passData.gBufferAHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA));
            passData.gBufferBHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
            // One read covers both planes: depth for position and the stencil for admission.
            passData.depthTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
            passData.hasOcclusion = hasOcclusion;
            if (hasOcclusion)
            {
                passData.occlusionTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer));
            }
            passData.hasIndirectDiffuse = hasIndirectDiffuse;
            if (hasIndirectDiffuse)
            {
                passData.ssgiTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSGIBuffer));
            }
            passData.hasReflection = hasReflection;
            if (hasReflection)
            {
                passData.ssrTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSRBuffer));
            }
            passData.hasContactShadow = hasContactShadow;
            if (hasContactShadow)
            {
                passData.contactShadowTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer));
                passData.contactShadowMaskHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
            }
            passData.hasRasterReflection = hasRasterReflection;
            if (hasRasterReflection)
            {
                passData.rasterReflectionHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.RasterReflectionBuffer));
            }
            passData.atmosphereGGXPrefilterHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter));

            LightLoopInputs lightLoop = LightTileMaskPass.QueryLightLoop(m_RGBuilder, context);
            lightLoop.Read(passHandle);
            passData.lightLoop = lightLoop;
            ReflectionInputs reflections = ReflectionInputs.Query(m_RGBuilder, context);
            reflections.Read(passHandle);
            passData.reflections = reflections;
            AnalyticShadowInputs analyticShadows = AnalyticShadowInputs.Query(m_RGBuilder, context);
            analyticShadows.Read(passHandle);
            passData.analyticShadows = analyticShadows;
            passData.hasAnalyticShadow = analyticShadows.active && context.frameSettings.IsEnabled(EFrameSetting.AnalyticShadow);
            ShadowInputs shadows = ShadowInputs.Query(m_RGBuilder, context);
            shadows.Read(passHandle);
            passData.shadows = shadows;
            passData.lightRecordBufferHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));

            passData.sidebandKeyword = kernels.deferredShadingSideband;
            passData.occlusionKeyword = kernels.deferredShadingOcclusion;
            passData.indirectDiffuseKeyword = kernels.deferredShadingIndirectDiffuse;
            passData.reflectionKeyword = kernels.deferredShadingReflection;
            passData.contactShadowKeyword = kernels.deferredShadingContactShadow;
            passData.rasterReflectionKeyword = kernels.deferredShadingRasterReflection;
            passData.analyticShadowKeyword = kernels.deferredShadingAnalyticShadow;

            passHandle.EnablePassCulling(false);
            passHandle.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder) =>
            {
                ComputeShader shader = passData.shader;
                int kernelIndex = passData.kernel;
                cmdEncoder.SetKeyword(shader, passData.sidebandKeyword, passData.hasSideband);
                cmdEncoder.SetKeyword(shader, passData.occlusionKeyword, passData.hasOcclusion);
                cmdEncoder.SetKeyword(shader, passData.indirectDiffuseKeyword, passData.hasIndirectDiffuse);
                cmdEncoder.SetKeyword(shader, passData.reflectionKeyword, passData.hasReflection);
                cmdEncoder.SetKeyword(shader, passData.contactShadowKeyword, passData.hasContactShadow);
                cmdEncoder.SetKeyword(shader, passData.rasterReflectionKeyword, passData.hasRasterReflection);
                cmdEncoder.SetKeyword(shader, passData.analyticShadowKeyword, passData.hasAnalyticShadow);
                cmdEncoder.SetComputeConstantBufferParam(shader, DeferredShadingPassUtilityData.CBV_DeferredShadingParametersID, passData.parametersHandle);

                cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.UAV_LightingTexture, passData.lightingTextureHandle);
                if (passData.hasSideband)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.UAV_Sideband, passData.sidebandHandle);
                }
                cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferAHandle);
                cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_StencilTexture, passData.depthTextureHandle, 0, RenderTextureSubElement.Stencil);
                if (passData.hasOcclusion)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionTextureHandle);
                }
                if (passData.hasIndirectDiffuse)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_SSGITexture, passData.ssgiTextureHandle);
                }
                if (passData.hasReflection)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, PassShaderIDs.SRV_SSRTexture, passData.ssrTextureHandle);
                }
                if (passData.hasContactShadow)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, ContactShadowPassUtilityData.SRV_ContactShadowSunID, passData.contactShadowTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, ContactShadowPassUtilityData.SRV_ContactShadowMaskID, passData.contactShadowMaskHandle);
                }
                if (passData.hasRasterReflection)
                {
                    cmdEncoder.SetComputeTextureParam(shader, kernelIndex, DeferredShadingPassUtilityData.SRV_RasterReflectionID, passData.rasterReflectionHandle);
                }
                cmdEncoder.SetComputeTextureParam(shader, kernelIndex, InfinityShaderIDs.AtmosphereGGXPrefilter, passData.atmosphereGGXPrefilterHandle);

                passData.lightLoop.Bind(cmdEncoder, shader, kernelIndex);
                passData.reflections.Bind(cmdEncoder, shader, kernelIndex);
                passData.analyticShadows.Bind(cmdEncoder, shader, kernelIndex);
                passData.shadows.Bind(cmdEncoder, shader, kernelIndex);
                cmdEncoder.SetComputeBufferParam(shader, kernelIndex, LightShaderIDs.LightRecordBuffer, passData.lightRecordBufferHandle);

                cmdEncoder.DispatchCompute(shader, kernelIndex, passData.groupCount.x, passData.groupCount.y, 1);
            });

            context.features.MarkProduced(EFrameFeature.DeferredShading);
        }
    }
}
