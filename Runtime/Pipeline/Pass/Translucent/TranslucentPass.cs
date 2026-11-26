using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.MeshPipeline;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class TranslucentPassUtilityData
    {
        internal static readonly GlobalKeyword ReactiveKeyword = GlobalKeyword.Create("INFINITY_WRITE_REACTIVE_MASK");
        internal static readonly string DepthTextureName = "TranslucentDepthTexture";
        internal static readonly GlobalKeyword VolumetricFogKeyword = GlobalKeyword.Create("_VOLUMETRIC_FOG");
        internal static readonly GlobalKeyword AerialKeyword = GlobalKeyword.Create("_AERIAL_PERSPECTIVE");
        internal static readonly GlobalKeyword RefractionKeyword = GlobalKeyword.Create("_REFRACTION_PYRAMID");
    }

    internal sealed class TranslucentPass
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly MeshPassId m_TranslucentDepthMeshPass, m_TranslucentT0MeshPass, m_TranslucentT1MeshPass, m_TranslucentT2MeshPass;

        internal TranslucentPass(RGBuilder graph, in InfinityRenderPipelineAsset.Settings settings, MeshPassId translucentDepthMeshPass,
            MeshPassId translucentT0MeshPass, MeshPassId translucentT1MeshPass, MeshPassId translucentT2MeshPass)
        {
            m_RGBuilder = graph;
            m_Settings = settings;
            m_TranslucentDepthMeshPass = translucentDepthMeshPass;
            m_TranslucentT0MeshPass = translucentT0MeshPass;
            m_TranslucentT1MeshPass = translucentT1MeshPass;
            m_TranslucentT2MeshPass = translucentT2MeshPass;
        }

        struct TranslucentDepthPassData
        {
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
        }

        struct TranslucentColorPassData
        {
            public RGMeshDrawListHandle meshDrawListHandle;
            public RGRendererListHandle rendererListHandle;
            public bool writeReactive;
            public bool bindFog;
            public bool bindAerial;
            public bool bindPyramid;
            public float fogMaxDistance;
            public float aerialDistance;
            public RGTextureHandle volumetricFogHandle;
            public RGTextureHandle aerialLutHandle;
            public RGTextureHandle colorPyramidHandle;
            public RGTextureHandle translucentDepthHandle;
        }

        internal void RasterTranslucentDepth(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RGMeshDrawListHandle depthMeshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, m_TranslucentDepthMeshPass);
            TextureDescriptor translucentDepthDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                translucentDepthDsc.name = TranslucentPassUtilityData.DepthTextureName;
                translucentDepthDsc.dimension = TextureDimension.Tex2D;
                translucentDepthDsc.depthStencilFormat = GraphicsFormat.D32_SFloat;
            }
            RGTextureHandle translucentDepthTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TranslucentDepthBuffer, translucentDepthDsc);

            RGRendererListHandle depthRendererListHandle = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterTranslucentDepthPass,
                InfinityRenderQueue.k_RenderQueue_AllTransparent, SortingCriteria.QuantizedFrontToBack, PerObjectData.None, m_Settings.enableInstanceBatch));

            {
                RGRasterPassHandle<TranslucentDepthPassData> passHandle = m_RGBuilder.AddRasterPass<TranslucentDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterTranslucentDepth));
                passHandle.EnablePassCulling(false);
                passHandle.SetDepthStencilAttachment(translucentDepthTextureHandle, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref TranslucentDepthPassData passData = ref passHandle.GetPassData();
                passData.meshDrawListHandle = passHandle.UseMeshDrawList(depthMeshDrawListHandle);
                passData.rendererListHandle = passHandle.UseRendererList(depthRendererListHandle);

                passHandle.SetExecuteFunc((in TranslucentDepthPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }
        }

        internal void RasterTranslucentT0(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RasterTranslucentColor(context, viewHandle, m_TranslucentT0MeshPass, InfinityPassIDs.RasterTranslucentT0Pass, CustomSamplerId.RasterTranslucentT0, bindFog: true, bindPyramid: false);
        }

        internal void RasterTranslucentT1(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RasterTranslucentColor(context, viewHandle, m_TranslucentT1MeshPass, InfinityPassIDs.RasterTranslucentT1Pass, CustomSamplerId.RasterTranslucentT1, bindFog: false, bindPyramid: true);
        }

        internal void RasterTranslucentT2(in ViewContext context, in RGMeshViewHandle viewHandle)
        {
            RasterTranslucentColor(context, viewHandle, m_TranslucentT2MeshPass, InfinityPassIDs.RasterTranslucentT2Pass, CustomSamplerId.RasterTranslucentT2, bindFog: false, bindPyramid: false);
        }

        // MeshComponent content is drawn back to front before the native renderer list in the same pass. The two lists are
        // sorted separately: there is no distance order between MeshComponent and native translucent renderers.
        void RasterTranslucentColor(in ViewContext context, in RGMeshViewHandle viewHandle, MeshPassId meshPass, ShaderTagId lightMode,
            CustomSamplerId samplerId, bool bindFog, bool bindPyramid)
        {
            RGMeshDrawListHandle meshDrawListHandle = m_RGBuilder.CreateMeshDrawList(viewHandle, meshPass);
            RGTextureHandle sceneColorHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.fogged);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool writeReactive = context.temporalRequirements.requiresReactiveMask || context.features.ShouldRecord(EFrameFeature.TAA);
            bool initializeReactive = false;
            RGTextureHandle reactiveMaskHandle = default;
            if (writeReactive && !m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.ReactiveMaskBuffer, out reactiveMaskHandle))
            {
                var size = context.dimensions.internalSize;
                var descriptor = new TextureDescriptor(size.x, size.y, ETextureSizingDomain.Internal)
                {
                    name = "ReactiveMask", colorFormat = GraphicsFormat.R8_UNorm,
                    depthStencilFormat = GraphicsFormat.None, dimension = TextureDimension.Tex2D,
                    clearColor = Color.clear, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                reactiveMaskHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ReactiveMaskBuffer, descriptor);
                initializeReactive = true;
            }
            RGTextureHandle motionTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            bool hasFog = bindFog && context.features.ShouldRecord(EFrameFeature.VolumetricFog);
            bool hasAerial = bindFog;
            bool hasPyramid = bindPyramid && context.features.ShouldRecord(EFrameFeature.ColorPyramid);
            RGTextureHandle volumetricFogHandle = hasFog ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.VolumetricFogBuffer) : default;
            RGTextureHandle aerialLutHandle = hasAerial ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT) : default;
            RGTextureHandle colorPyramidHandle = hasPyramid ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ColorPyramidBuffer) : default;
            // RasterTranslucentDepth runs before every translucent color pass and always registers its target.
            RGTextureHandle translucentDepthHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.TranslucentDepthBuffer);

            float fogMaxDistance = context.volumes.volumetricFog.MaxDistance.value;
            float aerialDistance = context.atmosphere.aerialPerspectiveDistance;

            // T2 also draws materials that only have Unity's unlit pass.
            RendererListParams listParameters = samplerId == CustomSamplerId.RasterTranslucentT2
                ? CameraRendererLists.CreateWithUnlit(context.cullingResults, context.camera, lightMode, InfinityRenderQueue.k_RenderQueue_AllTransparent,
                    SortingCriteria.CommonTransparent, PerObjectData.MotionVectors, m_Settings.enableInstanceBatch)
                : CameraRendererLists.Create(context.cullingResults, context.camera, lightMode, InfinityRenderQueue.k_RenderQueue_AllTransparent,
                    SortingCriteria.CommonTransparent, PerObjectData.MotionVectors, m_Settings.enableInstanceBatch);
            RGRendererListHandle rendererListHandle = m_RGBuilder.CreateRendererList(listParameters);

            {
                RGRasterPassHandle<TranslucentColorPassData> passHandle = m_RGBuilder.AddRasterPass<TranslucentColorPassData>(ProfilingSampler.Get(samplerId));
                passHandle.EnablePassCulling(false);
                passHandle.SetColorAttachment(sceneColorHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                if (writeReactive)
                    passHandle.SetColorAttachment(reactiveMaskHandle, 1, initializeReactive ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetColorAttachment(motionTextureHandle, writeReactive ? 2 : 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref TranslucentColorPassData passData = ref passHandle.GetPassData();
                passData.meshDrawListHandle = passHandle.UseMeshDrawList(meshDrawListHandle);
                passData.rendererListHandle = passHandle.UseRendererList(rendererListHandle);
                passData.writeReactive = writeReactive;
                passData.bindFog = hasFog;
                passData.bindAerial = hasAerial;
                passData.bindPyramid = hasPyramid;
                passData.fogMaxDistance = fogMaxDistance;
                passData.aerialDistance = aerialDistance;
                if (hasFog)
                {
                    passData.volumetricFogHandle = passHandle.ReadTexture(volumetricFogHandle);
                }
                if (hasAerial)
                {
                    passData.aerialLutHandle = passHandle.ReadTexture(aerialLutHandle);
                }
                if (hasPyramid)
                {
                    passData.colorPyramidHandle = passHandle.ReadTexture(colorPyramidHandle);
                }
                passData.translucentDepthHandle = passHandle.ReadTexture(translucentDepthHandle);

                passHandle.SetExecuteFunc((in TranslucentColorPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.ReactiveKeyword, passData.writeReactive);
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.VolumetricFogKeyword, passData.bindFog);
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.AerialKeyword, passData.bindAerial);
                    cmdEncoder.SetKeyword(TranslucentPassUtilityData.RefractionKeyword, passData.bindPyramid);
                    cmdEncoder.SetGlobalFloat(PassShaderIDs.VolFog_MaxDistance, passData.fogMaxDistance);
                    cmdEncoder.SetGlobalFloat(PassShaderIDs.VolFog_AerialDistance, passData.aerialDistance);
                    if (passData.bindFog)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.VolumetricFogBuffer, passData.volumetricFogHandle);
                    }
                    if (passData.bindAerial)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, passData.aerialLutHandle);
                    }
                    if (passData.bindPyramid)
                    {
                        cmdEncoder.SetGlobalTexture(InfinityShaderIDs.ColorPyramidBuffer, passData.colorPyramidHandle);
                    }
                    cmdEncoder.SetGlobalTexture(InfinityShaderIDs.TranslucentDepthBuffer, passData.translucentDepthHandle);
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawListHandle);
                    cmdEncoder.DrawRendererList(passData.rendererListHandle);
                });
            }
        }
    }
}
