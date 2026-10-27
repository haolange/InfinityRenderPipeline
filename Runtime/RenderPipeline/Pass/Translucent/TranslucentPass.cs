using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class TranslucentPassUtilityData
    {
        internal static readonly GlobalKeyword ReactiveKeyword = GlobalKeyword.Create("INFINITY_WRITE_REACTIVE_MASK");
        internal static string DepthTextureName = "TranslucentDepthTexture";
        internal static readonly GlobalKeyword VolumetricFogKeyword = GlobalKeyword.Create("_VOLUMETRIC_FOG");
        internal static readonly GlobalKeyword AerialKeyword = GlobalKeyword.Create("_AERIAL_PERSPECTIVE");
        internal static readonly GlobalKeyword RefractionKeyword = GlobalKeyword.Create("_REFRACTION_PYRAMID");
        internal static int VolFog_MaxDistanceID = Shader.PropertyToID("VolFog_MaxDistance");
        internal static int VolFog_AerialDistanceID = Shader.PropertyToID("VolFog_AerialDistance");
    }

    public partial class InfinityRenderPipeline
    {
        struct TranslucentDepthPassData
        {
            public RGRendererListHandle rendererList;
        }

        struct TranslucentColorPassData
        {
            public RGRendererListHandle rendererList;
            public bool writeReactive;
            public bool bindFog;
            public bool bindAerial;
            public bool bindPyramid;
            public bool bindTranslucentDepth;
            public float fogMaxDistance;
            public float aerialDistance;
            public RGTextureHandle volumetricFog;
            public RGTextureHandle aerialLut;
            public RGTextureHandle colorPyramid;
            public RGTextureHandle translucentDepth;
        }

        void RasterTranslucentDepth(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            TextureDescriptor translucentDepthDsc = new TextureDescriptor(m_ActiveFrameState.dimensions.internalSize.x, m_ActiveFrameState.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                translucentDepthDsc.name = TranslucentPassUtilityData.DepthTextureName;
                translucentDepthDsc.dimension = TextureDimension.Tex2D;
                translucentDepthDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureHandle translucentDepthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.TranslucentDepthBuffer, translucentDepthDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.RasterTranslucentDepthPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = InfinityRenderQueue.k_RenderQueue_AllTransparent;
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListHandle depthRendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<TranslucentDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterTranslucentDepth)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(translucentDepthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref TranslucentDepthPassData passData = ref passRef.GetPassData<TranslucentDepthPassData>();
                passData.rendererList = passRef.UseRendererList(depthRendererList);

                passRef.SetExecuteFunc((in TranslucentDepthPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }

        void RasterTranslucentT0(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RasterTranslucentColor(renderContext, camera, cullingResults, InfinityPassIDs.RasterTranslucentT0Pass, CustomSamplerId.RasterTranslucentT0, bindFog: true, bindPyramid: false);
        }

        void RasterTranslucentT1(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RasterTranslucentColor(renderContext, camera, cullingResults, InfinityPassIDs.RasterTranslucentT1Pass, CustomSamplerId.RasterTranslucentT1, bindFog: false, bindPyramid: true);
        }

        void RasterTranslucentT2(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RasterTranslucentColor(renderContext, camera, cullingResults, InfinityPassIDs.RasterTranslucentT2Pass, CustomSamplerId.RasterTranslucentT2, bindFog: false, bindPyramid: false);
        }

        void RasterTranslucentColor(
            RenderContext renderContext,
            Camera camera,
            in CullingResults cullingResults,
            ShaderTagId lightMode,
            CustomSamplerId samplerId,
            bool bindFog,
            bool bindPyramid)
        {
            RGTextureHandle sceneColor = m_RGScoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer);
            RGTextureHandle depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool writeReactive = ShouldRecordFeature(EFrameFeature.TAA);
            bool initializeReactive = false;
            RGTextureHandle reactiveMask = default;
            if (writeReactive && !m_RGScoper.TryQueryTexture(InfinityShaderIDs.ReactiveMaskBuffer, out reactiveMask))
            {
                var size = m_ActiveFrameState.dimensions.internalSize;
                var descriptor = new TextureDescriptor(size.x, size.y, ETextureSizingDomain.Internal)
                {
                    name = "ReactiveMask", colorFormat = GraphicsFormat.R8_UNorm,
                    depthBufferBits = EDepthBits.None, dimension = TextureDimension.Tex2D,
                    clearColor = Color.clear, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                reactiveMask = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.ReactiveMaskBuffer, descriptor);
                initializeReactive = true;
            }
            RGTextureHandle motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            RGTextureHandle volumetricFog = default;
            RGTextureHandle aerialLut = default;
            RGTextureHandle colorPyramid = default;
            RGTextureHandle translucentDepth = default;
            bool hasFog = bindFog && m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out volumetricFog);
            bool hasAerial = bindFog && m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out aerialLut);
            bool hasPyramid = bindPyramid && m_RGScoper.TryQueryTexture(InfinityShaderIDs.ColorPyramidBuffer, out colorPyramid);
            bool hasTranslucentDepth = m_RGScoper.TryQueryTexture(InfinityShaderIDs.TranslucentDepthBuffer, out translucentDepth);

            float fogMaxDistance = ActiveVolumeStack.GetComponent<InfinityTech.Rendering.PostProcess.VolumetricFog>().MaxDistance.value;
            float aerialDistance = pipelineAsset.atmosphericalProfile != null
                ? AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).aerialPerspectiveDistance
                : 0.0f;

            ShaderTagId[] tags = m_RGBuilder.GetRecordingArray<ShaderTagId>(samplerId == CustomSamplerId.RasterTranslucentT2 ? 3 : 1);
            tags[0] = lightMode;
            if (tags.Length == 3) tags[1] = new ShaderTagId("SRPDefaultUnlit");
            RendererListDesc rendererListDesc = new RendererListDesc(tags, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = InfinityRenderQueue.k_RenderQueue_AllTransparent;
                rendererListDesc.sortingCriteria = SortingCriteria.CommonTransparent;
                rendererListDesc.renderingLayerMask = uint.MaxValue;
                rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RGRendererListHandle rendererList = m_RGBuilder.CreateRendererList(rendererListDesc);

            using (RGRasterPassHandle passRef = m_RGBuilder.AddRasterPass<TranslucentColorPassData>(ProfilingSampler.Get(samplerId)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(sceneColor, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                if (writeReactive)
                    passRef.SetColorAttachment(reactiveMask, 1, initializeReactive ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionTexture, writeReactive ? 2 : 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGScoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer), writeReactive ? 3 : 2, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref TranslucentColorPassData passData = ref passRef.GetPassData<TranslucentColorPassData>();
                passData.rendererList = passRef.UseRendererList(rendererList);
                passData.writeReactive = writeReactive;
                passData.bindFog = hasFog;
                passData.bindAerial = hasAerial;
                passData.bindPyramid = hasPyramid;
                passData.bindTranslucentDepth = hasTranslucentDepth;
                passData.fogMaxDistance = fogMaxDistance;
                passData.aerialDistance = aerialDistance;
                if (hasFog)
                {
                    passData.volumetricFog = passRef.ReadTexture(volumetricFog);
                }
                if (hasAerial)
                {
                    passData.aerialLut = passRef.ReadTexture(aerialLut);
                }
                if (hasPyramid)
                {
                    passData.colorPyramid = passRef.ReadTexture(colorPyramid);
                }
                if (hasTranslucentDepth)
                {
                    passData.translucentDepth = passRef.ReadTexture(translucentDepth);
                }

                passRef.SetExecuteFunc((in TranslucentColorPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.ReactiveKeyword, passData.writeReactive);
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.VolumetricFogKeyword, passData.bindFog);
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.AerialKeyword, passData.bindAerial);
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.RefractionKeyword, passData.bindPyramid);
                    cmdEncoder.SetGlobalFloat(TranslucentPassUtilityData.VolFog_MaxDistanceID, passData.fogMaxDistance);
                    cmdEncoder.SetGlobalFloat(TranslucentPassUtilityData.VolFog_AerialDistanceID, passData.aerialDistance);
                    if (passData.bindFog)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.VolumetricFogBuffer, passData.volumetricFog);
                    }
                    if (passData.bindAerial)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, passData.aerialLut);
                    }
                    if (passData.bindPyramid)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.ColorPyramidBuffer, passData.colorPyramid);
                    }
                    if (passData.bindTranslucentDepth)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.TranslucentDepthBuffer, passData.translucentDepth);
                    }
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
