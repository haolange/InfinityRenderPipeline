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
        internal static int SSS_ScatteringDistanceID = Shader.PropertyToID("SSS_ScatteringDistance");
        internal static int SSS_SurfaceAlbedoID = Shader.PropertyToID("SSS_SurfaceAlbedo");
        internal static int SSS_NumSamplesID = Shader.PropertyToID("SSS_NumSamples");
        internal static int SSS_MaxRadiusID = Shader.PropertyToID("SSS_MaxRadius");
        internal static int SSS_ProfileCountID = Shader.PropertyToID("SSS_ProfileCount");
        internal static int SSS_HasOverrideID = Shader.PropertyToID("SSS_HasOverride");
        internal static int SRV_LightingTextureID = Shader.PropertyToID("SRV_LightingTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
        internal static int SRV_GBufferTextureCID = Shader.PropertyToID("SRV_GBufferTextureC");
        internal static int SRV_DiffusionProfilesID = Shader.PropertyToID("SRV_DiffusionProfiles");
        internal static int UAV_SubsurfaceTextureID = Shader.PropertyToID("UAV_SubsurfaceTexture");
    }

    public partial class InfinityRenderPipeline
    {
        struct SubsurfacePassData
        {
            public float scatteringDistance;
            public Color surfaceAlbedo;
            public int numSamples;
            public float maxRadius;
            public int profileCount;
            public int hasOverride;
            public int2 resolution;
            public ComputeShader subsurfaceShader;
            public GraphicsBuffer profileBuffer;
            public RGTextureRef lightingTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef gBufferC;
            public RGTextureRef subsurfaceTexture;
        }

        void EnsureDiffusionProfileBuffer(DiffusionProfile[] profiles, SubsurfaceScattering volume, out int profileCount, out float distance, out Color albedo, out float maxRadius, out int hasOverride)
        {
            profileCount = profiles != null ? profiles.Length : 0;
            int uploadCount = math.max(1, profileCount);
            int stride = Marshal.SizeOf<FDiffusionProfileRecord>();
            if (m_DiffusionProfileBuffer == null || m_DiffusionProfileCapacity < uploadCount)
            {
                m_DiffusionProfileBuffer?.Release();
                m_DiffusionProfileCapacity = uploadCount;
                m_DiffusionProfileBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_DiffusionProfileCapacity, stride);
            }

            var records = new FDiffusionProfileRecord[uploadCount];
            if (profileCount == 0)
            {
                records[0].scatterAlbedoDistance = new Vector4(0.8f, 0.4f, 0.3f, 1.0f);
                records[0].parameters = new Vector4(5.0f, 0.0f, 0.0f, 0.0f);
            }
            else
            {
                for (int i = 0; i < profileCount; ++i)
                {
                    records[i] = profiles[i] != null ? profiles[i].ToRecord() : default;
                }
            }

            m_DiffusionProfileBuffer.SetData(records);

            distance = records[0].scatterAlbedoDistance.w;
            albedo = new Color(records[0].scatterAlbedoDistance.x, records[0].scatterAlbedoDistance.y, records[0].scatterAlbedoDistance.z, 1.0f);
            maxRadius = records[0].parameters.x;
            hasOverride = 0;
            if (volume != null && GraphicsUtility.VolumeHasOverrides(volume))
            {
                hasOverride = 1;
                if (volume.ScatteringDistance.overrideState)
                {
                    distance = volume.ScatteringDistance.value;
                }

                if (volume.SurfaceAlbedo.overrideState)
                {
                    albedo = volume.SurfaceAlbedo.value;
                }

                if (volume.MaxRadius.overrideState)
                {
                    maxRadius = volume.MaxRadius.value;
                }
            }
        }

        void ComputeBurleySubsurface(RenderContext renderContext, Camera camera)
        {
            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.subsurfaceShader, "BurleySubsurfaceCS"))
            {
                return;
            }

            var sss = ActiveVolumeStack.GetComponent<SubsurfaceScattering>();
            EnsureDiffusionProfileBuffer(pipelineAsset.diffusionProfiles, sss, out int profileCount, out float distance, out Color albedo, out float maxRadius, out int hasOverride);
            int numSamples = DiffusionProfile.SampleCount(pipelineAsset.subsurfaceQuality);
            if (sss != null && sss.NumSamples.overrideState)
            {
                numSamples = sss.NumSamples.value;
            }

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor subsurfaceTextureDsc = new TextureDescriptor(width, height);
            {
                subsurfaceTextureDsc.name = SubsurfacePassUtilityData.TextureName;
                subsurfaceTextureDsc.dimension = TextureDimension.Tex2D;
                subsurfaceTextureDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                subsurfaceTextureDsc.depthBufferBits = EDepthBits.None;
                subsurfaceTextureDsc.enableRandomWrite = true;
            }
            RGTextureRef subsurfaceTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.SubsurfaceBuffer, subsurfaceTextureDsc);

            RGTextureRef lightingTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.LightingBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);
            RGTextureRef gBufferA = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferA);
            RGTextureRef gBufferB = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferB);
            RGTextureRef gBufferC = m_RGScoper.QueryTexture(InfinityShaderIDs.GBufferC);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface)))
            {
                ref SubsurfacePassData passData = ref passRef.GetPassData<SubsurfacePassData>();
                passData.scatteringDistance = distance;
                passData.surfaceAlbedo = albedo;
                passData.numSamples = numSamples;
                passData.maxRadius = maxRadius;
                passData.profileCount = profileCount;
                passData.hasOverride = hasOverride;
                passData.resolution = new int2(width, height);
                passData.subsurfaceShader = pipelineAsset.subsurfaceShader;
                passData.profileBuffer = m_DiffusionProfileBuffer;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.gBufferC = passRef.ReadTexture(gBufferC);
                passData.subsurfaceTexture = passRef.WriteTexture(subsurfaceTexture);

                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in SubsurfacePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeVectorParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_ScatteringDistanceID, passData.scatteringDistance);
                    cmdEncoder.SetComputeVectorParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_SurfaceAlbedoID, (Vector4)passData.surfaceAlbedo);
                    cmdEncoder.SetComputeIntParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_NumSamplesID, passData.numSamples);
                    cmdEncoder.SetComputeFloatParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_MaxRadiusID, passData.maxRadius);
                    cmdEncoder.SetComputeIntParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_ProfileCountID, passData.profileCount);
                    cmdEncoder.SetComputeIntParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_HasOverrideID, passData.hasOverride);
                    cmdEncoder.SetComputeBufferParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_DiffusionProfilesID, passData.profileBuffer);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_LightingTextureID, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_GBufferTextureCID, passData.gBufferC);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.UAV_SubsurfaceTextureID, passData.subsurfaceTexture);
                    cmdEncoder.DispatchCompute(passData.subsurfaceShader, 0, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
