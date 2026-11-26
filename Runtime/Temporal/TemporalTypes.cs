using Unity.Mathematics;

namespace InfinityTech.Rendering.Temporal
{
    public enum ETemporalProvider
    {
        Native = 0,
        NVIDIA = 1,
        AMD = 2,
        Intel = 3,
        Custom = 4,
    }

    public enum ETemporalBackend
    {
        None = 0,
        NativeTAA = 1,
        NativeTAAU = 2,
        DLSS = 3,
        FSR = 4,
        XeSS = 5,
    }

    public enum ETemporalReconstructionMode
    {
        NativeAA = 0,
        Upscaling = 1,
        Supersampling = 2,
    }

    public enum ETemporalQuality
    {
        UltraPerformance = 0,
        Performance = 1,
        Balanced = 2,
        Quality = 3,
        UltraQuality = 4,
        Native = 5,
        Custom = 6,
    }

    public enum ERenderResolutionPolicy
    {
        FixedScale = 0,
        QualityPreset = 1,
        DynamicScale = 2,
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
        internal readonly ERenderResolutionPolicy resolutionPolicy;
        internal readonly float customRenderScale;
        internal readonly float sharpness;

        internal TemporalSettings(
            ETemporalBackend backend,
            ETemporalReconstructionMode mode,
            ETemporalQuality quality,
            ERenderResolutionPolicy resolutionPolicy,
            float customRenderScale,
            float sharpness)
        {
            this.backend = backend;
            this.mode = mode;
            this.quality = quality;
            this.resolutionPolicy = resolutionPolicy;
            this.customRenderScale = customRenderScale;
            this.sharpness = sharpness;
        }
    }

    internal readonly struct TemporalPlanInput
    {
        internal readonly int2 displaySize;
        internal readonly bool temporalAllowed;
        internal readonly bool superResolutionAllowed;
        internal readonly TemporalSettings settings;
        internal readonly bool forceHistoryReset;

        internal TemporalPlanInput(int2 displaySize, bool temporalAllowed, bool superResolutionAllowed, in TemporalSettings settings, bool forceHistoryReset)
        {
            this.displaySize = math.max(displaySize, new int2(1, 1));
            this.temporalAllowed = temporalAllowed;
            this.superResolutionAllowed = superResolutionAllowed;
            this.settings = settings;
            this.forceHistoryReset = forceHistoryReset;
        }
    }

    /// <summary>
    /// Frame requirements declared by the active Temporal Reconstruction backend BEFORE CameraSetup and resource production (PRD §9.3).
    /// </summary>
    internal readonly struct TemporalFrameRequirements
    {
        internal readonly ETemporalBackend activeBackend;
        internal readonly ETemporalProvider provider;
        internal readonly ETemporalReconstructionMode mode;
        internal readonly int2 displaySize;
        internal readonly int2 renderSize;
        internal readonly float renderScale;
        internal readonly float mipBias;
        internal readonly float sharpness;
        internal readonly int jitterPhaseCount;
        internal readonly bool requiresJitter;
        internal readonly bool requiresMotionVectors;
        internal readonly bool requiresReactiveMask;
        internal readonly bool requiresTransparencyMask;
        internal readonly bool requiresPreExposure;
        internal readonly bool requiresHistoryReset;
        internal readonly ETemporalMotionConvention motionConvention;
        internal readonly ETemporalDepthConvention depthConvention;

        internal bool isActive => activeBackend != ETemporalBackend.None;
        internal bool isUpscaling => mode == ETemporalReconstructionMode.Upscaling || math.any(renderSize != displaySize);

        internal TemporalFrameRequirements(
            ETemporalBackend activeBackend,
            ETemporalProvider provider,
            ETemporalReconstructionMode mode,
            int2 displaySize,
            int2 renderSize,
            float renderScale,
            float mipBias,
            float sharpness,
            int jitterPhaseCount,
            bool requiresJitter,
            bool requiresMotionVectors,
            bool requiresReactiveMask,
            bool requiresTransparencyMask,
            bool requiresPreExposure,
            bool requiresHistoryReset,
            ETemporalMotionConvention motionConvention = ETemporalMotionConvention.NormalizedUVFlipY,
            ETemporalDepthConvention depthConvention = ETemporalDepthConvention.ReversedZ)
        {
            this.activeBackend = activeBackend;
            this.provider = provider;
            this.mode = mode;
            this.displaySize = displaySize;
            this.renderSize = renderSize;
            this.renderScale = renderScale;
            this.mipBias = mipBias;
            this.sharpness = sharpness;
            this.jitterPhaseCount = jitterPhaseCount;
            this.requiresJitter = requiresJitter;
            this.requiresMotionVectors = requiresMotionVectors;
            this.requiresReactiveMask = requiresReactiveMask;
            this.requiresTransparencyMask = requiresTransparencyMask;
            this.requiresPreExposure = requiresPreExposure;
            this.requiresHistoryReset = requiresHistoryReset;
            this.motionConvention = motionConvention;
            this.depthConvention = depthConvention;
        }

        internal static TemporalFrameRequirements Disabled(int2 displaySize)
            => new TemporalFrameRequirements(
                activeBackend: ETemporalBackend.None,
                provider: ETemporalProvider.Native,
                mode: ETemporalReconstructionMode.NativeAA,
                displaySize: math.max(displaySize, new int2(1, 1)),
                renderSize: math.max(displaySize, new int2(1, 1)),
                renderScale: 1.0f,
                mipBias: 0.0f,
                sharpness: 0.0f,
                jitterPhaseCount: 1,
                requiresJitter: false,
                requiresMotionVectors: false,
                requiresReactiveMask: false,
                requiresTransparencyMask: false,
                requiresPreExposure: false,
                requiresHistoryReset: true);
    }
}
