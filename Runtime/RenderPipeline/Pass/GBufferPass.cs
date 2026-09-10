using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Core;
using InfinityTech.Rendering.RenderGraph;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class GBufferPassUtilityData
    {
        internal static string TextureAName = "GBufferTextureA";
        internal static string TextureBName = "GBufferTextureB";
        internal static string TextureCName = "GBufferTextureC";
        internal static string LightingTextureName = "LightingTexture";
        internal static readonly GlobalKeyword DirectionalLightmapKeyword = GlobalKeyword.Create("DIRLIGHTMAP_COMBINED");
        internal static readonly GlobalKeyword ShadowmaskKeyword = GlobalKeyword.Create("SHADOWS_SHADOWMASK");
        internal static bool HasShadowmask()
        {
            foreach (var map in LightmapSettings.lightmaps) if (map.shadowMask != null) return true;
            return false;
        }
        internal static readonly GlobalKeyword DBufferKeyword = GlobalKeyword.Create("_DBUFFER");
    }

    public partial class InfinityRenderPipeline
    {
        struct GBufferPassData
        {
            public bool bindDBuffer;
            public int hasProbes;
            public bool directionalLightmap, shadowmask;
            public RendererList rendererList;
            public RGDrawListRef draws;
            public RGTextureRef dBufferA;
            public RGTextureRef dBufferB;
            public RGTextureRef dBufferC;
        }

        void RenderGBuffer(RenderContext renderContext, Camera camera, MeshVisibilityHandle visibility, in CullingResults cullingResults)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.GBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            TextureDescriptor gbufferADsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                gbufferADsc.name = GBufferPassUtilityData.TextureAName;
                gbufferADsc.dimension = TextureDimension.Tex2D;
                gbufferADsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                gbufferADsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef gbufferTextureA = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferA, gbufferADsc);

            TextureDescriptor gbufferBDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                gbufferBDsc.name = GBufferPassUtilityData.TextureBName;
                gbufferBDsc.dimension = TextureDimension.Tex2D;
                gbufferBDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                gbufferBDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef gbufferTextureB = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferB, gbufferBDsc);

            TextureDescriptor gbufferCDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                gbufferCDsc.name = GBufferPassUtilityData.TextureCName;
                gbufferCDsc.dimension = TextureDimension.Tex2D;
                gbufferCDsc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                gbufferCDsc.depthBufferBits = EDepthBits.None;
            }
            RGTextureRef gbufferTextureC = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.GBufferC, gbufferCDsc);

            TextureDescriptor lightingDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                lightingDsc.name = GBufferPassUtilityData.LightingTextureName;
                lightingDsc.dimension = TextureDimension.Tex2D;
                lightingDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                lightingDsc.depthBufferBits = EDepthBits.None;
                lightingDsc.enableRandomWrite = true;
            }
            RGTextureRef lightingTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.LightingBuffer, lightingDsc);

            RGTextureRef dBufferA = default;
            RGTextureRef dBufferB = default;
            RGTextureRef dBufferC = default;
            bool bindDBuffer = ActiveFeatures.IsProduced(EFrameFeature.DBuffer)
                && m_RGScoper.TryQueryTexture(InfinityShaderIDs.DBufferA, out dBufferA)
                && m_RGScoper.TryQueryTexture(InfinityShaderIDs.DBufferB, out dBufferB)
                && m_RGScoper.TryQueryTexture(InfinityShaderIDs.DBufferC, out dBufferC);

            Shader.SetKeyword(GBufferPassUtilityData.DBufferKeyword, bindDBuffer);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.GBufferPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = new RenderQueueRange(0, 2999);
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask | PerObjectData.OcclusionProbe;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList gbufferRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            MeshFilterProgram gbufferFilter = BuiltinMeshesPasses.GBuffer.defaultFilter;
            gbufferFilter.layerMask = camera.cullingMask;
            gbufferFilter.renderingLayerMask = (uint)ERenderingLayer.Everything;
            var gbufferRequest = new MeshDrawRequest
            {
                filter = gbufferFilter,
                sort = BuiltinMeshesPasses.GBuffer.defaultSort,
                backendPolicy = RenderCaptureService.BackendFor(camera),
                shaderPassIndex = BuiltinMeshesPasses.GBuffer.shaderPassIndex,
                lightModeTag = BuiltinMeshesPasses.GBuffer.lightModeTag,
                viewPosition = camera.transform.position,
                viewKey = UnityEntityId.ToUInt64(camera)
            };
            RGDrawListRef gbufferDraws = m_RGBuilder.DeclareDrawList(m_GBufferMeshProcessor, gbufferRequest, visibility, m_VisibilityShare);

            var bakedDescriptor = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight)
            { name = "BakedDiffuse", dimension = TextureDimension.Tex2D, wrapMode = TextureWrapMode.Clamp, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, clearColor = Color.clear };
            RGTextureRef bakedDiffuse = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.BakedDiffuseBuffer, bakedDescriptor);
            bakedDescriptor.name = "BakedOcclusion";
            bakedDescriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            RGTextureRef bakedOcclusion = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.BakedOcclusionBuffer, bakedDescriptor);

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<GBufferPassData>(ProfilingSampler.Get(CustomSamplerId.RenderGBuffer)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(gbufferTextureA, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(gbufferTextureB, 1, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(gbufferTextureC, 2, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(lightingTexture, 3, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(bakedDiffuse, 4, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(bakedOcclusion, 5, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref GBufferPassData passData = ref passRef.GetPassData<GBufferPassData>();
                {
                    passData.bindDBuffer = bindDBuffer;
                    passData.directionalLightmap = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional;
                    passData.shadowmask = GBufferPassUtilityData.HasShadowmask();
                    passData.hasProbes = LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0 ? 1 : 0;
                    passData.rendererList = gbufferRendererList;
                    passData.draws = passRef.UseDrawList(gbufferDraws);
                    if (bindDBuffer)
                    {
                        passData.dBufferA = passRef.ReadTexture(dBufferA);
                        passData.dBufferB = passRef.ReadTexture(dBufferB);
                        passData.dBufferC = passRef.ReadTexture(dBufferC);
                    }
                }

                passRef.SetExecuteFunc((in GBufferPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetGlobalInt(Shader.PropertyToID("_InfinityHasProbes"), passData.hasProbes);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.DirectionalLightmapKeyword, passData.directionalLightmap);
                    cmdEncoder.SetKeyword(GBufferPassUtilityData.ShadowmaskKeyword, passData.shadowmask);
                    if (passData.bindDBuffer)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DBufferA, passData.dBufferA);
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DBufferB, passData.dBufferB);
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.DBufferC, passData.dBufferC);
                    }

                    cmdEncoder.Draw(passData.draws);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }

            MarkFeatureProduced(EFrameFeature.GBuffer);
        }
    }
}
