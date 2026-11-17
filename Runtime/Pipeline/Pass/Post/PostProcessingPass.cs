using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.Volume;
using InfinityTech.Rendering;
using InfinityTech.Core;

namespace InfinityTech.Rendering.Pipeline
{
    // GPU-RECORD: CBV_BloomParameters Shaders/Pass/Post/Compute_PostProcessing.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct BloomParameters
    {
        public float threshold;
        public float scatter;
        public float exposureMultiplier;
        public float autoExposure;
    }

    // GPU-RECORD: CBV_ExposureParameters Shaders/Pass/Post/Compute_PostProcessing.compute
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExposureParameters
    {
        public Vector4 resolution;
        public Vector4 histogramRange;
        public float lowPercentile;
        public float highPercentile;
        public float adapt;
        public int resetHistory;
    }

    internal static class PostProcessingPassUtilityData
    {
        internal static readonly ProfilingSampler ExposureClearSampler = new ProfilingSampler("ExposureClear");
        internal static readonly ProfilingSampler ExposureHistogramSampler = new ProfilingSampler("ExposureHistogram");
        internal static readonly ProfilingSampler ExposureReduceSampler = new ProfilingSampler("ExposureReduce");
        internal static readonly ProfilingSampler BloomDownsampleSampler = new ProfilingSampler("BloomDownsample");
        internal static readonly ProfilingSampler BloomUpsampleSampler = new ProfilingSampler("BloomUpsample");
        internal static readonly string TextureName = "PostProcessTexture";
        internal static readonly string BloomTextureName = "BloomTexture";
        internal static readonly string ExposureEVName = "ExposureEV";
        internal static readonly string ExposureHistogramName = "ExposureHistogram";
        internal static readonly int PP_ResolutionID = Shader.PropertyToID("PP_Resolution");
        internal static readonly int PP_BloomIntensityID = Shader.PropertyToID("PP_BloomIntensity");
        internal static readonly int PP_VignetteIntensityID = Shader.PropertyToID("PP_VignetteIntensity");
        internal static readonly int PP_VignetteSmoothnessID = Shader.PropertyToID("PP_VignetteSmoothness");
        internal static readonly int PP_FilmGrainIntensityID = Shader.PropertyToID("PP_FilmGrainIntensity");
        internal static readonly int PP_FilmGrainResponseID = Shader.PropertyToID("PP_FilmGrainResponse");
        internal static readonly int PP_FrameIndexID = Shader.PropertyToID("PP_FrameIndex");
        internal static readonly int PP_ExposureMultiplierID = Shader.PropertyToID("PP_ExposureMultiplier");
        internal static readonly int PP_AutoExposureID = Shader.PropertyToID("PP_AutoExposure");
        internal static readonly int SRV_CombineLUTID = Shader.PropertyToID("SRV_CombineLUT");
        internal static readonly int SRV_ExposureEVID = Shader.PropertyToID("SRV_ExposureEV");

        internal static readonly int SRV_BloomSourceID = Shader.PropertyToID("SRV_BloomSource");
        internal static readonly int SRV_BloomTextureID = Shader.PropertyToID("SRV_BloomTexture");
        internal static readonly int UAV_BloomTargetID = Shader.PropertyToID("UAV_BloomTarget");
        internal static readonly int BloomMipSizeID = Shader.PropertyToID("BloomMipSize");
        internal static readonly int PP_BloomPrefilterID = Shader.PropertyToID("PP_BloomPrefilter");

        internal static readonly int UAV_ExposureHistogramID = Shader.PropertyToID("UAV_ExposureHistogram");
        internal static readonly int SRV_ExposureHistoryEVID = Shader.PropertyToID("SRV_ExposureHistoryEV");
        internal static readonly int UAV_ExposureEVID = Shader.PropertyToID("UAV_ExposureEV");

        internal static readonly int CBV_BloomParametersID = Shader.PropertyToID("CBV_BloomParameters");
        internal static readonly int CBV_ExposureParametersID = Shader.PropertyToID("CBV_ExposureParameters");

        internal static readonly int MaxBloomMips = 6;
    }

