using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ForwardPassUtilityData
    {
        internal static string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ForwardPassData
        {
            public RendererList rendererList;
            public RGDrawListRef draws;
            public RGTextureRef atmosphereGGX;
            public RGBufferRef atmosphereSH, tileRange, tileList;
            public RGBufferRef emptyTileRange, emptyTileList;
            public RGBufferRef lightRecords;
            public int directionalCount, localCount;
            public bool hasLocalLights;
            public float iblMaxMip;

            public readonly void BindLighting<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
            {
                commands.SetGlobalInt(LightShaderIDs.DirectionalLightCount, directionalCount);
                commands.SetGlobalInt(LightShaderIDs.LocalLightCount, localCount);
                commands.SetGlobalBuffer(LightShaderIDs.LightRecordBuffer, lightRecords);
                commands.SetGlobalTexture(InfinityShaderIDs.AtmosphereGGXPrefilter, atmosphereGGX);
                commands.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSkySH, atmosphereSH);
                commands.SetGlobalFloat(InfinityShaderIDs.AtmosphereIBLMaxMip, iblMaxMip);
                commands.SetGlobalInt(LightShaderIDs.HasTileLightList, hasLocalLights ? 1 : 0);
                if (hasLocalLights)
                {
                    commands.SetGlobalBuffer(ZBinningPassUtilityData.SRV_TileLightRangeID, tileRange);
                    commands.SetGlobalBuffer(ZBinningPassUtilityData.SRV_TileLightListID, tileList);
                }
                else
                {
                    commands.SetGlobalBuffer(ZBinningPassUtilityData.SRV_TileLightRangeID, emptyTileRange);
                    commands.SetGlobalBuffer(ZBinningPassUtilityData.SRV_TileLightListID, emptyTileList);
                }
            }
        }

        void RenderForward(RenderContext renderContext, Camera camera, MeshVisibilityHandle visibility, in CullingResults cullingResults)
        {
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.ForwardPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.OptimizeStateChanges;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList forwardRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            MeshFilterProgram forwardFilter = BuiltinMeshesPasses.Forward.defaultFilter;
            forwardFilter.layerMask = camera.cullingMask;
            forwardFilter.renderingLayerMask = (uint)ERenderingLayer.Everything;
            var forwardRequest = new MeshDrawRequest
            {
                filter = forwardFilter,
                sort = BuiltinMeshesPasses.Forward.defaultSort,
                backendPolicy = EMeshBackendPolicy.Auto,
                shaderPassIndex = BuiltinMeshesPasses.Forward.shaderPassIndex,
                lightModeTag = BuiltinMeshesPasses.Forward.lightModeTag,
                viewPosition = camera.transform.position,
                viewKey = UnityEntityId.ToUInt64(camera)
            };
            RGDrawListRef forwardDraws = m_RGBuilder.DeclareDrawList(m_ForwardMeshProcessor, forwardRequest, visibility, m_VisibilityShare);

            //Add ForwardPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(ProfilingSampler.Get(CustomSamplerId.RenderForward)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(lightingTexture, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                {
                    passData.rendererList = forwardRendererList;
                    passData.draws = passRef.UseDrawList(forwardDraws);
                    passData.atmosphereGGX = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter));
                    passData.atmosphereSH = passRef.ReadBuffer(m_RGScoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH));
                    passData.iblMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(
                        AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).cubemapSize) - 1;
                    passData.lightRecords = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LightRecordBuffer));
                    passData.directionalCount = renderContext.lightContext.DirectionalLightCount;
                    passData.localCount = renderContext.lightContext.LocalLightCount;
                    passData.hasLocalLights = renderContext.lightContext.HasZBinningLightList();
                    if (passData.hasLocalLights)
                    {
                        passData.tileRange = passRef.ReadBuffer(m_RGScoper.QueryBuffer(InfinityShaderIDs.TileLightRangeBuffer));
                        passData.tileList = passRef.ReadBuffer(m_RGScoper.QueryBuffer(InfinityShaderIDs.TileLightListBuffer));
                    }
                    passData.emptyTileRange = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.EmptyTileRangeBuffer));
                    passData.emptyTileList = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.EmptyTileListBuffer));
                }

                //Execute Phase
                passRef.SetExecuteFunc((in ForwardPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    passData.BindLighting(cmdEncoder);
                    //MeshDrawPipeline
                    cmdEncoder.Draw(passData.draws);

                    //UnityDrawPipeline
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
