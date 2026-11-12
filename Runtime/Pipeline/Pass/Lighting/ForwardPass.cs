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
        internal static readonly int SRV_ForwardOcclusionID = Shader.PropertyToID("SRV_ForwardOcclusion");
        internal static readonly GlobalKeyword SubsurfaceKeyword = GlobalKeyword.Create("INFINITY_SSS_OUTPUT");
        internal static readonly GlobalKeyword ContactShadowKeyword = GlobalKeyword.Create("INFINITY_FORWARD_CONTACT_SHADOW");
        internal static readonly GlobalKeyword OcclusionKeyword = GlobalKeyword.Create("INFINITY_FORWARD_AO");
        internal static readonly string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ForwardPassData
        {
            public bool hasSubsurface;
            public RGMeshDrawListHandle meshDrawList;
            public RGRendererListHandle rendererList;
            public RGTextureHandle atmosphereGGX;
            public RGTextureHandle occlusion;
            public bool hasOcclusion;
            public ShadowInputs shadows;
            public bool hasContactShadow;
            public RGTextureHandle contactShadowSun, contactShadowMask;
            public int hasProbes;
            public bool directionalLightmap, shadowmask;
            public RGBufferHandle atmosphereSH;
            public RGBufferHandle lightRecords;
            public LightLoopInputs lightLoop;
            public float iblMaxMip;

            public readonly void BindLighting<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
            {
                commands.SetKeyword(ForwardPassUtilityData.SubsurfaceKeyword, hasSubsurface);
                commands.SetKeyword(ForwardPassUtilityData.OcclusionKeyword, hasOcclusion);
                if (hasOcclusion)
                {
                    commands.SetGlobalTexture(ForwardPassUtilityData.SRV_ForwardOcclusionID, occlusion);
                }
                commands.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, directionalLightmap);
                commands.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, shadowmask);
                shadows.BindGlobal(commands);
                commands.SetKeyword(ForwardPassUtilityData.ContactShadowKeyword, hasContactShadow);
                if (hasContactShadow)
                {
                    commands.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowSunID, contactShadowSun);
                    commands.SetGlobalTexture(ContactShadowPassUtilityData.SRV_ContactShadowMaskID, contactShadowMask);
                }
                commands.SetGlobalInt(PassShaderIDs.InfinityHasProbes, hasProbes);
                lightLoop.BindGlobal(commands);
                commands.SetGlobalBuffer(LightShaderIDs.LightRecordBuffer, lightRecords);
                commands.SetGlobalTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, atmosphereGGX);
                commands.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSkySH, atmosphereSH);
                commands.SetGlobalFloat(InfinityShaderIDs.AtmosphereIBLMaxMip, iblMaxMip);
            }
        }

        void RasterForward(RenderContext renderContext, Camera camera, in MeshView view, in CullingResults cullingResults, in RGMeshDrawListHandle forwardMeshDrawList)
        {
            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.RasterForwardPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.OptimizeStateChanges;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListHandle forwardRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            //Add ForwardPass
            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RasterForward)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.IndirectDiffuseBuffer), 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.IndirectSpecularBuffer), 2, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                if (m_ActiveFrameState.subsurfaceEnabled)
                {
                    passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer), 3, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                }
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                {
                    passData.hasSubsurface = m_ActiveFrameState.subsurfaceEnabled;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = renderContext.lightContext.HasShadowmask;
                    ShadowInputs shadows = QueryShadowInputs(renderContext);
                    shadows.Read(passRef);
                    passData.shadows = shadows;
                    passData.hasContactShadow = m_RGScoper.TryQueryTexture(InfinityShaderIDs.ContactShadowBuffer, out RGTextureHandle contactShadowSun);
                    if (passData.hasContactShadow)
                    {
                        passData.contactShadowSun = passRef.ReadTexture(contactShadowSun);
                        passData.contactShadowMask = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.ContactShadowMaskBuffer));
                    }
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.hasOcclusion = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureHandle occlusion);
                    if (passData.hasOcclusion)
                    {
                        passData.occlusion = passRef.ReadTexture(occlusion);
                    }
                    passData.meshDrawList = passRef.UseMeshDrawList(forwardMeshDrawList);
                    passData.rendererList = passRef.UseRendererList(forwardRendererList);
                    passData.atmosphereGGX = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter));
                    passData.atmosphereSH = passRef.ReadBuffer(m_RGScoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH));
                    passData.iblMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(
                        AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).cubemapSize) - 1;
                    passData.lightRecords = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                    LightLoopInputs lightLoop = QueryLightLoop(renderContext);
                    lightLoop.Read(passRef);
                    passData.lightLoop = lightLoop;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    passData.BindLighting(cmdEncoder);
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
