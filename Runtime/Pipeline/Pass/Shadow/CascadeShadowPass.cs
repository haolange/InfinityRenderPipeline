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

        // _ShadowFilterParams of ShadowSampling.hlsl.
        internal static Vector4 FilterParams(in ShadowFilterSettings settings) =>
            new Vector4(settings.pcfRadius, settings.pcssBlockerSamples, settings.pcssFilterSamples, settings.pcssMaxPenumbra);


    }

    internal sealed class CascadeShadowPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly MeshPassId m_ShadowMeshPass;

        internal CascadeShadowPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId shadowMeshPass)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
            m_ShadowMeshPass = shadowMeshPass;
        }

        struct CascadeShadowPassData
        {
            public int cascadeCount;
            public int shadowMapResolution;
            public Matrix4x4[] shadowMatrices;
            public Vector4[] tileRects;
            public bool[] valid;
            public Vector4[] casterBias;
            public Vector4 casterLight;
            public RGMeshDrawListHandle[] meshDrawListsHandle;
            public RGRendererListHandle[] rendererListsHandle;
        }

        internal void RasterCascadeShadow(in ViewContext context)
        {
            int shadowMapResolution = m_Settings.cascadeShadowMapResolution;
            int cascadeCount = ShadowAllocator.CascadeCount;
            ShadowAllocator allocator = context.lights.ShadowAllocator;

            TextureDescriptor shadowMapDsc = new TextureDescriptor(shadowMapResolution * 2, shadowMapResolution * 2);
            {
                shadowMapDsc.name = CascadeShadowPassUtilityData.TextureName;
                shadowMapDsc.dimension = TextureDimension.Tex2D;
                shadowMapDsc.colorFormat = GraphicsFormat.None;
                shadowMapDsc.depthBufferBits = EDepthBits.Depth32;
                shadowMapDsc.isShadowMap = true;
                shadowMapDsc.filterMode = FilterMode.Bilinear;
            }
            RGTextureHandle shadowMapTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.CascadeShadowMap, shadowMapDsc);

            int lightIndex = allocator.CascadeVisibleLightIndex;
            Matrix4x4[] shadowMatrices = m_RGBuilder.GetRecordingArray<Matrix4x4>(cascadeCount);
            Vector4[] tileRects = m_RGBuilder.GetRecordingArray<Vector4>(cascadeCount);
            RGMeshDrawListHandle[] cascadeMeshDrawListsHandle = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(cascadeCount);
            RGRendererListHandle[] cascadeRendererListsHandle = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(cascadeCount);

            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                shadowMatrices[cascade] = slice.shadowMatrix;
                tileRects[cascade] = slice.atlasPixelRect;
                cascadeRendererListsHandle[cascade] = RGRendererListHandle.InvalidHandle;
            }

            if (lightIndex >= 0)
            {
                Light shadowLight = context.cullingResults.visibleLights[lightIndex].light;
                bool excludeStatic = ShadowCasterBounds.ExcludesStatic(shadowLight, context.shadowmaskMode);
                bool hasNativeCasters = context.cullingResults.GetShadowCasterBounds(lightIndex, out _);

                for (int cascade = 0; cascade < cascadeCount; ++cascade)
                {
                    CascadeShadowSlice slice = allocator.CascadeSlices[cascade];
                    if (!slice.valid)
                    {
                        continue;
                    }
                    // Each cascade is its own light view: no camera occluder and no motion reach it.
                    RGMeshViewHandle cascadeViewHandle = m_RGBuilder.CullMeshView(new MeshViewDesc(
                        CameraCulling.Context(context.camera, shadowLight, slice.splitData), default, MeshOccluder.None));
                    cascadeMeshDrawListsHandle[cascade] = m_RGBuilder.CreateMeshDrawList(cascadeViewHandle, m_ShadowMeshPass);
                    if (!hasNativeCasters)
                    {
                        continue;
                    }
                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(context.cullingResults, lightIndex);
                    shadowDrawingSettings.useRenderingLayerMaskTest = true;
                    shadowDrawingSettings.splitIndex = slice.cullingSplitIndex;
                    shadowDrawingSettings.objectsFilter = excludeStatic ? ShadowObjectsFilter.DynamicOnly : ShadowObjectsFilter.AllObjects;
                    cascadeRendererListsHandle[cascade] = m_RGBuilder.CreateShadowRendererList(shadowDrawingSettings);
                }
            }

            {
                RGRasterPassHandle<CascadeShadowPassData> passHandle = m_RGBuilder.AddRasterPass<CascadeShadowPassData>(ProfilingSampler.Get(CustomSamplerId.RasterCascadeShadow));
                passHandle.EnablePassCulling(false);
                passHandle.SetDepthStencilAttachment(shadowMapTextureHandle, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref CascadeShadowPassData passData = ref passHandle.GetPassData();
                {
                    passData.cascadeCount = cascadeCount;
                    passData.valid = m_RGBuilder.GetRecordingArray<bool>(cascadeCount);
                    for (int i = 0; i < cascadeCount; i++)
                    {
                        passData.valid[i] = allocator.CascadeSlices[i].valid;
                    }
                    passData.shadowMapResolution = shadowMapResolution;
                    passData.shadowMatrices = shadowMatrices;
                    passData.tileRects = tileRects;
                    passData.casterBias = m_RGBuilder.GetRecordingArray<Vector4>(cascadeCount);
                    for (int i = 0; i < cascadeCount; i++)
                    {
                        passData.casterBias[i] = allocator.CascadeSlices[i].casterBias;
                    }
                    if (lightIndex >= 0)
                    {
                        passData.casterLight = -context.cullingResults.visibleLights[lightIndex].light.transform.forward;
                    }

                    passData.meshDrawListsHandle = m_RGBuilder.GetRecordingArray<RGMeshDrawListHandle>(cascadeCount);
                    passData.rendererListsHandle = m_RGBuilder.GetRecordingArray<RGRendererListHandle>(cascadeCount);
                    for (int cascade = 0; cascade < cascadeCount; ++cascade)
                    {
                        passData.meshDrawListsHandle[cascade] = cascadeMeshDrawListsHandle[cascade].IsValid
                            ? passHandle.UseMeshDrawList(cascadeMeshDrawListsHandle[cascade])
                            : RGMeshDrawListHandle.InvalidHandle;
                        passData.rendererListsHandle[cascade] = cascadeRendererListsHandle[cascade].IsValid
                            ? passHandle.UseRendererList(cascadeRendererListsHandle[cascade])
                            : RGRendererListHandle.InvalidHandle;
                    }
                }

                passHandle.SetExecuteFunc((in CascadeShadowPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    int halfRes = passData.shadowMapResolution;

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
                        cmdEncoder.SetGlobalMatrix(PassShaderIDs.ShadowCasterViewProj, passData.shadowMatrices[cascade]);

                        if (passData.meshDrawListsHandle != null && cascade < passData.meshDrawListsHandle.Length && passData.meshDrawListsHandle[cascade].IsValid)
                        {
                            cmdEncoder.SubmitMeshDrawList(passData.meshDrawListsHandle[cascade]);
                        }

                        if (passData.rendererListsHandle != null && cascade < passData.rendererListsHandle.Length && passData.rendererListsHandle[cascade].IsValid)
                        {
                            cmdEncoder.DrawRendererList(passData.rendererListsHandle[cascade]);
                        }

                        cmdEncoder.SetGlobalDepthBias(0.0f, 0.0f);
                        cmdEncoder.EndSample(cascadeMarker);
                    }

                    cmdEncoder.SetViewport(new Rect(0, 0, halfRes * 2, halfRes * 2));
                });
            }
        }
    }
}
