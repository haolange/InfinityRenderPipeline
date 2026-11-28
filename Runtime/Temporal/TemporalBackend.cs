using System;
using Unity.Mathematics;

namespace InfinityTech.Rendering.Temporal
{
    internal readonly struct TemporalBackendCapability
    {
        internal readonly ETemporalProvider provider;
        internal readonly ETemporalBackend backend;
        internal readonly string displayName;
        internal readonly bool supported;
        internal readonly ETemporalModeMask supportedModes;
        internal readonly float minRenderScale;
        internal readonly string platformRequirement;
        internal readonly string unsupportedReason;

        internal bool SupportsMode(ETemporalReconstructionMode mode)
        {
            ETemporalModeMask bit = mode == ETemporalReconstructionMode.NativeAA ? ETemporalModeMask.NativeAA : ETemporalModeMask.Upscaling;
            return (supportedModes & bit) != 0;
        }

        internal bool HasSingleSupportedMode(out ETemporalReconstructionMode onlyMode)
        {
            if (supportedModes == ETemporalModeMask.NativeAA)
            {
                onlyMode = ETemporalReconstructionMode.NativeAA;
                return true;
            }
            if (supportedModes == ETemporalModeMask.Upscaling)
            {
                onlyMode = ETemporalReconstructionMode.Upscaling;
                return true;
            }
            onlyMode = ETemporalReconstructionMode.NativeAA;
            return false;
        }

        internal TemporalBackendCapability(
            ETemporalProvider provider,
            ETemporalBackend backend,
            string displayName,
            bool supported,
            ETemporalModeMask supportedModes,
            float minRenderScale,
            string platformRequirement,
            string unsupportedReason = null)
        {
            this.provider = provider;
            this.backend = backend;
            this.displayName = displayName;
            this.supported = supported;
            this.supportedModes = supportedModes;
            this.minRenderScale = minRenderScale;
            this.platformRequirement = platformRequirement;
            this.unsupportedReason = unsupportedReason;
        }
    }

