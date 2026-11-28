using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ForwardPassUtilityData
    {
        internal static readonly int SRV_ForwardOcclusionID = Shader.PropertyToID("SRV_ForwardOcclusion");
        internal static readonly GlobalKeyword IndirectDiffuseKeyword = GlobalKeyword.Create("INFINITY_FORWARD_SSGI");
        internal static readonly GlobalKeyword ReflectionKeyword = GlobalKeyword.Create("INFINITY_FORWARD_SSR");
        internal static readonly GlobalKeyword ContactShadowKeyword = GlobalKeyword.Create("INFINITY_FORWARD_CONTACT_SHADOW");
        internal static readonly GlobalKeyword OcclusionKeyword = GlobalKeyword.Create("INFINITY_FORWARD_AO");
    }

    // Forward materials complete their shading in one execution: Scene Color takes the whole result with blending off,
    // and the sideband, when allocated, takes the diffuse share in R (the shaders mask that target to R).

    internal sealed class ForwardPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly MeshPassId m_ForwardMeshPass;

        internal ForwardPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId forwardMeshPass)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
            m_ForwardMeshPass = forwardMeshPass;
        }

        struct ForwardPassData
        {
            public bool hasSideband;
            public bool hasIndirectDiffuse;
            public bool hasReflection;
            public RGTextureHandle ssgiTextureHandle;
            public RGTextureHandle ssgiConfidenceHandle;
            public RGTextureHandle ssrTextureHandle;
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle atmosphereGGXHandle;
            public RGTextureHandle occlusionHandle;
            public bool hasOcclusion;
            public ShadowInputs shadows;
            public bool hasContactShadow;
            public RGTextureHandle contactShadowSunHandle, contactShadowMaskHandle;
            public int hasProbes;
            public bool directionalLightmap;
            public bool shadowmask;
            public Vector4 mixedLightChannels;
            public RGBufferHandle atmosphereSHHandle;
            public RGBufferHandle lightRecordsHandle;
            public LightLoopInputs lightLoop;
            public ReflectionInputs reflections;
            public bool hasAnalyticShadow;
            public AnalyticShadowInputs analyticShadows;
            public float iblMaxMip;
            public bool bindDBuffer;
            public RGTextureHandle dBufferAHandle, dBufferBHandle, dBufferCHandle;
            public ClusteredDecalInputs clusteredDecals;

            public readonly void BindLighting<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
            {
                commands.SetKeyword(GBufferPassUtilityData.SidebandKeyword, hasSideband);
                commands.SetKeyword(ForwardPassUtilityData.IndirectDiffuseKeyword, hasIndirectDiffuse);
                if (hasIndirectDiffuse)
                {
                    commands.SetGlobalTexture(PassShaderIDs.SRV_SSGITexture, ssgiTextureHandle);
                    commands.SetGlobalTexture(PassShaderIDs.SRV_SSGIConfidenceTexture, ssgiConfidenceHandle);
                }
                commands.SetKeyword(ForwardPassUtilityData.ReflectionKeyword, hasReflection);
                if (hasReflection)
                {
                    commands.SetGlobalTexture(PassShaderIDs.SRV_SSRTexture, ssrTextureHandle);
                }
                commands.SetKeyword(ForwardPassUtilityData.OcclusionKeyword, hasOcclusion);
                if (hasOcclusion)
                {
                    commands.SetGlobalTexture(ForwardPassUtilityData.SRV_ForwardOcclusionID, occlusionHandle);
                }
                commands.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, directionalLightmap);
                commands.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, shadowmask);
                commands.SetGlobalVector(GBufferPassUtilityData.MixedLightChannelsID, mixedLightChannels);
                shadows.BindGlobal(commands);
                commands.SetKeyword(ForwardPassUtilityData.ContactShadowKeyword, hasContactShadow);
                if (hasContactShadow)
                {
                    commands.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowSunID, contactShadowSunHandle);
                    commands.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowMaskID, contactShadowMaskHandle);
                }
                commands.SetKeyword(AnalyticShadowPassUtilityData.ForwardAnalyticShadowKeyword, hasAnalyticShadow);
                if (hasAnalyticShadow)
                {
                    analyticShadows.BindGlobal(commands);
                }
                commands.SetGlobalInt(PassShaderIDs.InfinityHasProbes, hasProbes);
                lightLoop.BindGlobal(commands);
                reflections.BindGlobal(commands);
                commands.SetGlobalBuffer(LightShaderIDs.LightRecordBuffer, lightRecordsHandle);
                commands.SetGlobalTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, atmosphereGGXHandle);
                commands.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSkySH, atmosphereSHHandle);
                commands.SetGlobalFloat(InfinityShaderIDs.AtmosphereIBLMaxMip, iblMaxMip);
                commands.SetKeyword(GBufferPassUtilityData.DBufferKeyword, bindDBuffer);
                if (bindDBuffer)
                {
                    commands.SetGlobalTexture(InfinityShaderIDs.DBufferA, dBufferAHandle);
                    commands.SetGlobalTexture(InfinityShaderIDs.DBufferB, dBufferBHandle);
                    commands.SetGlobalTexture(InfinityShaderIDs.DBufferC, dBufferCHandle);
                }
                commands.SetKeyword(GBufferPassUtilityData.ClusteredDecalKeyword, clusteredDecals.active);
                clusteredDecals.BindGlobal(commands);
            }
        }

        internal void RasterForward(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RGMeshDrawListHandle forwardMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_ForwardMeshPass);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGRendererListHandle forwardRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterForwardPass,
                new RenderQueueRange(0, 2999), SortingCriteria.OptimizeStateChanges, PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume, m_Settings.enableInstanceBatch));

            bool hasSideband = context.sideband;
            bool hasIndirectDiffuse = context.features.ShouldRecord(EFrameFeature.SSGI);
            bool hasReflection = context.features.ShouldRecord(EFrameFeature.SSR);

            //Add ForwardPass
            {
                RGRasterPassHandle<ForwardPassData> passHandle = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RasterForward));
                //Setup Phase
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(lightingTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                if (hasSideband)
                {
                    passHandle.SetColorAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SidebandBuffer), 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                }
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passHandle.GetPassData();
                {
                    passData.hasSideband = hasSideband;
                    passData.hasIndirectDiffuse = hasIndirectDiffuse;
                    if (hasIndirectDiffuse)
                    {
                        passData.ssgiTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSGIBuffer));
                        passData.ssgiConfidenceHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSGIConfidenceBuffer));
                    }
                    passData.hasReflection = hasReflection;
                    if (hasReflection)
                    {
                        passData.ssrTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SSRBuffer));
                    }
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = context.lights.HasMixedLights;
                    passData.mixedLightChannels = context.lights.MixedLightChannels;
                    ShadowInputs shadows = ShadowInputs.Query(m_RGBuilder, context);
                    shadows.Read(passHandle);
                    passData.shadows = shadows;
                    passData.hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);
                    if (passData.hasContactShadow)
                    {
                        passData.contactShadowSunHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer));
                        passData.contactShadowMaskHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                    }
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.hasOcclusion = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);
                    if (passData.hasOcclusion)
                    {
                        passData.occlusionHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer));
                    }
                    passData.meshDrawListHandle = passHandle.UseMeshDrawList(forwardMeshDrawListHandle);
                    passData.rendererListHandle = passHandle.UseRendererList(forwardRendererListHandle);
                    passData.atmosphereGGXHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter));
                    passData.atmosphereSHHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH));
                    passData.iblMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(
                        context.atmosphere.cubemapSize) - 1;
                    passData.lightRecordsHandle = passHandle.ReadBuffer(m_RGBuilder.scoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                    LightLoopInputs lightLoop = LightTileMaskPass.QueryLightLoop(m_RGBuilder, context);
                    lightLoop.Read(passHandle);
                    passData.lightLoop = lightLoop;
                    ReflectionInputs reflections = ReflectionInputs.Query(m_RGBuilder, context);
                    reflections.Read(passHandle);
                    passData.reflections = reflections;
                    AnalyticShadowInputs analyticShadows = AnalyticShadowInputs.Query(m_RGBuilder, context);
                    passData.hasAnalyticShadow = analyticShadows.active && context.frameSettings.IsEnabled(EFrameSetting.AnalyticShadow);
                    analyticShadows.Read(passHandle);
                    passData.analyticShadows = analyticShadows;
                    passData.bindDBuffer = context.features.IsProduced(EFrameFeature.DBuffer);
                    if (passData.bindDBuffer)
                    {
                        passData.dBufferAHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferA));
                        passData.dBufferBHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferB));
                        passData.dBufferCHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferC));
                    }
                    ClusteredDecalInputs clusteredDecals = ClusteredDecalInputs.Query(m_RGBuilder, context);
                    clusteredDecals.Read(passHandle);
                    passData.clusteredDecals = clusteredDecals;
                }

                //Execute Phase
                passHandle.SetExecuteFunc((in ForwardPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    passData.BindLighting(cmdEncoder);
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }
        }
    }
}
