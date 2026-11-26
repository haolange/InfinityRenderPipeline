using System.Runtime.InteropServices;
using UnityEngine;
using InfinityTech.Core;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_SubsurfaceParameters Shaders/Pass/Lighting/Compute_BurleySubsurface.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct SubsurfaceParameters
    {
        public Vector4 resolution;
        public int numSamples;
        public int profileCount;
        public int padding0;
        public int padding1;
    }

    internal static class SubsurfacePassUtilityData
    {
        internal static readonly int CBV_SubsurfaceParametersID = Shader.PropertyToID("CBV_SubsurfaceParameters");
        internal static readonly int SRV_FilteredSubsurfaceID = Shader.PropertyToID("SRV_FilteredSubsurface");
        internal static readonly string TextureName = "SubsurfaceTexture";
        internal static readonly int SRV_DiffusionProfilesID = Shader.PropertyToID("SRV_DiffusionProfiles");
        internal static readonly int UAV_SubsurfaceTextureID = Shader.PropertyToID("UAV_SubsurfaceTexture");
    }

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
            public RGConstantsHandle parametersHandle;
            public ComputeKernel kernel;
            public LocalKeyword alphaKeyword;
            public bool alphaMode;
            public int2 resolution;
            public RGBufferHandle profileBufferHandle;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle sceneColorHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle surfaceMetadataHandle;
            public RGTextureHandle customDataHandle;
            public RGTextureHandle subsurfaceTextureHandle;
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

        internal void ComputeBurleySubsurface(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering))
            {
                return;
            }
            EffectivePipelineConfiguration config = m_Settings.config;
            bool alphaMode = config.subsurfaceLightingMode == ESubsurfaceLightingMode.SceneColorAlpha;
            var volume = context.volumes.subsurfaceScattering;
            int count = EnsureDiffusionProfileBuffer(m_Settings.diffusionProfiles);
            int2 size = context.dimensions.internalSize;
            int2 halfSize = (size + 1) / 2;
            var descriptor = SSRPassUtilityData.CreateDescriptor(halfSize.x, halfSize.y, "SubsurfaceDiffuse", config.scatterDiffuseFormat, FilterMode.Point, 2);
            RGTextureHandle filteredHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SubsurfaceBuffer, descriptor);
            RGTextureHandle colorHandle = m_RGBuilder.scoper.QueryTexture(context.sceneColor.opaque);
            RGTextureHandle lightingSourceHandle = alphaMode ? colorHandle : m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer);
            RGTextureHandle depthHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureHandle gBufferBHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureHandle metadataHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.SurfaceMetadataBuffer);
            RGTextureHandle customDataHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.CustomDataBuffer);
            var parameters = new SubsurfaceParameters
            {
                resolution = new Vector4(size.x, size.y, 1f / size.x, 1f / size.y),
                numSamples = volume.numSamples.value,
                profileCount = count
            };

            using (new RGProfilingScope(m_RGBuilder, ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface)))
            {
                {
                    RGComputePassHandle<SubsurfacePassData> passHandle = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface));
                    ref SubsurfacePassData passData = ref passHandle.GetPassData();
                    passData.kernel = m_Resources.kernels.burleySubsurface;
                    passData.alphaKeyword = m_Resources.kernels.subsurfaceAlpha;
                    passData.alphaMode = alphaMode;
                    passData.resolution = size;
                    passData.parametersHandle = passHandle.WriteConstants(parameters);
                    passData.profileBufferHandle = passHandle.ReadBuffer(m_RGBuilder.ImportBuffer(m_DiffusionProfileBufferHandle));
                    passData.lightingTextureHandle = passHandle.ReadTexture(lightingSourceHandle);
                    passData.depthTextureHandle = passHandle.ReadTexture(depthHandle);
                    passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                    passData.surfaceMetadataHandle = passHandle.ReadTexture(metadataHandle);
                    passData.customDataHandle = passHandle.ReadTexture(customDataHandle);
                    passData.subsurfaceTextureHandle = passHandle.WriteTexture(filteredHandle);
                    passHandle.SetExecuteFunc((in SubsurfacePassData d, in RGComputeEncoder commands) =>
                    {
                        var shader = d.kernel.shader;
                        commands.SetKeyword(shader, d.alphaKeyword, d.alphaMode);
                        commands.SetComputeConstantBufferParam(shader, SubsurfacePassUtilityData.CBV_SubsurfaceParametersID, d.parametersHandle);
                        BindSubsurface(commands, shader, d.kernel.index, d);
                        commands.SetComputeTextureParam(shader, d.kernel.index, PassShaderIDs.SRV_LightingTexture, d.lightingTextureHandle);
                        commands.SetComputeTextureParam(shader, d.kernel.index, SubsurfacePassUtilityData.UAV_SubsurfaceTextureID, d.subsurfaceTextureHandle);
                        commands.DispatchCompute(shader, d.kernel.index, d.kernel.GroupsX((d.resolution.x + 1) / 2), d.kernel.GroupsY((d.resolution.y + 1) / 2), 1);
                    });
                }
                {
                    RGComputePassHandle<SubsurfacePassData> passHandle = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface));
                    ref SubsurfacePassData passData = ref passHandle.GetPassData();
                    passData.kernel = m_Resources.kernels.composeSubsurface;
                    passData.alphaKeyword = m_Resources.kernels.subsurfaceAlpha;
                    passData.alphaMode = alphaMode;
                    passData.resolution = size;
                    passData.parametersHandle = passHandle.WriteConstants(parameters);
                    passData.profileBufferHandle = passHandle.ReadBuffer(m_RGBuilder.ImportBuffer(m_DiffusionProfileBufferHandle));
                    if (!alphaMode)
                    {
                        passData.lightingTextureHandle = passHandle.ReadTexture(lightingSourceHandle);
                    }
                    passData.depthTextureHandle = passHandle.ReadTexture(depthHandle);
                    passData.gBufferBHandle = passHandle.ReadTexture(gBufferBHandle);
                    passData.surfaceMetadataHandle = passHandle.ReadTexture(metadataHandle);
                    passData.customDataHandle = passHandle.ReadTexture(customDataHandle);
                    passData.subsurfaceTextureHandle = passHandle.ReadTexture(filteredHandle);
                    passHandle.ReadTexture(colorHandle);
                    passData.sceneColorHandle = passHandle.WriteTexture(colorHandle);
                    passHandle.SetExecuteFunc((in SubsurfacePassData d, in RGComputeEncoder commands) =>
                    {
                        var shader = d.kernel.shader;
                        commands.SetKeyword(shader, d.alphaKeyword, d.alphaMode);
                        commands.SetComputeConstantBufferParam(shader, SubsurfacePassUtilityData.CBV_SubsurfaceParametersID, d.parametersHandle);
                        BindSubsurface(commands, shader, d.kernel.index, d);
                        if (!d.alphaMode)
                        {
                            commands.SetComputeTextureParam(shader, d.kernel.index, PassShaderIDs.SRV_LightingTexture, d.lightingTextureHandle);
                        }
                        commands.SetComputeTextureParam(shader, d.kernel.index, SubsurfacePassUtilityData.SRV_FilteredSubsurfaceID, d.subsurfaceTextureHandle);
                        commands.SetComputeTextureParam(shader, d.kernel.index, PassShaderIDs.UAV_SceneColor, d.sceneColorHandle);
                        commands.DispatchCompute(shader, d.kernel.index, d.kernel.GroupsX(d.resolution.x), d.kernel.GroupsY(d.resolution.y), 1);
                    });
                }
            }
            context.features.MarkProduced(EFrameFeature.SubsurfaceScattering);
        }

        static void BindSubsurface(in RGComputeEncoder commands, ComputeShader shader, int kernel, in SubsurfacePassData d)
        {
            commands.SetComputeBufferParam(shader, kernel, SubsurfacePassUtilityData.SRV_DiffusionProfilesID, d.profileBufferHandle);
            commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_DepthTexture, d.depthTextureHandle);
            commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_GBufferTextureB, d.gBufferBHandle);
            commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_SurfaceMetadata, d.surfaceMetadataHandle);
            commands.SetComputeTextureParam(shader, kernel, PassShaderIDs.SRV_CustomData, d.customDataHandle);
        }
    }
}
