using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SubsurfacePassUtilityData
    {
        internal static readonly string IrradianceTextureName = "SubsurfaceIrradiance";
        internal static readonly int SRV_DiffusionProfilesID = Shader.PropertyToID("SRV_DiffusionProfiles");
        internal static readonly int SRV_SubsurfaceIrradianceID = Shader.PropertyToID("SRV_SubsurfaceIrradiance");
        internal static readonly int SubsurfaceResolutionID = Shader.PropertyToID("_SubsurfaceResolution");
        internal static readonly int SubsurfaceSamplingID = Shader.PropertyToID("_SubsurfaceSampling");
    }

    // Screen-space scattering of skin, two full-screen draws admitted by the subsurface stencil bit (Shaders/Library/Subsurface.hlsl):
    // extract each skin pixel's diffuse into a temporary irradiance target, then blur it within one profile and blend it
    // over Scene Color. It runs after forward shading, the last writer of the skin's color and diffuse share.
    internal sealed class SubsurfacePass : System.IDisposable
    {
        readonly RGBuilder m_RGBuilder;
        readonly InfinityRenderPipelineResources m_Resources;
        readonly InfinityRenderPipelineAsset.Settings m_Settings;
        readonly ResourcePool m_ResourcePool;
        // The pass-owned diffusion profile buffer and the records it was last uploaded from.
        BufferHandle m_DiffusionProfileBufferHandle;
        DiffusionProfileRecord[] m_DiffusionProfileRecords = System.Array.Empty<DiffusionProfileRecord>();

        internal SubsurfacePass(RGBuilder graph, InfinityRenderPipelineResources resources, in InfinityRenderPipelineAsset.Settings settings, ResourcePool resourcePool)
        {
            m_RGBuilder = graph;
            m_Resources = resources;
            m_Settings = settings;
            m_ResourcePool = resourcePool;
        }

        // Returns the diffusion buffer to the pool; the pipeline disposes this pass before the render context disposes the pool.
        public void Dispose()
        {
            BufferHandle bufferHandle = m_DiffusionProfileBufferHandle;
            m_DiffusionProfileBufferHandle = default;
            m_DiffusionProfileRecords = System.Array.Empty<DiffusionProfileRecord>();
            if (bufferHandle.buffer != null)
            {
                m_ResourcePool.ReleaseBuffer(bufferHandle);
            }
        }

        struct SubsurfacePassData
        {
            public int shaderPass;
            public Vector4 resolution;
            public Vector4 sampling;
            public RGBufferHandle profileBufferHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle sidebandHandle;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle irradianceTextureHandle;
        }

        int EnsureDiffusionProfileBuffer(DiffusionProfile[] profiles)
        {
            int profileCount = profiles != null ? profiles.Length : 0;
            int uploadCount = math.max(1, profileCount);
            bool changed = m_DiffusionProfileRecords.Length != uploadCount;
            for (int i = 0; i < uploadCount && !changed; ++i)
            {
                var record = i < profileCount && profiles[i] != null ? profiles[i].ToRecord() : default;
                changed = !record.scatterAlbedoDistance.Equals(m_DiffusionProfileRecords[i].scatterAlbedoDistance)
                    || !record.parameters.Equals(m_DiffusionProfileRecords[i].parameters);
            }
            if (changed)
            {
                var next = m_ResourcePool.GetBuffer(new BufferDescriptor(uploadCount, Marshal.SizeOf<DiffusionProfileRecord>()) { name = "DiffusionProfiles" });
                var nextRecords = new DiffusionProfileRecord[uploadCount];
                for (int i = 0; i < profileCount; ++i)
                {
                    nextRecords[i] = profiles[i] != null ? profiles[i].ToRecord() : default;
                }
                try
                {
                    next.buffer.SetData(nextRecords);
                }
                catch
                {
                    m_ResourcePool.ReleaseBuffer(next);
                    throw;
                }
                if (m_DiffusionProfileBufferHandle.buffer != null)
                {
                    m_ResourcePool.ReleaseBuffer(m_DiffusionProfileBufferHandle);
                }
                m_DiffusionProfileBufferHandle = next;
                m_DiffusionProfileRecords = nextRecords;
            }
            return profileCount;
        }

        internal void RasterSubsurfaceScattering(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering))
            {
                return;
            }
            int profileCount = EnsureDiffusionProfileBuffer(m_Settings.diffusionProfiles);
            int2 size = context.dimensions.internalSize;
            var resolution = new Vector4(size.x, size.y, 1.0f / size.x, 1.0f / size.y);
            var sampling = new Vector4(context.volumes.subsurfaceScattering.numSamples.value, profileCount, 0, 0);

            var irradianceDescriptor = new TextureDescriptor(size.x, size.y, ETextureSizingDomain.Internal)
            {
                name = SubsurfacePassUtilityData.IrradianceTextureName,
                dimension = TextureDimension.Tex2D,
                colorFormat = EffectivePipelineConfiguration.SceneColorFormat,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            RGTextureHandle irradianceTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SubsurfaceIrradianceBuffer, irradianceDescriptor);
            RGTextureHandle lightingTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureHandle depthTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle sidebandHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SidebandBuffer);

            using (new RGProfilingScope(m_RGBuilder, ProfilingSampler.Get(CustomSamplerId.RasterSubsurfaceScattering)))
            {
                // The stencil test reads the depth-stencil attachment; the shaders sample its depth through the same handle.
                {
                    RGRasterPassHandle<SubsurfacePassData> passHandle = m_RGBuilder.AddRasterPass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.RasterSubsurfaceExtract));
                    passHandle.SetColorAttachment(irradianceTextureHandle, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);
                    passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
                    ref SubsurfacePassData passData = ref passHandle.GetPassData();
                    passData = default;
                    passData.shaderPass = m_Resources.blitPasses.subsurfaceExtract;
                    passData.resolution = resolution;
                    passData.sampling = sampling;
                    passData.depthTextureHandle = depthTextureHandle;
                    passData.sidebandHandle = passHandle.ReadTexture(sidebandHandle);
                    passData.lightingTextureHandle = passHandle.ReadTexture(lightingTextureHandle);
                    passHandle.SetExecuteFunc((in SubsurfacePassData passData, in RGRasterEncoder cmdEncoder) =>
                    {
                        cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_Sideband, passData.sidebandHandle);
                        cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_LightingTexture, passData.lightingTextureHandle);
                        cmdEncoder.DrawProcedural(Matrix4x4.identity, GraphicsUtility.BlitMaterial, passData.shaderPass, MeshTopology.Triangles, 3);
                    });
                }
                {
                    RGRasterPassHandle<SubsurfacePassData> passHandle = m_RGBuilder.AddRasterPass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.RasterSubsurfaceScatter));
                    passHandle.SetColorAttachment(lightingTextureHandle, 0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    passHandle.SetDepthStencilAttachment(depthTextureHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, EDepthAccess.ReadOnly);
                    ref SubsurfacePassData passData = ref passHandle.GetPassData();
                    passData = default;
                    passData.shaderPass = m_Resources.blitPasses.subsurfaceScatter;
                    passData.resolution = resolution;
                    passData.sampling = sampling;
                    passData.profileBufferHandle = passHandle.ReadBuffer(m_RGBuilder.ImportBuffer(m_DiffusionProfileBufferHandle));
                    passData.depthTextureHandle = depthTextureHandle;
                    passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                    passData.sidebandHandle = passHandle.ReadTexture(sidebandHandle);
                    passData.irradianceTextureHandle = passHandle.ReadTexture(irradianceTextureHandle);
                    passHandle.SetExecuteFunc((in SubsurfacePassData passData, in RGRasterEncoder cmdEncoder) =>
                    {
                        cmdEncoder.SetGlobalVector(SubsurfacePassUtilityData.SubsurfaceResolutionID, passData.resolution);
                        cmdEncoder.SetGlobalVector(SubsurfacePassUtilityData.SubsurfaceSamplingID, passData.sampling);
                        cmdEncoder.SetGlobalBuffer(SubsurfacePassUtilityData.SRV_DiffusionProfilesID, passData.profileBufferHandle);
                        cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_DepthTexture, passData.depthTextureHandle);
                        cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_GBufferTextureB, passData.gBufferBHandle);
                        cmdEncoder.SetGlobalTexture(PassShaderIDs.SRV_Sideband, passData.sidebandHandle);
                        cmdEncoder.SetGlobalTexture(SubsurfacePassUtilityData.SRV_SubsurfaceIrradianceID, passData.irradianceTextureHandle);
                        cmdEncoder.DrawProcedural(Matrix4x4.identity, GraphicsUtility.BlitMaterial, passData.shaderPass, MeshTopology.Triangles, 3);
                    });
                }
            }
            context.features.MarkProduced(EFrameFeature.SubsurfaceScattering);
        }
    }
}
