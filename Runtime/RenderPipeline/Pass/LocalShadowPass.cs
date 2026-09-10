using UnityEngine;
using Unity.Mathematics;
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
        struct LocalShadowPassData
        {
            public int lightCount;
            public int sliceCount;
            public int shadowMapResolution;
            public Matrix4x4 cameraViewProj;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] tileRects;
            public Vector4[] casterBias, casterLight;
            public RendererList[] rendererLists;
            public RGDrawListRef[] draws;
        }

        void RenderLocalShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.localShadowMapResolution;
            MeshScene meshScene = renderContext.GetMeshScene();
            Matrix4x4 cameraViewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            Plane[] shadowPlanes = new Plane[6];
            ShadowAllocator allocator = renderContext.lightContext.ShadowAllocator;
            int sliceCount = allocator.LocalSliceCount;
            int matrixCount = math.max(1, sliceCount);

            TextureDescriptor shadowMapDsc = new TextureDescriptor(shadowMapResolution, shadowMapResolution);
            {
                shadowMapDsc.name = LocalShadowPassUtilityData.TextureName;
                shadowMapDsc.dimension = TextureDimension.Tex2D;
                shadowMapDsc.colorFormat = GraphicsFormat.None;
                shadowMapDsc.depthBufferBits = EDepthBits.Depth32;
                shadowMapDsc.isShadowMap = true;
                shadowMapDsc.filterMode = FilterMode.Bilinear;
            }
            RGTextureRef shadowMapTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LocalShadowMap, shadowMapDsc);

            Matrix4x4[] shadowMatrices = new Matrix4x4[matrixCount];
            Vector4[] tileRects = new Vector4[matrixCount];
            RendererList[] rendererLists = new RendererList[matrixCount];
            RGDrawListRef[] sliceDraws = new RGDrawListRef[matrixCount];
            for (int i = 0; i < matrixCount; ++i)
            {
                shadowMatrices[i] = Matrix4x4.identity;
                tileRects[i] = Vector4.zero;
                sliceDraws[i] = RGDrawListRef.Invalid;
            }

            int lightCount = 0;
            int lastRecord = -1;
            for (int slice = 0; slice < sliceCount; ++slice)
            {
                FLocalShadowSlice local = allocator.LocalSlices[slice];
                shadowMatrices[slice] = local.shadowMatrix;
                tileRects[slice] = local.atlasPixelRect;
                if (local.recordIndex != lastRecord)
                {
                    lastRecord = local.recordIndex;
                    lightCount++;
                }

                if (!local.valid)
                {
                    continue;
                }

                int lightIdx = local.visibleLightIndex;
                VisibleLight visibleLight = cullingResults.visibleLights[lightIdx];
                Light shadowLight = visibleLight.light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                Vector3 lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
                uint shadowRenderingLayerMask = RenderingLayerUtility.Validate(unchecked((uint)shadowLight.renderingLayerMask));

                ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIdx);
                shadowDrawingSettings.useRenderingLayerMaskTest = true;
                shadowDrawingSettings.splitIndex = local.face;
                rendererLists[slice] = renderContext.scriptableRenderContext.CreateShadowRendererList(ref shadowDrawingSettings);

                ulong viewKey = lightInstanceId;
                shadowPlanes = new Plane[local.splitData.cullingPlaneCount];
                for (int plane = 0; plane < shadowPlanes.Length; plane++) shadowPlanes[plane] = local.splitData.GetCullingPlane(plane);
                MeshVisibilityHandle localVis = m_VisibilityShare.Acquire(
                    meshScene,
                    viewKey,
                    shadowPlanes,
                    MeshVisibilityShare.PolicyLocalShadow,
                    enable: true, subviewIndex: local.face);

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
                    viewPosition = lightPosition,
                    viewKey = viewKey
                };
                sliceDraws[slice] = m_RGBuilder.DeclareDrawList(m_ShadowMeshProcessor, shadowRequest, localVis, m_VisibilityShare);
                m_VisibilityShare.Release(localVis);
            }

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<LocalShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RenderLocalShadow)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref LocalShadowPassData passData = ref passRef.GetPassData<LocalShadowPassData>();
                {
                    passData.lightCount = lightCount;
                    passData.sliceCount = sliceCount;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.cameraViewProj = cameraViewProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.casterBias = new Vector4[matrixCount];
                    passData.casterLight = new Vector4[matrixCount];
                    for (int i = 0; i < sliceCount; i++)
                    {
                        var local = allocator.LocalSlices[i];
                        passData.casterBias[i] = local.casterBias;
                        Vector3 position = cullingResults.visibleLights[local.visibleLightIndex].light.transform.position;
                        passData.casterLight[i] = new Vector4(position.x, position.y, position.z, 1);
                    }
                    passData.rendererLists = rendererLists;
                    passData.draws = new RGDrawListRef[matrixCount];
                    for (int slice = 0; slice < matrixCount; ++slice)
                    {
                        passData.draws[slice] = sliceDraws[slice].IsValid
                            ? passRef.UseDrawList(sliceDraws[slice])
                            : RGDrawListRef.Invalid;
                    }
                }

                passRef.SetExecuteFunc((in LocalShadowPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalInt(LocalShadowPassUtilityData.LocalShadowCountID, passData.lightCount);
                    cmdEncoder.SetGlobalVector(LocalShadowPassUtilityData.LocalShadowMapSizeID, new Vector4(
                        passData.shadowMapResolution,
                        passData.shadowMapResolution,
                        1.0f / passData.shadowMapResolution,
                        1.0f / passData.shadowMapResolution));

                    for (int slice = 0; slice < passData.sliceCount; ++slice)
                    {
                        string faceMarker = $"LocalShadowSlice{slice}";
                        cmdEncoder.BeginSample(faceMarker);
                        Vector4 rect = passData.tileRects[slice];
                        cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        cmdEncoder.SetGlobalDepthBias(0, 0);
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ShadowCasterBias"), passData.casterBias[slice]);
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ShadowCasterLight"), passData.casterLight[slice]);
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
                        cmdEncoder.EndSample(faceMarker);
                    }

                    cmdEncoder.SetGlobalMatrix(LocalShadowPassUtilityData.MatrixViewProjID, passData.cameraViewProj);
                });
            }
        }
    }
}
