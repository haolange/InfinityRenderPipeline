using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Component;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Experimental.Rendering;
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
        internal static int MatrixViewProjID = Shader.PropertyToID("Matrix_ViewProj");
    }

    public partial class InfinityRenderPipeline
    {
        struct LocalShadowCandidate
        {
            public int lightIndex;
            public int faceCount;
            public float score;
            public bool isSpot;
        }

        struct LocalShadowPassData
        {
            public int lightCount;
            public int sliceCount;
            public int shadowMapResolution;
            public int tilesPerRow;
            public int tileResolution;
            public Matrix4x4 cameraViewProj;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] shadowParams;
            public Vector4[] tileRects;
            public int[] lightFaceCounts;
            public int[] lightSliceOffsets;
            public RendererList[] rendererLists;
            public RGDrawListRef[] draws;
        }

        void RenderLocalShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.localShadowMapResolution;
            int maxLocalLights = 16;
            int tileResolution = math.max(1, shadowMapResolution / 4);
            int tilesPerRow = math.max(1, shadowMapResolution / tileResolution);
            int tileBudget = tilesPerRow * tilesPerRow;
            MeshScene meshScene = renderContext.GetMeshScene();
            Matrix4x4 cameraViewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            Vector3 cameraPosition = camera.transform.position;
            Plane[] shadowPlanes = new Plane[6];

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

            // Collect shadow-casting local lights, then greedy-allocate atlas tiles.
            var candidates = new List<LocalShadowCandidate>(cullingResults.visibleLights.Length);
            for (int i = 0; i < cullingResults.visibleLights.Length; ++i)
            {
                VisibleLight visibleLight = cullingResults.visibleLights[i];
                if (visibleLight.light == null || visibleLight.light.shadows == LightShadows.None || !cullingResults.GetShadowCasterBounds(i, out _))
                {
                    continue;
                }

                bool isSpot = visibleLight.lightType == LightType.Spot;
                bool isPoint = visibleLight.lightType == LightType.Point;
                if (!isSpot && !isPoint)
                {
                    continue;
                }

                Vector3 lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
                float distance = math.max(0.01f, Vector3.Distance(cameraPosition, lightPosition));
                float score = visibleLight.light.shadowStrength / distance;
                // Prefer Spot when scores are close so more lights fit the tile budget.
                if (isSpot)
                {
                    score += 1e-4f;
                }

                candidates.Add(new LocalShadowCandidate
                {
                    lightIndex = i,
                    faceCount = isPoint ? 6 : 1,
                    score = score,
                    isSpot = isSpot
                });
            }

            candidates.Sort((a, b) =>
            {
                int cmp = b.score.CompareTo(a.score);
                if (cmp != 0)
                {
                    return cmp;
                }

                // Spot before Point on ties (visibleLights order preserved via lightIndex).
                if (a.isSpot != b.isSpot)
                {
                    return a.isSpot ? -1 : 1;
                }

                return a.lightIndex.CompareTo(b.lightIndex);
            });

            var accepted = new List<LocalShadowCandidate>(math.min(maxLocalLights, candidates.Count));
            int tilesUsed = 0;
            for (int i = 0; i < candidates.Count; ++i)
            {
                LocalShadowCandidate candidate = candidates[i];
                if (accepted.Count >= maxLocalLights || tilesUsed + candidate.faceCount > tileBudget)
                {
                    MeshPipelineDiagnostics.LocalShadowBudgetDropped++;
                    continue;
                }

                accepted.Add(candidate);
                tilesUsed += candidate.faceCount;
            }

            int lightCount = accepted.Count;
            int sliceCount = tilesUsed;
            int matrixCount = math.max(1, sliceCount);
            int paramCount = math.max(1, lightCount);

            Matrix4x4[] shadowMatrices = new Matrix4x4[matrixCount];
            Vector4[] shadowParams = new Vector4[paramCount];
            Vector4[] tileRects = new Vector4[matrixCount];
            int[] lightFaceCounts = new int[paramCount];
            int[] lightSliceOffsets = new int[paramCount];
            RendererList[] rendererLists = new RendererList[matrixCount];
            RGDrawListRef[] sliceDraws = new RGDrawListRef[matrixCount];

            for (int i = 0; i < matrixCount; ++i)
            {
                shadowMatrices[i] = Matrix4x4.identity;
                tileRects[i] = Vector4.zero;
                sliceDraws[i] = RGDrawListRef.Invalid;
            }

            int nextSlice = 0;
            for (int lightSlot = 0; lightSlot < lightCount; ++lightSlot)
            {
                LocalShadowCandidate candidate = accepted[lightSlot];
                int lightIdx = candidate.lightIndex;
                VisibleLight visibleLight = cullingResults.visibleLights[lightIdx];
                Light shadowLight = visibleLight.light;
                int lightInstanceId = UnityEntityId.ToInt32(shadowLight);
                Vector3 lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);

                uint shadowRenderingLayerMask = (uint)ERenderingLayer.Everything;
                if (shadowLight.TryGetComponent(out LightComponent lightComponent))
                {
                    shadowRenderingLayerMask = (uint)lightComponent.shadowLayer;
                }

                int faceCount = candidate.faceCount;
                int sliceOffset = nextSlice;
                lightFaceCounts[lightSlot] = faceCount;
                lightSliceOffsets[lightSlot] = sliceOffset;
                shadowParams[lightSlot] = new Vector4(
                    shadowLight.shadowBias,
                    shadowLight.shadowNormalBias,
                    visibleLight.range,
                    candidate.isSpot ? 0.0f : 1.0f);

                for (int face = 0; face < faceCount; ++face)
                {
                    int slice = sliceOffset + face;
                    int tileIndex = slice;
                    int col = tileIndex % tilesPerRow;
                    int row = tileIndex / tilesPerRow;
                    int x = col * tileResolution;
                    int y = row * tileResolution;
                    tileRects[slice] = new Vector4(x, y, tileResolution, tileResolution);

                    Matrix4x4 viewMatrix = Matrix4x4.identity;
                    Matrix4x4 projMatrix = Matrix4x4.identity;
                    ShadowSplitData splitData = default;
                    bool valid = false;

                    if (candidate.isSpot)
                    {
                        valid = cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
                            lightIdx, out viewMatrix, out projMatrix, out splitData);
                    }
                    else
                    {
                        valid = cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                            lightIdx, (CubemapFace)face, visibleLight.light.shadowNearPlane,
                            out viewMatrix, out projMatrix, out splitData);
                    }

                    if (!valid)
                    {
                        shadowMatrices[slice] = Matrix4x4.identity;
                        sliceDraws[slice] = RGDrawListRef.Invalid;
                        continue;
                    }

                    shadowMatrices[slice] = projMatrix * viewMatrix;

                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIdx);
                    shadowDrawingSettings.splitIndex = face;
                    RecordShadowCasterSplit(lightIdx, face, splitData, BatchCullingProjectionType.Perspective);
                    rendererLists[slice] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);

                    ulong viewKey = MeshVisibilityShare.MakeLocalShadowViewKey(lightInstanceId, face);
                    GeometryUtility.CalculateFrustumPlanes(shadowMatrices[slice], shadowPlanes);
                    MeshVisibilityHandle localVis = m_VisibilityShare.Acquire(
                        meshScene,
                        viewKey,
                        shadowPlanes,
                        MeshVisibilityShare.PolicyLocalShadow,
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
                        viewPosition = lightPosition,
                        renderingLayerMask = shadowFilter.renderingLayerMask,
                        viewKey = viewKey
                    };
                    sliceDraws[slice] = m_RGBuilder.DeclareDrawList(m_ShadowMeshProcessor, shadowRequest, localVis, m_VisibilityShare);
                    m_VisibilityShare.Release(localVis);
                }

                nextSlice += faceCount;
            }

            //Add LocalShadowPass
            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<LocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RenderLocalShadow)))
            {
                //Setup Phase
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref LocalShadowPassData passData = ref passRef.GetPassData<LocalShadowPassData>();
                {
                    passData.lightCount = lightCount;
                    passData.sliceCount = sliceCount;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.tilesPerRow = tilesPerRow;
                    passData.tileResolution = tileResolution;
                    passData.cameraViewProj = cameraViewProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.shadowParams = shadowParams;
                    passData.tileRects = tileRects;
                    passData.lightFaceCounts = lightFaceCounts;
                    passData.lightSliceOffsets = lightSliceOffsets;
                    passData.rendererLists = rendererLists;

                    passData.draws = new RGDrawListRef[matrixCount];
                    for (int slice = 0; slice < matrixCount; ++slice)
                    {
                        passData.draws[slice] = sliceDraws[slice].IsValid
                            ? passRef.UseDrawList(sliceDraws[slice])
                            : RGDrawListRef.Invalid;
                    }
                }

                //Execute Phase
                passRef.SetExecuteFunc((in LocalShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalInt(LocalShadowPassUtilityData.LocalShadowCountID, passData.lightCount);
                    cmdEncoder.SetGlobalVector(LocalShadowPassUtilityData.LocalShadowMapSizeID, new Vector4(
                        passData.shadowMapResolution,
                        passData.shadowMapResolution,
                        1.0f / passData.shadowMapResolution,
                        1.0f / passData.shadowMapResolution));

                    if (passData.lightCount > 0)
                    {
                        cmdEncoder.SetGlobalMatrixArray(LocalShadowPassUtilityData.LocalShadowMatricesID, passData.shadowMatrices);
                        cmdEncoder.SetGlobalVectorArray(LocalShadowPassUtilityData.LocalShadowParamsID, passData.shadowParams);
                    }

                    for (int slice = 0; slice < passData.sliceCount; ++slice)
                    {
                        Vector4 rect = passData.tileRects[slice];
                        cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        cmdEncoder.SetGlobalDepthBias(1.0f, 2.5f);
                        cmdEncoder.SetGlobalMatrix(LocalShadowPassUtilityData.MatrixViewProjID, passData.shadowMatrices[slice]);

                        if (passData.draws != null && slice < passData.draws.Length && passData.draws[slice].IsValid)
                        {
                            cmdEncoder.Draw(passData.draws[slice]);
                        }

                        if (passData.rendererLists != null && slice < passData.rendererLists.Length && passData.rendererLists[slice].isValid)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererLists[slice]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                    }

                    cmdEncoder.SetGlobalMatrix(LocalShadowPassUtilityData.MatrixViewProjID, passData.cameraViewProj);
                });
            }
        }
    }
}
