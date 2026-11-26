using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    public enum EDepthStencilFormat
    {
        D24S8 = 0,
        D32FS8 = 1,
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
        internal readonly bool supportsR8G8B8A8SRGB;
        internal readonly bool supportsR8G8B8A8LoadStore;
        internal readonly bool supportsR11G11B10FLoadStore;
        internal readonly bool supportsRG16F;
        internal readonly bool supportsAsyncCompute;
        internal readonly bool supportsRayTracing;

        internal DeviceCapabilitySnapshot(
            bool supportsD24S8,
            bool supportsD32FS8,
            bool supportsR8G8B8A8SRGB,
            bool supportsR8G8B8A8LoadStore,
            bool supportsR11G11B10FLoadStore,
            bool supportsRG16F,
            bool supportsAsyncCompute,
            bool supportsRayTracing)
        {
            this.supportsD24S8 = supportsD24S8;
            this.supportsD32FS8 = supportsD32FS8;
            this.supportsR8G8B8A8SRGB = supportsR8G8B8A8SRGB;
            this.supportsR8G8B8A8LoadStore = supportsR8G8B8A8LoadStore;
            this.supportsR11G11B10FLoadStore = supportsR11G11B10FLoadStore;
            this.supportsRG16F = supportsRG16F;
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
            const GraphicsFormatUsage rtLoadStore = GraphicsFormatUsage.Render | GraphicsFormatUsage.LoadStore;
            const GraphicsFormatUsage rtLoadStoreSample = GraphicsFormatUsage.Render | GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample;
            const GraphicsFormatUsage loadStoreSample = GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample;
            bool supportsStencilView = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UInt, GraphicsFormatUsage.StencilSampling);
            return new DeviceCapabilitySnapshot(
                // The combined format is the attachment; shaders bind its depth and stencil planes separately.
                supportsD24S8: supportsStencilView && SystemInfo.IsFormatSupported(GraphicsFormat.D24_UNorm_S8_UInt, GraphicsFormatUsage.Render),
                supportsD32FS8: supportsStencilView && SystemInfo.IsFormatSupported(GraphicsFormat.D32_SFloat_S8_UInt, GraphicsFormatUsage.Render),
                supportsR8G8B8A8SRGB: SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SRGB, rtSample),
                supportsR8G8B8A8LoadStore: SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, rtLoadStoreSample),
                // Metal reports Sample as false for this format even when the render texture is shader-readable.
                supportsR11G11B10FLoadStore: SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, rtLoadStore),
                supportsRG16F: SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, rtSample),
                supportsAsyncCompute: SystemInfo.supportsAsyncCompute,
                supportsRayTracing: SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, loadStoreSample));
        }

        internal static DeviceCapabilitySnapshot AllSupported => new DeviceCapabilitySnapshot(
            supportsD24S8: true,
            supportsD32FS8: true,
            supportsR8G8B8A8SRGB: true,
            supportsR8G8B8A8LoadStore: true,
            supportsR11G11B10FLoadStore: true,
            supportsRG16F: true,
            supportsAsyncCompute: true,
            supportsRayTracing: true);
    }

    // The fixed opaque attachment layout and its device checks; formats are constants of the color path, depth is the one choice.
    internal readonly struct EffectivePipelineConfiguration
    {
        // Attachments

        internal const GraphicsFormat GBufferAFormat    = GraphicsFormat.R8G8B8A8_SRGB;
        internal const GraphicsFormat GBufferBFormat    = GraphicsFormat.R8G8B8A8_UNorm;
        internal const GraphicsFormat SceneColorFormat  = GraphicsFormat.B10G11R11_UFloatPack32;
        internal const GraphicsFormat SidebandFormat    = GraphicsFormat.R8G8B8A8_UNorm;
        internal const GraphicsFormat MotionFormat      = GraphicsFormat.R16G16_SFloat;
        // Profiles 1..15 fill the sideband's low nibble; 0 marks a pixel that is not subsurface.
        internal const int MaxDiffusionProfiles = 15;

        internal readonly bool enableRenderingLayers;
        internal readonly GraphicsFormat depthStencilFormat;
        internal readonly int geometryColorBitsPerPixel;

        internal readonly ETemporalBackend effectiveTemporalBackend;
        internal readonly ETemporalReconstructionMode effectiveTemporalMode;
        internal readonly float effectiveRenderScale;

        // Field states

        internal readonly ConfigFieldState colorPathState;
        internal readonly ConfigFieldState depthStencilState;
        internal readonly ConfigFieldState subsurfaceState;
        internal readonly ConfigFieldState reflectionState;
        internal readonly ConfigFieldState analyticShadowState;
        internal readonly ConfigFieldState analyticIndirectState;
        internal readonly ConfigFieldState analyticDirectState;
        internal readonly ConfigFieldState temporalBackendState;
        internal readonly ConfigFieldState temporalModeState;
        internal readonly ConfigFieldState temporalQualityState;
        internal readonly ConfigFieldState renderScaleState;
        // The first conflict that prevents the pipeline from running, or null.
        internal readonly string blockingConflict;

        internal bool hasBlockingConflict => blockingConflict != null;

        internal EffectivePipelineConfiguration(
            bool enableRenderingLayers,
            GraphicsFormat depthStencilFormat,
            int geometryColorBitsPerPixel,
            ETemporalBackend effectiveTemporalBackend,
            ETemporalReconstructionMode effectiveTemporalMode,
            float effectiveRenderScale,
            ConfigFieldState colorPathState,
            ConfigFieldState depthStencilState,
            ConfigFieldState subsurfaceState,
            ConfigFieldState reflectionState,
            ConfigFieldState analyticShadowState,
            ConfigFieldState analyticIndirectState,
            ConfigFieldState analyticDirectState,
            ConfigFieldState temporalBackendState,
            ConfigFieldState temporalModeState,
            ConfigFieldState temporalQualityState,
            ConfigFieldState renderScaleState,
            string blockingConflict)
        {
            this.enableRenderingLayers = enableRenderingLayers;
            this.depthStencilFormat = depthStencilFormat;
            this.geometryColorBitsPerPixel = geometryColorBitsPerPixel;
            this.effectiveTemporalBackend = effectiveTemporalBackend;
            this.effectiveTemporalMode = effectiveTemporalMode;
            this.effectiveRenderScale = effectiveRenderScale;
            this.colorPathState = colorPathState;
            this.depthStencilState = depthStencilState;
            this.subsurfaceState = subsurfaceState;
            this.reflectionState = reflectionState;
            this.analyticShadowState = analyticShadowState;
            this.analyticIndirectState = analyticIndirectState;
            this.analyticDirectState = analyticDirectState;
            this.temporalBackendState = temporalBackendState;
            this.temporalModeState = temporalModeState;
            this.temporalQualityState = temporalQualityState;
            this.renderScaleState = renderScaleState;
            this.blockingConflict = blockingConflict;
        }
    }
}
