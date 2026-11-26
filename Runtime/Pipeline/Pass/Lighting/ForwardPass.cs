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
        internal static readonly GlobalKeyword SubsurfaceKeyword = GlobalKeyword.Create("INFINITY_SSS_OUTPUT");
        internal static readonly GlobalKeyword SubsurfaceAlphaKeyword = GlobalKeyword.Create("INFINITY_SSS_ALPHA");
        internal static readonly GlobalKeyword IndirectDiffuseKeyword = GlobalKeyword.Create("INFINITY_FORWARD_INDIRECT_DIFFUSE");
        internal static readonly GlobalKeyword IndirectSpecularKeyword = GlobalKeyword.Create("INFINITY_FORWARD_INDIRECT_SPECULAR");
        internal static readonly GlobalKeyword ContactShadowKeyword = GlobalKeyword.Create("INFINITY_FORWARD_CONTACT_SHADOW");
        internal static readonly GlobalKeyword OcclusionKeyword = GlobalKeyword.Create("INFINITY_FORWARD_AO");
        internal static readonly string TextureName = "LightingTexture";
    }

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
            public bool hasSeparateSubsurface;
            public bool hasAlphaSubsurface;
            public bool needsIndirectDiffuse;
            public bool needsIndirectSpecular;
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle atmosphereGGXHandle;
            public RGTextureHandle occlusionHandle;
            public bool hasOcclusion;
            public ShadowInputs shadows;
            public bool hasContactShadow;
            public RGTextureHandle contactShadowSunHandle, contactShadowMaskHandle;
            public int hasProbes;
            public bool directionalLightmap, shadowmask;
            public RGBufferHandle atmosphereSHHandle;
            public RGBufferHandle lightRecordsHandle;
            public LightLoopInputs lightLoop;
            public ReflectionInputs reflections;
            public bool hasAnalyticShadow;
            public AnalyticShadowInputs analyticShadows;
            public float iblMaxMip;

            public readonly void BindLighting<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
            {
                commands.SetKeyword(ForwardPassUtilityData.SubsurfaceKeyword, hasSeparateSubsurface);
                commands.SetKeyword(ForwardPassUtilityData.SubsurfaceAlphaKeyword, hasAlphaSubsurface);
                commands.SetKeyword(ForwardPassUtilityData.IndirectDiffuseKeyword, needsIndirectDiffuse);
                commands.SetKeyword(ForwardPassUtilityData.IndirectSpecularKeyword, needsIndirectSpecular);
                commands.SetKeyword(ForwardPassUtilityData.OcclusionKeyword, hasOcclusion);
                if (hasOcclusion)
                {
                    commands.SetGlobalTexture(ForwardPassUtilityData.SRV_ForwardOcclusionID, occlusionHandle);
                }
                commands.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, directionalLightmap);
                commands.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, shadowmask);
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
            }
        }

        internal void RasterForward(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RGMeshDrawListHandle forwardMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_ForwardMeshPass);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGRendererListHandle forwardRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterForwardPass,
                new RenderQueueRange(0, 2999), SortingCriteria.OptimizeStateChanges, PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume, m_Settings.enableInstanceBatch));

            bool needsIndirectDiffuse = context.features.ShouldRecord(EFrameFeature.SSGI);
            bool needsIndirectSpecular = context.features.ShouldRecord(EFrameFeature.SSR);
            bool hasSSS = context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
            bool separateSSS = hasSSS && m_Settings.config.subsurfaceLightingMode == ESubsurfaceLightingMode.SeparateBuffer;
            bool alphaSSS = hasSSS && m_Settings.config.subsurfaceLightingMode == ESubsurfaceLightingMode.SceneColorAlpha;

            //Add ForwardPass
            {
                RGRasterPassHandle<ForwardPassData> passHandle = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RasterForward));
                //Setup Phase
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(lightingTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                int nextAttachmentSlot = 1;
                if (needsIndirectDiffuse)
                {
                    passHandle.SetColorAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.IndirectDiffuseBuffer), nextAttachmentSlot++, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                }
                if (needsIndirectSpecular)
                {
                    passHandle.SetColorAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.IndirectSpecularBuffer), nextAttachmentSlot++, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                }
                if (separateSSS)
                {
                    passHandle.SetColorAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer), nextAttachmentSlot++, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                }
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passHandle.GetPassData();
                {
                    passData.hasSeparateSubsurface = separateSSS;
                    passData.hasAlphaSubsurface = alphaSSS;
                    passData.needsIndirectDiffuse = needsIndirectDiffuse;
                    passData.needsIndirectSpecular = needsIndirectSpecular;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = context.lights.HasShadowmask;
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
