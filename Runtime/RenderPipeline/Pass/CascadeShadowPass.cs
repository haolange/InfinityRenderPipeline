using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class CascadeShadowPassUtilityData
    {
        internal static string TextureName = "CascadeShadowMapTexture";
        internal static int CascadeCountID = Shader.PropertyToID("_CascadeCount");
        internal static int CascadeShadowMapSizeID = Shader.PropertyToID("_CascadeShadowMapSize");
        internal static int CascadeMatricesID = Shader.PropertyToID("_CascadeMatrices");
        internal static int CascadeSplitDistancesID = Shader.PropertyToID("_CascadeSplitDistances");
        internal static int ShadowBiasID = Shader.PropertyToID("_ShadowBias");
        internal static int ShadowDistanceID = Shader.PropertyToID("_ShadowDistance");
        internal static int MatrixViewProjID = Shader.PropertyToID("Matrix_ViewProj");
    }

    public partial class InfinityRenderPipeline
    {
        struct CascadeShadowPassData
        {
            public int cascadeCount;
            public int shadowMapResolution;
            public float shadowDistance;
            public Matrix4x4 cameraViewProj;
            public Matrix4x4[] shadowMatrices;
            public Vector4 cascadeSplitDistances;
            public Vector4 shadowBias;
            public RendererList[] rendererLists;
            public RGDrawListRef[] draws;
        }

        void RenderCascadeShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.cascadeShadowMapResolution;
            float shadowDistance = pipelineAsset.shadowDistance;
            int cascadeCount = 4;
            MeshScene meshScene = renderContext.GetMeshScene();
            Matrix4x4 cameraViewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;

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
            Vector4 cascadeSplitDistances = Vector4.zero;
            RendererList[] rendererLists = new RendererList[cascadeCount];
            RGDrawListRef[] cascadeDraws = new RGDrawListRef[cascadeCount];
            Plane[] cascadePlanes = new Plane[6];

            if (lightIndex >= 0 && !cullingResults.GetShadowCasterBounds(lightIndex, out _))
            {
                lightIndex = -1;
            }

            if (lightIndex >= 0)
            {
                float[] cascadeRatios = new float[] { 0.067f, 0.2f, 0.467f, 1.0f };
                Light shadowLight = cullingResults.visibleLights[lightIndex].light;
                int lightInstanceId = UnityEntityId.ToInt32(shadowLight);
                uint shadowRenderingLayerMask = (uint)ERenderingLayer.Everything;
                if (shadowLight != null && shadowLight.TryGetComponent(out LightComponent lightComponent))
                {
                    shadowRenderingLayerMask = (uint)lightComponent.shadowLayer;
                }

                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    if (!cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                        lightIndex, cascade, cascadeCount, new Vector3(cascadeRatios[0], cascadeRatios[1], cascadeRatios[2]),
                        shadowMapResolution, cullingResults.visibleLights[lightIndex].light.shadowNearPlane,
                        out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData splitData))
                    {
                        shadowMatrices[cascade] = Matrix4x4.identity;
                        cascadeDraws[cascade] = RGDrawListRef.Invalid;
                        continue;
                    }

                    shadowMatrices[cascade] = projMatrix * viewMatrix;
                    cascadeSplitDistances[cascade] = cascadeRatios[cascade] * shadowDistance;

                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex);
                    shadowDrawingSettings.splitIndex = cascade;
                    RecordShadowCasterSplit(lightIndex, cascade, splitData, BatchCullingProjectionType.Orthographic);
                    rendererLists[cascade] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);

                    ulong cascadeKey = MeshVisibilityShare.MakeCascadeViewKey(lightInstanceId, cascade);
                    GeometryUtility.CalculateFrustumPlanes(shadowMatrices[cascade], cascadePlanes);
                    MeshVisibilityHandle cascadeVis = m_VisibilityShare.Acquire(
                        meshScene,
                        cascadeKey,
                        cascadePlanes,
                        MeshVisibilityShare.PolicyCascadeShadow,
                        enable: true);

                    MeshFilterProgram shadowFilter = BuiltinMeshesPasses.Shadow.defaultFilter;
                    shadowFilter.layerMask = shadowLight.cullingMask;
                    shadowFilter.renderingLayerMask = shadowRenderingLayerMask;
                    var shadowRequest = new MeshDrawRequest
                    {
                        filter = shadowFilter,
                        sort = BuiltinMeshesPasses.Shadow.defaultSort,
                        backendPolicy = EMeshBackendPolicy.Auto,
                        shaderPassIndex = BuiltinMeshesPasses.Shadow.shaderPassIndex,
                        viewPosition = camera.transform.position,
                        renderingLayerMask = shadowFilter.renderingLayerMask,
                        viewKey = cascadeKey
                    };
                    cascadeDraws[cascade] = m_RGBuilder.DeclareDrawList(m_ShadowMeshProcessor, shadowRequest, cascadeVis, m_VisibilityShare);
                    m_VisibilityShare.Release(cascadeVis);
                }
            }
            else
            {
                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    shadowMatrices[cascade] = Matrix4x4.identity;
                    cascadeDraws[cascade] = RGDrawListRef.Invalid;
                }
            }

            m_ActiveCascadeCount = lightIndex >= 0 ? cascadeCount : 0;
            m_ActiveCascadeSplitDistances = cascadeSplitDistances;
            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                m_ActiveCascadeMatrices[cascade] = shadowMatrices[cascade];
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
                    passData.cameraViewProj = cameraViewProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.cascadeSplitDistances = cascadeSplitDistances;
                    passData.shadowBias = new Vector4(0.001f, 1.0f, 0.0f, 0.0f);
                    passData.rendererLists = rendererLists;

                    passData.draws = new RGDrawListRef[cascadeCount];
                    for (int cascade = 0; cascade < cascadeCount; ++cascade)
                    {
                        passData.draws[cascade] = cascadeDraws[cascade].IsValid
                            ? passRef.UseDrawList(cascadeDraws[cascade])
                            : RGDrawListRef.Invalid;
                    }
                }

                //Execute Phase
                passRef.SetExecuteFunc((in CascadeShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    int halfRes = passData.shadowMapResolution;

                    // Set global shadow parameters
                    cmdEncoder.SetGlobalInt(CascadeShadowPassUtilityData.CascadeCountID, passData.cascadeCount);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.CascadeShadowMapSizeID, new Vector4(halfRes * 2, halfRes * 2, 1.0f / (halfRes * 2), 1.0f / (halfRes * 2)));
                    cmdEncoder.SetGlobalMatrixArray(CascadeShadowPassUtilityData.CascadeMatricesID, passData.shadowMatrices);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.CascadeSplitDistancesID, passData.cascadeSplitDistances);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.ShadowBiasID, passData.shadowBias);
                    cmdEncoder.SetGlobalFloat(CascadeShadowPassUtilityData.ShadowDistanceID, passData.shadowDistance);

                    // Render each cascade into its quadrant
                    for (int cascade = 0; cascade < passData.cascadeCount; ++cascade)
                    {
                        int x = (cascade % 2) * halfRes;
                        int y = (cascade / 2) * halfRes;

                        cmdEncoder.SetViewport(new Rect(x, y, halfRes, halfRes));
                        cmdEncoder.SetGlobalDepthBias(1.0f, 2.5f);
                        cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewProjID, passData.shadowMatrices[cascade]);

                        if (passData.draws != null && cascade < passData.draws.Length && passData.draws[cascade].IsValid)
                        {
                            cmdEncoder.Draw(passData.draws[cascade]);
                        }

                        if (passData.rendererLists != null && cascade < passData.rendererLists.Length && passData.rendererLists[cascade].isValid)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererLists[cascade]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                    }

                    cmdEncoder.SetViewport(new Rect(0, 0, halfRes * 2, halfRes * 2));
                    cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewProjID, passData.cameraViewProj);
                });
            }
        }
    }
}
