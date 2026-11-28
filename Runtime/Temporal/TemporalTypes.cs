using System;
using Unity.Mathematics;

namespace InfinityTech.Rendering.Temporal
{
    public enum ETemporalProvider
    {
        Native = 0,
        Unity = 1,
        AMD = 2,
        Qualcomm = 3,
        Intel = 4,
        NVIDIA = 5,
    }

    public enum ETemporalBackend
    {
        None = 0,
        Native = 1,
        STP = 2,
        FSR = 3,
        SGSR = 4,
        XeSS = 5,
        DLSS = 6,
    }

    public enum ETemporalReconstructionMode
    {
        NativeAA = 0,
        Upscaling = 1,
    }

    [Flags]
    public enum ETemporalModeMask
    {
        None = 0,
        NativeAA = 1 << 0,
        Upscaling = 1 << 1,
        All = NativeAA | Upscaling,
    }

    public enum ETemporalQuality
    {
        UltraPerformance = 0,
        Performance = 1,
        Balanced = 2,
        Quality = 3,
        UltraQuality = 4,
    }

    public enum ETemporalCameraOverride
    {
        FollowPipeline = 0,
        ForceNativeAA = 1,
        Off = 2,
    }

    internal enum ETemporalMotionConvention
    {
        NormalizedUVFlipY = 0,
        PixelSpace = 1,
    }

    internal enum ETemporalDepthConvention
    {
        ReversedZ = 0,
        LinearEye = 1,
    }

    internal readonly struct TemporalSettings
    {
        internal readonly ETemporalBackend backend;
        internal readonly ETemporalReconstructionMode mode;
        internal readonly ETemporalQuality quality;
        internal readonly float sharpness;

        internal TemporalSettings(
            ETemporalBackend backend,
            ETemporalReconstructionMode mode,
            ETemporalQuality quality,
            float sharpness)
        {
            this.backend = backend;
            this.mode = mode;
            this.quality = quality;
            this.sharpness = math.clamp(sharpness, 0.0f, 1.0f);
        }
    }

    internal readonly struct TemporalDeviceCapabilities
    {
        internal readonly bool nativeSupported;
        internal readonly bool stpSupported;
        internal readonly bool fsrSupported;
        internal readonly bool sgsrSupported;
        internal readonly bool xessSupported;
        internal readonly bool dlssSupported;
        internal readonly string nativeUnavailableReason;
        internal readonly string stpUnavailableReason;
        internal readonly string fsrUnavailableReason;
        internal readonly string sgsrUnavailableReason;

        internal TemporalDeviceCapabilities(
            bool nativeSupported,
            bool stpSupported,
            bool fsrSupported,
            bool sgsrSupported,
            bool xessSupported = false,
            bool dlssSupported = false,
            string nativeUnavailableReason = null,
            string stpUnavailableReason = null,
            string fsrUnavailableReason = null,
            string sgsrUnavailableReason = null)
        {
            this.nativeSupported = nativeSupported;
            this.stpSupported = stpSupported;
            this.fsrSupported = fsrSupported;
            this.sgsrSupported = sgsrSupported;
            this.xessSupported = xessSupported;
            this.dlssSupported = dlssSupported;
            this.nativeUnavailableReason = nativeUnavailableReason;
            this.stpUnavailableReason = stpUnavailableReason;
            this.fsrUnavailableReason = fsrUnavailableReason;
            this.sgsrUnavailableReason = sgsrUnavailableReason;
        }

        internal static TemporalDeviceCapabilities AllCrossPlatformSupported => new TemporalDeviceCapabilities(
            nativeSupported: true,
            stpSupported: true,
            fsrSupported: true,
            sgsrSupported: true);
    }

    internal readonly struct TemporalPlanInput
    {
        internal readonly int2 displaySize;
        internal readonly bool temporalAllowed;
        internal readonly bool upscalingAllowed;
        internal readonly ETemporalCameraOverride cameraOverride;
        internal readonly TemporalSettings settings;
        internal readonly bool forceHistoryReset;

        internal TemporalPlanInput(
            int2 displaySize,
            bool temporalAllowed,
            bool upscalingAllowed,
            ETemporalCameraOverride cameraOverride,
            in TemporalSettings settings,
            bool forceHistoryReset)
        {
            this.displaySize = math.max(displaySize, new int2(1, 1));
            this.temporalAllowed = temporalAllowed;
            this.upscalingAllowed = upscalingAllowed;
            this.cameraOverride = cameraOverride;
            this.settings = settings;
            this.forceHistoryReset = forceHistoryReset;
        }
    }

