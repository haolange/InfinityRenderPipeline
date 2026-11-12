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

    public partial class InfinityRenderPipeline
    {
        struct TranslucentDepthPassData
        {
            public RGMeshDrawListHandle meshDrawList;
            public RGRendererListHandle rendererList;
        }

        struct TranslucentColorPassData
        {
            public RGMeshDrawListHandle meshDrawList;
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

        void RasterTranslucentDepth(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle depthMeshDrawList)
        {
            TextureDescriptor translucentDepthDsc = new TextureDescriptor(context.dimensions.internalSize.x, context.dimensions.internalSize.y, ETextureSizingDomain.Internal);
            {
                translucentDepthDsc.name = TranslucentPassUtilityData.DepthTextureName;
                translucentDepthDsc.dimension = TextureDimension.Tex2D;
                translucentDepthDsc.depthBufferBits = EDepthBits.Depth32;
            }
            RGTextureHandle translucentDepthTexture = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.TranslucentDepthBuffer, translucentDepthDsc);

            RGRendererListHandle depthRendererList = m_RGBuilder.CreateRendererList(CameraRendererLists.Create(context.cullingResults, context.camera, InfinityPassIDs.RasterTranslucentDepthPass,
                InfinityRenderQueue.k_RenderQueue_AllTransparent, SortingCriteria.QuantizedFrontToBack, PerObjectData.None, pipelineAsset));

            using (var passRef = m_RGBuilder.AddRasterPass<TranslucentDepthPassData>(ProfilingSampler.Get(CustomSamplerId.RasterTranslucentDepth)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetDepthStencilAttachment(translucentDepthTexture, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store, EDepthAccess.Write);

                ref TranslucentDepthPassData passData = ref passRef.GetPassData();
                passData.meshDrawList = passRef.UseMeshDrawList(depthMeshDrawList);
                passData.rendererList = passRef.UseRendererList(depthRendererList);

                passRef.SetExecuteFunc((in TranslucentDepthPassData passData, in RGRasterEncoder cmdEncoder) =>
                {
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }

        void RasterTranslucentT0(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle meshDrawList)
        {
            RasterTranslucentColor(context, view, meshDrawList, InfinityPassIDs.RasterTranslucentT0Pass, CustomSamplerId.RasterTranslucentT0, bindFog: true, bindPyramid: false);
        }

        void RasterTranslucentT1(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle meshDrawList)
        {
            RasterTranslucentColor(context, view, meshDrawList, InfinityPassIDs.RasterTranslucentT1Pass, CustomSamplerId.RasterTranslucentT1, bindFog: false, bindPyramid: true);
        }

        void RasterTranslucentT2(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle meshDrawList)
        {
            RasterTranslucentColor(context, view, meshDrawList, InfinityPassIDs.RasterTranslucentT2Pass, CustomSamplerId.RasterTranslucentT2, bindFog: false, bindPyramid: false);
        }

        // MeshComponent content is drawn back to front before the native renderer list in the same pass. The two lists are
        // sorted separately: there is no distance order between MeshComponent and native translucent renderers.
        void RasterTranslucentColor(in CameraRecordContext context, in MeshView view, in RGMeshDrawListHandle meshDrawList, ShaderTagId lightMode, CustomSamplerId samplerId, bool bindFog, bool bindPyramid)
        {
            RGTextureHandle sceneColor = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer);
            RGTextureHandle depthTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            bool writeReactive = context.features.ShouldRecord(EFrameFeature.TAA);
            bool initializeReactive = false;
            RGTextureHandle reactiveMask = default;
            if (writeReactive && !m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.ReactiveMaskBuffer, out reactiveMask))
            {
                var size = context.dimensions.internalSize;
                var descriptor = new TextureDescriptor(size.x, size.y, ETextureSizingDomain.Internal)
                {
                    name = "ReactiveMask", colorFormat = GraphicsFormat.R8_UNorm,
                    depthBufferBits = EDepthBits.None, dimension = TextureDimension.Tex2D,
                    clearColor = Color.clear, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                reactiveMask = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.ReactiveMaskBuffer, descriptor);
                initializeReactive = true;
            }
            RGTextureHandle motionTexture = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionBuffer);

            RGTextureHandle volumetricFog = default;
            RGTextureHandle aerialLut = default;
            RGTextureHandle colorPyramid = default;
            RGTextureHandle translucentDepth = default;
            bool hasFog = bindFog && m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.VolumetricFogBuffer, out volumetricFog);
            bool hasAerial = bindFog && m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.AtmosphereAerialPerspectiveLUT, out aerialLut);
            bool hasPyramid = bindPyramid && m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.ColorPyramidBuffer, out colorPyramid);
            bool hasTranslucentDepth = m_RGBuilder.scoper.TryQueryTexture(InfinityShaderIDs.TranslucentDepthBuffer, out translucentDepth);

            float fogMaxDistance = context.volumes.volumetricFog.MaxDistance.value;
            float aerialDistance = context.atmosphere.aerialPerspectiveDistance;

            // T2 also draws materials that only have Unity's unlit pass.
            RendererListParams listParameters = samplerId == CustomSamplerId.RasterTranslucentT2
                ? CameraRendererLists.CreateWithUnlit(context.cullingResults, context.camera, lightMode, InfinityRenderQueue.k_RenderQueue_AllTransparent,
                    SortingCriteria.CommonTransparent, PerObjectData.MotionVectors, pipelineAsset)
                : CameraRendererLists.Create(context.cullingResults, context.camera, lightMode, InfinityRenderQueue.k_RenderQueue_AllTransparent,
                    SortingCriteria.CommonTransparent, PerObjectData.MotionVectors, pipelineAsset);
            RGRendererListHandle rendererList = m_RGBuilder.CreateRendererList(listParameters);

            using (var passRef = m_RGBuilder.AddRasterPass<TranslucentColorPassData>(ProfilingSampler.Get(samplerId)))
            {
                passRef.EnablePassCulling(false);
                passRef.SetColorAttachment(sceneColor, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                if (writeReactive)
                    passRef.SetColorAttachment(reactiveMask, 1, initializeReactive ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(motionTexture, writeReactive ? 2 : 1, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetColorAttachment(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.MotionMetadataBuffer), writeReactive ? 3 : 2, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);

                ref TranslucentColorPassData passData = ref passRef.GetPassData();
                passData.meshDrawList = passRef.UseMeshDrawList(meshDrawList);
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
                    cmdEncoder.SetGlobalFloat(PassShaderIDs.VolFog_MaxDistance, passData.fogMaxDistance);
                    cmdEncoder.SetGlobalFloat(PassShaderIDs.VolFog_AerialDistance, passData.aerialDistance);
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
                    cmdEncoder.SubmitMeshDrawList(passData.meshDrawList);
                    cmdEncoder.DrawRendererList(passData.rendererList);
                });
            }
        }
    }
}
