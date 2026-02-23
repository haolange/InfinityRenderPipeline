using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class PostProcessingPassUtilityData
    {
        internal static string TextureName = "PostProcessTexture";
        internal static string BloomTextureName = "BloomTexture";
        internal static int PP_ResolutionID = Shader.PropertyToID("PP_Resolution");
        internal static int PP_BloomIntensityID = Shader.PropertyToID("PP_BloomIntensity");
        internal static int PP_BloomThresholdID = Shader.PropertyToID("PP_BloomThreshold");
        internal static int PP_VignetteIntensityID = Shader.PropertyToID("PP_VignetteIntensity");
        internal static int PP_ChromaticAberrationID = Shader.PropertyToID("PP_ChromaticAberration");
        internal static int PP_FilmGrainIntensityID = Shader.PropertyToID("PP_FilmGrainIntensity");
        internal static int PP_FrameIndexID = Shader.PropertyToID("PP_FrameIndex");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static int SRV_CombineLUTTextureID = Shader.PropertyToID("SRV_CombineLUTTexture");
        internal static int SRV_VolumetricFogTextureID = Shader.PropertyToID("SRV_VolumetricFogTexture");
        internal static int SRV_DepthTextureID = Shader.PropertyToID("SRV_DepthTexture");
        internal static int UAV_PostProcessTextureID = Shader.PropertyToID("UAV_PostProcessTexture");

        // Bloom pipeline textures and parameters
        internal static int SRV_BloomSourceID = Shader.PropertyToID("SRV_BloomSource");
        internal static int UAV_BloomTargetID = Shader.PropertyToID("UAV_BloomTarget");
        internal static int BloomMipSizeID = Shader.PropertyToID("BloomMipSize");

        internal static int KernelBloomDownsample = 0;
        internal static int KernelBloomUpsample = 1;
        internal static int KernelCombine = 2;

        internal static int MaxBloomMips = 6;
    }

    public partial class InfinityRenderPipeline
    {
        struct PostProcessingPassData
        {
            public int2 resolution;
            public float bloomIntensity;
            public float bloomThreshold;
            public float vignetteIntensity;
            public float chromaticAberration;
            public float filmGrainIntensity;
            public int frameIndex;
            public ComputeShader postProcessingShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef volumetricFogTexture;
            public RGTextureRef depthTexture;
            public RGTextureRef bloomTexture;
            public RGTextureRef postProcessTexture;
        }

        void ComputePostProcessing(RenderContext renderContext, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor postProcessDsc = new TextureDescriptor(width, height);
            {
                postProcessDsc.name = PostProcessingPassUtilityData.TextureName;
                postProcessDsc.dimension = TextureDimension.Tex2D;
                postProcessDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                postProcessDsc.depthBufferBits = EDepthBits.None;
                postProcessDsc.enableRandomWrite = true;
            }
            RGTextureRef postProcessTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, postProcessDsc);

            // Bloom texture with mip chain for downsample/upsample
            int bloomWidth = Mathf.Max(1, width >> 1);
            int bloomHeight = Mathf.Max(1, height >> 1);
            TextureDescriptor bloomDsc = new TextureDescriptor(bloomWidth, bloomHeight);
            {
                bloomDsc.name = PostProcessingPassUtilityData.BloomTextureName;
                bloomDsc.dimension = TextureDimension.Tex2D;
                bloomDsc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                bloomDsc.depthBufferBits = EDepthBits.None;
                bloomDsc.enableRandomWrite = true;
                bloomDsc.useMipMap = true;
                bloomDsc.autoGenerateMips = false;
            }
            RGTextureRef bloomTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.BloomBuffer, bloomDsc);

            // Use SuperResolution output if available, otherwise use lighting buffer
            RGTextureRef sceneColorTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.SuperResolutionBuffer);
            RGTextureRef volumetricFogTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.VolumetricFogBuffer);
            RGTextureRef depthTexture = m_RGScoper.QueryTexture(InfinityShaderIDs.DepthBuffer);

            //Add PostProcessingPass
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<PostProcessingPassData>(ProfilingSampler.Get(CustomSamplerId.ComputePostProcessing)))
            {
                //Setup Phase
                ref PostProcessingPassData passData = ref passRef.GetPassData<PostProcessingPassData>();
                passData.resolution = new int2(width, height);
                passData.bloomIntensity = 0.5f;
                passData.bloomThreshold = 1.0f;
                passData.vignetteIntensity = 0.3f;
                passData.chromaticAberration = 0.0f;
                passData.filmGrainIntensity = 0.0f;
                passData.frameIndex = Time.frameCount;
                passData.postProcessingShader = pipelineAsset.postProcessingShader;
                passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                passData.volumetricFogTexture = passRef.ReadTexture(volumetricFogTexture);
                passData.depthTexture = passRef.ReadTexture(depthTexture);
                passData.bloomTexture = passRef.WriteTexture(bloomTexture);
                passData.postProcessTexture = passRef.WriteTexture(postProcessTexture);

                //Execute Phase
                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in PostProcessingPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    if (passData.postProcessingShader == null) return;

                    // Set common uniforms
                    cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomIntensityID, passData.bloomIntensity);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomThresholdID, passData.bloomThreshold);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_VignetteIntensityID, passData.vignetteIntensity);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_ChromaticAberrationID, passData.chromaticAberration);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_FilmGrainIntensityID, passData.filmGrainIntensity);
                    cmdEncoder.SetComputeIntParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_FrameIndexID, passData.frameIndex);

                    int bloomWidth = Mathf.Max(1, passData.resolution.x >> 1);
                    int bloomHeight = Mathf.Max(1, passData.resolution.y >> 1);
                    int numBloomMips = Mathf.Min(PostProcessingPassUtilityData.MaxBloomMips, (int)math.floor(math.log2(math.max(bloomWidth, bloomHeight))));

                    // === Bloom Downsample Chain ===
                    // Mip 0: downsample from scene color to bloom mip 0 (with threshold)
                    cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(bloomWidth, bloomHeight, 1.0f / bloomWidth, 1.0f / bloomHeight));
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, 0);
                    cmdEncoder.DispatchCompute(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, Mathf.CeilToInt(bloomWidth / 8.0f), Mathf.CeilToInt(bloomHeight / 8.0f), 1);

                    // Subsequent downsample mips
                    int prevWidth = bloomWidth;
                    int prevHeight = bloomHeight;
                    for (int mip = 1; mip < numBloomMips; ++mip)
                    {
                        int mipWidth = Mathf.Max(1, prevWidth >> 1);
                        int mipHeight = Mathf.Max(1, prevHeight >> 1);

                        cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTexture, mip - 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, Mathf.CeilToInt(mipWidth / 8.0f), Mathf.CeilToInt(mipHeight / 8.0f), 1);

                        prevWidth = mipWidth;
                        prevHeight = mipHeight;
                    }

                    // === Bloom Upsample Chain ===
                    // Upsample from lowest mip back up, accumulating into each mip level
                    for (int mip = numBloomMips - 2; mip >= 0; --mip)
                    {
                        int mipWidth = Mathf.Max(1, bloomWidth >> mip);
                        int mipHeight = Mathf.Max(1, bloomHeight >> mip);

                        cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomUpsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTexture, mip + 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomUpsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomUpsample, Mathf.CeilToInt(mipWidth / 8.0f), Mathf.CeilToInt(mipHeight / 8.0f), 1);
                    }

                    // === Final Combine ===
                    // Apply bloom + volumetric fog + tone mapping + vignette + film grain + sRGB
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_VolumetricFogTextureID, passData.volumetricFogTexture);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_DepthTextureID, passData.depthTexture);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.UAV_PostProcessTextureID, passData.postProcessTexture);
                    cmdEncoder.DispatchCompute(passData.postProcessingShader, PostProcessingPassUtilityData.KernelCombine, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