    internal interface ITemporalBackendPlanner
    {
        TemporalBackendCapability Capability { get; }
        float ResolveQualityScale(ETemporalQuality quality);
        TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, ETemporalBackend requestedBackend);
    }

    internal sealed class TemporalReconstructionSystem
    {
        static readonly int s_BackendCount = Enum.GetValues(typeof(ETemporalBackend)).Length;
        static readonly ETemporalBackend[] s_CrossPlatformFallbackOrder =
        {
            ETemporalBackend.FSR,
            ETemporalBackend.STP,
            ETemporalBackend.Native,
        };

        readonly ITemporalBackendPlanner[] m_Planners = new ITemporalBackendPlanner[s_BackendCount];

        internal TemporalReconstructionSystem() : this(TemporalDeviceCapabilities.AllCrossPlatformSupported)
        {
        }

        internal TemporalReconstructionSystem(in TemporalDeviceCapabilities deviceCaps)
        {
            RegisterPlanner(new NativeBackendPlanner(deviceCaps.nativeSupported, deviceCaps.nativeUnavailableReason));
            RegisterPlanner(new StpBackendPlanner(deviceCaps.stpSupported, deviceCaps.stpUnavailableReason));
            RegisterPlanner(new FsrBackendPlanner(deviceCaps.fsrSupported, deviceCaps.fsrUnavailableReason));
            RegisterPlanner(new SgsrBackendPlanner(deviceCaps.sgsrSupported, deviceCaps.sgsrUnavailableReason));
            RegisterPlanner(new UnintegratedSlotPlanner(
                ETemporalProvider.Intel,
                ETemporalBackend.XeSS,
                "Intel XeSS",
                deviceCaps.xessSupported,
                "Windows DX12/Vulkan with Intel XeSS SDK native plugin (Arc XMX or DP4a SM6.4+)."));
            RegisterPlanner(new UnintegratedSlotPlanner(
                ETemporalProvider.NVIDIA,
                ETemporalBackend.DLSS,
                "NVIDIA DLSS / DLAA",
                deviceCaps.dlssSupported,
                "Windows DX12/Vulkan with NVIDIA RTX GPU and Unity NVIDIA module (ENABLE_NVIDIA)."));
        }

        internal void RegisterPlanner(ITemporalBackendPlanner planner)
        {
            if (planner == null)
            {
                throw new ArgumentNullException(nameof(planner));
            }
            m_Planners[(int)planner.Capability.backend] = planner;
        }

        internal TemporalBackendCapability GetCapability(ETemporalBackend backend, bool upscalingAllowed = true)
        {
            if (backend == ETemporalBackend.None)
            {
                return new TemporalBackendCapability(
                    ETemporalProvider.Native,
                    ETemporalBackend.None,
                    "Off",
                    true,
                    ETemporalModeMask.NativeAA,
                    1.0f,
                    "Cross-platform.");
            }

            ITemporalBackendPlanner planner = (uint)backend < (uint)m_Planners.Length ? m_Planners[(int)backend] : null;
            if (planner == null)
            {
                return new TemporalBackendCapability(
                    ETemporalProvider.Native,
                    backend,
                    backend.ToString(),
                    false,
                    ETemporalModeMask.None,
                    1.0f,
                    "Unregistered backend.",
                    $"Temporal backend '{backend}' is not registered.");
            }

            TemporalBackendCapability cap = planner.Capability;
            if (cap.supported && cap.supportedModes == ETemporalModeMask.Upscaling && !upscalingAllowed)
            {
                return new TemporalBackendCapability(
                    cap.provider,
                    cap.backend,
                    cap.displayName,
                    false,
                    cap.supportedModes,
                    cap.minRenderScale,
                    cap.platformRequirement,
                    $"{cap.displayName} supports Upscaling only, which is disabled on this view.");
            }
            return cap;
        }

        internal ETemporalBackend ResolveEffectiveBackend(ETemporalBackend requested, bool upscalingAllowed = true, ETemporalCameraOverride cameraOverride = ETemporalCameraOverride.FollowPipeline)
        {
            if (requested == ETemporalBackend.None || cameraOverride == ETemporalCameraOverride.Off)
            {
                return ETemporalBackend.None;
            }

            ETemporalModeMask requiredModeMask = cameraOverride == ETemporalCameraOverride.ForceNativeAA || !upscalingAllowed
                ? ETemporalModeMask.NativeAA
                : ETemporalModeMask.None;

            TemporalBackendCapability requestedCap = GetCapability(requested, upscalingAllowed);
            if (requestedCap.supported)
            {
                if (requiredModeMask == ETemporalModeMask.None || (requestedCap.supportedModes & requiredModeMask) != 0)
                {
                    return requested;
                }
                if (requiredModeMask == ETemporalModeMask.NativeAA && GetCapability(ETemporalBackend.Native, upscalingAllowed).supported)
                {
                    return ETemporalBackend.Native;
                }
            }

            for (int i = 0; i < s_CrossPlatformFallbackOrder.Length; ++i)
            {
                ETemporalBackend candidate = s_CrossPlatformFallbackOrder[i];
                if (candidate == requested)
                {
                    continue;
                }
                TemporalBackendCapability cap = GetCapability(candidate, upscalingAllowed);
                if (cap.supported && (requiredModeMask == ETemporalModeMask.None || (cap.supportedModes & requiredModeMask) != 0))
                {
                    return candidate;
                }
            }
            return ETemporalBackend.None;
        }

        internal string GetBackendFieldMessage(ETemporalBackend requested, bool upscalingAllowed = true)
        {
            TemporalBackendCapability cap = GetCapability(requested, upscalingAllowed);
            if (cap.supported)
            {
                return null;
            }

            ETemporalBackend fallback = ResolveEffectiveBackend(requested, upscalingAllowed);
            string fallbackLabel = fallback == ETemporalBackend.None ? "Off" : GetCapability(fallback, upscalingAllowed).displayName;
            string reason = string.IsNullOrEmpty(cap.unsupportedReason) ? cap.platformRequirement : cap.unsupportedReason;
            return $"{reason} Effective fallback on this device: {fallbackLabel}.";
        }

        internal float GetQualityScale(ETemporalBackend backend, ETemporalQuality quality)
        {
            ITemporalBackendPlanner planner = (uint)backend < (uint)m_Planners.Length ? m_Planners[(int)backend] : null;
            return planner != null ? planner.ResolveQualityScale(quality) : ResolveStandardQualityScale(quality);
        }

        internal TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, out TemporalBackendCapability resolvedCapability)
        {
            if (!input.temporalAllowed || input.settings.backend == ETemporalBackend.None || input.cameraOverride == ETemporalCameraOverride.Off)
            {
                resolvedCapability = GetCapability(ETemporalBackend.None, input.upscalingAllowed);
                return TemporalFrameRequirements.Disabled(input.displaySize, input.settings.backend);
            }

            ETemporalBackend effectiveBackend = ResolveEffectiveBackend(input.settings.backend, input.upscalingAllowed, input.cameraOverride);
            if (effectiveBackend == ETemporalBackend.None)
            {
                resolvedCapability = GetCapability(ETemporalBackend.None, input.upscalingAllowed);
                return TemporalFrameRequirements.Disabled(input.displaySize, input.settings.backend);
            }

            ITemporalBackendPlanner planner = m_Planners[(int)effectiveBackend];
            resolvedCapability = GetCapability(effectiveBackend, input.upscalingAllowed);
            return planner.PlanFrame(in input, input.settings.backend);
        }

        internal static float ResolveStandardQualityScale(ETemporalQuality quality)
        {
            switch (quality)
            {
                case ETemporalQuality.UltraPerformance: return 0.50f;
                case ETemporalQuality.Performance:      return 0.58f;
                case ETemporalQuality.Balanced:         return 0.67f;
                case ETemporalQuality.Quality:          return 0.77f;
                case ETemporalQuality.UltraQuality:     return 0.85f;
                default:                                return 0.77f;
            }
        }

        internal static float ResolveFsrQualityScale(ETemporalQuality quality)
        {
            switch (quality)
            {
                case ETemporalQuality.UltraPerformance: return 1.0f / 3.0f;
                case ETemporalQuality.Performance:      return 0.50f;
                case ETemporalQuality.Balanced:         return 1.0f / 1.7f;
                case ETemporalQuality.Quality:          return 1.0f / 1.5f;
                case ETemporalQuality.UltraQuality:     return 0.85f;
                default:                                return 1.0f / 1.5f;
            }
        }

        internal static ETemporalReconstructionMode ResolveEffectiveMode(in TemporalBackendCapability cap, in TemporalPlanInput input)
        {
            if (input.cameraOverride == ETemporalCameraOverride.ForceNativeAA || !input.upscalingAllowed)
            {
                return ETemporalReconstructionMode.NativeAA;
            }
            if (cap.HasSingleSupportedMode(out ETemporalReconstructionMode onlyMode))
            {
                return onlyMode;
            }
            return input.settings.mode;
        }

        internal static int2 ComputeRenderSize(int2 displaySize, float scale)
        {
            return new int2(
                math.max(1, (int)math.ceil(displaySize.x * scale)),
                math.max(1, (int)math.ceil(displaySize.y * scale)));
        }

        internal static int ComputeRatioPhases(float scale, int minPhases = 8, int maxPhases = 64)
        {
            float ratio = 1.0f / math.max(scale, 0.25f);
            int phases = (int)math.floor(8.0f * ratio * ratio + 0.5f);
            return math.clamp(phases, minPhases, maxPhases);
        }

        sealed class NativeBackendPlanner : ITemporalBackendPlanner
        {
            readonly TemporalBackendCapability m_Cap;

            internal NativeBackendPlanner(bool supported, string unavailableReason)
            {
                m_Cap = new TemporalBackendCapability(
                    ETemporalProvider.Native,
                    ETemporalBackend.Native,
                    "Infinity Native",
                    supported,
                    ETemporalModeMask.All,
                    0.50f,
                    "Cross-platform compute shaders.",
                    supported ? null : (unavailableReason ?? "Native temporal compute shaders are unavailable."));
            }

            public TemporalBackendCapability Capability => m_Cap;

            public float ResolveQualityScale(ETemporalQuality quality) => ResolveStandardQualityScale(quality);

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, ETemporalBackend requestedBackend)
            {
                ETemporalReconstructionMode mode = ResolveEffectiveMode(in m_Cap, in input);
                bool upscaling = mode == ETemporalReconstructionMode.Upscaling;
                float scale = upscaling ? math.max(m_Cap.minRenderScale, ResolveQualityScale(input.settings.quality)) : 1.0f;
                int2 render = upscaling ? ComputeRenderSize(input.displaySize, scale) : input.displaySize;
                float mipBias = upscaling ? math.log2(math.max(scale, 0.25f)) : 0.0f;
                int phases = upscaling ? ComputeRatioPhases(scale, 8, 32) : 8;
                float jitterSpread = upscaling ? 1.0f : 0.75f;
                return new TemporalFrameRequirements(
                    requestedBackend: requestedBackend,
                    activeBackend: ETemporalBackend.Native,
                    provider: m_Cap.provider,
                    mode: mode,
                    quality: input.settings.quality,
                    displaySize: input.displaySize,
                    renderSize: render,
                    renderScale: scale,
                    mipBias: mipBias,
                    sharpness: input.settings.sharpness,
                    jitterPhaseCount: phases,
                    jitterSpread: jitterSpread,
                    requiresJitter: true,
                    requiresMotionVectors: true,
                    requiresReactiveMask: true,
                    requiresTransparencyMask: false,
                    requiresPreExposure: false,
                    requiresThreeFrameHistory: false,
                    requiresHistoryReset: input.forceHistoryReset);
            }
        }

        sealed class StpBackendPlanner : ITemporalBackendPlanner
        {
            readonly TemporalBackendCapability m_Cap;

            internal StpBackendPlanner(bool supported, string unavailableReason)
            {
                m_Cap = new TemporalBackendCapability(
                    ETemporalProvider.Unity,
                    ETemporalBackend.STP,
                    "Unity STP",
                    supported,
                    ETemporalModeMask.All,
                    0.50f,
                    "Cross-platform compute shaders (DX12 / Vulkan / Metal; excludes OpenGLES3).",
                    supported ? null : (unavailableReason ?? "STP compute shader or blue-noise tile is unavailable."));
            }

            public TemporalBackendCapability Capability => m_Cap;

            public float ResolveQualityScale(ETemporalQuality quality) => ResolveStandardQualityScale(quality);

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, ETemporalBackend requestedBackend)
            {
                ETemporalReconstructionMode mode = ResolveEffectiveMode(in m_Cap, in input);
                float scale = mode == ETemporalReconstructionMode.Upscaling ? math.max(m_Cap.minRenderScale, ResolveQualityScale(input.settings.quality)) : 1.0f;
                int2 render = mode == ETemporalReconstructionMode.Upscaling ? ComputeRenderSize(input.displaySize, scale) : input.displaySize;
                float mipBias = mode == ETemporalReconstructionMode.Upscaling ? math.log2(math.max(scale, 0.25f)) : 0.0f;
                return new TemporalFrameRequirements(
                    requestedBackend: requestedBackend,
                    activeBackend: ETemporalBackend.STP,
                    provider: m_Cap.provider,
                    mode: mode,
                    quality: input.settings.quality,
                    displaySize: input.displaySize,
                    renderSize: render,
                    renderScale: scale,
                    mipBias: mipBias,
                    sharpness: input.settings.sharpness,
                    jitterPhaseCount: 16,
                    jitterSpread: 1.0f,
                    requiresJitter: true,
                    requiresMotionVectors: true,
                    requiresReactiveMask: true,
                    requiresTransparencyMask: false,
                    requiresPreExposure: false,
                    requiresThreeFrameHistory: true,
                    requiresHistoryReset: input.forceHistoryReset);
            }
        }

        sealed class FsrBackendPlanner : ITemporalBackendPlanner
        {
            readonly TemporalBackendCapability m_Cap;

            internal FsrBackendPlanner(bool supported, string unavailableReason)
            {
                m_Cap = new TemporalBackendCapability(
                    ETemporalProvider.AMD,
                    ETemporalBackend.FSR,
                    "AMD FSR 3",
                    supported,
                    ETemporalModeMask.All,
                    1.0f / 3.0f,
                    "Cross-platform compute shaders (pure HLSL FSR 3.1 upscaler, no Frame Generation).",
                    supported ? null : (unavailableReason ?? "FSR 3 compute shader is unavailable."));
            }

            public TemporalBackendCapability Capability => m_Cap;

            public float ResolveQualityScale(ETemporalQuality quality) => ResolveFsrQualityScale(quality);

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, ETemporalBackend requestedBackend)
            {
                ETemporalReconstructionMode mode = ResolveEffectiveMode(in m_Cap, in input);
                float scale = mode == ETemporalReconstructionMode.Upscaling ? math.max(m_Cap.minRenderScale, ResolveQualityScale(input.settings.quality)) : 1.0f;
                int2 render = mode == ETemporalReconstructionMode.Upscaling ? ComputeRenderSize(input.displaySize, scale) : input.displaySize;
                float mipBias = mode == ETemporalReconstructionMode.Upscaling ? math.log2(math.max(scale, 0.25f)) - 1.0f : 0.0f;
                float ratio = (float)input.displaySize.x / render.x;
                return new TemporalFrameRequirements(
                    requestedBackend: requestedBackend,
                    activeBackend: ETemporalBackend.FSR,
                    provider: m_Cap.provider,
                    mode: mode,
                    quality: input.settings.quality,
                    displaySize: input.displaySize,
                    renderSize: render,
                    renderScale: scale,
                    mipBias: mipBias,
                    sharpness: input.settings.sharpness,
                    jitterPhaseCount: math.max(1, (int)(8f * ratio * ratio)),
                    jitterSpread: 1.0f,
                    requiresJitter: true,
                    requiresMotionVectors: true,
                    requiresReactiveMask: true,
                    requiresTransparencyMask: false,
                    requiresPreExposure: false,
                    requiresThreeFrameHistory: false,
                    requiresHistoryReset: input.forceHistoryReset);
            }
        }

        sealed class SgsrBackendPlanner : ITemporalBackendPlanner
        {
            readonly TemporalBackendCapability m_Cap;

            internal SgsrBackendPlanner(bool supported, string unavailableReason)
            {
                m_Cap = new TemporalBackendCapability(
                    ETemporalProvider.Qualcomm,
                    ETemporalBackend.SGSR,
                    "Qualcomm SGSR v2",
                    supported,
                    ETemporalModeMask.Upscaling,
                    0.50f,
                    "Cross-platform compute shaders (3-pass SGSR v2 mobile-oriented upscaler; Upscaling mode only).",
                    supported ? null : (unavailableReason ?? "SGSR v2 compute shader is unavailable."));
            }

            public TemporalBackendCapability Capability => m_Cap;

            public float ResolveQualityScale(ETemporalQuality quality) => ResolveStandardQualityScale(quality);

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, ETemporalBackend requestedBackend)
            {
                float scale = math.max(m_Cap.minRenderScale, ResolveQualityScale(input.settings.quality));
                int2 render = ComputeRenderSize(input.displaySize, scale);
                float mipBias = math.log2(math.max(scale, 0.25f)) - 0.5f;
                return new TemporalFrameRequirements(
                    requestedBackend: requestedBackend,
                    activeBackend: ETemporalBackend.SGSR,
                    provider: m_Cap.provider,
                    mode: ETemporalReconstructionMode.Upscaling,
                    quality: input.settings.quality,
                    displaySize: input.displaySize,
                    renderSize: render,
                    renderScale: scale,
                    mipBias: mipBias,
                    sharpness: input.settings.sharpness,
                    jitterPhaseCount: 32,
                    jitterSpread: 1.0f,
                    requiresJitter: true,
                    requiresMotionVectors: true,
                    requiresReactiveMask: true,
                    requiresTransparencyMask: false,
                    requiresPreExposure: false,
                    requiresThreeFrameHistory: false,
                    requiresHistoryReset: input.forceHistoryReset);
            }
        }

        sealed class UnintegratedSlotPlanner : ITemporalBackendPlanner
        {
            readonly TemporalBackendCapability m_Cap;

            internal UnintegratedSlotPlanner(
                ETemporalProvider provider,
                ETemporalBackend backend,
                string displayName,
                bool supported,
                string platformRequirement)
            {
                m_Cap = new TemporalBackendCapability(
                    provider,
                    backend,
                    displayName,
                    supported,
                    ETemporalModeMask.All,
                    0.3333f,
                    platformRequirement,
                    supported ? null : $"{displayName} requires {platformRequirement}");
            }

            public TemporalBackendCapability Capability => m_Cap;

            public float ResolveQualityScale(ETemporalQuality quality) => ResolveFsrQualityScale(quality);

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, ETemporalBackend requestedBackend)
            {
                return TemporalFrameRequirements.Disabled(input.displaySize, requestedBackend);
            }
        }
    }
}
