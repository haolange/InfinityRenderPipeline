using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_DeferredShadingParameters Shaders/Pass/Lighting/DeferredShading.shader
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

    // Adds direct light, environment and screen-space indirect onto the indirect diffuse the geometry draw stored.
    // Blend One SrcAlpha scales that indirect term by occlusion and by one minus the SSGI confidence. The stencil test
    // admits only pixels with the deferred bit; pixels without it keep the indirect color. Emission is the next pass.
    internal sealed class DeferredShadingPass
    {
        internal static readonly GlobalKeyword SidebandKeyword = GlobalKeyword.Create("INFINITY_SIDEBAND");
        internal static readonly GlobalKeyword OcclusionKeyword = GlobalKeyword.Create("DEFERRED_AO");
        internal static readonly GlobalKeyword IndirectDiffuseKeyword = GlobalKeyword.Create("DEFERRED_SSGI");
        internal static readonly GlobalKeyword ReflectionKeyword = GlobalKeyword.Create("DEFERRED_SSR");
        internal static readonly GlobalKeyword ContactShadowKeyword = GlobalKeyword.Create("DEFERRED_CONTACT_SHADOW");
        internal static readonly GlobalKeyword RasterReflectionKeyword = GlobalKeyword.Create("DEFERRED_RASTER_REFLECTION");
        internal static readonly GlobalKeyword AnalyticShadowKeyword = GlobalKeyword.Create("DEFERRED_ANALYTIC_SHADOW");

        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly Material m_Material;

        internal DeferredShadingPass(RGBuilder graph, InfinityRenderPipelineResources resources, in InfinityRenderPipelineAsset.Settings settings)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_Settings = settings;
            Shader shader = resources.shaders.deferredShadingShader;
            if (shader == null)
            {
                throw new InvalidOperationException("The deferred shading shader is not assigned.");
            }
            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        struct DeferredShadingPassData
        {
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
            public Material material;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_Material);
        }

        internal void RasterDeferredShading(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;

            // The feature set decides which optional inputs exist; a recorded producer that did not register its output throws.
            bool hasSideband = context.sideband;
            bool hasOcclusion = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
            bool hasIndirectDiffuse = context.features.ShouldRecord(EFrameFeature.SSGI);
            bool hasReflection = context.features.ShouldRecord(EFrameFeature.SSR);
            bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);
            bool hasRasterReflection = context.reflections.RasterReflectionCount > 0 && context.features.ShouldRecord(EFrameFeature.ReflectionProbes);

            RGRasterPassHandle<DeferredShadingPassData> passHandle = m_RGBuilder.AddRasterPass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.RasterDeferredShading));
            ref DeferredShadingPassData passData = ref passHandle.GetPassData();
            passData.material = m_Material;
            passData.parametersHandle = passHandle.WriteConstants(new DeferredShadingParameters
            {
                resolution = new Vector4(width, height, 1.0f / width, 1.0f / height),
                atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(context.atmosphere.cubemapSize) - 1,
                hasRenderingLayers = m_Settings.config.enableRenderingLayers ? 1 : 0
            });

            // Scene Color is the blend destination. The stencil test reads the depth attachment; the shader samples that depth.
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            passHandle.SetColorAttachment(lightingTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            passData.lightingTextureHandle = lightingTextureHandle;
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
            passData.depthTextureHandle = depthTextureHandle;
            passData.hasSideband = hasSideband;
            if (hasSideband)
            {
                passData.sidebandHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SidebandBuffer));
            }
            passData.gBufferAHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA));
            passData.gBufferBHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
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

            passHandle.SetExecuteFunc((in DeferredShadingPassData passData, in RGRasterEncoder cmdEncoder) =>
            {
                cmdEncoder.SetKeyword(SidebandKeyword, passData.hasSideband);
                cmdEncoder.SetKeyword(OcclusionKeyword, passData.hasOcclusion);
                cmdEncoder.SetKeyword(IndirectDiffuseKeyword, passData.hasIndirectDiffuse);
                cmdEncoder.SetKeyword(ReflectionKeyword, passData.hasReflection);
                cmdEncoder.SetKeyword(ContactShadowKeyword, passData.hasContactShadow);
                cmdEncoder.SetKeyword(RasterReflectionKeyword, passData.hasRasterReflection);
                cmdEncoder.SetKeyword(AnalyticShadowKeyword, passData.hasAnalyticShadow);
                cmdEncoder.SetGlobalConstantBuffer(DeferredShadingPassUtilityData.CBV_DeferredShadingParametersID, passData.parametersHandle);
                if (passData.hasSideband)
                {
                    cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_Sideband, passData.sidebandHandle);
                }
                cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_GBufferTextureA, passData.gBufferAHandle);
                cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                if (passData.hasOcclusion)
                {
                    cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_OcclusionTexture, passData.occlusionTextureHandle);
                }
                if (passData.hasIndirectDiffuse)
                {
                    cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_SSGITexture, passData.ssgiTextureHandle);
                }
                if (passData.hasReflection)
                {
                    cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_SSRTexture, passData.ssrTextureHandle);
                }
                if (passData.hasContactShadow)
                {
                    cmdEncoder.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowSunID, passData.contactShadowTextureHandle);
                    cmdEncoder.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowMaskID, passData.contactShadowMaskHandle);
                }
                if (passData.hasRasterReflection)
                {
                    cmdEncoder.SetGlobalTexture(DeferredShadingPassUtilityData.SRV_RasterReflectionID, passData.rasterReflectionHandle);
                }
                cmdEncoder.SetGlobalTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, passData.atmosphereGGXPrefilterHandle);
                passData.lightLoop.BindGlobal(cmdEncoder);
                passData.reflections.BindGlobal(cmdEncoder);
                passData.analyticShadows.BindGlobal(cmdEncoder);
                passData.shadows.BindGlobal(cmdEncoder);
                cmdEncoder.SetGlobalBuffer(LightShaderIDs.LightRecordBuffer, passData.lightRecordBufferHandle);
                cmdEncoder.DrawProcedural(Matrix4x4.identity, passData.material, 0, MeshTopology.Triangles, 3);
            });

            context.features.MarkProduced(EFrameFeature.DeferredShading);
        }
    }
}
