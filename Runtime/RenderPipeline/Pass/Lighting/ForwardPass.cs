using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ForwardPassUtilityData
    {
        internal static readonly GlobalKeyword SubsurfaceKeyword = GlobalKeyword.Create("INFINITY_SSS_OUTPUT");
        internal static readonly GlobalKeyword ContactShadowKeyword = GlobalKeyword.Create("INFINITY_FORWARD_CONTACT_SHADOW");
        internal static readonly GlobalKeyword OcclusionKeyword = GlobalKeyword.Create("INFINITY_FORWARD_AO");
        internal static string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ForwardPassData
        {
            public bool hasSubsurface;
            public RGDrawListRef draws;
            public RGRendererListRef rendererList;
            public RGTextureRef atmosphereGGX;
            public RGTextureRef occlusion;
            public bool hasOcclusion;
            public FShadowInputs shadows;
            public bool hasContactShadow;
            public RGTextureRef contactShadowSun, contactShadowMask;
            public int hasProbes;
            public bool directionalLightmap, shadowmask;
            public RGBufferRef atmosphereSH;
            public RGBufferRef lightRecords;
            public FLightLoopInputs lightLoop;
            public float iblMaxMip;

            public readonly void BindLighting<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
            {
                commands.SetKeyword(ForwardPassUtilityData.SubsurfaceKeyword, hasSubsurface);
                commands.SetKeyword(ForwardPassUtilityData.OcclusionKeyword, hasOcclusion);
                if (hasOcclusion) commands.SetGlobalTexture(Shader.PropertyToID("SRV_ForwardOcclusion"), occlusion);
                commands.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, directionalLightmap);
                commands.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, shadowmask);
                shadows.BindGlobal(commands);
                commands.SetKeyword(ForwardPassUtilityData.ContactShadowKeyword, hasContactShadow);
                if (hasContactShadow)
                {
                    commands.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowSunID, contactShadowSun);
                    commands.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowMaskID, contactShadowMask);
                }
                commands.SetGlobalInt(Shader.PropertyToID("_InfinityHasProbes"), hasProbes);
                lightLoop.BindGlobal(commands);
                commands.SetGlobalBuffer(LightShaderIDs.LightRecordBuffer, lightRecords);
                commands.SetGlobalTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, atmosphereGGX);
                commands.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSkySH, atmosphereSH);
                commands.SetGlobalFloat(InfinityShaderIDs.AtmosphereIBLMaxMip, iblMaxMip);
            }
        }

        void RasterForward(RenderContext renderContext, Camera camera, in MeshView view, in CullingResults cullingResults)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);

            RGDrawListRef forwardDraws = m_RGBuilder.CreateDrawList(view, MeshPassId.Forward);
            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.RasterForwardPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.OptimizeStateChanges;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListRef forwardRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            //Add ForwardPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RasterForward)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.IndirectDiffuseBuffer), 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.IndirectSpecularBuffer), 2, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                if (m_ActiveFrameState.subsurfaceEnabled) passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer), 3, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                {
                    passData.hasSubsurface = m_ActiveFrameState.subsurfaceEnabled;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = renderContext.lightContext.HasShadowmask;
                    FShadowInputs shadows = QueryShadowInputs(renderContext);
                    shadows.Read(passRef);
                    passData.shadows = shadows;
                    passData.hasContactShadow = m_RGScoper.TryQueryTexture(InfinityShaderIDs.ContactShadowBuffer, out RGTextureRef contactShadowSun);
                    if (passData.hasContactShadow)
                    {
                        passData.contactShadowSun = passRef.ReadTexture(contactShadowSun);
                        passData.contactShadowMask = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                    }
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.hasOcclusion = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureRef occlusion);
                    if (passData.hasOcclusion) passData.occlusion = passRef.ReadTexture(occlusion);
                    passData.draws = passRef.UseDrawList(forwardDraws);
                    passData.rendererList = passRef.UseRendererList(forwardRendererList);
                    passData.atmosphereGGX = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter));
                    passData.atmosphereSH = passRef.ReadBuffer(m_RGScoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH));
                    passData.iblMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(
                        AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).cubemapSize) - 1;
                    passData.lightRecords = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                    FLightLoopInputs lightLoop = QueryLightLoop(renderContext);
                    lightLoop.Read(passRef);
                    passData.lightLoop = lightLoop;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    passData.BindLighting(cmdEncoder);
                    cmdEncoder.Draw(passData.draws);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
