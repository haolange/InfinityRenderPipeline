using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

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
        internal static int SRV_LightingTextureID = Shader.PropertyToID("SRV_LightingTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int SRV_GBufferTextureAID = Shader.PropertyToID("SRV_GBufferTextureA");
        internal static int SRV_GBufferTextureBID = Shader.PropertyToID("SRV_GBufferTextureB");
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
            public int2 resolution;
            public ComputeShader subsurfaceShader;
            public RGTextureRef lightingTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef gBufferA;
            public RGTextureRef gBufferB;
            public RGTextureRef subsurfaceTexture;
        }

        void ComputeBurleySubsurface(RenderContext renderContext, Camera camera)
        {
            var stack = VolumeManager.instance.stack;
            var sss = stack.GetComponent<SubsurfaceScattering>();
            if (sss == null) return;

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

            //Add SubsurfacePass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<SubsurfacePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBurleySubsurface)))
            {
                //Setup Phase
                ref SubsurfacePassData passData = ref passRef.GetPassData<SubsurfacePassData>();
                passData.scatteringDistance = sss.ScatteringDistance.value;
                passData.surfaceAlbedo = sss.SurfaceAlbedo.value;
                passData.numSamples = sss.NumSamples.value;
                passData.maxRadius = sss.MaxRadius.value;
                passData.resolution = new int2(width, height);
                passData.subsurfaceShader = pipelineAsset.subsurfaceShader;
                passData.lightingTexture = passRef.ReadTexture(lightingTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.gBufferA = passRef.ReadTexture(gBufferA);
                passData.gBufferB = passRef.ReadTexture(gBufferB);
                passData.subsurfaceTexture = passRef.WriteTexture(subsurfaceTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.EnableAsyncCompute(true);
                passRef.SetExecuteFunc((in SubsurfacePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.subsurfaceShader == null) return;

                    cmdEncoder.SetComputeVectorParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_ScatteringDistanceID, passData.scatteringDistance);
                    cmdEncoder.SetComputeVectorParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_SurfaceAlbedoID, (Vector4)passData.surfaceAlbedo);
                    cmdEncoder.SetComputeIntParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_NumSamplesID, passData.numSamples);
                    cmdEncoder.SetComputeFloatParam(passData.subsurfaceShader, SubsurfacePassUtilityData.SSS_MaxRadiusID, passData.maxRadius);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_LightingTextureID, passData.lightingTexture);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_GBufferTextureAID, passData.gBufferA);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.SRV_GBufferTextureBID, passData.gBufferB);
                    cmdEncoder.SetComputeTextureParam(passData.subsurfaceShader, 0, SubsurfacePassUtilityData.UAV_SubsurfaceTextureID, passData.subsurfaceTexture);
                    cmdEncoder.DispatchCompute(passData.subsurfaceShader, 0, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
