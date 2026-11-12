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
        internal static readonly string TextureName = "CascadeShadowMapTexture";
        internal static readonly int CascadeSpheresID = Shader.PropertyToID("_CascadeSpheres");
        internal static readonly int CascadeCountID = Shader.PropertyToID("_CascadeCount");
        internal static readonly int CascadeShadowMapSizeID = Shader.PropertyToID("_CascadeShadowMapSize");
        internal static readonly int CascadeMatricesID = Shader.PropertyToID("_CascadeMatrices");
        internal static readonly int CascadeSplitDistancesID = Shader.PropertyToID("_CascadeSplitDistances");
        internal static readonly int ShadowDistanceID = Shader.PropertyToID("_ShadowDistance");
        // Shadow-caster shaders (ShadowPass / ShadowCaster) project with Matrix_ViewFlipYProj; the shadow passes set it
        // to the shadow matrix (a renderIntoTexture GPU projection) and restore the camera's afterwards.
        internal static readonly int CascadeShadowParamsID = Shader.PropertyToID("_CascadeShadowParams");
        internal static readonly int ShadowFilterParamsID = Shader.PropertyToID("_ShadowFilterParams");

        // _ShadowFilterParams of ShadowSampling.hlsl.
        internal static Vector4 FilterParams(in ShadowFilterSettings settings) =>
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
            public RGMeshDrawListHandle[] meshDrawLists;
            public RGRendererListHandle[] rendererLists;
        }

        void RasterCascadeShadow(in CameraRecordContext context, RGMeshDrawListHandle[] cascadeMeshDrawLists)
        {
            int shadowMapResolution = m_Settings.cascadeShadowMapResolution;
            float shadowDistance = m_Settings.shadowDistance;
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
            RGTextureHandle shadowMapTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.CascadeShadowMap, shadowMapDsc);

            int lightIndex = allocator.CascadeVisibleLightIndex;
            Matrix4x4[] shadowMatrices = m_RGBuilder.GetRecordingArray<Matrix4x4>(cascadeCount);
            Vector4[] tileRects = m_RGBuilder.GetRecordingArray<Vector4>(cascadeCount);
            Vector4 cascadeSplitDistances = allocator.CascadeSplitDistances;
            RGRendererListHandle[] cascadeRendererLists = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(cascadeCount);

            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                shadowMatrices[cascade] = slice.shadowMatrix;
                tileRects[cascade] = slice.atlasPixelRect;
                cascadeRendererLists[cascade] = RGRendererListHandle.Invalid;
            }

            if (lightIndex >= 0)
            {
                Light shadowLight = context.cullingResults.visibleLights[lightIndex].light;
                bool excludeStatic = ShadowCasterBounds.ExcludesStatic(shadowLight);
                bool hasNativeCasters = context.cullingResults.GetShadowCasterBounds(lightIndex, out _);

                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }

                    if (!hasNativeCasters)
                    {
                        continue;
                    }
                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(context.cullingResults, lightIndex);
                    shadowDrawingSettings.useRenderingLayerMaskTest = true;
                    shadowDrawingSettings.splitIndex = slice.cullingSplitIndex;
                    shadowDrawingSettings.objectsFilter = excludeStatic ? ShadowObjectsFilter.DynamicOnly : ShadowObjectsFilter.AllObjects;
                    cascadeRendererLists[cascade] = m_RGBuilder.CreateShadowRendererList(shadowDrawingSettings);
                }
            }

            using (var passRef = m_RGBuilder.AddRasterPass<CascadeShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(shadowMapTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref CascadeShadowPassData passData = ref passRef.GetPassData();
                {
                    passData.cascadeCount = cascadeCount;
                    passData.allocatedCount = allocator.CascadeAllocatedCount;
                    passData.spheres = m_RGBuilder.SnapshotRecordingArray(allocator.CascadeSpheres);
                    passData.valid = m_RGBuilder.GetRecordingArray<bool>(cascadeCount);
                    for (int i = 0; i < cascadeCount; i++)
                    {
                        passData.valid[i] = allocator.CascadeSlices[i].valid;
                    }
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.shadowDistance = shadowDistance;
                    passData.cameraViewProj = context.cameraUniform.matrix_ViewProj;
                    passData.cameraViewFlipYProj = context.cameraUniform.matrix_ViewFlipYProj;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.cascadeSplitDistances = cascadeSplitDistances;
                    passData.casterBias = m_RGBuilder.GetRecordingArray<Vector4>(cascadeCount);
                    for (int i = 0; i < cascadeCount; i++)
                    {
                        passData.casterBias[i] = allocator.CascadeSlices[i].casterBias;
                    }
                    if (lightIndex >= 0)
                    {
                        passData.casterLight = -context.cullingResults.visibleLights[lightIndex].light.transform.forward;
                    }

                    passData.meshDrawLists = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(cascadeCount);
                    passData.rendererLists = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(cascadeCount);
                    for (int cascade = 0; cascade < cascadeCount; ++cascade)
                    {
                        passData.meshDrawLists[cascade] = cascadeMeshDrawLists[cascade].IsValid
                            ? passRef.UseMeshDrawList(cascadeMeshDrawLists[cascade])
                            : RGMeshDrawListHandle.Invalid;
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
                        if (!passData.valid[cascade])
                        {
                            continue;
                        }
                        ProfilingSampler cascadeMarker = MeshDrawProfiling.SliceSampler(cascade);
                        cmdEncoder.BeginSample(cascadeMarker);
                        Vector4 rect = passData.tileRects[cascade];
                        cmdEncoder.SetViewport(new Rect(rect.x, rect.y, rect.z, rect.w));
                        cmdEncoder.SetGlobalDepthBias(0, 0);
                        cmdEncoder.SetGlobalVector(PassShaderIDs.ShadowCasterBias, passData.casterBias[cascade]);
                        cmdEncoder.SetGlobalVector(PassShaderIDs.ShadowCasterLight, passData.casterLight);
                        cmdEncoder.SetGlobalMatrix(CameraUniform.ID_Matrix_ViewProj, passData.shadowMatrices[cascade]);
                        cmdEncoder.SetGlobalMatrix(CameraUniform.ID_Matrix_ViewFlipYProj, passData.shadowMatrices[cascade]);

                        if (passData.meshDrawLists != null && cascade < passData.meshDrawLists.Length && passData.meshDrawLists[cascade].IsValid)
                        {
                            cmdEncoder.SubmitMeshDrawList(passData.meshDrawLists[cascade]);
                        }

                        if (passData.rendererLists != null && cascade < passData.rendererLists.Length && passData.rendererLists[cascade].IsValid)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererLists[cascade]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                        cmdEncoder.EndSample(cascadeMarker);
                    }

                    cmdEncoder.SetViewport(new Rect(0, 0, halfRes * 2, halfRes * 2));
                    cmdEncoder.SetGlobalMatrix(CameraUniform.ID_Matrix_ViewProj, passData.cameraViewProj);
                    cmdEncoder.SetGlobalMatrix(CameraUniform.ID_Matrix_ViewFlipYProj, passData.cameraViewFlipYProj);
                });
            }
        }
    }
}