    public partial class InfinityRenderPipeline
    {
        static readonly GlobalKeyword s_PostBloomKeyword = GlobalKeyword.Create("INFINITY_POST_BLOOM");

        struct BloomPassData
        {
            public ComputeKernel bloomDownsample;
            public ComputeKernel bloomUpsample;
            public int2 resolution;
            public RGConstantsHandle parametersHandle;
            public ComputeShader postProcessingShader;
            public RGTextureHandle sceneColorTextureHandle;
            public RGTextureHandle bloomTextureHandle;
            public RGTextureHandle exposureEVHandle;
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
            public RGTextureHandle sceneColorTextureHandle;
            public RGTextureHandle bloomTextureHandle;
            public RGTextureHandle combineLUTHandle;
            public RGTextureHandle exposureEVHandle;
        }

        struct ExposurePassData
        {
            public ComputeKernel exposureClear;
            public ComputeKernel exposureHistogram;
            public ComputeKernel exposureReduce;
            public int2 resolution;
            public RGConstantsHandle parametersHandle;
            public ComputeShader postProcessingShader;
            public RGTextureHandle sceneColorTextureHandle;
            public RGTextureHandle exposureHistoryHandle;
            public RGTextureHandle exposureEVHandle;
            public RGBufferHandle histogramHandle;
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

        void RasterPostProcessing(in ViewContext context, in OutputPlan plan, RGTextureHandle targetHandle)
        {
            context.features.ThrowIfCannotProduce(EFrameFeature.PostProcess);

            RGTextureHandle sceneColorTextureHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ResolvedSceneColor);

            if (!context.postProcessingEnabled)
            {
                if (plan.linearIntermediate)
                {
                    // Only writers need an independent texture; a read-only capture can alias the source.
                    if (!plan.debugOutput && (plan.editorDepth || plan.uiOverlay))
                    {
                        var descriptor = m_RGBuilder.GetTextureDescriptor(sceneColorTextureHandle);
                        descriptor.name = "PostProcessTexture";
                        var output = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, descriptor);
                        RasterFinalOutput(context, sceneColorTextureHandle, output, false);
                    }
                    else
                    {
                        m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.PostProcessBuffer, sceneColorTextureHandle);
                    }
                }
                else
                {
                    RasterFinalOutput(context, sceneColorTextureHandle, targetHandle, true);
                }
                context.features.MarkProduced(EFrameFeature.PostProcess);
                return;
            }