    internal readonly struct TemporalFrameRequirements
    {
        internal readonly ETemporalBackend requestedBackend;
        internal readonly ETemporalBackend activeBackend;
        internal readonly ETemporalProvider provider;
        internal readonly ETemporalReconstructionMode mode;
        internal readonly ETemporalQuality quality;
        internal readonly int2 displaySize;
        internal readonly int2 renderSize;
        internal readonly float renderScale;
        internal readonly float mipBias;
        internal readonly float sharpness;
        internal readonly int jitterPhaseCount;
        internal readonly float jitterSpread;
        internal readonly bool requiresJitter;
        internal readonly bool requiresMotionVectors;
        internal readonly bool requiresReactiveMask;
        internal readonly bool requiresTransparencyMask;
        internal readonly bool requiresPreExposure;
        internal readonly bool requiresThreeFrameHistory;
        internal readonly bool requiresHistoryReset;
        internal readonly ETemporalMotionConvention motionConvention;
        internal readonly ETemporalDepthConvention depthConvention;

        internal bool isActive => activeBackend != ETemporalBackend.None;
        internal bool isFallback => requestedBackend != activeBackend;
        internal bool isUpscaling => mode == ETemporalReconstructionMode.Upscaling || math.any(renderSize != displaySize);

        internal TemporalFrameRequirements(
            ETemporalBackend requestedBackend,
            ETemporalBackend activeBackend,
            ETemporalProvider provider,
            ETemporalReconstructionMode mode,
            ETemporalQuality quality,
            int2 displaySize,
            int2 renderSize,
            float renderScale,
            float mipBias,
            float sharpness,
            int jitterPhaseCount,
            float jitterSpread,
            bool requiresJitter,
            bool requiresMotionVectors,
            bool requiresReactiveMask,
            bool requiresTransparencyMask,
            bool requiresPreExposure,
            bool requiresThreeFrameHistory,
            bool requiresHistoryReset,
            ETemporalMotionConvention motionConvention = ETemporalMotionConvention.NormalizedUVFlipY,
            ETemporalDepthConvention depthConvention = ETemporalDepthConvention.ReversedZ)
        {
            this.requestedBackend = requestedBackend;
            this.activeBackend = activeBackend;
            this.provider = provider;
            this.mode = mode;
            this.quality = quality;
            this.displaySize = displaySize;
            this.renderSize = renderSize;
            this.renderScale = renderScale;
            this.mipBias = mipBias;
            this.sharpness = sharpness;
            this.jitterPhaseCount = jitterPhaseCount;
            this.jitterSpread = jitterSpread;
            this.requiresJitter = requiresJitter;
            this.requiresMotionVectors = requiresMotionVectors;
            this.requiresReactiveMask = requiresReactiveMask;
            this.requiresTransparencyMask = requiresTransparencyMask;
            this.requiresPreExposure = requiresPreExposure;
            this.requiresThreeFrameHistory = requiresThreeFrameHistory;
            this.requiresHistoryReset = requiresHistoryReset;
            this.motionConvention = motionConvention;
            this.depthConvention = depthConvention;
        }

        internal static TemporalFrameRequirements Disabled(int2 displaySize, ETemporalBackend requestedBackend = ETemporalBackend.None)
        {
            int2 clamped = math.max(displaySize, new int2(1, 1));
            return new TemporalFrameRequirements(
                requestedBackend: requestedBackend,
                activeBackend: ETemporalBackend.None,
                provider: ETemporalProvider.Native,
                mode: ETemporalReconstructionMode.NativeAA,
                quality: ETemporalQuality.Quality,
                displaySize: clamped,
                renderSize: clamped,
                renderScale: 1.0f,
                mipBias: 0.0f,
                sharpness: 0.0f,
                jitterPhaseCount: 1,
                jitterSpread: 0.0f,
                requiresJitter: false,
                requiresMotionVectors: false,
                requiresReactiveMask: false,
                requiresTransparencyMask: false,
                requiresPreExposure: false,
                requiresThreeFrameHistory: false,
                requiresHistoryReset: false);
        }
    }
}
