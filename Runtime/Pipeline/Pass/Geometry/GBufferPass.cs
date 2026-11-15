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
        internal static readonly string TextureCName = "GBufferTextureC";
        internal static readonly string LightingTextureName = "LightingTexture";
        internal static readonly GlobalKeyword DirectionalLightmapKeyword = GlobalKeyword.Create("DIRLIGHTMAP_COMBINED");
        internal static readonly GlobalKeyword ShadowmaskKeyword = GlobalKeyword.Create("SHADOWS_SHADOWMASK");
        internal static readonly GlobalKeyword DBufferKeyword = GlobalKeyword.Create("_DBUFFER");
    }

    public partial class InfinityRenderPipeline
    {
        struct GBufferPassData
        {
            public bool bindDBuffer;
            public int hasProbes;
            public bool directionalLightmap, shadowmask;
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
            public RGTextureHandle dBufferAHandle;
            public RGTextureHandle dBufferBHandle;
            public RGTextureHandle dBufferCHandle;
        }

        void RasterGBuffer(in CameraRecordContext context)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.GBuffer);
            RGMeshDrawListHandle gbufferMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(new MeshDrawListDesc(context.mainCulling, InfinityPassIDs.RasterGBufferPass));
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor gbufferADsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                gbufferADsc.name = GBufferPassUtilityData.TextureAName;
                gbufferADsc.dimension = TextureDimension.Tex2D;
                gbufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            }
            RGTextureHandle gbufferTextureAHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferA, gbufferADsc);

            TextureDescriptor gbufferBDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                gbufferBDsc.name = GBufferPassUtilityData.TextureBName;
                gbufferBDsc.dimension = TextureDimension.Tex2D;
                gbufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            }
            RGTextureHandle gbufferTextureBHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);

            TextureDescriptor gbufferCDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                gbufferCDsc.name = GBufferPassUtilityData.TextureCName;
                gbufferCDsc.dimension = TextureDimension.Tex2D;
                gbufferCDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            }
            RGTextureHandle gbufferTextureCHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferC, gbufferCDsc);

            TextureDescriptor lightingDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                lightingDsc.name = GBufferPassUtilityData.LightingTextureName;
                lightingDsc.dimension = TextureDimension.Tex2D;
                lightingDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                lightingDsc.enableRandomWrite = true;
            }
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, lightingDsc);

            bool bindDBuffer = context.features.IsProduced(EFrameFeature.DBuffer);
            RGTextureHandle dBufferAHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferA) : default;
            RGTextureHandle dBufferBHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferB) : default;
            RGTextureHandle dBufferCHandle = bindDBuffer ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DBufferC) : default;

            RGRendererListHandle gbufferRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterGBufferPass,
                new RenderQueueRange(0, 2999), SortingCriteria.QuantizedFrontToBack, PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.OcclusionProbe | PerObjectData.LightProbeProxyVolume | PerObjectData.OcclusionProbeProxyVolume, m_Settings.enableInstanceBatch));

            var bakedDescriptor = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal)
            { name = "BakedDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, clearColor = Color.clear };
            RGTextureHandle bakedDiffuseHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.BakedDiffuseBuffer, bakedDescriptor);
            bakedDescriptor.name = "BakedOcclusion";
            bakedDescriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            RGTextureHandle bakedOcclusionHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.BakedOcclusionBuffer, bakedDescriptor);

            {
                RGRasterPassHandle<GBufferPassData> passHandle = m_RGBuilder.AddRasterPass<GBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RasterGBuffer));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(gbufferTextureAHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(gbufferTextureBHandle, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(gbufferTextureCHandle, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(lightingTextureHandle, 3, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(bakedDiffuseHandle, 4, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(bakedOcclusionHandle, 5, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref GBufferPassData passData = ref passHandle.GetPassData();
                {
                    passData.bindDBuffer = bindDBuffer;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = renderContext.lightContext.HasShadowmask;
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
