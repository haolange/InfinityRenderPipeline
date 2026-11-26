using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
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
        public float farDepth;
        public float atmosphereIBLMaxMip;
        public int hasRenderingLayers;
        public int padding1;
    }

    internal static class DeferredShadingPassUtilityData
    {
        internal static readonly int CBV_DeferredShadingParametersID = Shader.PropertyToID("CBV_DeferredShadingParameters");
        internal static readonly int SRV_BakedDiffuseID = Shader.PropertyToID("SRV_BakedDiffuse");
        internal static readonly int SRV_BakedShadowmaskID = Shader.PropertyToID("SRV_BakedShadowmask");
        internal static readonly int SRV_RasterReflectionID = Shader.PropertyToID("SRV_RasterReflection");
        internal static readonly int UAV_IndirectDiffuseID = Shader.PropertyToID("UAV_IndirectDiffuse");
        internal static readonly int UAV_IndirectSpecularID = Shader.PropertyToID("UAV_IndirectSpecular");
        internal static readonly string TextureName = "LightingTexture";
    }

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
            public int kernel;
            public int2 groupCount;
            public LightLoopInputs lightLoop;
            public ReflectionInputs reflections;
            public ClusteredDecalInputs decals;
            public AnalyticShadowInputs analyticShadows;
            public RGConstantsHandle parametersHandle;
            public bool hasSeparateSubsurface;
            public bool hasAlphaSubsurface;
            public bool hasBakedDiffuse;
            public bool hasShadowmask;
            public bool hasIndirectDiffuse;
            public bool hasIndirectSpecular;
            public bool hasRasterReflection;
            public bool hasClusteredDecals;
            public bool hasAnalyticShadow;
            public RGTextureHandle scatterDiffuseHandle;
            public RGTextureHandle rasterReflectionHandle;
            public int hasAO;
            public int hasContactShadow;
            public RGBufferHandle lightRecordBufferHandle;
            public ShadowInputs shadows;
            public ComputeShader deferredShadingShader;
            public LocalKeyword subsurfaceKeyword;
            public LocalKeyword subsurfaceAlphaKeyword;
            public LocalKeyword bakedDiffuseKeyword;
            public LocalKeyword shadowmaskKeyword;
            public LocalKeyword indirectDiffuseKeyword;
            public LocalKeyword indirectSpecularKeyword;
            public LocalKeyword occlusionKeyword;
            public LocalKeyword contactShadowKeyword;
            public LocalKeyword rasterReflectionKeyword;
            public LocalKeyword clusteredDecalsKeyword;
            public LocalKeyword analyticShadowKeyword;
            public RGTextureHandle bakedDiffuseHandle, bakedShadowmaskHandle, indirectDiffuseHandle, indirectSpecularHandle;
            public RGTextureHandle gBufferAHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle surfaceMetadataHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle occlusionTextureHandle;
            public RGTextureHandle contactShadowTextureHandle;
            public RGTextureHandle contactShadowMaskHandle;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle atmosphereGGXPrefilterHandle;
            public RGBufferHandle atmosphereSkySHHandle;
        }

        internal void ComputeDeferredShading(in ViewContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            EffectivePipelineConfiguration config = m_Settings.config;
            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            ComputeKernel kernel = m_Resources.kernels.deferredShading;
            int2 groupCount = new int2(kernel.GroupsX(width), kernel.GroupsY(height));

            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle gBufferAHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle surfaceMetadataHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SurfaceMetadataBuffer);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            // The feature set decides which optional inputs exist; a recorded producer that did not register its output throws.
            bool hasAO = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
            RGTextureHandle occlusionTextureHandle = hasAO ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer) : default;

            bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);
            RGTextureHandle contactShadowTextureHandle = hasContactShadow ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer) : default;
            RGTextureHandle atmosphereGGXPrefilterHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter);
            RGBufferHandle atmosphereSkySHHandle = m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);
            AtmosphereParameter atmosphereParameter = context.atmosphere;

            bool hasBakedDiffuse = GBufferPass.NeedsBakedDiffuse(in context);
            bool hasShadowmask = context.lights.HasShadowmask;
            bool hasIndirectDiffuse = context.features.ShouldRecord(EFrameFeature.SSGI);
            bool hasIndirectSpecular = context.features.ShouldRecord(EFrameFeature.SSR);
            bool hasSSS = context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
            bool separateSSS = hasSSS && config.subsurfaceLightingMode == ESubsurfaceLightingMode.SeparateBuffer;
            bool alphaSSS = hasSSS && config.subsurfaceLightingMode == ESubsurfaceLightingMode.SceneColorAlpha;

            RGTextureHandle indirectDiffuseHandle = default;
            if (hasIndirectDiffuse)
            {
                var indirectDiffuseDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
                { name = "IndirectDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = config.indirectLightingFormat, enableRandomWrite = true };
                indirectDiffuseHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectDiffuseBuffer, indirectDiffuseDsc);
            }
            RGTextureHandle indirectSpecularHandle = default;
            if (hasIndirectSpecular)
            {
                var indirectSpecularDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
                { name = "IndirectSpecular", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = config.indirectLightingFormat, enableRandomWrite = true };
                indirectSpecularHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.IndirectSpecularBuffer, indirectSpecularDsc);
            }

            RGTextureHandle scatterDiffuseHandle = default;
            if (separateSSS)
            {
                var scatterDescriptor = new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
                { name = "ScatterDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = config.scatterDiffuseFormat, enableRandomWrite = true };
                scatterDiffuseHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ScatterDiffuseBuffer, scatterDescriptor);
            }

            {
                RGComputePassHandle<DeferredShadingPassData> passHandle = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading));
                ref DeferredShadingPassData passData = ref passHandle.GetPassData();
                passData.hasBakedDiffuse = hasBakedDiffuse;
                if (hasBakedDiffuse)
                {
                    passData.bakedDiffuseHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.BakedDiffuseBuffer));
                }
                passData.hasShadowmask = hasShadowmask;
                if (hasShadowmask)
                {
                    passData.bakedShadowmaskHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.BakedShadowmaskBuffer));
                }
                passData.hasIndirectDiffuse = hasIndirectDiffuse;
                if (hasIndirectDiffuse)
                {
                    passData.indirectDiffuseHandle = passHandle.WriteTexture(indirectDiffuseHandle);
                }
                passData.hasIndirectSpecular = hasIndirectSpecular;
                if (hasIndirectSpecular)
                {
                    passData.indirectSpecularHandle = passHandle.WriteTexture(indirectSpecularHandle);
                }
                passData.hasSeparateSubsurface = separateSSS;
                passData.hasAlphaSubsurface = alphaSSS;
                if (separateSSS)
                {
                    passData.scatterDiffuseHandle = passHandle.WriteTexture(scatterDiffuseHandle);
                }
                passData.kernel = kernel.index;
                passData.groupCount = groupCount;
                passData.parametersHandle = passHandle.WriteConstants(new DeferredShadingParameters
                {
                    resolution = new Vector4(width, height, 1.0f / width, 1.0f / height),
                    farDepth = GraphicsConventions.SampledFarDepth,
                    atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(atmosphereParameter.cubemapSize) - 1,
                    hasRenderingLayers = config.enableRenderingLayers ? 1 : 0
                });
                passData.hasAO = hasAO ? 1 : 0;
                passData.hasContactShadow = hasContactShadow ? 1 : 0;
                LightLoopInputs lightLoop = LightTileMaskPass.QueryLightLoop(m_RGBuilder, context);
                lightLoop.Read(passHandle);
                passData.lightLoop = lightLoop;
                ReflectionInputs reflections = ReflectionInputs.Query(m_RGBuilder, context);
                reflections.Read(passHandle);
                passData.reflections = reflections;
                bool hasRasterReflection = context.reflections.RasterReflectionCount > 0 && context.features.ShouldRecord(EFrameFeature.ReflectionProbes);
                passData.hasRasterReflection = hasRasterReflection;
                if (hasRasterReflection)
                {
                    passData.rasterReflectionHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.RasterReflectionBuffer));
                }
                ClusteredDecalInputs decals = ClusteredDecalInputs.Query(m_RGBuilder, context);
                passData.hasClusteredDecals = decals.active;
                decals.Read(passHandle);
                passData.decals = decals;
                AnalyticShadowInputs analyticShadows = AnalyticShadowInputs.Query(m_RGBuilder, context);
                passData.hasAnalyticShadow = analyticShadows.active && context.frameSettings.IsEnabled(EFrameSetting.AnalyticShadow);
                analyticShadows.Read(passHandle);
                passData.analyticShadows = analyticShadows;
                passData.lightRecordBufferHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                ShadowInputs shadows = ShadowInputs.Query(m_RGBuilder, context);
                shadows.Read(passHandle);
                passData.shadows = shadows;
                passData.deferredShadingShader = kernel.shader;
                passData.subsurfaceKeyword = m_Resources.kernels.deferredShadingSubsurface;
                passData.subsurfaceAlphaKeyword = m_Resources.kernels.deferredShadingSubsurfaceAlpha;
                passData.bakedDiffuseKeyword = m_Resources.kernels.deferredShadingBakedDiffuse;
                passData.shadowmaskKeyword = m_Resources.kernels.deferredShadingShadowmask;
                passData.indirectDiffuseKeyword = m_Resources.kernels.deferredShadingIndirectDiffuse;
                passData.indirectSpecularKeyword = m_Resources.kernels.deferredShadingIndirectSpecular;
                passData.occlusionKeyword = m_Resources.kernels.deferredShadingOcclusion;
                passData.contactShadowKeyword = m_Resources.kernels.deferredShadingContactShadow;
                passData.rasterReflectionKeyword = m_Resources.kernels.deferredShadingRasterReflection;
                passData.clusteredDecalsKeyword = m_Resources.kernels.deferredShadingClusteredDecals;
                passData.analyticShadowKeyword = m_Resources.kernels.deferredShadingAnalyticShadow;
                passData.gBufferAHandle = passHandle.ReadTexture(gBufferAHandle);
                passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                passData.surfaceMetadataHandle = passHandle.ReadTexture(surfaceMetadataHandle);
                passData.depthTextureHandle = passHandle.ReadTexture(depthTextureHandle);
                if (hasAO)
                {
                    passData.occlusionTextureHandle = passHandle.ReadTexture(occlusionTextureHandle);
                }
                if (hasContactShadow)
                {
                    passData.contactShadowTextureHandle = passHandle.ReadTexture(contactShadowTextureHandle);
                    passData.contactShadowMaskHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                }
                passData.lightingTextureHandle = passHandle.WriteTexture(lightingTextureHandle);
                passData.atmosphereGGXPrefilterHandle = passHandle.ReadTexture(atmosphereGGXPrefilterHandle);
                passData.atmosphereSkySHHandle = passHandle.ReadBuffer(atmosphereSkySHHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.deferredShadingShader;
                    cmdEncoder.SetKeyword(shader, passData.subsurfaceKeyword, passData.hasSeparateSubsurface);
                    cmdEncoder.SetKeyword(shader, passData.subsurfaceAlphaKeyword, passData.hasAlphaSubsurface);
                    cmdEncoder.SetKeyword(shader, passData.bakedDiffuseKeyword, passData.hasBakedDiffuse);
                    cmdEncoder.SetKeyword(shader, passData.shadowmaskKeyword, passData.hasShadowmask);
                    cmdEncoder.SetKeyword(shader, passData.indirectDiffuseKeyword, passData.hasIndirectDiffuse);
                    cmdEncoder.SetKeyword(shader, passData.indirectSpecularKeyword, passData.hasIndirectSpecular);
                    if (passData.hasSeparateSubsurface)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_ScatterDiffuse, passData.scatterDiffuseHandle);
                    }
                    if (passData.hasBakedDiffuse)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_BakedDiffuseID, passData.bakedDiffuseHandle);
                    }
                    if (passData.hasShadowmask)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_BakedShadowmaskID, passData.bakedShadowmaskHandle);
                    }
                    if (passData.hasIndirectDiffuse)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_IndirectDiffuseID, passData.indirectDiffuseHandle);
                    }
                    if (passData.hasIndirectSpecular)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.UAV_IndirectSpecularID, passData.indirectSpecularHandle);
                    }
                    cmdEncoder.SetComputeConstantBufferParam(shader, DeferredShadingPassUtilityData.CBV_DeferredShadingParametersID, passData.parametersHandle);
                    passData.lightLoop.Bind(cmdEncoder, shader, passData.kernel);
                    passData.reflections.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetKeyword(shader, passData.rasterReflectionKeyword, passData.hasRasterReflection);
                    if (passData.hasRasterReflection)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, DeferredShadingPassUtilityData.SRV_RasterReflectionID, passData.rasterReflectionHandle);
                    }
                    cmdEncoder.SetKeyword(shader, passData.clusteredDecalsKeyword, passData.hasClusteredDecals);
                    passData.decals.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetKeyword(shader, passData.analyticShadowKeyword, passData.hasAnalyticShadow);
                    passData.analyticShadows.Bind(cmdEncoder, shader, passData.kernel);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, LightShaderIDs.LightRecordBuffer, passData.lightRecordBufferHandle);
                    passData.shadows.Bind(cmdEncoder, shader, passData.kernel);

                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureA, passData.gBufferAHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_SurfaceMetadata, passData.surfaceMetadataHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                    cmdEncoder.SetKeyword(shader, passData.occlusionKeyword, passData.hasAO != 0);
                    if (passData.hasAO != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.SRV_OcclusionTexture, passData.occlusionTextureHandle);
                    }
                    cmdEncoder.SetKeyword(shader, passData.contactShadowKeyword, passData.hasContactShadow != 0);
                    if (passData.hasContactShadow != 0)
                    {
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowSunID, passData.contactShadowTextureHandle);
                        cmdEncoder.SetComputeTextureParam(shader, passData.kernel, ContactShadowPassUtilityData.SRV_ContactShadowMaskID, passData.contactShadowMaskHandle);
                    }
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, PassShaderIDs.UAV_LightingTexture, passData.lightingTextureHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernel, InfinityShaderIDs.AtmosphereGGXPrefilter, passData.atmosphereGGXPrefilterHandle);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernel, InfinityShaderIDs.AtmosphereSkySH, passData.atmosphereSkySHHandle);

                    cmdEncoder.DispatchCompute(shader, passData.kernel, passData.groupCount.x, passData.groupCount.y, 1);
                });
            }

            context.features.MarkProduced(EFrameFeature.DeferredShading);
        }
    }
}
