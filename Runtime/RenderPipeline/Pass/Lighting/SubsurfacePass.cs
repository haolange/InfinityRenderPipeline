using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;
using InfinityTech.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class SubsurfacePassUtilityData
    {
        internal static string TextureName = "SubsurfaceTexture";
        internal static int SSS_ResolutionID = Shader.PropertyToID("SSS_Resolution");
        internal static int SSS_NumSamplesID = Shader.PropertyToID("SSS_NumSamples");
        internal static int SSS_ProfileCountID = Shader.PropertyToID("SSS_ProfileCount");
        internal static int SRV_LightingTextureID = Shader.PropertyToID("SRV_LightingTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_DiffusionProfilesID = Shader.PropertyToID("SRV_DiffusionProfiles");
        internal static int UAV_SubsurfaceTextureID = Shader.PropertyToID("UAV_SubsurfaceTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct SubsurfacePassData
        {
            public int numSamples;
            public int profileCount;
            public int kernel, composeKernel;
            public Matrix4x4 projection, inverseProjection;
            public int2 resolution;
            public ComputeShader subsurfaceShader;
            public RGBufferRef profileBuffer;
            public RGTextureRef lightingTexture;
            public RGTextureRef sceneColor;
            public RGTextureRef depthTexture;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef subsurfaceTexture;
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
                var next = m_ResourcePool.GetBuffer(new BufferDescriptor(uploadCount, Marshal.SizeOf<FDiffusionProfileRecord>()) { name = "DiffusionProfiles" });
                var nextRecords = new FDiffusionProfileRecord[uploadCount];
                for (int i = 0; i < profileCount; ++i) nextRecords[i] = profiles[i] != null ? profiles[i].ToRecord() : default;
                try { next.buffer.SetData(nextRecords); }
                catch { m_ResourcePool.ReleaseBuffer(next); throw; }
                if (m_DiffusionProfileBuffer.buffer != null) m_ResourcePool.ReleaseBuffer(m_DiffusionProfileBuffer);
                m_DiffusionProfileBuffer = next; m_DiffusionProfileRecords = nextRecords;
            }
            return profileCount;
        }

        void ComputeBurleySubsurface(RenderContext renderContext, Camera camera)
        {
            if (!m_ActiveFrameState.subsurfaceEnabled) return;
            var volume = ActiveVolumeStack.GetComponent<SubsurfaceScattering>();
            int count = EnsureDiffusionProfileBuffer(pipelineAsset.diffusionProfiles);
            int2 size = m_ActiveFrameState.dimensions.internalSize;
            int2 halfSize = (size + 1) / 2;
            var descriptor = SSRPassUtilityData.CreateDescriptor(halfSize.x, halfSize.y, "SubsurfaceDiffuse", GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, 2);
            RGTextureRef filtered = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SubsurfaceBuffer, descriptor);
            RGTextureRef color = m_RGScoper.QueryTexture(InfinityShaderIDs.OpaqueSceneColorBuffer);
            using (var pass = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface)))
            {
                ref var data = ref pass.GetPassData<SubsurfacePassData>();
                data.subsurfaceShader = shaders.subsurfaceShader;
                data.kernel = shaders.subsurfaceShader.FindKernel("BurleySubsurfaceCS");
                data.composeKernel = shaders.subsurfaceShader.FindKernel("ComposeSubsurfaceCS");
                data.projection = m_CameraUniform.matrix_FlipYJitterProj;
                data.inverseProjection = m_CameraUniform.matrix_InvFlipYJitterProj;
                data.resolution = size;
                data.profileCount = count;
                data.numSamples = volume.numSamples.value;
                data.profileBuffer = pass.ReadBuffer(m_RGBuilder.ImportBuffer(m_DiffusionProfileBuffer));
                data.lightingTexture = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.ScatterDiffuseBuffer));
                data.depthTexture = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer));
                data.gBufferB = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB));
                data.gBufferC = pass.ReadTexture(m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC));
                data.subsurfaceTexture = pass.WriteTexture(filtered);
                pass.ReadTexture(color); data.sceneColor = pass.WriteTexture(color);
                pass.SetExecuteFunc((in SubsurfacePassData d, in RGComputeEncoder commands) =>
                {
                    var shader = d.subsurfaceShader;
                    commands.SetComputeVectorParam(shader, SubsurfacePassUtilityData.SSS_ResolutionID, new Vector4(d.resolution.x,d.resolution.y,1f/d.resolution.x,1f/d.resolution.y));
                    commands.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_Proj"), d.projection);
                    commands.SetComputeMatrixParam(shader, Shader.PropertyToID("Matrix_InvProj"), d.inverseProjection);
                    commands.SetComputeIntParam(shader, SubsurfacePassUtilityData.SSS_NumSamplesID, d.numSamples);
                    commands.SetComputeIntParam(shader, SubsurfacePassUtilityData.SSS_ProfileCountID, d.profileCount);
                    BindSubsurface(commands, shader, d.kernel, d);
                    commands.SetComputeTextureParam(shader, d.kernel, SubsurfacePassUtilityData.UAV_SubsurfaceTextureID, d.subsurfaceTexture);
                    commands.DispatchCompute(shader, d.kernel, ((d.resolution.x+1)/2+7)/8, ((d.resolution.y+1)/2+7)/8, 1);
                    BindSubsurface(commands, shader, d.composeKernel, d);
                    commands.SetComputeTextureParam(shader, d.composeKernel, Shader.PropertyToID("SRV_FilteredSubsurface"), d.subsurfaceTexture);
                    commands.SetComputeTextureParam(shader, d.composeKernel, Shader.PropertyToID("UAV_SceneColor"), d.sceneColor);
                    commands.DispatchCompute(shader, d.composeKernel, (d.resolution.x+7)/8, (d.resolution.y+7)/8, 1);
                });
            }
            MarkFeatureProduced(EFrameFeature.SubsurfaceScattering);
        }

        static void BindSubsurface(in RGComputeEncoder commands, ComputeShader shader, int kernel, in SubsurfacePassData d)
        {
            commands.SetComputeBufferParam(shader,kernel,SubsurfacePassUtilityData.SRV_DiffusionProfilesID,d.profileBuffer);
            commands.SetComputeTextureParam(shader,kernel,SubsurfacePassUtilityData.SRV_LightingTextureID,d.lightingTexture);
            commands.SetComputeTextureParam(shader,kernel,SubsurfacePassUtilityData.SRV_DepthTextureID,d.depthTexture);
            commands.SetComputeTextureParam(shader,kernel,SubsurfacePassUtilityData.SRV_GBufferTextureBID,d.gBufferB);
            commands.SetComputeTextureParam(shader,kernel,SubsurfacePassUtilityData.SRV_GBufferTextureCID,d.gBufferC);
        }
    }
}
