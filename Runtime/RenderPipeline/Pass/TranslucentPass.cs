using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using UnityEngine.Rendering.RendererUtils;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class TranslucentPassUtilityData
    {
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
            public RendererList rendererList;
        }

        struct TranslucentColorPassData
        {
            public RendererList rendererList;
            public bool bindFog;
            public bool bindAerial;
            public bool bindPyramid;
            public bool bindTranslucentDepth;
            public float fogMaxDistance;
            public float aerialDistance;
            public RGTextureRef volumetricFog;
            public RGTextureRef aerialLut;
            public RGTextureRef colorPyramid;
            public RGTextureRef translucentDepth;
        }

        void RenderTranslucentDepth(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            TextureDescriptor translucentDepthDsc = new TextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            {
                translucentDepthDsc.name = TranslucentPassUtilityData.DepthTextureName;
                translucentDepthDsc.dimension = TextureDimension.Tex2D;
                translucentDepthDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureRef translucentDepthTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.TranslucentDepthBuffer, translucentDepthDsc);

            RendererListDesc rendererListDesc = new RendererListDesc(InfinityPassIDs.TranslucentDepthPass, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = InfinityRenderQueue.k_RenderQueue_AllTransparent;
                rendererListDesc.sortingCriteria = SortingCriteria.QuantizedFrontToBack;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.None;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList depthRendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<TranslucentDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RenderTranslucentDepth)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(translucentDepthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref TranslucentDepthPassData passData = ref passRef.GetPassData<TranslucentDepthPassData>();
                passData.rendererList = depthRendererList;

                passRef.SetExecuteFunc((in TranslucentDepthPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }

        void RenderTranslucentT0(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RenderTranslucentColor(renderContext, camera, cullingResults, InfinityPassIDs.TranslucentT0Pass, CustomSamplerId.RenderTranslucentT0, bindFog: true, bindPyramid: false);
        }

        void RenderTranslucentT1(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RenderTranslucentColor(renderContext, camera, cullingResults, InfinityPassIDs.TranslucentT1Pass, CustomSamplerId.RenderTranslucentT1, bindFog: false, bindPyramid: true);
        }

        void RenderTranslucentT2(RenderContext renderContext, Camera camera, in CullingResults cullingResults)
        {
            RenderTranslucentColor(renderContext, camera, cullingResults, InfinityPassIDs.TranslucentT2Pass, CustomSamplerId.RenderTranslucentT2, bindFog: false, bindPyramid: false);
        }

        void RenderTranslucentColor(
            RenderContext renderContext,
            Camera camera,
            in CullingResults cullingResults,
            ShaderTagId lightMode,
            CustomSamplerId samplerId,
            bool bindFog,
            bool bindPyramid)
        {
            RGTextureRef sceneColor = m_RGScoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef reactiveMask = m_RGScoper.QueryTexture(InfinityShaderIDs.ReactiveMaskBuffer);
            RGTextureRef motionTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            RGTextureRef volumetricFog = default;
            RGTextureRef aerialLut = default;
            RGTextureRef colorPyramid = default;
            RGTextureRef translucentDepth = default;
            bool hasFog = bindFog && m_RGScoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out volumetricFog);
            bool hasAerial = bindFog && m_RGScoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out aerialLut);
            bool hasPyramid = bindPyramid && m_RGScoper.TryQueryTexture(InfinityShaderIDs.ColorPyramidBuffer, out colorPyramid);
            bool hasTranslucentDepth = m_RGScoper.TryQueryTexture(InfinityShaderIDs.TranslucentDepthBuffer, out translucentDepth);

            Shader.SetKeyword(TranslucentPassUtilityData.VolumetricFogKeyword, hasFog);
            Shader.SetKeyword(TranslucentPassUtilityData.AerialKeyword, hasAerial);
            Shader.SetKeyword(TranslucentPassUtilityData.RefractionKeyword, hasPyramid);

            float fogMaxDistance = ActiveVolumeStack.GetComponent<InfinityTech.Rendering.PostProcess.VolumetricFog>().MaxDistance.value;
            float aerialDistance = pipelineAsset.atmosphericalProfile != null
                ? AtmosphereParameter.FromProfile(pipelineAsset.atmosphericalProfile).aerialPerspectiveDistance
                : 0.0f;

            RendererListDesc rendererListDesc = new RendererListDesc(lightMode, cullingResults, camera);
            {
                rendererListDesc.layerMask = camera.cullingMask;
                rendererListDesc.renderQueueRange = InfinityRenderQueue.k_RenderQueue_AllTransparent;
                rendererListDesc.sortingCriteria = SortingCriteria.CommonTransparent;
                rendererListDesc.renderingLayerMask = 1;
                rendererListDesc.rendererConfiguration = PerObjectData.MotionVectors;
                rendererListDesc.excludeObjectMotionVectors = false;
            }
            RendererList rendererList = renderContext.scriptableRenderContext.CreateRendererList(rendererListDesc);

            using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<TranslucentColorPassData>(ProfilingSampler.Get(samplerId)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(sceneColor, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(reactiveMask, 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionTexture, 2, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref TranslucentColorPassData passData = ref passRef.GetPassData<TranslucentColorPassData>();
                passData.rendererList = rendererList;
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

                passRef.SetExecuteFunc((in TranslucentColorPassData passData, in RGRasterEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
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
