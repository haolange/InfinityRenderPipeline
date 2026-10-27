using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
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
        // Shadow-caster shaders (ShadowPass / ShadowCaster) project with Matrix_ViewFlipYProj; the shadow passes set it
        // to the shadow matrix (a renderIntoTexture GPU projection) and restore the camera's afterwards.
        internal static int MatrixViewFlipYProjID = Shader.PropertyToID("Matrix_ViewFlipYProj");
        internal static readonly int CascadeShadowParamsID = Shader.PropertyToID("_CascadeShadowParams");
        internal static readonly int ShadowFilterParamsID = Shader.PropertyToID("_ShadowFilterParams");

        // _ShadowFilterParams of ShadowSampling.hlsl.
        internal static Vector4 FilterParams(in FShadowFilterSettings settings) =>
            new Vector4(settings.pcfRadius, settings.pcssBlockerSamples, settings.pcssFilterSamples, settings.pcssMaxPenumbra);


    }

    public partial class InfinityRenderPipeline
    {
        struct CascadeShadowPassData
        {
            public int cascadeCount;
            public int shadowMapResolution;
            public float shadowDistance;
            public Matrix4x4 cameraViewProj;
            public Matrix4x4 cameraViewFlipYProj;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] tileRects;
            public Vector4[] spheres;
            public bool[] valid;
            public int allocatedCount;
            public Vector4 cascadeSplitDistances;
            public Vector4[] casterBias;
            public Vector4 casterLight;
            public RGDrawListHandle[] draws;
            public RGRendererListHandle[] rendererLists;
        }

        void RasterCascadeShadow(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            int shadowMapResolution = pipelineAsset.cascadeShadowMapResolution;
            float shadowDistance = pipelineAsset.shadowDistance;
            int cascadeCount = ShadowAllocator.CascadeCount;
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
            RGTextureHandle shadowMapTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.CascadeShadowMap, shadowMapDsc);

            int lightIndex = allocator.CascadeVisibleLightIndex;
            Matrix4x4[] shadowMatrices = m_RGBuilder.GetRecordingArray<Matrix4x4>(cascadeCount);
            Vector4[] tileRects = m_RGBuilder.GetRecordingArray<Vector4>(cascadeCount);
            Vector4 cascadeSplitDistances = allocator.CascadeSplitDistances;
            RGDrawListHandle[] cascadeDraws = m_RGBuilder.GetRecordingArray<RGDrawListHandle>(cascadeCount);
            RGRendererListHandle[] cascadeRendererLists = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(cascadeCount);

            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                shadowMatrices[cascade] = slice.shadowMatrix;
                tileRects[cascade] = slice.atlasPixelRect;
                cascadeDraws[cascade] = RGDrawListHandle.Invalid;
                cascadeRendererLists[cascade] = RGRendererListHandle.Invalid;
            }

            if (lightIndex >= 0)
            {
                Light shadowLight = cullingResults.visibleLights[lightIndex].light;
                ulong lightInstanceId = UnityEntityId.ToUInt64(shadowLight);
                uint shadowRenderingLayerMask = RenderingLayerUtility.Validate(unchecked((uint)shadowLight.renderingLayerMask));
                bool excludeStatic = ShadowCasterBounds.ExcludesStatic(shadowLight);
                bool hasNativeCasters = cullingResults.GetShadowCasterBounds(lightIndex, out _);

                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    FCascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }

                    var cascadePlanes = m_RGBuilder.GetRecordingArray<Plane>(slice.splitData.cullingPlaneCount);
                    for (int plane = 0; plane < cascadePlanes.Length; plane++)
                        cascadePlanes[plane] = slice.splitData.GetCullingPlane(plane);
                    MeshView cascadeView = MeshView.FromCascadeShadow(
                        lightInstanceId,
                        cascadePlanes,
                        camera.transform.position,
                        shadowLight.cullingMask,
                        shadowRenderingLayerMask,
                        cascade,
                        sceneCullingMask: MeshView.SceneCullingMaskFor(camera));
                    cascadeDraws[cascade] = m_RGBuilder.CreateDrawList(cascadeView, MeshPassId.Shadow);

                    if (!hasNativeCasters) continue;
                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex);
                    shadowDrawingSettings.useRenderingLayerMaskTest = true;
                    shadowDrawingSettings.splitIndex = slice.cullingSplitIndex;
                    shadowDrawingSettings.objectsFilter = excludeStatic ? ShadowObjectsFilter.DynamicOnly : ShadowObjectsFilter.AllObjects;
                    cascadeRendererLists[cascade] = m_RGBuilder.CreateShadowRendererList(shadowDrawingSettings);
                }
            }

            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<CascadeShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref CascadeShadowPassData passData = ref passRef.GetPassData<CascadeShadowPassData>();
                {
                    passData.cascadeCount = cascadeCount;
                    passData.allocatedCount = allocator.CascadeAllocatedCount;
                    passData.spheres = m_RGBuilder.SnapshotRecordingArray(allocator.CascadeSpheres);
                    passData.valid = m_RGBuilder.GetRecordingArray<bool>(cascadeCount);
                    for (int i = 0; i < cascadeCount; i++) passData.valid[i] = allocator.CascadeSlices[i].valid;
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.shadowDistance = shadowDistance;
                    passData.cameraViewProj = m_CameraUniform.matrix_ViewProj;
                    passData.cameraViewFlipYProj = m_CameraUniform.matrix_ViewFlipYProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.cascadeSplitDistances = cascadeSplitDistances;
                    passData.casterBias = m_RGBuilder.GetRecordingArray<Vector4>(cascadeCount);
                    for (int i = 0; i < cascadeCount; i++) passData.casterBias[i] = allocator.CascadeSlices[i].casterBias;
                    if (lightIndex >= 0) passData.casterLight = -cullingResults.visibleLights[lightIndex].light.transform.forward;

                    passData.draws = m_RGBuilder.GetRecordingArray<RGDrawListHandle>(cascadeCount);
                    passData.rendererLists = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(cascadeCount);
                    for (int cascade = 0; cascade < cascadeCount; ++cascade)
                    {
                        passData.draws[cascade] = cascadeDraws[cascade].IsValid
                            ? passRef.UseDrawList(cascadeDraws[cascade])
                            : RGDrawListHandle.Invalid;
                        passData.rendererLists[cascade] = cascadeRendererLists[cascade].IsValid
                            ? passRef.UseRendererList(cascadeRendererLists[cascade])
                            : RGRendererListHandle.Invalid;
                    }
                }

                passRef.SetExecuteFunc((in CascadeShadowPassData passData, in RGRasterEncoder cmdEncoder) =>
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
                        string cascadeMarker = MeshDrawProfiling.Slice(EMeshViewKind.CascadeShadow, cascade);
                        cmdEncoder.BeginSample(cascadeMarker);
                        Vector4 rect = passData.tileRects[cascade];
                        cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        cmdEncoder.SetGlobalDepthBias(0, 0);
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ShadowCasterBias"), passData.casterBias[cascade]);
                        cmdEncoder.SetGlobalVector(Shader.PropertyToID("_ShadowCasterLight"), passData.casterLight);
                        cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewProjID, passData.shadowMatrices[cascade]);
                        cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewFlipYProjID, passData.shadowMatrices[cascade]);

                        if (passData.draws != null && cascade < passData.draws.Length && passData.draws[cascade].IsValid)
                        {
                            cmdEncoder.Draw(passData.draws[cascade]);
                        }

                        if (passData.rendererLists != null && cascade < passData.rendererLists.Length && passData.rendererLists[cascade].IsValid)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererLists[cascade]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                        cmdEncoder.EndSample(cascadeMarker);
                    }

                    cmdEncoder.SetViewport(new Rect(0, 0, halfRes * 2, halfRes * 2));
                    cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewProjID, passData.cameraViewProj);
                    cmdEncoder.SetGlobalMatrix(CascadeShadowPassUtilityData.MatrixViewFlipYProjID, passData.cameraViewFlipYProj);
                });
            }
        }
    }
}
