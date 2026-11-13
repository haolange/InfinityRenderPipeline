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
    internal static class SubsurfacePassUtilityData
    {
        internal static readonly int SRV_FilteredSubsurfaceID = Shader.PropertyToID("SRV_FilteredSubsurface");
        internal static readonly string TextureName = "SubsurfaceTexture";
        internal static readonly int SSS_ResolutionID = Shader.PropertyToID("SSS_Resolution");
        internal static readonly int SSS_NumSamplesID = Shader.PropertyToID("SSS_NumSamples");
        internal static readonly int SSS_ProfileCountID = Shader.PropertyToID("SSS_ProfileCount");
        internal static readonly int SRV_DiffusionProfilesID = Shader.PropertyToID("SRV_DiffusionProfiles");
        internal static readonly int UAV_SubsurfaceTextureID = Shader.PropertyToID("UAV_SubsurfaceTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct SubsurfacePassData
        {
            public int numSamples;
            public int profileCount;
            public ComputeKernel burleyKernel, composeKernel;
            public int2 resolution;
            public RGBufferHandle profileBuffer;
            public RGTextureHandle lightingTexture;
            public RGTextureHandle sceneColor;
            public RGTextureHandle depthTexture;
            public RGTextureHandle gBufferB;
            public RGTextureHandle gBufferC;
            public RGTextureHandle subsurfaceTexture;
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
                if (m_DiffusionProfileBuffer.buffer != null)
                {
                    m_ResourcePool.ReleaseBuffer(m_DiffusionProfileBuffer);
                }
                m_DiffusionProfileBuffer = next;
                m_DiffusionProfileRecords = nextRecords;
            }
            return profileCount;
        }

        void ComputeBurleySubsurface(in CameraRecordContext context)
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
            RGTextureHandle filtered = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.SubsurfaceBuffer, descriptor);
            RGTextureHandle color = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            using (var pass = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface)))
            {
                ref var data = ref pass.GetPassData();
                data.burleyKernel = kernels.burleySubsurface;
                data.composeKernel = kernels.composeSubsurface;
                data.resolution = size;
                data.profileCount = count;
                data.numSamples = volume.numSamples.value;
                data.profileBuffer = pass.ReadBuffer(m_RGBuilder.ImportBuffer(m_DiffusionProfileBuffer));
                data.lightingTexture = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer));
                data.depthTexture = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                data.gBufferB = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferB));
                data.gBufferC = pass.ReadTexture(m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.GBufferC));
                data.subsurfaceTexture = pass.WriteTexture(filtered);
                pass.ReadTexture(color);
                data.sceneColor = pass.WriteTexture(color);
                pass.SetExecuteFunc((in SubsurfacePassData d, in RGComputeEncoder commands) =>
                {
                    var shader = d.burleyKernel.shader;
                    commands.SetComputeVectorParam(shader, SubsurfacePassUtilityData.SSS_ResolutionID, new Vector4(d.resolution.x,d.resolution.y,1f/d.resolution.x,1f/d.resolution.y));
                    commands.SetComputeIntParam(shader, SubsurfacePassUtilityData.SSS_NumSamplesID, d.numSamples);
                    commands.SetComputeIntParam(shader, SubsurfacePassUtilityData.SSS_ProfileCountID, d.profileCount);
                    BindSubsurface(commands, shader, d.burleyKernel.index, d);
                    commands.SetComputeTextureParam(shader, d.burleyKernel.index, SubsurfacePassUtilityData.UAV_SubsurfaceTextureID, d.subsurfaceTexture);
                    commands.DispatchCompute(shader, d.burleyKernel.index, d.burleyKernel.GroupsX((d.resolution.x+1)/2), d.burleyKernel.GroupsY((d.resolution.y+1)/2), 1);
                    BindSubsurface(commands, shader, d.composeKernel.index, d);
                    commands.SetComputeTextureParam(shader, d.composeKernel.index, SubsurfacePassUtilityData.SRV_FilteredSubsurfaceID, d.subsurfaceTexture);
                    commands.SetComputeTextureParam(shader, d.composeKernel.index, PassShaderIDs.UAV_SceneColor, d.sceneColor);
                    commands.DispatchCompute(shader, d.composeKernel.index, d.composeKernel.GroupsX(d.resolution.x), d.composeKernel.GroupsY(d.resolution.y), 1);
                });
            }
            context.features.MarkProduced(EFrameFeature.SubsurfaceScattering);
        }

        static void BindSubsurface(in RGComputeEncoder commands, ComputeShader shader, int kernel, in SubsurfacePassData d)
        {
            commands.SetComputeBufferParam(shader,kernel,SubsurfacePassUtilityData.SRV_DiffusionProfilesID,d.profileBuffer);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_LightingTexture,d.lightingTexture);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_DepthTexture,d.depthTexture);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_GBufferTextureB,d.gBufferB);
            commands.SetComputeTextureParam(shader,kernel,PassShaderIDs.SRV_GBufferTextureC,d.gBufferC);
        }
    }
}
