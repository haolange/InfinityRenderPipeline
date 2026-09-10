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
        internal static readonly GlobalKeyword OcclusionKeyword = GlobalKeyword.Create("INFINITY_FORWARD_AO");
        internal static string TextureName = "LightingTexture";
    }

    public partial class InfinityRenderPipeline
    {
        struct ForwardPassData
        {
            public RendererList rendererList;
            public RGDrawListRef draws;
            public RGTextureRef atmosphereGGX;
            public RGTextureRef occlusion;
            public bool hasOcclusion;
            public RGTextureRef cascadeShadow, localShadow;
            public RGBufferRef shadowMatrices, shadowRects;
            public Matrix4x4[] cascadeMatrices;
            public Vector4[] cascadeSpheres;
            public Vector4 cascadeSplits, cascadeSize, localSize;
            public int cascadeCount, shadowmaskMode, hasProbes;
            public float shadowDistance;
            public bool directionalLightmap, shadowmask;
            public RGBufferRef atmosphereSH, tileRange, tileList;
            public RGBufferRef emptyTileRange, emptyTileList;
            public RGBufferRef lightRecords;
            public int directionalCount, localCount;
            public bool hasLocalLights;
            public float iblMaxMip;

            public readonly void BindLighting<TCommands>(in TCommands commands) where TCommands : struct, IRasterCommands
            {
                commands.SetKeyword(ForwardPassUtilityData.OcclusionKeyword, hasOcclusion);
                if (hasOcclusion) commands.SetGlobalTexture(Shader.PropertyToID("SRV_ForwardOcclusion"), occlusion);
                commands.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, directionalLightmap);
                commands.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, shadowmask);
                commands.SetGlobalTexture(Shader.PropertyToID("SRV_CascadeShadowMap"), cascadeShadow);
                commands.SetGlobalTexture(Shader.PropertyToID("SRV_LocalShadowMap"), localShadow);
                commands.SetGlobalBuffer(Shader.PropertyToID("SRV_LocalShadowMatrices"), shadowMatrices);
                commands.SetGlobalBuffer(Shader.PropertyToID("SRV_LocalShadowRects"), shadowRects);
                commands.SetGlobalMatrixArray(CascadeShadowPassUtilityData.CascadeMatricesID, cascadeMatrices);
                commands.SetGlobalVectorArray(CascadeShadowPassUtilityData.CascadeSpheresID, cascadeSpheres);
                commands.SetGlobalVector(CascadeShadowPassUtilityData.CascadeSplitDistancesID, cascadeSplits);
                commands.SetGlobalVector(CascadeShadowPassUtilityData.CascadeShadowMapSizeID, cascadeSize);
                commands.SetGlobalVector(Shader.PropertyToID("_LocalShadowMapSize"), localSize);
                commands.SetGlobalInt(CascadeShadowPassUtilityData.CascadeCountID, cascadeCount);
                commands.SetGlobalInt(Shader.PropertyToID("_InfinityShadowmaskMode"), shadowmaskMode);
                commands.SetGlobalFloat(Shader.PropertyToID("_InfinityShadowDistance"), shadowDistance);
                commands.SetGlobalInt(Shader.PropertyToID("_InfinityHasProbes"), hasProbes);
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
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume;
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
                backendPolicy = RenderCaptureService.BackendFor(camera),
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
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.IndirectDiffuseBuffer), 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.IndirectSpecularBuffer), 2, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref ForwardPassData passData = ref passRef.GetPassData<ForwardPassData>();
                {
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = GBufferPassUtilityData.HasShadowmask();
                    var allocator = renderContext.lightContext.ShadowAllocator;
                    passData.cascadeShadow = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap));
                    passData.localShadow = passRef.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.LocalShadowMap));
                    passData.shadowMatrices = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LocalShadowMatrixBuffer));
                    passData.shadowRects = passRef.ReadBuffer(m_RGScoper.QueryBuffer(LightShaderIDs.LocalShadowRectBuffer));
                    passData.cascadeMatrices = (Matrix4x4[])allocator.CascadeMatrices.Clone();
                    passData.cascadeSpheres = (Vector4[])allocator.CascadeSpheres.Clone();
                    passData.cascadeSplits = allocator.CascadeSplitDistances;
                    passData.cascadeCount = allocator.CascadeAllocatedCount;
                    float cascadeSize = pipelineAsset.cascadeShadowMapResolution * 2;
                    float localSize = pipelineAsset.localShadowMapResolution;
                    passData.cascadeSize = new Vector4(cascadeSize, cascadeSize, 1 / cascadeSize, 1 / cascadeSize);
                    passData.localSize = new Vector4(localSize, localSize, 1 / localSize, 1 / localSize);
                    passData.shadowmaskMode = QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask ? 1 : 0;
                    passData.shadowDistance = pipelineAsset.shadowDistance;
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.hasOcclusion = m_RGScoper.TryQueryTexture(InfinityShaderIDs.OcclusionBuffer, out RGTextureRef occlusion);
                    if (passData.hasOcclusion) passData.occlusion = passRef.ReadTexture(occlusion);
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
