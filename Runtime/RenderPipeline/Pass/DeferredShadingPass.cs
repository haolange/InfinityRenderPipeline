using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class DeferredShadingPassUtilityData
    {
        internal static string TextureName = "LightingTexture";
        internal static int DeferredShading_ResolutionID = Shader.PropertyToID("DeferredShading_Resolution");
        internal static int DeferredShading_TileSizeID = Shader.PropertyToID("DeferredShading_TileSize");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_OcclusionTextureID = Shader.PropertyToID("SRV_OcclusionTexture");
        internal static int SRV_ContactShadowTextureID = Shader.PropertyToID("SRV_ContactShadowTexture");
        internal static int SRV_CascadeShadowMapID = Shader.PropertyToID("SRV_CascadeShadowMap");
        internal static int SRV_LocalShadowMapID = Shader.PropertyToID("SRV_LocalShadowMap");
        internal static int UAV_LightingTextureID = Shader.PropertyToID("UAV_LightingTexture");
        internal static int DeferredShading_FarDepthID = Shader.PropertyToID("DeferredShading_FarDepth");
        internal static int DeferredShading_HasTileListID = Shader.PropertyToID("DeferredShading_HasTileList");
        internal static int DeferredShading_NumTilesXID = Shader.PropertyToID("DeferredShading_NumTilesX");
        internal static int DeferredShading_NumBinsID = Shader.PropertyToID("DeferredShading_NumBins");
        internal static int DeferredShading_NearFarID = Shader.PropertyToID("DeferredShading_NearFar");
        internal static int LocalShadowMapSizeID = Shader.PropertyToID("_LocalShadowMapSize");
        internal static int SRV_TileLightRangeID = Shader.PropertyToID("SRV_TileLightRange");
        internal static int SRV_TileLightListID = Shader.PropertyToID("SRV_TileLightList");
        internal static int SRV_ZBinRangeID = Shader.PropertyToID("SRV_ZBinRange");
        internal static int SRV_ZBinLightListID = Shader.PropertyToID("SRV_ZBinLightList");
        internal static int SRV_LocalShadowMatricesID = Shader.PropertyToID("SRV_LocalShadowMatrices");
        internal static int SRV_LocalShadowRectsID = Shader.PropertyToID("SRV_LocalShadowRects");
    }

    public partial class InfinityRenderPipeline
    {
        struct DeferredShadingPassData
        {
            public int tileSize;
            public int2 resolution;
            public int numTilesX;
            public int hasTileList;
            public float nearPlane;
            public float farPlane;
            public Matrix4x4 matrix_InvProj;
            public Matrix4x4 matrix_InvViewProj;
            public Vector4 worldSpaceCameraPos;
            public int directionalLightCount;
            public int localLightCount;
            public GraphicsBuffer lightRecordBuffer;
            public GraphicsBuffer localShadowMatrixBuffer;
            public GraphicsBuffer localShadowRectBuffer;
            public GraphicsBuffer emptyTileRange;
            public GraphicsBuffer emptyTileList;
            public GraphicsBuffer emptyZBinRange;
            public GraphicsBuffer emptyZBinList;
            public int cascadeCount;
            public Matrix4x4[] cascadeMatrices;
            public Vector4 cascadeSplitDistances;
            public Vector4 localShadowMapSize;
            public ComputeShader deferredShadingShader;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef depthTexture;
            public RGTextureRef occlusionTexture;
            public RGTextureRef contactShadowTexture;
            public RGTextureRef cascadeShadowMap;
            public RGTextureRef localShadowMap;
            public RGTextureRef lightingTexture;
            public RGTextureRef atmosphereGGXPrefilter;
            public RGBufferRef atmosphereSkySH;
            public float atmosphereIBLMaxMip;
            public RGBufferRef tileRange;
            public RGBufferRef tileList;
            public RGBufferRef zBinRange;
            public RGBufferRef zBinList;
        }

        void ComputeDeferredShading(RenderContext renderContext, Camera camera)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.DeferredShading);

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.deferredShadingShader, "DeferredShadingCS"))
            {
                throw new System.InvalidOperationException("InfinityRP: Deferred shading is the LightingBuffer producer but deferredShadingShader is missing or kernel DeferredShadingCS is invalid.");
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            int tileSize = 16;

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef gBufferC = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef occlusionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer);
            RGTextureRef contactShadowTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer);
            RGTextureRef cascadeShadowMap = m_RGScoper.QueryTexture(InfinityShaderIDs.CascadeShadowMap);
            RGTextureRef localShadowMap = m_RGScoper.QueryTexture(InfinityShaderIDs.LocalShadowMap);
            RGTextureRef atmosphereGGXPrefilter = m_RGScoper.QueryTexture(InfinityShaderIDs.AtmosphereGGXPrefilter);
            RGBufferRef atmosphereSkySH = m_RGScoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);
            AtmosphereParameter atmosphereParameter = AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile);
            atmosphereParameter.ThrowIfInvalid();

            RGBufferRef tileRange = default;
            RGBufferRef tileList = default;
            RGBufferRef zBinRange = default;
            RGBufferRef zBinList = default;
            bool hasTileList =
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.TileLightRangeBuffer, out tileRange) &&
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.TileLightListBuffer, out tileList) &&
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.ZBinRangeBuffer, out zBinRange) &&
                m_RGScoper.TryQueryBuffer(InfinityShaderIDs.ZBinLightListBuffer, out zBinList);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<DeferredShadingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeDeferredShading)))
            {
                ref DeferredShadingPassData passData = ref passRef.GetPassData<DeferredShadingPassData>();
                passData.tileSize = tileSize;
                passData.resolution = new int2(width, height);
                passData.numTilesX = Mathf.CeilToInt((float)width / tileSize);
                passData.hasTileList = hasTileList ? 1 : 0;
                passData.nearPlane = camera.nearClipPlane;
                passData.farPlane = camera.farClipPlane;
                passData.matrix_InvProj = m_CameraUniform.matrix_InvFlipYJitterProj;
                passData.matrix_InvViewProj = m_CameraUniform.matrix_InvViewFlipYJitterProj;
                passData.worldSpaceCameraPos = camera.transform.position;
                passData.directionalLightCount = renderContext.lightContext.DirectionalLightCount;
                passData.localLightCount = renderContext.lightContext.LocalLightCount;
                passData.lightRecordBuffer = renderContext.lightContext.LightRecordBuffer;
                passData.localShadowMatrixBuffer = renderContext.lightContext.LocalShadowMatrixBuffer;
                passData.localShadowRectBuffer = renderContext.lightContext.LocalShadowRectBuffer;
                passData.emptyTileRange = renderContext.lightContext.EmptyTileRangeBuffer;
                passData.emptyTileList = renderContext.lightContext.EmptyTileListBuffer;
                passData.emptyZBinRange = renderContext.lightContext.EmptyZBinRangeBuffer;
                passData.emptyZBinList = renderContext.lightContext.EmptyZBinListBuffer;
                passData.cascadeCount = m_ActiveCascadeCount;
                passData.cascadeMatrices = m_ActiveCascadeMatrices;
                passData.cascadeSplitDistances = m_ActiveCascadeSplitDistances;
                passData.localShadowMapSize = new Vector4(
                    pipelineAsset.localShadowMapResolution,
                    pipelineAsset.localShadowMapResolution,
                    1.0f / pipelineAsset.localShadowMapResolution,
                    1.0f / pipelineAsset.localShadowMapResolution);
                passData.deferredShadingShader = pipelineAsset.deferredShadingShader;
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.gBufferC = passRef.ReadTexture(gBufferC);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.occlusionTexture = passRef.ReadTexture(occlusionTexture);
                passData.contactShadowTexture = passRef.ReadTexture(contactShadowTexture);
                passData.cascadeShadowMap = passRef.ReadTexture(cascadeShadowMap);
                passData.localShadowMap = passRef.ReadTexture(localShadowMap);
                passData.lightingTexture = passRef.WriteTexture(lightingTexture);
                passData.atmosphereGGXPrefilter = passRef.ReadTexture(atmosphereGGXPrefilter);
                passData.atmosphereSkySH = passRef.ReadBuffer(atmosphereSkySH);
                passData.atmosphereIBLMaxMip = AtmosphericLUTPassUtilityData.GGXMipCount(atmosphereParameter.cubemapSize) - 1;
                if (hasTileList)
                {
                    passData.tileRange = passRef.ReadBuffer(tileRange);
                    passData.tileList = passRef.ReadBuffer(tileList);
                    passData.zBinRange = passRef.ReadBuffer(zBinRange);
                    passData.zBinList = passRef.ReadBuffer(zBinList);
                }

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in DeferredShadingPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.deferredShadingShader;
                    cmdEncoder.SetComputeVectorParam(shader, DeferredShadingPassUtilityData.DeferredShading_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeIntParam(shader, DeferredShadingPassUtilityData.DeferredShading_TileSizeID, passData.tileSize);
                    cmdEncoder.SetComputeFloatParam(shader, DeferredShadingPassUtilityData.DeferredShading_FarDepthID, GraphicsUtility.SampledFarDepth);
                    cmdEncoder.SetComputeIntParam(shader, DeferredShadingPassUtilityData.DeferredShading_HasTileListID, passData.hasTileList);
                    cmdEncoder.SetComputeIntParam(shader, DeferredShadingPassUtilityData.DeferredShading_NumTilesXID, passData.numTilesX);
                    cmdEncoder.SetComputeIntParam(shader, DeferredShadingPassUtilityData.DeferredShading_NumBinsID, ZBinningPassUtilityData.NumBins);
                    cmdEncoder.SetComputeVectorParam(shader, DeferredShadingPassUtilityData.DeferredShading_NearFarID, new Vector4(passData.nearPlane, passData.farPlane, 0, 0));
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvProj"), passData.matrix_InvProj);
                    cmdEncoder.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvViewProj"), passData.matrix_InvViewProj);
                    cmdEncoder.SetComputeVectorParam(shader, Shader.PropertyToID("_WorldSpaceCameraPos"), passData.worldSpaceCameraPos);
                    cmdEncoder.SetComputeIntParam(shader, LightShaderIDs.DirectionalLightCount, passData.directionalLightCount);
                    cmdEncoder.SetComputeIntParam(shader, LightShaderIDs.LocalLightCount, passData.localLightCount);
                    cmdEncoder.SetComputeBufferParam(shader, 0, LightShaderIDs.LightRecordBuffer, passData.lightRecordBuffer);
                    cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_LocalShadowMatricesID, passData.localShadowMatrixBuffer);
                    cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_LocalShadowRectsID, passData.localShadowRectBuffer);
                    cmdEncoder.SetComputeIntParam(shader, CascadeShadowPassUtilityData.CascadeCountID, passData.cascadeCount);
                    cmdEncoder.SetComputeMatrixArrayParam(shader, CascadeShadowPassUtilityData.CascadeMatricesID, passData.cascadeMatrices);
                    cmdEncoder.SetComputeVectorParam(shader, CascadeShadowPassUtilityData.CascadeSplitDistancesID, passData.cascadeSplitDistances);
                    cmdEncoder.SetComputeVectorParam(shader, DeferredShadingPassUtilityData.LocalShadowMapSizeID, passData.localShadowMapSize);

                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_OcclusionTextureID, passData.occlusionTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_ContactShadowTextureID, passData.contactShadowTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_CascadeShadowMapID, passData.cascadeShadowMap);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.SRV_LocalShadowMapID, passData.localShadowMap);
                    cmdEncoder.SetComputeTextureParam(shader, 0, DeferredShadingPassUtilityData.UAV_LightingTextureID, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(shader, 0, InfinityShaderIDs.AtmosphereGGXPrefilter, passData.atmosphereGGXPrefilter);
                    cmdEncoder.SetComputeBufferParam(shader, 0, InfinityShaderIDs.AtmosphereSkySH, passData.atmosphereSkySH);
                    cmdEncoder.SetComputeFloatParam(shader, InfinityShaderIDs.AtmosphereIBLMaxMip, passData.atmosphereIBLMaxMip);

                    if (passData.hasTileList != 0)
                    {
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_TileLightRangeID, passData.tileRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_TileLightListID, passData.tileList);
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_ZBinRangeID, passData.zBinRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_ZBinLightListID, passData.zBinList);
                    }
                    else
                    {
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_TileLightRangeID, passData.emptyTileRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_TileLightListID, passData.emptyTileList);
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_ZBinRangeID, passData.emptyZBinRange);
                        cmdEncoder.SetComputeBufferParam(shader, 0, DeferredShadingPassUtilityData.SRV_ZBinLightListID, passData.emptyZBinList);
                    }

                    cmdEncoder.DispatchCompute(shader, 0, Mathf.CeilToInt(passData.resolution.x / (float)passData.tileSize), Mathf.CeilToInt(passData.resolution.y / (float)passData.tileSize), 1);
                });
            }

            MarkFeatureProduced(EFrameFeature.DeferredShading);
        }
    }
}
