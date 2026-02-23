using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class CascadeShadowPassUtilityData
    {
        internal static string TextureName = "CascadeShadowMapTexture";
        internal static int CascadeCountID = Shader.PropertyToID("_CascadeCount");
        internal static int CascadeShadowMapSizeID = Shader.PropertyToID("_CascadeShadowMapSize");
        internal static int ShadowMatricesID = Shader.PropertyToID("_ShadowMatrices");
        internal static int CascadeSplitsID = Shader.PropertyToID("_CascadeSplits");
        internal static int ShadowBiasID = Shader.PropertyToID("_ShadowBias");
        internal static int ShadowDistanceID = Shader.PropertyToID("_ShadowDistance");
    }

    public partial class InfinityRenderPipeline
    {
        struct CascadeShadowPassData
        {
            public int cascadeCount;
            public int shadowMapResolution;
            public float shadowDistance;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] cascadeSplits;
            public Vector4 shadowBias;
            public RendererList[] rendererLists;
        }

        void RenderCascadeShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.cascadeShadowMapResolution;
            float shadowDistance = pipelineAsset.shadowDistance;
            int cascadeCount = 4;

            TextureDescriptor shadowMapDsc = new TextureDescriptor(shadowMapResolution * 2, shadowMapResolution * 2);
            {
                shadowMapDsc.name = CascadeShadowPassUtilityData.TextureName;
                shadowMapDsc.dimension = TextureDimension.Tex2D;
                shadowMapDsc.colorFormat = GraphicsFormat.None;
                shadowMapDsc.depthBufferBits = EDepthBits.Depth16;
                shadowMapDsc.isShadowMap = true;
                shadowMapDsc.filterMode = FilterMode.Bilinear;
            }
            RGTextureRef shadowMapTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.CascadeShadowMap, shadowMapDsc);

            // Find first directional light that casts shadows
            int lightIndex = -1;
            for (int i = 0; i < cullingResults.visibleLights.Length; ++i)
            {
                VisibleLight visibleLight = cullingResults.visibleLights[i];
                if (visibleLight.lightType == LightType.Directional && visibleLight.light.shadows != LightShadows.None)
                {
                    lightIndex = i;
                    break;
                }
            }

            Matrix4x4[] shadowMatrices = new Matrix4x4[cascadeCount];
            Vector4[] cascadeSplits = new Vector4[cascadeCount];
            RendererList[] rendererLists = new RendererList[cascadeCount];

            if (lightIndex >= 0)
            {
                float[] cascadeRatios = new float[] { 0.067f, 0.2f, 0.467f, 1.0f };

                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    float prevSplit = cascade > 0 ? cascadeRatios[cascade - 1] : 0.0f;
                    float currSplit = cascadeRatios[cascade];

                    if (cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                        lightIndex, cascade, cascadeCount, new Vector3(cascadeRatios[0], cascadeRatios[1], cascadeRatios[2]),
                        shadowMapResolution, cullingResults.visibleLights[lightIndex].light.shadowNearPlane,
                        out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData splitData))
                    {
                        shadowMatrices[cascade] = projMatrix * viewMatrix;
                        cascadeSplits[cascade] = new Vector4(splitData.cullingSphere.x, splitData.cullingSphere.y, splitData.cullingSphere.z, splitData.cullingSphere.w);

                        ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex);
                        shadowDrawingSettings.splitData = splitData;
                        rendererLists[cascade] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);
                    }
                    else
                    {
                        shadowMatrices[cascade] = Matrix4x4.identity;
                        cascadeSplits[cascade] = Vector4.zero;
                        ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex);
                        rendererLists[cascade] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);
                    }
                }
            }
            else
            {
                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    shadowMatrices[cascade] = Matrix4x4.identity;
                    cascadeSplits[cascade] = Vector4.zero;
                }
            }

            //Add CascadeShadowPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<CascadeShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RenderCascadeShadow)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref CascadeShadowPassData passData = ref passRef.GetPassData<CascadeShadowPassData>();
                {
                    passData.cascadeCount = cascadeCount;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.shadowDistance = shadowDistance;
                    passData.shadowMatrices = shadowMatrices;
                    passData.cascadeSplits = cascadeSplits;
                    passData.shadowBias = new Vector4(0.001f, 1.0f, 0.0f, 0.0f);
                    passData.rendererLists = rendererLists;
                }

                //Execute Phase
                passRef.SetExecuteFunc((in CascadeShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    int halfRes = passData.shadowMapResolution;

                    // Set global shadow parameters
                    cmdEncoder.SetGlobalInt(CascadeShadowPassUtilityData.CascadeCountID, passData.cascadeCount);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.CascadeShadowMapSizeID, new Vector4(halfRes * 2, halfRes * 2, 1.0f / (halfRes * 2), 1.0f / (halfRes * 2)));
                    cmdEncoder.SetGlobalMatrixArray(CascadeShadowPassUtilityData.ShadowMatricesID, passData.shadowMatrices);
                    cmdEncoder.SetGlobalVectorArray(CascadeShadowPassUtilityData.CascadeSplitsID, passData.cascadeSplits);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.ShadowBiasID, passData.shadowBias);
                    cmdEncoder.SetGlobalFloat(CascadeShadowPassUtilityData.ShadowDistanceID, passData.shadowDistance);

                    // Render each cascade into its quadrant
                    for (int cascade = 0; cascade < passData.cascadeCount; ++cascade)
                    {
                        int x = (cascade % 2) * halfRes;
                        int y = (cascade / 2) * halfRes;

                        cmdEncoder.SetViewport(new Rect(x, y, halfRes, halfRes));
                        cmdEncoder.SetGlobalDepthBias(1.0f, 2.5f);

                        if (passData.rendererLists != null && cascade < passData.rendererLists.Length)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererLists[cascade]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                    }
                });
            }
        }
    }
}
