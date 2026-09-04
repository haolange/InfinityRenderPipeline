using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class PostProcessingPassUtilityData
    {
        internal static string TextureName = "PostProcessTexture";
        internal static string BloomTextureName = "BloomTexture";
        internal static string ExposureEVName = "ExposureEV";
        internal static string ExposureHistogramName = "ExposureHistogram";
        internal static int PP_ResolutionID = Shader.PropertyToID("PP_Resolution");
        internal static int PP_BloomIntensityID = Shader.PropertyToID("PP_BloomIntensity");
        internal static int PP_BloomThresholdID = Shader.PropertyToID("PP_BloomThreshold");
        internal static int PP_VignetteIntensityID = Shader.PropertyToID("PP_VignetteIntensity");
        internal static int PP_VignetteSmoothnessID = Shader.PropertyToID("PP_VignetteSmoothness");
        internal static int PP_FilmGrainIntensityID = Shader.PropertyToID("PP_FilmGrainIntensity");
        internal static int PP_FilmGrainResponseID = Shader.PropertyToID("PP_FilmGrainResponse");
        internal static int PP_FrameIndexID = Shader.PropertyToID("PP_FrameIndex");
        internal static int PP_ExposureMultiplierID = Shader.PropertyToID("PP_ExposureMultiplier");
        internal static int PP_AutoExposureID = Shader.PropertyToID("PP_AutoExposure");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
        internal static int UAV_PostProcessTextureID = Shader.PropertyToID("UAV_PostProcessTexture");
        internal static int SRV_CombineLUTID = Shader.PropertyToID("SRV_CombineLUT");
        internal static int SRV_ExposureEVID = Shader.PropertyToID("SRV_ExposureEV");

        internal static int SRV_BloomSourceID = Shader.PropertyToID("SRV_BloomSource");
        internal static int SRV_BloomTextureID = Shader.PropertyToID("SRV_BloomTexture");
        internal static int UAV_BloomTargetID = Shader.PropertyToID("UAV_BloomTarget");
        internal static int BloomMipSizeID = Shader.PropertyToID("BloomMipSize");
        internal static int PP_BloomPrefilterID = Shader.PropertyToID("PP_BloomPrefilter");

        internal static int UAV_ExposureHistogramID = Shader.PropertyToID("UAV_ExposureHistogram");
        internal static int SRV_ExposureHistoryEVID = Shader.PropertyToID("SRV_ExposureHistoryEV");
        internal static int UAV_ExposureEVID = Shader.PropertyToID("UAV_ExposureEV");
        internal static int PP_ExposureHistogramRangeID = Shader.PropertyToID("PP_ExposureHistogramRange");
        internal static int PP_ExposureLowPercentileID = Shader.PropertyToID("PP_ExposureLowPercentile");
        internal static int PP_ExposureHighPercentileID = Shader.PropertyToID("PP_ExposureHighPercentile");
        internal static int PP_ExposureAdaptID = Shader.PropertyToID("PP_ExposureAdapt");
        internal static int PP_ExposureResetID = Shader.PropertyToID("PP_ExposureReset");

        internal static int KernelBloomDownsample = 0;
        internal static int KernelBloomUpsample = 1;
        internal static int KernelCombine = 2;
        internal static int KernelExposureClear = 3;
        internal static int KernelExposureHistogram = 4;
        internal static int KernelExposureReduce = 5;

        internal static int MaxBloomMips = 6;
    }

    public partial class InfinityRenderPipeline
    {
        struct BloomPassData
        {
            public int2 resolution;
            public float bloomThreshold;
            public float exposureMultiplier;
            public float autoExposure;
            public ComputeShader postProcessingShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef bloomTexture;
            public RGTextureRef exposureEV;
        }

        struct PostCombinePassData
        {
            public int2 resolution;
            public float bloomIntensity;
            public float vignetteIntensity;
            public float vignetteSmoothness;
            public float filmGrainIntensity;
            public float filmGrainResponse;
            public float exposureMultiplier;
            public float autoExposure;
            public int frameIndex;
            public ComputeShader postProcessingShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef bloomTexture;
            public RGTextureRef combineLUT;
            public RGTextureRef exposureEV;
            public RGTextureRef postProcessTexture;
        }

        struct ExposurePassData
        {
            public int2 resolution;
            public float lowPercentile;
            public float highPercentile;
            public float adapt;
            public int resetHistory;
            public ComputeShader postProcessingShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef exposureHistory;
            public RGTextureRef exposureEV;
            public RGBufferRef histogram;
        }

        struct PostVolumeState
        {
            public float exposureMultiplier;
            public float autoExposure;
            public float bloomThreshold;
            public float bloomIntensity;
            public float vignetteIntensity;
            public float vignetteSmoothness;
            public float filmGrainIntensity;
            public float filmGrainResponse;
            public bool recordAutoExposure;
            public float adaptSpeed;
            public float lowPercentile;
            public float highPercentile;
        }

        static readonly string[] PostProcessingKernelNames =
        {
            "BloomDownsample", "BloomUpsample", "FinalCombine",
            "ExposureClear", "ExposureHistogram", "ExposureReduce"
        };

        void ComputePostProcessing(RenderContext renderContext, Camera camera, CameraFrameState frameState)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.PostProcess);

            if (!GraphicsUtility.HasRequiredKernels(pipelineAsset.postProcessingShader, PostProcessingKernelNames))
            {
                throw new InvalidOperationException("InfinityRP: Display/PostProcess is required but postProcessingShader kernels are missing.");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.AntiAliasingBuffer, out RGTextureRef sceneColorTexture) &&
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.SuperResolutionBuffer, out sceneColorTexture) &&
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer, out sceneColorTexture))
            {
                throw new InvalidOperationException("InfinityRP: PostProcess has no scene color input (AntiAliasing/SuperResolution/FoggedSceneColor).");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.CombineLookupTexture, out RGTextureRef combineLUT))
            {
                throw new InvalidOperationException("InfinityRP: PostProcess requires CombineLookupTexture.");
            }

            PostVolumeState volumes = ResolvePostVolumeState(frameState);
            frameState.exposureState.evCompensation = ExposureUtility.ResolveCpuEvCompensation(frameState.volumeStack.GetComponent<Exposure>());

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            TextureDescriptor postProcessDsc = new TextureDescriptor(width, height);
            postProcessDsc.name = PostProcessingPassUtilityData.TextureName;
            postProcessDsc.dimension = TextureDimension.Tex2D;
            postProcessDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            postProcessDsc.depthBufferBits = EDepthBits.None;
            postProcessDsc.enableRandomWrite = true;
            postProcessDsc.filterMode = FilterMode.Bilinear;
            postProcessDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef postProcessTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, postProcessDsc);

            int bloomWidth = Mathf.Max(1, width >> 1);
            int bloomHeight = Mathf.Max(1, height >> 1);
            TextureDescriptor bloomDsc = new TextureDescriptor(bloomWidth, bloomHeight);
            bloomDsc.name = PostProcessingPassUtilityData.BloomTextureName;
            bloomDsc.dimension = TextureDimension.Tex2D;
            bloomDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            bloomDsc.depthBufferBits = EDepthBits.None;
            bloomDsc.enableRandomWrite = true;
            bloomDsc.useMipMap = true;
            bloomDsc.autoGenerateMips = false;
            bloomDsc.filterMode = FilterMode.Bilinear;
            bloomDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef bloomTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.BloomBuffer, bloomDsc);

            RGTextureRef exposureEV;
            using (new RGProfilingScope(m_RGBuilder, ProfilingSampler.Get(CustomSamplerId.PostProcessing)))
            {
                if (volumes.recordAutoExposure)
                {
                    exposureEV = ComputeAutoExposure(camera, frameState, sceneColorTexture, volumes);
                }
                else
                {
                    exposureEV = ImportIdleExposureEV(frameState);
                }

                ComputeBloom(camera, sceneColorTexture, bloomTexture, exposureEV, volumes);
                ComputePostCombine(camera, sceneColorTexture, bloomTexture, combineLUT, exposureEV, postProcessTexture, volumes);
            }

            MarkFeatureProduced(EFrameFeature.PostProcess);
            ComputeDebugView(camera);
        }

        static PostVolumeState ResolvePostVolumeState(CameraFrameState frameState)
        {
            VolumeStack stack = frameState.volumeStack;
            Exposure exposure = stack.GetComponent<Exposure>();
            Bloom bloom = stack.GetComponent<Bloom>();
            Vignette vignette = stack.GetComponent<Vignette>();
            FilmGrain filmGrain = stack.GetComponent<FilmGrain>();

            PostVolumeState state = default;
            bool exposureActive = ExposureUtility.VolumeIsActive(exposure);
            state.recordAutoExposure = ExposureUtility.ShouldRecordAuto(exposure);
            state.exposureMultiplier = exposureActive
                ? ExposureUtility.EvToMultiplier(exposure.evCompensation.value)
                : 1.0f;
            state.autoExposure = state.recordAutoExposure ? 1.0f : 0.0f;
            if (state.recordAutoExposure)
            {
                state.adaptSpeed = exposure.adaptSpeed.value;
                state.lowPercentile = exposure.lowPercentile.value;
                state.highPercentile = exposure.highPercentile.value;
            }

            if (GraphicsUtility.VolumeHasOverrides(bloom))
            {
                state.bloomThreshold = bloom.threshold.value;
                state.bloomIntensity = bloom.intensity.value;
            }

            if (GraphicsUtility.VolumeHasOverrides(vignette))
            {
                state.vignetteIntensity = vignette.intensity.value;
                state.vignetteSmoothness = vignette.smoothness.value;
            }
            else
            {
                state.vignetteSmoothness = 0.4f;
            }

            if (GraphicsUtility.VolumeHasOverrides(filmGrain))
            {
                state.filmGrainIntensity = filmGrain.intensity.value;
                state.filmGrainResponse = filmGrain.response.value;
            }
            else
            {
                state.filmGrainResponse = 0.8f;
            }

            return state;
        }

        static TextureDescriptor CreateExposureEVDescriptor()
        {
            TextureDescriptor evDsc = new TextureDescriptor(1, 1);
            evDsc.name = PostProcessingPassUtilityData.ExposureEVName;
            evDsc.dimension = TextureDimension.Tex2D;
            evDsc.colorFormat = GraphicsFormat.R32_SFloat;
            evDsc.depthBufferBits = EDepthBits.None;
            evDsc.enableRandomWrite = true;
            evDsc.filterMode = FilterMode.Point;
            evDsc.wrapMode = TextureWrapMode.Clamp;
            return evDsc;
        }

        RGTextureRef ImportIdleExposureEV(CameraFrameState frameState)
        {
            TextureDescriptor evDsc = CreateExposureEVDescriptor();
            FTextureRef evRead = frameState.historyCache.GetTexture(InfinityShaderIDs.ExposureEVBuffer, evDsc);
            RGTextureRef exposureRead = m_RGBuilder.ImportTexture(evRead);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.ExposureEVBuffer, exposureRead);
            return exposureRead;
        }

        RGTextureRef ComputeAutoExposure(Camera camera, CameraFrameState frameState, in RGTextureRef sceneColorTexture, in PostVolumeState volumes)
        {
            TextureDescriptor evDsc = CreateExposureEVDescriptor();

            FTextureRef evWrite = frameState.historyCache.GetWriteTexture(InfinityShaderIDs.ExposureEVBuffer, evDsc);
            FTextureRef evRead = frameState.historyCache.GetTexture(InfinityShaderIDs.ExposureEVBuffer, evDsc, out bool created);
            RGTextureRef exposureWrite = m_RGBuilder.ImportTexture(evWrite);
            RGTextureRef exposureRead = m_RGBuilder.ImportTexture(evRead);
            m_RGScoper.RegisterTexture(InfinityShaderIDs.ExposureEVBuffer, exposureWrite);

            BufferDescriptor histogramDsc = new BufferDescriptor(ExposureUtility.HistogramBinCount, sizeof(uint), ComputeBufferType.Default);
            histogramDsc.name = PostProcessingPassUtilityData.ExposureHistogramName;
            RGBufferRef histogram = m_RGBuilder.CreateBuffer(histogramDsc);

            float dt = math.max(Time.unscaledDeltaTime, 1.0f / 120.0f);
            float adapt = 1.0f - math.exp(-volumes.adaptSpeed * dt);

            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ExposurePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeExposure)))
            {
                ref ExposurePassData passData = ref passRef.GetPassData<ExposurePassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.lowPercentile = volumes.lowPercentile;
                passData.highPercentile = volumes.highPercentile;
                passData.adapt = adapt;
                passData.resetHistory = created ? 1 : 0;
                passData.postProcessingShader = pipelineAsset.postProcessingShader;
                passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                passData.exposureHistory = passRef.ReadTexture(exposureRead);
                passData.exposureEV = passRef.WriteTexture(exposureWrite);
                passData.histogram = passRef.WriteBuffer(histogram);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in ExposurePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.postProcessingShader;
                    cmdEncoder.SetComputeVectorParam(shader, PostProcessingPassUtilityData.PP_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeVectorParam(shader, PostProcessingPassUtilityData.PP_ExposureHistogramRangeID, new Vector4(
                        ExposureUtility.HistogramMinLog,
                        ExposureUtility.HistogramMaxLog,
                        1.0f / (ExposureUtility.HistogramMaxLog - ExposureUtility.HistogramMinLog),
                        0.0f));
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_ExposureLowPercentileID, passData.lowPercentile);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_ExposureHighPercentileID, passData.highPercentile);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_ExposureAdaptID, passData.adapt);
                    cmdEncoder.SetComputeIntParam(shader, PostProcessingPassUtilityData.PP_ExposureResetID, passData.resetHistory);

                    cmdEncoder.BeginSample("ExposureClear");
                    cmdEncoder.SetComputeBufferParam(shader, PostProcessingPassUtilityData.KernelExposureClear, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogram);
                    cmdEncoder.DispatchCompute(shader, PostProcessingPassUtilityData.KernelExposureClear, 4, 1, 1);
                    cmdEncoder.EndSample("ExposureClear");

                    cmdEncoder.BeginSample("ExposureHistogram");
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelExposureHistogram, PostProcessingPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeBufferParam(shader, PostProcessingPassUtilityData.KernelExposureHistogram, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogram);
                    cmdEncoder.DispatchCompute(shader, PostProcessingPassUtilityData.KernelExposureHistogram, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                    cmdEncoder.EndSample("ExposureHistogram");

                    cmdEncoder.BeginSample("ExposureReduce");
                    cmdEncoder.SetComputeBufferParam(shader, PostProcessingPassUtilityData.KernelExposureReduce, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogram);
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelExposureReduce, PostProcessingPassUtilityData.SRV_ExposureHistoryEVID, passData.exposureHistory);
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelExposureReduce, PostProcessingPassUtilityData.UAV_ExposureEVID, passData.exposureEV);
                    cmdEncoder.DispatchCompute(shader, PostProcessingPassUtilityData.KernelExposureReduce, 1, 1, 1);
                    cmdEncoder.EndSample("ExposureReduce");
                });
            }

            frameState.historyCache.MarkProduced(InfinityShaderIDs.ExposureEVBuffer);
            return exposureWrite;
        }

        void ComputeBloom(Camera camera, in RGTextureRef sceneColorTexture, in RGTextureRef bloomTexture, in RGTextureRef exposureEV, in PostVolumeState volumes)
        {
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<BloomPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBloom)))
            {
                ref BloomPassData passData = ref passRef.GetPassData<BloomPassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.bloomThreshold = volumes.bloomThreshold;
                passData.exposureMultiplier = volumes.exposureMultiplier;
                passData.autoExposure = volumes.autoExposure;
                passData.postProcessingShader = pipelineAsset.postProcessingShader;
                passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                passData.bloomTexture = passRef.WriteTexture(bloomTexture);
                passData.exposureEV = passRef.ReadTexture(exposureEV);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in BloomPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomThresholdID, passData.bloomThreshold);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_ExposureMultiplierID, passData.exposureMultiplier);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_AutoExposureID, passData.autoExposure);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.SRV_ExposureEVID, passData.exposureEV);

                    int bloomWidth = Mathf.Max(1, passData.resolution.x >> 1);
                    int bloomHeight = Mathf.Max(1, passData.resolution.y >> 1);
                    int numBloomMips = Mathf.Min(PostProcessingPassUtilityData.MaxBloomMips, (int)math.floor(math.log2(math.max(bloomWidth, bloomHeight))));

                    cmdEncoder.BeginSample("BloomDownsample");
                    cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(bloomWidth, bloomHeight, 1.0f / bloomWidth, 1.0f / bloomHeight));
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomPrefilterID, 1.0f);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, 0);
                    cmdEncoder.DispatchCompute(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomDownsample, Mathf.CeilToInt(bloomWidth / 8.0f), Mathf.CeilToInt(bloomHeight / 8.0f), 1);

                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomPrefilterID, 0.0f);
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
                    cmdEncoder.EndSample("BloomDownsample");

                    cmdEncoder.BeginSample("BloomUpsample");
                    for (int mip = numBloomMips - 2; mip >= 0; --mip)
                    {
                        int mipWidth = Mathf.Max(1, bloomWidth >> mip);
                        int mipHeight = Mathf.Max(1, bloomHeight >> mip);

                        cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomUpsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTexture, mip + 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomUpsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, PostProcessingPassUtilityData.KernelBloomUpsample, Mathf.CeilToInt(mipWidth / 8.0f), Mathf.CeilToInt(mipHeight / 8.0f), 1);
                    }
                    cmdEncoder.EndSample("BloomUpsample");
                });
            }
        }

        void ComputePostCombine(
            Camera camera,
            in RGTextureRef sceneColorTexture,
            in RGTextureRef bloomTexture,
            in RGTextureRef combineLUT,
            in RGTextureRef exposureEV,
            in RGTextureRef postProcessTexture,
            in PostVolumeState volumes)
        {
            using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<PostCombinePassData>(ProfilingSampler.Get(CustomSamplerId.ComputePostCombine)))
            {
                ref PostCombinePassData passData = ref passRef.GetPassData<PostCombinePassData>();
                passData.resolution = new int2(camera.pixelWidth, camera.pixelHeight);
                passData.bloomIntensity = volumes.bloomIntensity;
                passData.vignetteIntensity = volumes.vignetteIntensity;
                passData.vignetteSmoothness = volumes.vignetteSmoothness;
                passData.filmGrainIntensity = volumes.filmGrainIntensity;
                passData.filmGrainResponse = volumes.filmGrainResponse;
                passData.exposureMultiplier = volumes.exposureMultiplier;
                passData.autoExposure = volumes.autoExposure;
                passData.frameIndex = Time.frameCount;
                passData.postProcessingShader = pipelineAsset.postProcessingShader;
                passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                passData.bloomTexture = passRef.ReadTexture(bloomTexture);
                passData.combineLUT = passRef.ReadTexture(combineLUT);
                passData.postProcessTexture = passRef.WriteTexture(postProcessTexture);
                passData.exposureEV = passRef.ReadTexture(exposureEV);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in PostCombinePassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    ComputeShader shader = passData.postProcessingShader;
                    cmdEncoder.SetComputeVectorParam(shader, PostProcessingPassUtilityData.PP_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y));
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_BloomIntensityID, passData.bloomIntensity);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_VignetteIntensityID, passData.vignetteIntensity);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_VignetteSmoothnessID, passData.vignetteSmoothness);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_FilmGrainIntensityID, passData.filmGrainIntensity);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_FilmGrainResponseID, passData.filmGrainResponse);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_ExposureMultiplierID, passData.exposureMultiplier);
                    cmdEncoder.SetComputeFloatParam(shader, PostProcessingPassUtilityData.PP_AutoExposureID, passData.autoExposure);
                    cmdEncoder.SetComputeIntParam(shader, PostProcessingPassUtilityData.PP_FrameIndexID, passData.frameIndex);

                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_BloomTextureID, passData.bloomTexture);
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_CombineLUTID, passData.combineLUT);
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.UAV_PostProcessTextureID, passData.postProcessTexture);
                    cmdEncoder.SetComputeTextureParam(shader, PostProcessingPassUtilityData.KernelCombine, PostProcessingPassUtilityData.SRV_ExposureEVID, passData.exposureEV);

                    cmdEncoder.DispatchCompute(shader, PostProcessingPassUtilityData.KernelCombine, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                });
            }
        }
    }
}
