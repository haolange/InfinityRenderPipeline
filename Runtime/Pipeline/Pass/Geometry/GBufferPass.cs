using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class GBufferPassUtilityData
    {
        internal static readonly string TextureAName = "GBufferTextureA";
        internal static readonly string TextureBName = "GBufferTextureB";
        internal static readonly string SceneColorName = "SceneColor";
        internal static readonly string SidebandName = "Sideband";
        internal static readonly GlobalKeyword DirectionalLightmapKeyword = GlobalKeyword.Create("DIRLIGHTMAP_COMBINED");
        internal static readonly GlobalKeyword ShadowmaskKeyword = GlobalKeyword.Create("SHADOWS_SHADOWMASK");
        internal static readonly GlobalKeyword DBufferKeyword = GlobalKeyword.Create("_DBUFFER");
        internal static readonly GlobalKeyword ClusteredDecalKeyword = GlobalKeyword.Create("_CLUSTERED_DECALS");
        internal static readonly GlobalKeyword SidebandKeyword = GlobalKeyword.Create("INFINITY_SIDEBAND");
        internal static readonly int MixedLightChannelsID = Shader.PropertyToID("_MixedLightChannels");

        internal static TextureDescriptor CreateSidebandDescriptor(int width, int height)
        {
            return new TextureDescriptor(width, height, ETextureSizingDomain.Internal)
            {
                name = SidebandName,
                dimension = TextureDimension.Tex2D,
                colorFormat = EffectivePipelineConfiguration.SidebandFormat,
                enableRandomWrite = true,
                // An uncovered pixel has no diffuse share, baked visibility, light channels or profile.
                clearColor = Color.clear
            };
        }
    }

    internal sealed class GBufferPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly MeshPassId m_GBufferMeshPass;

        internal GBufferPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId gbufferMeshPass)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
            m_GBufferMeshPass = gbufferMeshPass;
        }

        struct GBufferPassData
        {
            public bool bindDBuffer;
            public ClusteredDecalInputs clusteredDecals;
            public int hasProbes;
            public bool directionalLightmap, shadowmask, sideband;
            public Vector4 mixedLightChannels;
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle dBufferAHandle;
            public RGTextureHandle dBufferBHandle;
            public RGTextureHandle dBufferCHandle;
            public RGBufferHandle atmosphereSkySHHandle;
        }

        internal void RasterGBuffer(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.GBuffer);
            int width = context.dimensions.internalSize.x;
            int height = context.dimensions.internalSize.y;
            RGMeshDrawListHandle gbufferMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_GBufferMeshPass);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            // Attachments

            TextureDescriptor gbufferADsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                gbufferADsc.name = GBufferPassUtilityData.TextureAName;
                gbufferADsc.dimension = TextureDimension.Tex2D;
                gbufferADsc.colorFormat = EffectivePipelineConfiguration.GBufferAFormat;
            }
            RGTextureHandle gbufferTextureAHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferA, gbufferADsc);

            TextureDescriptor gbufferBDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                gbufferBDsc.name = GBufferPassUtilityData.TextureBName;
                gbufferBDsc.dimension = TextureDimension.Tex2D;
                gbufferBDsc.colorFormat = EffectivePipelineConfiguration.GBufferBFormat;
            }
            RGTextureHandle gbufferTextureBHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);

            // Deferred shading and the atmosphere read and write Scene Color in place.
            TextureDescriptor sceneColorDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Internal);
            {
                sceneColorDsc.name = GBufferPassUtilityData.SceneColorName;
                sceneColorDsc.dimension = TextureDimension.Tex2D;
                sceneColorDsc.colorFormat = EffectivePipelineConfiguration.SceneColorFormat;
                sceneColorDsc.enableRandomWrite = true;
            }
            RGTextureHandle sceneColorHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, sceneColorDsc);

            // Deferred shading rewrites the sideband's diffuse share in place.
            bool sideband = context.sideband;
            RGTextureHandle sidebandHandle = default;
            if (sideband)
            {
                TextureDescriptor sidebandDsc = GBufferPassUtilityData.CreateSidebandDescriptor(width, height);
                sidebandHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SidebandBuffer, sidebandDsc);
            }

            // Inputs

            bool bindDBuffer = context.features.IsProduced(EFrameFeature.DBuffer);
            RGTextureHandle dBufferAHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferA) : default;
            RGTextureHandle dBufferBHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferB) : default;
            RGTextureHandle dBufferCHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferC) : default;
            RGBufferHandle atmosphereSkySHHandle = m_RGBuilder.scoper.QueryBuffer(InfinityShaderIDs.AtmosphereSkySH);

            RGRendererListHandle gbufferRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterGBufferPass,
                new RenderQueueRange(0, 2999), SortingCriteria.QuantizedFrontToBack, PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.OcclusionProbe | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume, m_Settings.enableInstanceBatch));

            {
                RGRasterPassHandle<GBufferPassData> passHandle = m_RGBuilder.AddRasterPass<GBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RasterGBuffer));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(gbufferTextureAHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(gbufferTextureBHandle, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(sceneColorHandle, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                if (sideband)
                {
                    passHandle.SetColorAttachment(sidebandHandle, 3, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                }
                // Depth comes from the depth prepass; the stencil receives the deferred and subsurface admission bits.
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref GBufferPassData passData = ref passHandle.GetPassData();
                {
                    passData.bindDBuffer = bindDBuffer;
                    ClusteredDecalInputs clusteredDecals = ClusteredDecalInputs.Query(m_RGBuilder, context);
                    clusteredDecals.Read(passHandle);
                    passData.clusteredDecals = clusteredDecals;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = context.lights.HasMixedLights;
                    passData.sideband = sideband;
                    passData.mixedLightChannels = context.lights.MixedLightChannels;
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.meshDrawListHandle = passHandle.UseMeshDrawList(gbufferMeshDrawListHandle);
                    passData.rendererListHandle = passHandle.UseRendererList(gbufferRendererListHandle);
                    passData.atmosphereSkySHHandle = passHandle.ReadBuffer(atmosphereSkySHHandle);
                    if (bindDBuffer)
                    {
                        passData.dBufferAHandle = passHandle.ReadTexture(dBufferAHandle);
                        passData.dBufferBHandle = passHandle.ReadTexture(dBufferBHandle);
                        passData.dBufferCHandle = passHandle.ReadTexture(dBufferCHandle);
                    }
                }

                passHandle.SetExecuteFunc((in GBufferPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.DBufferKeyword, passData.bindDBuffer);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.ClusteredDecalKeyword, passData.clusteredDecals.active);
                    passData.clusteredDecals.BindGlobal(cmdEncoder);
                    cmdEncoder.SetGlobalInt(PassShaderIDs.InfinityHasProbes, passData.hasProbes);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, passData.directionalLightmap);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, passData.shadowmask);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.SidebandKeyword, passData.sideband);
                    cmdEncoder.SetGlobalVector(GBufferPassUtilityData.MixedLightChannelsID, passData.mixedLightChannels);
                    cmdEncoder.SetGlobalBuffer(InfinityShaderIDs.AtmosphereSkySH, passData.atmosphereSkySHHandle);
                    if (passData.bindDBuffer)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DBufferA, passData.dBufferAHandle);
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DBufferB, passData.dBufferBHandle);
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DBufferC, passData.dBufferCHandle);
                    }

                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }

            context.features.MarkProduced(EFrameFeature.GBuffer);
        }
    }
}
