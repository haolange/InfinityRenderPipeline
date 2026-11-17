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

    public partial class InfinityRenderPipeline
    {
        struct SubsurfacePassData
        {
            public RGConstantsHandle parametersHandle;
            public ComputeKernel burleyKernel, composeKernel;
            public int2 resolution;
            public RGBufferHandle profileBufferHandle;
            public RGTextureHandle lightingTextureHandle;
            public RGTextureHandle sceneColorHandle;
            public RGTextureHandle depthTextureHandle;
            public RGTextureHandle gBufferBHandle;
            public RGTextureHandle gBufferCHandle;
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

        void ComputeBurleySubsurface(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.SubsurfaceScattering))
            {
                return;
            }
            var volume = context.volumes.subsurfaceScattering;
            int count = EnsureDiffusionProfileBuffer(m_Settings.diffusionProfiles);
            int2 size = context.dimensions.internalSize;
            int2 halfSize = (size + 1) / 2;
            var descriptor = SSRPassUtilityData.CreateDescriptor(halfSize.x, halfSize.y, "SubsurfaceDiffuse", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, 2);
            RGTextureHandle filteredHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SubsurfaceBuffer, descriptor);
            RGTextureHandle colorHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            {
                RGComputePassHandle<SubsurfacePassData> passHandle = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface));
                ref SubsurfacePassData passData = ref passHandle.GetPassData();
                passData.burleyKernel = kernels.burleySubsurface;
                passData.composeKernel = kernels.composeSubsurface;
                passData.resolution = size;
                passData.parametersHandle = passHandle.WriteConstants(new SubsurfaceParameters
                {
                    resolution = new Vector4(size.x, size.y, 1f / size.x, 1f / size.y),
                    numSamples = volume.numSamples.value,
                    profileCount = count
                });
                passData.profileBufferHandle = passHandle.ReadBuffer(m_RGBuilder.ImportBuffer(m_DiffusionProfileBufferHandle));
                passData.lightingTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer));
                passData.depthTextureHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                passData.gBufferBHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                passData.gBufferCHandle = passHandle.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC));
                passData.subsurfaceTextureHandle = passHandle.WriteTexture(filteredHandle);
                passHandle.ReadTexture(colorHandle);
                passData.sceneColorHandle = passHandle.WriteTexture(colorHandle);
                passHandle.SetExecuteFunc((in SubsurfacePassData d, in RGComputeEncoder commands) =>
                {
                    var shader = d.burleyKernel.shader;
                    commands.SetComputeConstantBufferParam(shader, SubsurfacePassUtilityData.CBV_SubsurfaceParametersID, d.parametersHandle);
                    BindSubsurface(commands, shader, d.burleyKernel.index, d);
                    commands.SetComputeTextureParam(shader, d.burleyKernel.index, SubsurfacePassUtilityData.UAV_SubsurfaceTextureID, d.subsurfaceTextureHandle);
                    commands.DispatchCompute(shader, d.burleyKernel.index, d.burleyKernel.GroupsX((d.resolution.x+1)/2), d.burleyKernel.GroupsY((d.resolution.y+1)/2), 1);
                    BindSubsurface(commands, shader, d.composeKernel.index, d);
                    commands.SetComputeTextureParam(shader, d.composeKernel.index, SubsurfacePassUtilityData.SRV_FilteredSubsurfaceID, d.subsurfaceTextureHandle);
                    commands.SetComputeTextureParam(shader, d.composeKernel.index, PassShaderIDs.UAV_SceneColor, d.sceneColorHandle);
                    commands.DispatchCompute(shader, d.composeKernel.index, d.composeKernel.GroupsX(d.resolution.x), d.composeKernel.GroupsY(d.resolution.y), 1);
                });
            }
            context.features.MarkProduced(EFrameFeature.SubsurfaceScattering);
        }

        static void BindSubsurface(in RGComputeEncoder commands, ComputeShader shader, int kernel, in SubsurfacePassData d)
        {
            commands.SetComputeBufferParam(shader,kernel,SubsurfacePassUtilityData.SRV_DiffusionProfilesID,d.profileBufferHandle);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_LightingTexture,d.lightingTextureHandle);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_DepthTexture,d.depthTextureHandle);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_GBufferTextureB,d.gBufferBHandle);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_GBufferTextureC,d.gBufferCHandle);
        }
    }
}
