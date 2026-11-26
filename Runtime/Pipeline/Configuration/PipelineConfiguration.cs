using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    public enum EGBufferLayout
    {
        Compact64Bit = 0,
        HighPrecision96Bit = 1,
        BestFitYCoCg64Bit = 2,
    }

    public enum ESceneColorFormat
    {
        R11G11B10F = 0,
        RGBA16F = 1,
    }

    public enum EDepthStencilFormat
    {
        D24S8 = 0,
        D32FS8 = 1,
        D32F = 2,
        D16 = 3,
    }

    public enum EMotionVectorFormat
    {
        RG16F = 0,
        RG32F = 1,
    }

    public enum EIndirectLightingFormat
    {
        R11G11B10F = 0,
        RGBA16F = 1,
    }

    public enum ESubsurfaceLightingMode
    {
        SeparateBuffer = 0,
        SceneColorAlpha = 1,
    }

    public enum EConfigStatus
    {
        Supported = 0,
        Unsupported = 1,
        Inactive = 2,
        Required = 3,
        Conflict = 4,
        PlatformOverride = 5,
    }

    internal readonly struct ConfigFieldState
    {
        internal readonly EConfigStatus status;
        internal readonly string reason;

        internal ConfigFieldState(EConfigStatus status, string reason = null)
        {
            this.status = status;
            this.reason = reason;
        }

        internal static ConfigFieldState Supported => new ConfigFieldState(EConfigStatus.Supported);
    }

    // Pure snapshot of graphics format and hardware capabilities so configuration resolution is testable without SystemInfo.
    internal readonly struct DeviceCapabilitySnapshot
    {
        internal readonly bool supportsD24S8;
        internal readonly bool supportsD32FS8;
        internal readonly bool supportsD32F;
        internal readonly bool supportsD16;
        internal readonly bool supportsR11G11B10FUAV;
        internal readonly bool supportsRGBA16F;
        internal readonly bool supportsRGBA16UnormRT;
        internal readonly bool supportsR8UnormRT;
        internal readonly bool supportsR8G8UnormRT;
        internal readonly bool supportsR8G8B8A8SRGB;
        internal readonly bool supportsRG16F;
        internal readonly bool supportsRG32F;
        internal readonly bool supportsAsyncCompute;
        internal readonly bool supportsRayTracing;

        internal DeviceCapabilitySnapshot(
            bool supportsD24S8,
            bool supportsD32FS8,
            bool supportsD32F,
            bool supportsD16,
            bool supportsR11G11B10FUAV,
            bool supportsRGBA16F,
            bool supportsRGBA16UnormRT,
            bool supportsR8UnormRT,
            bool supportsR8G8UnormRT,
            bool supportsR8G8B8A8SRGB,
            bool supportsRG16F,
            bool supportsRG32F,
            bool supportsAsyncCompute,
            bool supportsRayTracing)
        {
            this.supportsD24S8 = supportsD24S8;
            this.supportsD32FS8 = supportsD32FS8;
            this.supportsD32F = supportsD32F;
            this.supportsD16 = supportsD16;
            this.supportsR11G11B10FUAV = supportsR11G11B10FUAV;
            this.supportsRGBA16F = supportsRGBA16F;
            this.supportsRGBA16UnormRT = supportsRGBA16UnormRT;
            this.supportsR8UnormRT = supportsR8UnormRT;
            this.supportsR8G8UnormRT = supportsR8G8UnormRT;
            this.supportsR8G8B8A8SRGB = supportsR8G8B8A8SRGB;
            this.supportsRG16F = supportsRG16F;
            this.supportsRG32F = supportsRG32F;
            this.supportsAsyncCompute = supportsAsyncCompute;
            this.supportsRayTracing = supportsRayTracing;
        }

        internal static DeviceCapabilitySnapshot FromCurrentDevice()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return AllSupported;
            }

            const GraphicsFormatUsage rtSample = GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample;
            const GraphicsFormatUsage rtUavSample = GraphicsFormatUsage.Render | GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample;
            const GraphicsFormatUsage uavSample = GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample;
            return new DeviceCapabilitySnapshot(
                supportsD24S8: SystemInfo.IsFormatSupported(GraphicsFormat.D24_UNorm_S8_UInt, rtSample),
                supportsD32FS8: SystemInfo.IsFormatSupported(GraphicsFormat.D32_SFloat_S8_UInt, rtSample),
                supportsD32F: SystemInfo.IsFormatSupported(GraphicsFormat.D32_SFloat, rtSample),
                supportsD16: SystemInfo.IsFormatSupported(GraphicsFormat.D16_UNorm, rtSample),
                supportsR11G11B10FUAV: SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, rtUavSample),
                supportsRGBA16F: SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, rtUavSample),
                supportsRGBA16UnormRT: SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_UNorm, rtSample),
                supportsR8UnormRT: SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, rtSample),
                supportsR8G8UnormRT: SystemInfo.IsFormatSupported(GraphicsFormat.R8G8_UNorm, rtSample),
                supportsR8G8B8A8SRGB: SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SRGB, rtSample),
                supportsRG16F: SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, rtSample),
                supportsRG32F: SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, rtSample),
                supportsAsyncCompute: SystemInfo.supportsAsyncCompute,
                supportsRayTracing: SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, uavSample));
        }

        internal static DeviceCapabilitySnapshot AllSupported => new DeviceCapabilitySnapshot(
            supportsD24S8: true,
            supportsD32FS8: true,
            supportsD32F: true,
            supportsD16: true,
            supportsR11G11B10FUAV: true,
            supportsRGBA16F: true,
            supportsRGBA16UnormRT: true,
            supportsR8UnormRT: true,
            supportsR8G8UnormRT: true,
            supportsR8G8B8A8SRGB: true,
            supportsRG16F: true,
            supportsRG32F: true,
            supportsAsyncCompute: true,
            supportsRayTracing: true);
    }

    internal readonly struct EffectivePipelineConfiguration
    {
        internal readonly EGBufferLayout gBufferLayout;
        internal readonly bool useBestFitYCoCg;
        internal readonly ESubsurfaceLightingMode subsurfaceLightingMode;
        internal readonly bool enableRenderingLayers;
        internal readonly bool hasStencil;
        internal readonly bool directDecalSupported;

        internal readonly GraphicsFormat gBufferAFormat;
        internal readonly GraphicsFormat gBufferBFormat;
        internal readonly GraphicsFormat surfaceMetadataFormat;
        internal readonly GraphicsFormat sceneColorFormat;
        internal readonly GraphicsFormat depthStencilFormat;
        internal readonly GraphicsFormat motionVectorFormat;
        internal readonly GraphicsFormat motionMetadataFormat;
        internal readonly GraphicsFormat indirectLightingFormat;
        internal readonly GraphicsFormat bakedDiffuseFormat;
        internal readonly GraphicsFormat scatterDiffuseFormat;
        internal readonly GraphicsFormat customDataSSSFormat;

        internal readonly ETemporalBackend effectiveTemporalBackend;
        internal readonly ETemporalReconstructionMode effectiveTemporalMode;
        internal readonly float effectiveRenderScale;

        internal readonly int coreGBufferBitsPerPixel;
        internal readonly int surfaceMetadataBitsPerPixel;
        internal readonly int motionMetadataBitsPerPixel;
        internal readonly int commonGBufferPassBitsPerPixel;

        internal readonly ConfigFieldState gBufferLayoutState;
        internal readonly ConfigFieldState sceneColorState;
        internal readonly ConfigFieldState depthStencilState;
        internal readonly ConfigFieldState motionVectorState;
        internal readonly ConfigFieldState indirectLightingState;
        internal readonly ConfigFieldState subsurfaceModeState;
        internal readonly ConfigFieldState renderingLayersState;
        internal readonly ConfigFieldState reflectionState;
        internal readonly ConfigFieldState directDecalState;
        internal readonly ConfigFieldState analyticShadowState;
        internal readonly ConfigFieldState analyticIndirectState;
        internal readonly ConfigFieldState analyticDirectState;
        internal readonly ConfigFieldState temporalBackendState;
        internal readonly ConfigFieldState temporalModeState;
        internal readonly ConfigFieldState temporalQualityState;
        internal readonly ConfigFieldState renderScaleState;
        internal readonly bool hasBlockingConflict;

        internal EffectivePipelineConfiguration(
            EGBufferLayout gBufferLayout,
            bool useBestFitYCoCg,
            ESubsurfaceLightingMode subsurfaceLightingMode,
            bool enableRenderingLayers,
            bool hasStencil,
            bool directDecalSupported,
            GraphicsFormat gBufferAFormat,
            GraphicsFormat gBufferBFormat,
            GraphicsFormat surfaceMetadataFormat,
            GraphicsFormat sceneColorFormat,
            GraphicsFormat depthStencilFormat,
            GraphicsFormat motionVectorFormat,
            GraphicsFormat motionMetadataFormat,
            GraphicsFormat indirectLightingFormat,
            GraphicsFormat bakedDiffuseFormat,
            GraphicsFormat scatterDiffuseFormat,
            GraphicsFormat customDataSSSFormat,
            ETemporalBackend effectiveTemporalBackend,
            ETemporalReconstructionMode effectiveTemporalMode,
            float effectiveRenderScale,
            int coreGBufferBitsPerPixel,
            int surfaceMetadataBitsPerPixel,
            int motionMetadataBitsPerPixel,
            int commonGBufferPassBitsPerPixel,
            ConfigFieldState gBufferLayoutState,
            ConfigFieldState sceneColorState,
            ConfigFieldState depthStencilState,
            ConfigFieldState motionVectorState,
            ConfigFieldState indirectLightingState,
            ConfigFieldState subsurfaceModeState,
            ConfigFieldState renderingLayersState,
            ConfigFieldState reflectionState,
            ConfigFieldState directDecalState,
            ConfigFieldState analyticShadowState,
            ConfigFieldState analyticIndirectState,
            ConfigFieldState analyticDirectState,
            ConfigFieldState temporalBackendState,
            ConfigFieldState temporalModeState,
            ConfigFieldState temporalQualityState,
            ConfigFieldState renderScaleState,
            bool hasBlockingConflict)
        {
            this.gBufferLayout = gBufferLayout;
            this.useBestFitYCoCg = useBestFitYCoCg;
            this.subsurfaceLightingMode = subsurfaceLightingMode;
            this.enableRenderingLayers = enableRenderingLayers;
            this.hasStencil = hasStencil;
            this.directDecalSupported = directDecalSupported;
            this.gBufferAFormat = gBufferAFormat;
            this.gBufferBFormat = gBufferBFormat;
            this.surfaceMetadataFormat = surfaceMetadataFormat;
            this.sceneColorFormat = sceneColorFormat;
            this.depthStencilFormat = depthStencilFormat;
            this.motionVectorFormat = motionVectorFormat;
            this.motionMetadataFormat = motionMetadataFormat;
            this.indirectLightingFormat = indirectLightingFormat;
            this.bakedDiffuseFormat = bakedDiffuseFormat;
            this.scatterDiffuseFormat = scatterDiffuseFormat;
            this.customDataSSSFormat = customDataSSSFormat;
            this.effectiveTemporalBackend = effectiveTemporalBackend;
            this.effectiveTemporalMode = effectiveTemporalMode;
            this.effectiveRenderScale = effectiveRenderScale;
            this.coreGBufferBitsPerPixel = coreGBufferBitsPerPixel;
            this.surfaceMetadataBitsPerPixel = surfaceMetadataBitsPerPixel;
            this.motionMetadataBitsPerPixel = motionMetadataBitsPerPixel;
            this.commonGBufferPassBitsPerPixel = commonGBufferPassBitsPerPixel;
            this.gBufferLayoutState = gBufferLayoutState;
            this.sceneColorState = sceneColorState;
            this.depthStencilState = depthStencilState;
            this.motionVectorState = motionVectorState;
            this.indirectLightingState = indirectLightingState;
            this.subsurfaceModeState = subsurfaceModeState;
            this.renderingLayersState = renderingLayersState;
            this.reflectionState = reflectionState;
            this.directDecalState = directDecalState;
            this.analyticShadowState = analyticShadowState;
            this.analyticIndirectState = analyticIndirectState;
            this.analyticDirectState = analyticDirectState;
            this.temporalBackendState = temporalBackendState;
            this.temporalModeState = temporalModeState;
            this.temporalQualityState = temporalQualityState;
            this.renderScaleState = renderScaleState;
            this.hasBlockingConflict = hasBlockingConflict;
        }
    }
}
