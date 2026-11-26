using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class GBufferPassUtilityData
    {
        internal static readonly string TextureAName = "GBufferTextureA";
        internal static readonly string TextureBName = "GBufferTextureB";
        internal static readonly string SurfaceMetadataName = "SurfaceMetadata";
        internal static readonly string LightingTextureName = "LightingTexture";
        internal static readonly string BakedDiffuseName = "BakedDiffuse";
        internal static readonly string BakedShadowmaskName = "BakedShadowmask";
        internal static readonly string CustomDataName = "CustomData";
        internal static readonly GlobalKeyword DirectionalLightmapKeyword = GlobalKeyword.Create("DIRLIGHTMAP_COMBINED");
        internal static readonly GlobalKeyword ShadowmaskKeyword = GlobalKeyword.Create("SHADOWS_SHADOWMASK");
        internal static readonly GlobalKeyword DBufferKeyword = GlobalKeyword.Create("_DBUFFER");
        internal static readonly GlobalKeyword BestFitYCoCgKeyword = GlobalKeyword.Create("INFINITY_GBUFFER_BESTFIT_YCOCG");
        internal static readonly GlobalKeyword BakedDiffuseKeyword = GlobalKeyword.Create("INFINITY_GBUFFER_BAKED_DIFFUSE");
        internal static readonly GlobalKeyword CustomDataSSSKeyword = GlobalKeyword.Create("INFINITY_GBUFFER_CUSTOM_DATA_SSS");
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

        internal static bool NeedsBakedDiffuse(in ViewContext context)
        {
            return context.features.ShouldRecord(EFrameFeature.GTAO)
                || context.features.ShouldRecord(EFrameFeature.RTAO)
                || context.features.ShouldRecord(EFrameFeature.SSGI)
                || context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);
        }

        struct GBufferPassData
        {
            public bool bindDBuffer;
            public int hasProbes;
            public bool directionalLightmap, shadowmask, needsBakedDiffuse, hasSubsurface;
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle dBufferAHandle;
            public RGTextureHandle dBufferBHandle;
            public RGTextureHandle dBufferCHandle;
        }

        internal void RasterGBuffer(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.GBuffer);
            EffectivePipelineConfiguration config = m_Settings.config;
            RGMeshDrawListHandle gbufferMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_GBufferMeshPass);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor gbufferADsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                gbufferADsc.name = GBufferPassUtilityData.TextureAName;
                gbufferADsc.dimension = TextureDimension.Tex2D;
                gbufferADsc.colorFormat = config.gBufferAFormat;
            }
            RGTextureHandle gbufferTextureAHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferA, gbufferADsc);

            TextureDescriptor gbufferBDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                gbufferBDsc.name = GBufferPassUtilityData.TextureBName;
                gbufferBDsc.dimension = TextureDimension.Tex2D;
                gbufferBDsc.colorFormat = config.gBufferBFormat;
            }
            RGTextureHandle gbufferTextureBHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);

            TextureDescriptor metadataDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                metadataDsc.name = GBufferPassUtilityData.SurfaceMetadataName;
                metadataDsc.dimension = TextureDimension.Tex2D;
                metadataDsc.colorFormat = config.surfaceMetadataFormat;
            }
            RGTextureHandle surfaceMetadataHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SurfaceMetadataBuffer, metadataDsc);

            TextureDescriptor lightingDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                lightingDsc.name = GBufferPassUtilityData.LightingTextureName;
                lightingDsc.dimension = TextureDimension.Tex2D;
                lightingDsc.colorFormat = config.sceneColorFormat;
                lightingDsc.enableRandomWrite = true;
            }
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, lightingDsc);

            bool bindDBuffer = context.features.IsProduced(EFrameFeature.DBuffer);
            RGTextureHandle dBufferAHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferA) : default;
            RGTextureHandle dBufferBHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferB) : default;
            RGTextureHandle dBufferCHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferC) : default;

            RGRendererListHandle gbufferRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterGBufferPass,
                new RenderQueueRange(0, 2999), SortingCriteria.QuantizedFrontToBack, PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.OcclusionProbe | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume, m_Settings.enableInstanceBatch));

            bool needsBakedDiffuse = NeedsBakedDiffuse(in context);
            bool hasShadowmask = context.lights.HasShadowmask;
            bool hasSubsurface = context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering);

            RGTextureHandle bakedDiffuseHandle = default;
            if (needsBakedDiffuse)
            {
                var bakedDescriptor = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal)
                { name = GBufferPassUtilityData.BakedDiffuseName, dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = config.bakedDiffuseFormat, clearColor = Color.clear };
                bakedDiffuseHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.BakedDiffuseBuffer, bakedDescriptor);
            }

            RGTextureHandle bakedShadowmaskHandle = default;
            if (hasShadowmask)
            {
                var shadowmaskDescriptor = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal)
                { name = GBufferPassUtilityData.BakedShadowmaskName, dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearColor = Color.clear };
                bakedShadowmaskHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.BakedShadowmaskBuffer, shadowmaskDescriptor);
            }

            RGTextureHandle customDataHandle = default;
            if (hasSubsurface)
            {
                var customDataDescriptor = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal)
                { name = GBufferPassUtilityData.CustomDataName, dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = config.customDataSSSFormat, clearColor = Color.clear };
                customDataHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.CustomDataBuffer, customDataDescriptor);
            }

            {
                RGRasterPassHandle<GBufferPassData> passHandle = m_RGBuilder.AddRasterPass<GBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RasterGBuffer));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(gbufferTextureAHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(gbufferTextureBHandle, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(surfaceMetadataHandle, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(lightingTextureHandle, 3, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                int nextAttachmentSlot = 4;
                if (needsBakedDiffuse)
                {
                    passHandle.SetColorAttachment(bakedDiffuseHandle, nextAttachmentSlot++, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                }
                if (hasShadowmask)
                {
                    passHandle.SetColorAttachment(bakedShadowmaskHandle, nextAttachmentSlot++, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                }
                if (hasSubsurface)
                {
                    passHandle.SetColorAttachment(customDataHandle, nextAttachmentSlot++, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                }
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref GBufferPassData passData = ref passHandle.GetPassData();
                {
                    passData.bindDBuffer = bindDBuffer;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = hasShadowmask;
                    passData.needsBakedDiffuse = needsBakedDiffuse;
                    passData.hasSubsurface = hasSubsurface;
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.meshDrawListHandle = passHandle.UseMeshDrawList(gbufferMeshDrawListHandle);
                    passData.rendererListHandle = passHandle.UseRendererList(gbufferRendererListHandle);
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
                    cmdEncoder.SetGlobalInt(PassShaderIDs.InfinityHasProbes, passData.hasProbes);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, passData.directionalLightmap);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, passData.shadowmask);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.BakedDiffuseKeyword, passData.needsBakedDiffuse);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.CustomDataSSSKeyword, passData.hasSubsurface);
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