            RGTextureHandle combineLUTHandle = m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.CombineLookupTexture);

            PostVolumeState volumes = ResolvePostVolumeState(context);

            int width = context.dimensions.displaySize.x;
            int height = context.dimensions.displaySize.y;

            TextureDescriptor postProcessDsc = new TextureDescriptor(width, height, ETextureSizingDomain.Display);
            postProcessDsc.name = PostProcessingPassUtilityData.TextureName;
            postProcessDsc.dimension = TextureDimension.Tex2D;
            postProcessDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            postProcessDsc.enableRandomWrite = false;
            postProcessDsc.filterMode = FilterMode.Bilinear;
            postProcessDsc.wrapMode = TextureWrapMode.Clamp;
            RGTextureHandle postProcessTextureHandle = plan.linearIntermediate
                ? m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.PostProcessBuffer, postProcessDsc) : targetHandle;

            RGTextureHandle bloomTextureHandle = default;
            if (volumes.bloomIntensity > 0)
            {
                int bloomWidth = Mathf.Max(1, width >> 1);
                int bloomHeight = Mathf.Max(1, height >> 1);
                TextureDescriptor bloomDsc = new TextureDescriptor(bloomWidth, bloomHeight, ETextureSizingDomain.Display, 2);
                bloomDsc.name = PostProcessingPassUtilityData.BloomTextureName;
                bloomDsc.dimension = TextureDimension.Tex2D;
                bloomDsc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                bloomDsc.enableRandomWrite = true;
                bloomDsc.useMipMap = true;
                bloomDsc.autoGenerateMips = false;
                bloomDsc.filterMode = FilterMode.Bilinear;
                bloomDsc.wrapMode = TextureWrapMode.Clamp;
                bloomTextureHandle = m_RGBuilder.scoper.CreateAndRegisterTexture(InfinityShaderIDs.BloomBuffer, bloomDsc);
            }

            RGTextureHandle exposureEVHandle;
            using (new RGProfilingScope(m_RGBuilder, ProfilingSampler.Get(CustomSamplerId.PostProcessing)))
            {
                if (volumes.recordAutoExposure)
                {
                    exposureEVHandle = ComputeAutoExposure(context, sceneColorTextureHandle, volumes);
                }
                else
                {
                    exposureEVHandle = ImportIdleExposureEV(context);
                }

                if (volumes.bloomIntensity > 0)
                    ComputeBloom(context, sceneColorTextureHandle, bloomTextureHandle, exposureEVHandle, volumes);
                RasterPostCombine(context, sceneColorTextureHandle, bloomTextureHandle, combineLUTHandle, exposureEVHandle, postProcessTextureHandle, volumes, !plan.linearIntermediate);
            }

            context.features.MarkProduced(EFrameFeature.PostProcess);
        }

        static PostVolumeState ResolvePostVolumeState(in ViewContext context)
        {
            Exposure exposure = context.volumes.exposure;
            Bloom bloom = context.volumes.bloom;
            Vignette vignette = context.volumes.vignette;
            FilmGrain filmGrain = context.volumes.filmGrain;

            PostVolumeState state = default;
            state.recordAutoExposure = context.sceneLightingEnabled && ExposureUtility.ShouldRecordAuto(exposure);
            state.exposureMultiplier = context.sceneLightingEnabled ? ExposureUtility.ResolveMultiplier(exposure) : 1;
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
            TextureDescriptor evDsc = TextureDescriptor.Create2D(PostProcessingPassUtilityData.ExposureEVName, 1, 1, GraphicsFormat.R32_SFloat, ETextureSizingDomain.Exact);
            evDsc.filterMode = FilterMode.Point;
            return evDsc;
        }

        RGTextureHandle ImportIdleExposureEV(in ViewContext context)
        {
            TextureDescriptor evDsc = CreateExposureEVDescriptor();
            TextureHandle evReadHandle = context.historyCache.GetTexture(InfinityShaderIDs.ExposureEVBuffer, evDsc);
            RGTextureHandle exposureReadHandle = m_RGBuilder.ImportTexture(evReadHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.ExposureEVBuffer, exposureReadHandle);
            return exposureReadHandle;
        }

        RGTextureHandle ComputeAutoExposure(in ViewContext context, in RGTextureHandle sceneColorTextureHandle, in PostVolumeState volumes)
        {
            TextureDescriptor evDsc = CreateExposureEVDescriptor();

            TextureHandle evWriteHandle = context.historyCache.GetWriteTexture(InfinityShaderIDs.ExposureEVBuffer, evDsc);
            // Exposure adapts across camera cuts, so only a missing or relaid-out history resets it.
            HistoryRead evRead = context.historyCache.Read(InfinityShaderIDs.ExposureEVBuffer, evDsc, false);
            RGTextureHandle exposureWriteHandle = m_RGBuilder.ImportTexture(evWriteHandle);
            RGTextureHandle exposureReadHandle = m_RGBuilder.ImportTexture(evRead.textureHandle);
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.ExposureEVBuffer, exposureWriteHandle);

            BufferDescriptor histogramDsc = new BufferDescriptor(ExposureUtility.HistogramBinCount, sizeof(uint), GraphicsBuffer.Target.Structured);
            histogramDsc.name = PostProcessingPassUtilityData.ExposureHistogramName;
            RGBufferHandle histogramHandle = m_RGBuilder.CreateBuffer(histogramDsc);

            float dt = math.max(Time.unscaledDeltaTime, 1.0f / 120.0f);
            float adapt = 1.0f - math.exp(-volumes.adaptSpeed * dt);

            {
                RGComputePassHandle<ExposurePassData> passHandle = m_RGBuilder.AddComputePass<ExposurePassData>(ProfilingSampler.Get(CustomSamplerId.ComputeExposure));
                ref ExposurePassData passData = ref passHandle.GetPassData();
                passData.exposureClear = kernels.exposureClear;
                passData.exposureHistogram = kernels.exposureHistogram;
                passData.exposureReduce = kernels.exposureReduce;
                passData.resolution = new int2(context.dimensions.displaySize.x, context.dimensions.displaySize.y);
                passData.parametersHandle = passHandle.WriteConstants(new ExposureParameters
                {
                    resolution = new Vector4(passData.resolution.x, passData.resolution.y, 1.0f / passData.resolution.x, 1.0f / passData.resolution.y),
                    histogramRange = new Vector4(
                        ExposureUtility.HistogramMinLog,
                        ExposureUtility.HistogramMaxLog,
                        1.0f / (ExposureUtility.HistogramMaxLog - ExposureUtility.HistogramMinLog),
                        0.0f),
                    lowPercentile = volumes.lowPercentile,
                    highPercentile = volumes.highPercentile,
                    adapt = adapt,
                    resetHistory = evRead.valid ? 0 : 1,
                });
                passData.postProcessingShader = shaders.postProcessingShader;
                passData.sceneColorTextureHandle = passHandle.ReadTexture(sceneColorTextureHandle);
                passData.exposureHistoryHandle = passHandle.ReadTexture(exposureReadHandle);
                passData.exposureEVHandle = passHandle.WriteTexture(exposureWriteHandle);
                passData.histogramHandle = passHandle.WriteBuffer(histogramHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in ExposurePassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    ComputeShader shader = passData.postProcessingShader;
                    cmdEncoder.SetComputeConstantBufferParam(shader, PostProcessingPassUtilityData.CBV_ExposureParametersID, passData.parametersHandle);

                    cmdEncoder.BeginSample(PostProcessingPassUtilityData.ExposureClearSampler);
                    cmdEncoder.SetComputeBufferParam(shader, passData.exposureClear.index, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogramHandle);
                    cmdEncoder.DispatchCompute(shader, passData.exposureClear.index, 4, 1, 1);
                    cmdEncoder.EndSample(PostProcessingPassUtilityData.ExposureClearSampler);

                    cmdEncoder.BeginSample(PostProcessingPassUtilityData.ExposureHistogramSampler);
                    cmdEncoder.SetComputeTextureParam(shader, passData.exposureHistogram.index, PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTextureHandle);
                    cmdEncoder.SetComputeBufferParam(shader, passData.exposureHistogram.index, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogramHandle);
                    cmdEncoder.DispatchCompute(shader, passData.exposureHistogram.index, passData.exposureHistogram.GroupsX(passData.resolution.x), passData.exposureHistogram.GroupsY(passData.resolution.y), 1);
                    cmdEncoder.EndSample(PostProcessingPassUtilityData.ExposureHistogramSampler);

                    cmdEncoder.BeginSample(PostProcessingPassUtilityData.ExposureReduceSampler);
                    cmdEncoder.SetComputeBufferParam(shader, passData.exposureReduce.index, PostProcessingPassUtilityData.UAV_ExposureHistogramID, passData.histogramHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.exposureReduce.index, PostProcessingPassUtilityData.SRV_ExposureHistoryEVID, passData.exposureHistoryHandle);
                    cmdEncoder.SetComputeTextureParam(shader, passData.exposureReduce.index, PostProcessingPassUtilityData.UAV_ExposureEVID, passData.exposureEVHandle);
                    cmdEncoder.DispatchCompute(shader, passData.exposureReduce.index, 1, 1, 1);
                    cmdEncoder.EndSample(PostProcessingPassUtilityData.ExposureReduceSampler);
                });
            }

            context.historyCache.MarkProduced(InfinityShaderIDs.ExposureEVBuffer);
            return exposureWriteHandle;
        }

        void ComputeBloom(in ViewContext context, in RGTextureHandle sceneColorTextureHandle, in RGTextureHandle bloomTextureHandle, in RGTextureHandle exposureEVHandle, in PostVolumeState volumes)
        {
            {
                RGComputePassHandle<BloomPassData> passHandle = m_RGBuilder.AddComputePass<BloomPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeBloom));
                ref BloomPassData passData = ref passHandle.GetPassData();
                passData.bloomDownsample = kernels.bloomDownsample;
                passData.bloomUpsample = kernels.bloomUpsample;
                passData.resolution = new int2(context.dimensions.displaySize.x, context.dimensions.displaySize.y);
                passData.parametersHandle = passHandle.WriteConstants(new BloomParameters
                {
                    threshold = volumes.bloomThreshold,
                    scatter = volumes.bloomScatter,
                    exposureMultiplier = volumes.exposureMultiplier,
                    autoExposure = volumes.autoExposure,
                });
                passData.postProcessingShader = shaders.postProcessingShader;
                passData.sceneColorTextureHandle = passHandle.ReadTexture(sceneColorTextureHandle);
                passData.bloomTextureHandle = passHandle.WriteTexture(bloomTextureHandle);
                passData.exposureEVHandle = passHandle.ReadTexture(exposureEVHandle);

                passHandle.EnablePassCulling(false);
                passHandle.SetExecuteFunc((in BloomPassData passData, in RGComputeEncoder cmdEncoder) =>
                {
                    cmdEncoder.SetComputeConstantBufferParam(passData.postProcessingShader, PostProcessingPassUtilityData.CBV_BloomParametersID, passData.parametersHandle);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomDownsample.index, PostProcessingPassUtilityData.SRV_ExposureEVID, passData.exposureEVHandle);

                    int bloomWidth = Mathf.Max(1, passData.resolution.x >> 1);
                    int bloomHeight = Mathf.Max(1, passData.resolution.y >> 1);
                    int numBloomMips = Mathf.Min(PostProcessingPassUtilityData.MaxBloomMips, (int)math.floor(math.log2(math.max(bloomWidth, bloomHeight))));

                    cmdEncoder.BeginSample(PostProcessingPassUtilityData.BloomDownsampleSampler);
                    cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(bloomWidth, bloomHeight, 1.0f / bloomWidth, 1.0f / bloomHeight));
                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomPrefilterID, 1.0f);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomDownsample.index, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.sceneColorTextureHandle);
                    cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomDownsample.index, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTextureHandle, 0);
                    cmdEncoder.DispatchCompute(passData.postProcessingShader, passData.bloomDownsample.index, passData.bloomDownsample.GroupsX(bloomWidth), passData.bloomDownsample.GroupsY(bloomHeight), 1);

                    cmdEncoder.SetComputeFloatParam(passData.postProcessingShader, PostProcessingPassUtilityData.PP_BloomPrefilterID, 0.0f);
                    int prevWidth = bloomWidth;
                    int prevHeight = bloomHeight;
                    for (int mip = 1; mip < numBloomMips; ++mip)
                    {
                        int mipWidth = Mathf.Max(1, prevWidth >> 1);
                        int mipHeight = Mathf.Max(1, prevHeight >> 1);

                        cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomDownsample.index, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTextureHandle, mip - 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomDownsample.index, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTextureHandle, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, passData.bloomDownsample.index, passData.bloomDownsample.GroupsX(mipWidth), passData.bloomDownsample.GroupsY(mipHeight), 1);

                        prevWidth = mipWidth;
                        prevHeight = mipHeight;
                    }
                    cmdEncoder.EndSample(PostProcessingPassUtilityData.BloomDownsampleSampler);

                    cmdEncoder.BeginSample(PostProcessingPassUtilityData.BloomUpsampleSampler);
                    for (int mip = numBloomMips - 2; mip >= 0; --mip)
                    {
                        int mipWidth = Mathf.Max(1, bloomWidth >> mip);
                        int mipHeight = Mathf.Max(1, bloomHeight >> mip);

                        cmdEncoder.SetComputeVectorParam(passData.postProcessingShader, PostProcessingPassUtilityData.BloomMipSizeID, new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomUpsample.index, PostProcessingPassUtilityData.SRV_BloomSourceID, passData.bloomTextureHandle, mip + 1);
                        cmdEncoder.SetComputeTextureParam(passData.postProcessingShader, passData.bloomUpsample.index, PostProcessingPassUtilityData.UAV_BloomTargetID, passData.bloomTextureHandle, mip);
                        cmdEncoder.DispatchCompute(passData.postProcessingShader, passData.bloomUpsample.index, passData.bloomUpsample.GroupsX(mipWidth), passData.bloomUpsample.GroupsY(mipHeight), 1);
                    }
                    cmdEncoder.EndSample(PostProcessingPassUtilityData.BloomUpsampleSampler);
                });
            }
        }

        void RasterPostCombine(in ViewContext context, RGTextureHandle sceneColorHandle, RGTextureHandle bloomHandle, RGTextureHandle lutHandle, RGTextureHandle exposureHandle, RGTextureHandle targetHandle, in PostVolumeState volumes, bool finalTarget)
        {
            var sampler = finalTarget ? CustomSamplerId.RasterFinalOutput : CustomSamplerId.RasterPostCombine;
            {
                RGRasterPassHandle<PostCombinePassData> passHandle = m_RGBuilder.AddRasterPass<PostCombinePassData>(ProfilingSampler.Get(sampler));
                passHandle.EnablePassCulling(false);
                if (finalTarget)
                {
                    passHandle.EnableNativeRenderPass(false);
                }
                passHandle.SetColorAttachment(targetHandle, 0, finalTarget && !OutputPlan.CoversTarget(context.camera.rect) ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                ref PostCombinePassData passData = ref passHandle.GetPassData();
                passData.output = PrepareFinalOutput(context, sceneColorHandle, finalTarget, blitPasses.postCombine);
                passData.resolution = context.dimensions.displaySize;
                passData.bloomIntensity = volumes.bloomIntensity;
                passData.vignetteIntensity = volumes.vignetteIntensity;
                passData.vignetteSmoothness = volumes.vignetteSmoothness;
                passData.filmGrainIntensity = volumes.filmGrainIntensity;
                passData.filmGrainResponse = volumes.filmGrainResponse;
                passData.exposureMultiplier = volumes.exposureMultiplier;
                passData.autoExposure = volumes.autoExposure;
                passData.frameIndex = context.frame;
                passData.hasBloom = volumes.bloomIntensity > 0;
                passData.sceneColorTextureHandle = passHandle.ReadTexture(sceneColorHandle);
                if (passData.hasBloom)
                {
                    passData.bloomTextureHandle = passHandle.ReadTexture(bloomHandle);
                }
                passData.combineLUTHandle = passHandle.ReadTexture(lutHandle);
                passData.exposureEVHandle = passHandle.ReadTexture(exposureHandle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (passData.output.displayProbe != null)
                {
                    passHandle.SetQueueObserver(passData.output.displayProbe);
                }
#endif
                passHandle.SetExecuteFunc((in PostCombinePassData passData, in RGRasterEncoder commands) =>
                {
                    BindFinalOutput(passData.output, commands);
                    commands.SetKeyword(s_PostBloomKeyword, passData.hasBloom);
                    commands.SetGlobalVector(PostProcessingPassUtilityData.PP_ResolutionID, new Vector4(passData.resolution.x, passData.resolution.y, 1f / passData.resolution.x, 1f / passData.resolution.y));
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_BloomIntensityID, passData.bloomIntensity);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_VignetteIntensityID, passData.vignetteIntensity);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_VignetteSmoothnessID, passData.vignetteSmoothness);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_FilmGrainIntensityID, passData.filmGrainIntensity);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_FilmGrainResponseID, passData.filmGrainResponse);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_ExposureMultiplierID, passData.exposureMultiplier);
                    commands.SetGlobalFloat(PostProcessingPassUtilityData.PP_AutoExposureID, passData.autoExposure);
                    commands.SetGlobalInt(PostProcessingPassUtilityData.PP_FrameIndexID, passData.frameIndex);
                    commands.SetGlobalTexture(PassShaderIDs.SRV_SceneColorTexture, passData.sceneColorTextureHandle);
                    if (passData.hasBloom)
                    {
                        commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_BloomTextureID, passData.bloomTextureHandle);
                    }
                    commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_CombineLUTID, passData.combineLUTHandle);
                    commands.SetGlobalTexture(PostProcessingPassUtilityData.SRV_ExposureEVID, passData.exposureEVHandle);
                    commands.DrawMesh(GraphicsUtility.FullScreenMesh, Matrix4x4.identity, GraphicsUtility.BlitMaterial, 0, passData.output.shaderPass);
                    CompleteFinalOutput(passData.output, commands);
                });
            }
        }
    }
}
