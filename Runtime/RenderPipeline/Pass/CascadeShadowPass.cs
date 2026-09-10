using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.LightPipeline;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class CascadeShadowPassUtilityData
    {
        internal static string TextureName = "CascadeShadowMapTexture";
        internal static int CascadeSpheresID = Shader.PropertyToID("_CascadeSpheres");
        internal static int CascadeCountID = Shader.PropertyToID("_CascadeCount");
        internal static int CascadeShadowMapSizeID = Shader.PropertyToID("_CascadeShadowMapSize");
        internal static int CascadeMatricesID = Shader.PropertyToID("_CascadeMatrices");
        internal static int CascadeSplitDistancesID = Shader.PropertyToID("_CascadeSplitDistances");
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
            public Vector4[] tileRects;
            public Vector4[] spheres;
            public bool[] valid;
            public int allocatedCount;
            public Vector4 cascadeSplitDistances;
            public Vector4[] casterBias;
            public Vector4 casterLight;
            public RendererList[] rendererLists;
            public RGDrawListRef[] draws;
        }

        void RenderCascadeShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.cascadeShadowMapResolution;
            float shadowDistance = pipelineAsset.shadowDistance;
            Matrix4x4 cameraViewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            int cascadeCount = ShadowAllocator.CascadeCount;
            MeshScene meshScene = renderContext.GetMeshScene();
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;

            TextureDescriptor shadowMapDsc = new TextureDescriptor(shadowMapResolution * 2, shadowMapResolution * 2);
            {
                shadowMapDsc.name = CascadeShadowPassUtilityData.TextureName;
                shadowMapDsc.dimension = TextureDimension.Tex2D;
                shadowMapDsc.colorFormat = GraphicsFormat.None;
                shadowMapDsc.depthBufferBits = EDepthBits.Depth32;
                shadowMapDsc.isShadowMap = true;
                shadowMapDsc.filterMode = FilterMode.Bilinear;
            }
            RGTextureRef shadowMapTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.CascadeShadowMap, shadowMapDsc);

            int lightIndex = allocator.CascadeVisibleLightIndex;
            Matrix4x4[] shadowMatrices = new Matrix4x4[cascadeCount];
            Vector4[] tileRects = new Vector4[cascadeCount];
            Vector4 cascadeSplitDistances = allocator.CascadeSplitDistances;
            RendererList[] rendererLists = new RendererList[cascadeCount];
            RGDrawListRef[] cascadeDraws = new RGDrawListRef[cascadeCount];

            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                shadowMatrices[cascade] = slice.shadowMatrix;
                tileRects[cascade] = slice.atlasPixelRect;
                cascadeDraws[cascade] = RGDrawListRef.Invalid;
            }

            if (lightIndex >= 0)
            {
                Light shadowLight = cullingResults.visibleLights[lightIndex].light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                uint shadowRenderingLayerMask = RenderingLayerUtility.Validate(unchecked((uint)shadowLight.renderingLayerMask));

                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }

                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex);
                    shadowDrawingSettings.useRenderingLayerMaskTest = true;
                    shadowDrawingSettings.splitIndex = cascade;
                    rendererLists[cascade] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);

                    ulong cascadeKey = lightInstanceId;
                    var cascadePlanes = new Plane[slice.splitData.cullingPlaneCount];
                    for (int plane = 0; plane < cascadePlanes.Length; plane++)
                        cascadePlanes[plane] = slice.splitData.GetCullingPlane(plane);
                    MeshVisibilityHandle cascadeVis = m_VisibilityShare.Acquire(
                        meshScene,
                        cascadeKey,
                        cascadePlanes,
                        MeshVisibilityShare.PolicyCascadeShadow,
                        enable: true, subviewIndex: cascade);

                    MeshFilterProgram shadowFilter = BuiltinMeshesPasses.Shadow.defaultFilter;
                    shadowFilter.layerMask = shadowLight.cullingMask;
                    shadowFilter.renderingLayerMask = shadowRenderingLayerMask;
                    shadowFilter.filterRenderingLayers = true;
                    var shadowRequest = new MeshDrawRequest
                    {
                        filter = shadowFilter,
                        sort = BuiltinMeshesPasses.Shadow.defaultSort,
                        backendPolicy = RenderCaptureService.BackendFor(camera),
                        shaderPassIndex = BuiltinMeshesPasses.Shadow.shaderPassIndex,
                        lightModeTag = BuiltinMeshesPasses.Shadow.lightModeTag,
                        viewPosition = camera.transform.position,
                        viewKey = cascadeKey
                    };
                    cascadeDraws[cascade] = m_RGBuilder.DeclareDrawList(m_ShadowMeshProcessor, shadowRequest, cascadeVis, m_VisibilityShare);
                    m_VisibilityShare.Release(cascadeVis);
                }
            }

            m_ActiveCascadeCount = allocator.CascadeAllocatedCount;
            m_ActiveCascadeSplitDistances = cascadeSplitDistances;
            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                m_ActiveCascadeMatrices[cascade] = shadowMatrices[cascade];
            }

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<CascadeShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RenderCascadeShadow)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref CascadeShadowPassData passData = ref passRef.GetPassData<CascadeShadowPassData>();
                {
                    passData.cascadeCount = cascadeCount;
                    passData.allocatedCount = allocator.CascadeAllocatedCount;
                    passData.spheres = (Vector4[])allocator.CascadeSpheres.Clone();
                    passData.valid = new bool[cascadeCount];
                    for (int i = 0; i < cascadeCount; i++) passData.valid[i] = allocator.CascadeSlices[i].valid;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.shadowDistance = shadowDistance;
                    passData.cameraViewProj = cameraViewProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.cascadeSplitDistances = cascadeSplitDistances;
                    passData.casterBias = new Vector4[cascadeCount];
                    for (int i = 0; i < cascadeCount; i++) passData.casterBias[i] = allocator.CascadeSlices[i].casterBias;
                    if (lightIndex >= 0) passData.casterLight = -cullingResults.visibleLights[lightIndex].light.transform.forward;
                    passData.rendererLists = rendererLists;

                    passData.draws = new RGDrawListRef[cascadeCount];
                    for (int cascade = 0; cascade < cascadeCount; ++cascade)
                    {
                        passData.draws[cascade] = cascadeDraws[cascade].IsValid
                            ? passRef.UseDrawList(cascadeDraws[cascade])
                            : RGDrawListRef.Invalid;
                    }
                }

                passRef.SetExecuteFunc((in CascadeShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    int halfRes = passData.shadowMapResolution;
                    cmdEncoder.SetGlobalInt(CascadeShadowPassUtilityData.CascadeCountID, passData.allocatedCount);
                    cmdEncoder.SetGlobalVectorArray(CascadeShadowPassUtilityData.CascadeSpheresID, passData.spheres);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.CascadeShadowMapSizeID, new Vector4(halfRes * 2, halfRes * 2, 1.0f / (halfRes * 2), 1.0f / (halfRes * 2)));
                    cmdEncoder.SetGlobalMatrixArray(CascadeShadowPassUtilityData.CascadeMatricesID, passData.shadowMatrices);
                    cmdEncoder.SetGlobalVector(CascadeShadowPassUtilityData.CascadeSplitDistancesID, passData.cascadeSplitDistances);
                    cmdEncoder.SetGlobalFloat(CascadeShadowPassUtilityData.ShadowDistanceID, passData.shadowDistance);

                    for (int cascade = 0; cascade < passData.cascadeCount; ++cascade)
                    {
                        if (!passData.valid[cascade]) continue;
                        string cascadeMarker = $"CascadeSlice{cascade}";
                        cmdEncoder.BeginSample(cascadeMarker);
                        Vector4 rect = passData.tileRects[cascade];
                        cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        cmdEncoder.SetGlobalDepthBias(0, 0);
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ShadowCasterBias"), passData.casterBias[cascade]);
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ShadowCasterLight"), passData.casterLight);
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
                        cmdEncoder.EndSample(cascadeMarker);
                    }

                    cmdEncoder.SetViewport(new Rect(0, 0, halfRes * 2, halfRes * 2));
                    cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewProjID, passData.cameraViewProj);
                });
            }
        }
    }
}
