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
        internal static int PP_BloomScatterID = Shader.PropertyToID("PP_BloomScatter");
        internal static int PP_VignetteIntensityID = Shader.PropertyToID("PP_VignetteIntensity");
        internal static int PP_VignetteSmoothnessID = Shader.PropertyToID("PP_VignetteSmoothness");
        internal static int PP_FilmGrainIntensityID = Shader.PropertyToID("PP_FilmGrainIntensity");
        internal static int PP_FilmGrainResponseID = Shader.PropertyToID("PP_FilmGrainResponse");
        internal static int PP_FrameIndexID = Shader.PropertyToID("PP_FrameIndex");
        internal static int PP_ExposureMultiplierID = Shader.PropertyToID("PP_ExposureMultiplier");
        internal static int PP_AutoExposureID = Shader.PropertyToID("PP_AutoExposure");
        internal static int SRV_SceneColorTextureID = Shader.PropertyToID("SRV_SceneColorTexture");
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

        internal static int MaxBloomMips = 6;
    }

    public partial class InfinityRenderPipeline
    {
        readonly struct FPostComputeKernels
        {
            internal readonly int bloomDownsample, bloomUpsample, exposureClear, exposureHistogram, exposureReduce;
            internal FPostComputeKernels(ComputeShader shader)
            {
                bloomDownsample = shader.FindKernel("BloomDownsample");
                bloomUpsample = shader.FindKernel("BloomUpsample");
                exposureClear = shader.FindKernel("ExposureClear");
                exposureHistogram = shader.FindKernel("ExposureHistogram");
                exposureReduce = shader.FindKernel("ExposureReduce");
            }
        }
        ComputeShader m_PostKernelShader;
        FPostComputeKernels m_PostComputeKernels;
        static readonly GlobalKeyword s_PostBloomKeyword = GlobalKeyword.Create("INFINITY_POST_BLOOM");

        struct BloomPassData
        {
            public FPostComputeKernels kernels;
            public int2 resolution;
            public float bloomThreshold;
            public float bloomScatter;
            public float exposureMultiplier;
            public float autoExposure;
            public ComputeShader postProcessingShader;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef bloomTexture;
            public RGTextureRef exposureEV;
        }

        struct PostCombinePassData
        {
            public bool hasBloom;
            public int2 resolution;
            public float bloomIntensity;
            public float vignetteIntensity;
            public float vignetteSmoothness;
            public float filmGrainIntensity;
            public float filmGrainResponse;
            public float exposureMultiplier;
            public float autoExposure;
            public int frameIndex;
            public FinalOutputPassData output;
            public RGTextureRef sceneColorTexture;
            public RGTextureRef bloomTexture;
            public RGTextureRef combineLUT;
            public RGTextureRef exposureEV;
        }

        struct ExposurePassData
        {
            public FPostComputeKernels kernels;
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
            public float bloomScatter;
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
            "BloomDownsample", "BloomUpsample",
            "ExposureClear", "ExposureHistogram", "ExposureReduce"
        };

        void RasterPostProcessing(RenderContext renderContext, Camera camera, CameraFrameState frameState, in OutputTransformDecision decision, in FOutputPlan plan, RGTextureRef target)
        {
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.PostProcess);

            if (!GraphicsUtility.HasRequiredKernels(shaders.postProcessingShader, PostProcessingKernelNames))
            {
                throw new InvalidOperationException("InfinityRP: Display/PostProcess is required but postProcessingShader kernels are missing.");
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.AntiAliasingBuffer, out RGTextureRef sceneColorTexture) &&
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.SuperResolutionBuffer, out sceneColorTexture) &&
                !m_RGScoper.TryQueryTexture(InfinityShaderIDs.FoggedSceneColorBuffer, out sceneColorTexture))
            {
                throw new InvalidOperationException("InfinityRP: PostProcess has no scene color input (AntiAliasing/SuperResolution/FoggedSceneColor).");
            }

            if (!frameState.postProcessingEnabled)
            {
                if (plan.linearIntermediate)
                {
                    // Only writers need an independent texture; a read-only capture can alias the source.
                    if (frameState.debugView == EDebugView.None && (plan.editorDepth || plan.uiOverlay))
                    {
                        var descriptor = m_RGBuilder.GetTextureDescriptor(sceneColorTexture);
                        descriptor.name = "PostProcessTexture";
                        var output = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, descriptor);
                        RasterFinalOutput(camera, decision, sceneColorTexture, output, false);
                    }
                    else m_RGScoper.RegisterTexture(InfinityShaderIDs.PostProcessBuffer, sceneColorTexture);
                }
                else RasterFinalOutput(camera, decision, sceneColorTexture, target, true);
                frameState.exposureState.evCompensation = 0;
                MarkFeatureProduced(EFrameFeature.PostProcess);
                return;
            }

            if (m_PostKernelShader != shaders.postProcessingShader)
            {
                m_PostComputeKernels = new FPostComputeKernels(shaders.postProcessingShader);
                m_PostKernelShader = shaders.postProcessingShader;
            }

            if (!m_RGScoper.TryQueryTexture(InfinityShaderIDs.CombineLookupTexture, out RGTextureRef combineLUT))
            {
                throw new InvalidOperationException("InfinityRP: PostProcess requires CombineLookupTexture.");
            }

            PostVolumeState volumes = ResolvePostVolumeState(frameState);
            frameState.exposureState.evCompensation = frameState.sceneLightingEnabled ? ExposureUtility.ResolveCpuEvCompensation(frameState.volumeStack.GetComponent<Exposure>()) : 0;

            int width = m_ActiveFrameState.dimensions.displaySize.x;
            int height = m_ActiveFrameState.dimensions.displaySize.y;

            TextureDescriptor postProcessDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Display);
            postProcessDsc.name = PostProcessingPassUtilityData.TextureName;
            postProcessDsc.dimension = TextureDimension.Tex2D;
            postProcessDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            postProcessDsc.depthBufferBits = EDepthBits.None;
            postProcessDsc.enableRandomWrite = false;
            postProcessDsc.filterMode = FilterMode.Bilinear;
            postProcessDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureRef postProcessTexture = plan.linearIntermediate
                ? m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, postProcessDsc) : target;

            RGTextureRef bloomTexture = default;
            if (volumes.bloomIntensity > 0)
            {
                int bloomWidth = Mathf.Max(1, width >> 1);
                int bloomHeight = Mathf.Max(1, height >> 1);
                TextureDescriptor bloomDsc = new TextureDescriptor(bloomWidth, bloomHeight, ETextureSizingDomain.Display, 2);
                bloomDsc.name = PostProcessingPassUtilityData.BloomTextureName;
                bloomDsc.dimension = TextureDimension.Tex2D;
                bloomDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                bloomDsc.depthBufferBits = EDepthBits.None;
                bloomDsc.enableRandomWrite = true;
                bloomDsc.useMipMap = true;
                bloomDsc.autoGenerateMips = false;
                bloomDsc.filterMode = FilterMode.Bilinear;
                bloomDsc.wrapMode = TextureWrapMode.Clamp;
                bloomTexture = m_RGScoper.CreateAndRegisterTexture(InfinityShaderIDs.BloomBuffer, bloomDsc);
            }

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

                if (volumes.bloomIntensity > 0)
                    ComputeBloom(camera, sceneColorTexture, bloomTexture, exposureEV, volumes);
                RasterPostCombine(camera, sceneColorTexture, bloomTexture, combineLUT, exposureEV, postProcessTexture, volumes, decision, !plan.linearIntermediate);
            }

            MarkFeatureProduced(EFrameFeature.PostProcess);
        }

        static PostVolumeState ResolvePostVolumeState(CameraFrameState frameState)
        {
            VolumeStack stack = frameState.volumeStack;
            Exposure exposure = stack.GetComponent<Exposure>();
            Bloom bloom = stack.GetComponent<Bloom>();
            Vignette vignette = stack.GetComponent<Vignette>();
            FilmGrain filmGrain = stack.GetComponent<FilmGrain>();

            PostVolumeState state = default;
            state.recordAutoExposure = frameState.sceneLightingEnabled && ExposureUtility.ShouldRecordAuto(exposure);
            state.exposureMultiplier = frameState.sceneLightingEnabled ? ExposureUtility.EvToMultiplier(ExposureUtility.ResolveCpuEvCompensation(exposure)) : 1;
            state.autoExposure = state.recordAutoExposure ? 1.0f : 0.0f;
            if (state.recordAutoExposure)
            {
                state.adaptSpeed = exposure.adaptSpeed.value;
                state.lowPercentile = exposure.lowPercentile.value;
                state.highPercentile = exposure.highPercentile.value;
            }

            if (VolumeComponentActive(bloom))
            {
                state.bloomThreshold = bloom.threshold.value;
                state.bloomIntensity = bloom.intensity.value;
                state.bloomScatter = bloom.scatter.value;
            }

            if (VolumeComponentActive(vignette))
            {
                state.vignetteIntensity = vignette.intensity.value;
            }
            state.vignetteSmoothness = vignette.smoothness.value;

            if (VolumeComponentActive(filmGrain))
            {
                state.filmGrainIntensity = filmGrain.intensity.value;
            }
            state.filmGrainResponse = filmGrain.response.value;

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
                passData.kernels = m_PostComputeKernels;
                passData.resolution = new int2(m_ActiveFrameState.dimensions.displaySize.x, m_ActiveFrameState.dimensions.displaySize.y);
                passData.lowPercentile = volumes.lowPercentile;
                passData.highPercentile = volumes.highPercentile;
                passData.adapt = adapt;
                passData.resetHistory = created ? 1 : 0;
                passData.postProcessingShader = shaders.postProcessingShader;
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
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernels.exposureClear, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogram);
                    cmdEncoder.DispatchCompute(shader, passData.kernels.exposureClear, 4, 1, 1);
                    cmdEncoder.EndSample("ExposureClear");

                    cmdEncoder.BeginSample("ExposureHistogram");
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernels.exposureHistogram, PostProcessingPassUtilityData.SRV_SceneColorTextureID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernels.exposureHistogram, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogram);
                    cmdEncoder.DispatchCompute(shader, passData.kernels.exposureHistogram, Mathf.CeilToInt(passData.resolution.x / 8.0f), Mathf.CeilToInt(passData.resolution.y / 8.0f), 1);
                    cmdEncoder.EndSample("ExposureHistogram");

                    cmdEncoder.BeginSample("ExposureReduce");
                    cmdEncoder.SetComputeBufferParam(shader, passData.kernels.exposureReduce, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogram);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernels.exposureReduce, PostProcessingPassUtilityData.SRV_ExposureHistoryEVID, passData.exposureHistory);
                    cmdEncoder.SetComputeTextureParam(shader, passData.kernels.exposureReduce, PostProcessingPassUtilityData.UAV_ExposureEVID, passData.exposureEV);
                    cmdEncoder.DispatchCompute(shader, passData.kernels.exposureReduce, 1, 1, 1);
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
                passData.kernels = m_PostComputeKernels;
                passData.resolution = new int2(m_ActiveFrameState.dimensions.displaySize.x, m_ActiveFrameState.dimensions.displaySize.y);
                passData.bloomThreshold = volumes.bloomThreshold;
                passData.bloomScatter = volumes.bloomScatter;
                passData.exposureMultiplier = volumes.exposureMultiplier;
                passData.autoExposure = volumes.autoExposure;
                passData.postProcessingShader = shaders.postProcessingShader;
                passData.sceneColorTexture = passRef.ReadTexture(sceneColorTexture);
                passData.bloomTexture = passRef.WriteTexture(bloomTexture);
                passData.exposureEV = passRef.ReadTexture(exposureEV);

                passRef.EnablePassCulling(false);
                passRef.SetExecuteFunc((in BloomPassData passData, in RGComputeEncoder cmdEncoder, RGObjectPool objectPool) =>
                {
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomThresholdID, passData.bloomThreshold);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomScatterID, passData.bloomScatter);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_ExposureMultiplierID, passData.exposureMultiplier);
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_AutoExposureID, passData.autoExposure);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomDownsample, PostProcessingPassUtilityData.SRV_ExposureEVID, passData.exposureEV);

                    int bloomWidth = Mathf.Max(1, passData.resolution.x >> 1);
                    int bloomHeight = Mathf.Max(1, passData.resolution.y >> 1);
                    int numBloomMips = Mathf.Min(PostProcessingPassUtilityData.MaxBloomMips, (int)math.floor(math.log2(math.max(bloomWidth, bloomHeight))));

                    cmdEncoder.BeginSample("BloomDownsample");
                    cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(bloomWidth, bloomHeight, 1.0f / bloomWidth, 1.0f / bloomHeight));
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomPrefilterID, 1.0f);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomDownsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.sceneColorTexture);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomDownsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, 0);
                    cmdEncoder.DispatchCompute(passData.postProcessingShader, passData.kernels.bloomDownsample, Mathf.CeilToInt(bloomWidth / 8.0f), Mathf.CeilToInt(bloomHeight / 8.0f), 1);

                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomPrefilterID, 0.0f);
                    int prevWidth = bloomWidth;
                    int prevHeight = bloomHeight;
                    for (int mip = 1; mip < numBloomMips; ++mip)
                    {
                        int mipWidth = Mathf.Max(1, prevWidth >> 1);
                        int mipHeight = Mathf.Max(1, prevHeight >> 1);

                        cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomDownsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTexture, mip - 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomDownsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, passData.kernels.bloomDownsample, Mathf.CeilToInt(mipWidth / 8.0f), Mathf.CeilToInt(mipHeight / 8.0f), 1);

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
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomUpsample, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTexture, mip + 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.kernels.bloomUpsample, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTexture, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, passData.kernels.bloomUpsample, Mathf.CeilToInt(mipWidth / 8.0f), Mathf.CeilToInt(mipHeight / 8.0f), 1);
                    }
                    cmdEncoder.EndSample("BloomUpsample");
                });
            }
        }

        void RasterPostCombine(Camera camera, RGTextureRef sceneColor, RGTextureRef bloom, RGTextureRef lut,
            RGTextureRef exposure, RGTextureRef target, in PostVolumeState volumes, in OutputTransformDecision decision, bool finalTarget)
        {
            var sampler = finalTarget ? CustomSamplerId.RasterFinalOutput : CustomSamplerId.RasterPostCombine;
            using (var pass = m_RGBuilder.AddRasterPass<PostCombinePassData>(ProfilingSampler.Get(sampler)))
            {
                pass.EnablePassCulling(false);
                if (finalTarget) pass.EnableNativeRenderPass(false);
                pass.SetColorAttachment(target, 0, finalTarget && !FOutputPlan.CoversTarget(camera.rect) ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                ref var data = ref pass.GetPassData<PostCombinePassData>();
                data.output = PrepareFinalOutput(camera, decision, sceneColor, finalTarget, "RasterPostCombine");
                data.resolution = m_ActiveFrameState.dimensions.displaySize;
                data.bloomIntensity = volumes.bloomIntensity;
                data.vignetteIntensity = volumes.vignetteIntensity;
                data.vignetteSmoothness = volumes.vignetteSmoothness;
                data.filmGrainIntensity = volumes.filmGrainIntensity;
                data.filmGrainResponse = volumes.filmGrainResponse;
                data.exposureMultiplier = volumes.exposureMultiplier;
                data.autoExposure = volumes.autoExposure;
                data.frameIndex = Time.frameCount;
                data.hasBloom = volumes.bloomIntensity > 0;
                data.sceneColorTexture = pass.ReadTexture(sceneColor);
                if (data.hasBloom) data.bloomTexture = pass.ReadTexture(bloom);
                data.combineLUT = pass.ReadTexture(lut);
                data.exposureEV = pass.ReadTexture(exposure);
                if (data.output.displayProbe != null) pass.SetQueueObserver(data.output.displayProbe);
                pass.SetExecuteFunc((in PostCombinePassData data, in RGRasterEncoder commands, RGObjectPool pool) =>
                {
                    BindFinalOutput(data.output, commands);
                    commands.SetKeyword(s_PostBloomKeyword, data.hasBloom);
                    commands.SetGlobalVector(PostProcessingPassUtilityData.PP_ResolutionID, new Vector4(data.resolution.x, data.resolution.y, 1f / data.resolution.x, 1f / data.resolution.y));
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_BloomIntensityID, data.bloomIntensity);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_VignetteIntensityID, data.vignetteIntensity);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_VignetteSmoothnessID, data.vignetteSmoothness);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_FilmGrainIntensityID, data.filmGrainIntensity);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_FilmGrainResponseID, data.filmGrainResponse);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_ExposureMultiplierID, data.exposureMultiplier);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_AutoExposureID, data.autoExposure);
                    commands.SetGlobalInt(PostProcessingPassUtilityData.PP_FrameIndexID, data.frameIndex);
                    commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_SceneColorTextureID, data.sceneColorTexture);
                    if (data.hasBloom) commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_BloomTextureID, data.bloomTexture);
                    commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_CombineLUTID, data.combineLUT);
                    commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_ExposureEVID, data.exposureEV);
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, data.output.shaderPass);
                    CompleteFinalOutput(data.output, commands);
                });
            }
        }
    }
}
