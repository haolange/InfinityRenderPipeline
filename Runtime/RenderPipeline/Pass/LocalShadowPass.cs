using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;
using System.Collections.Generic;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class LocalShadowPassUtilityData
    {
        internal static string TextureName = "LocalShadowMapTexture";
        internal static int LocalShadowMapSizeID = Shader.PropertyToID("_LocalShadowMapSize");
        internal static int LocalShadowCountID = Shader.PropertyToID("_LocalShadowCount");
        internal static int LocalShadowMatricesID = Shader.PropertyToID("_LocalShadowMatrices");
        internal static int LocalShadowParamsID = Shader.PropertyToID("_LocalShadowParams");
    }

    public partial class InfinityRenderPipeline
    {
        struct LocalShadowPassData
        {
            public int shadowCount;
            public int shadowMapResolution;
            public int tilesPerRow;
            public int tileResolution;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] shadowParams;
            public RendererList[] rendererLists;
        }

        void RenderLocalShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.localShadowMapResolution;
            int maxLocalShadows = 16;
            int tileResolution = shadowMapResolution / 4;
            int tilesPerRow = shadowMapResolution / tileResolution;

            TextureDescriptor shadowMapDsc = new TextureDescriptor(shadowMapResolution, shadowMapResolution);
            {
                shadowMapDsc.name = LocalShadowPassUtilityData.TextureName;
                shadowMapDsc.dimension = TextureDimension.Tex2D;
                shadowMapDsc.colorFormat = GraphicsFormat.None;
                shadowMapDsc.depthBufferBits = EDepthBits.Depth16;
                shadowMapDsc.isShadowMap = true;
                shadowMapDsc.filterMode = FilterMode.Bilinear;
            }
            RGTextureRef shadowMapTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LocalShadowMap, shadowMapDsc);

            // Collect shadow-casting local lights
            List<int> shadowLightIndices = new List<int>();
            for (int i = 0; i < cullingResults.visibleLights.Length && shadowLightIndices.Count < maxLocalShadows; ++i)
            {
                VisibleLight visibleLight = cullingResults.visibleLights[i];
                if ((visibleLight.lightType == LightType.Point || visibleLight.lightType == LightType.Spot)
                    && visibleLight.light.shadows != LightShadows.None)
                {
                    shadowLightIndices.Add(i);
                }
            }

            int shadowCount = shadowLightIndices.Count;
            Matrix4x4[] shadowMatrices = new Matrix4x4[math.max(1, shadowCount)];
            Vector4[] shadowParams = new Vector4[math.max(1, shadowCount)];
            RendererList[] rendererLists = new RendererList[math.max(1, shadowCount)];

            for (int s = 0; s < shadowCount; ++s)
            {
                int lightIdx = shadowLightIndices[s];
                VisibleLight visibleLight = cullingResults.visibleLights[lightIdx];

                if (visibleLight.lightType == LightType.Spot)
                {
                    if (cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(lightIdx,
                        out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData splitData))
                    {
                        shadowMatrices[s] = projMatrix * viewMatrix;
                        shadowParams[s] = new Vector4(visibleLight.light.shadowBias, visibleLight.light.shadowNormalBias, visibleLight.range, 0.0f);

                        ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIdx);
                        shadowDrawingSettings.splitData = splitData;
                        rendererLists[s] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);
                    }
                    else
                    {
                        shadowMatrices[s] = Matrix4x4.identity;
                        shadowParams[s] = Vector4.zero;
                    }
                }
                else if (visibleLight.lightType == LightType.Point)
                {
                    // For point lights, render only face 0 as a simple approximation
                    if (cullingResults.ComputePointShadowMatricesAndCullingPrimitives(lightIdx, CubemapFace.PositiveX, 0.0f,
                        out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData splitData))
                    {
                        shadowMatrices[s] = projMatrix * viewMatrix;
                        shadowParams[s] = new Vector4(visibleLight.light.shadowBias, visibleLight.light.shadowNormalBias, visibleLight.range, 1.0f);

                        ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIdx);
                        shadowDrawingSettings.splitData = splitData;
                        rendererLists[s] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);
                    }
                    else
                    {
                        shadowMatrices[s] = Matrix4x4.identity;
                        shadowParams[s] = Vector4.zero;
                    }
                }
            }

            //Add LocalShadowPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<LocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RenderLocalShadow)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref LocalShadowPassData passData = ref passRef.GetPassData<LocalShadowPassData>();
                {
                    passData.shadowCount = shadowCount;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.tilesPerRow = tilesPerRow;
                    passData.tileResolution = tileResolution;
                    passData.shadowMatrices = shadowMatrices;
                    passData.shadowParams = shadowParams;
                    passData.rendererLists = rendererLists;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in LocalShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalInt(LocalShadowPassUtilityData.LocalShadowCountID, passData.shadowCount);
                    cmdEncoder.SetGlobalVector(LocalShadowPassUtilityData.LocalShadowMapSizeID, new Vector4(passData.shadowMapResolution, passData.shadowMapResolution, 1.0f / passData.shadowMapResolution, 1.0f / passData.shadowMapResolution));

                    if (passData.shadowCount > 0)
                    {
                        cmdEncoder.SetGlobalMatrixArray(LocalShadowPassUtilityData.LocalShadowMatricesID, passData.shadowMatrices);
                        cmdEncoder.SetGlobalVectorArray(LocalShadowPassUtilityData.LocalShadowParamsID, passData.shadowParams);
                    }

                    for (int s = 0; s < passData.shadowCount; ++s)
                    {
                        int col = s % passData.tilesPerRow;
                        int row = s / passData.tilesPerRow;
                        int x = col * passData.tileResolution;
                        int y = row * passData.tileResolution;

                        cmdEncoder.SetViewport(new Rect(x, y, passData.tileResolution, passData.tileResolution));
                        cmdEncoder.SetGlobalDepthBias(1.0f, 2.5f);

                        if (passData.rendererLists != null && s < passData.rendererLists.Length)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererLists[s]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                    }
                });
            }
        }
    }
}
