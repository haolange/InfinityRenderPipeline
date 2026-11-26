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
        internal readonly string unsupportedReason;

        internal TemporalBackendCapability(ETemporalProvider provider, ETemporalBackend backend, string displayName, bool supported, string unsupportedReason = null)
        {
            this.provider = provider;
            this.backend = backend;
            this.displayName = displayName;
            this.supported = supported;
            this.unsupportedReason = unsupportedReason;
        }
    }

    internal interface ITemporalReconstructionBackend
    {
        TemporalBackendCapability Capability { get; }
        TemporalFrameRequirements PlanFrame(in TemporalPlanInput input);
    }

    /// <summary>
    /// Pipeline-lifetime registry and planner for unified Temporal Reconstruction (PRD §9).
    /// Resolves backend support honestly and computes <see cref="TemporalFrameRequirements"/> before CameraSetup.
    /// </summary>
    internal sealed class TemporalReconstructionSystem
    {
        readonly ITemporalReconstructionBackend[] m_Backends = new ITemporalReconstructionBackend[6];

        internal TemporalReconstructionSystem()
        {
            RegisterBackend(new BuiltinNativeTaaPlanner());
            RegisterBackend(new BuiltinNativeTaauPlanner());
        }

        internal void RegisterBackend(ITemporalReconstructionBackend backend)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }
            m_Backends[(int)backend.Capability.backend] = backend;
        }

        internal TemporalBackendCapability GetCapability(ETemporalBackend backend, bool superResolutionAvailable = true)
        {
            switch (backend)
            {
                case ETemporalBackend.None:
                    return new TemporalBackendCapability(ETemporalProvider.Native, ETemporalBackend.None, "Off", true);
                case ETemporalBackend.NativeTAA:
                    return m_Backends[(int)ETemporalBackend.NativeTAA]?.Capability
                        ?? new TemporalBackendCapability(ETemporalProvider.Native, ETemporalBackend.NativeTAA, "Native TAA", false, "Native TAA planner is not registered.");
                case ETemporalBackend.NativeTAAU:
                    return superResolutionAvailable
                        ? (m_Backends[(int)ETemporalBackend.NativeTAAU]?.Capability
                            ?? new TemporalBackendCapability(ETemporalProvider.Native, ETemporalBackend.NativeTAAU, "Native TAAU / TSR", false, "Native TAAU planner is not registered."))
                        : new TemporalBackendCapability(ETemporalProvider.Native, ETemporalBackend.NativeTAAU, "Native TAAU / TSR", false, "Super Resolution kernel or camera capability is inactive.");
                case ETemporalBackend.DLSS:
                    return m_Backends[(int)ETemporalBackend.DLSS]?.Capability
                        ?? new TemporalBackendCapability(ETemporalProvider.NVIDIA, ETemporalBackend.DLSS, "NVIDIA DLSS / DLAA", false, "NVIDIA Streamline / NGX native SDK backend is not integrated on this device; effective backend falls back to Native TAA.");
                case ETemporalBackend.FSR:
                    return m_Backends[(int)ETemporalBackend.FSR]?.Capability
                        ?? new TemporalBackendCapability(ETemporalProvider.AMD, ETemporalBackend.FSR, "AMD FSR", false, "AMD FidelityFX Super Resolution native SDK backend is not integrated; effective backend falls back to Native TAA.");
                case ETemporalBackend.XeSS:
                    return m_Backends[(int)ETemporalBackend.XeSS]?.Capability
                        ?? new TemporalBackendCapability(ETemporalProvider.Intel, ETemporalBackend.XeSS, "Intel XeSS", false, "Intel XeSS native SDK backend is not integrated; effective backend falls back to Native TAA.");
                default:
                    return new TemporalBackendCapability(ETemporalProvider.Custom, backend, backend.ToString(), false, "Unknown temporal backend.");
            }
        }

        internal TemporalFrameRequirements PlanFrame(in TemporalPlanInput input, out TemporalBackendCapability resolvedCapability)
        {
            if (!input.temporalAllowed || input.settings.backend == ETemporalBackend.None)
            {
                resolvedCapability = GetCapability(ETemporalBackend.None, input.superResolutionAllowed);
                return TemporalFrameRequirements.Disabled(input.displaySize);
            }

            TemporalBackendCapability requestedCap = GetCapability(input.settings.backend, input.superResolutionAllowed);
            ETemporalBackend selected = requestedCap.supported ? input.settings.backend : ETemporalBackend.NativeTAA;
            if (selected == ETemporalBackend.NativeTAAU && !input.superResolutionAllowed)
            {
                selected = ETemporalBackend.NativeTAA;
            }

            ITemporalReconstructionBackend planner = m_Backends[(int)selected];
            resolvedCapability = GetCapability(selected, input.superResolutionAllowed);
            return planner != null
                ? planner.PlanFrame(in input)
                : TemporalFrameRequirements.Disabled(input.displaySize);
        }

        internal static float ResolveQualityPresetScale(ETemporalQuality quality, ETemporalReconstructionMode mode, float customScale)
        {
            if (mode == ETemporalReconstructionMode.NativeAA || quality == ETemporalQuality.Native)
            {
                return 1.0f;
            }
            switch (quality)
            {
                case ETemporalQuality.UltraPerformance: return 0.50f;
                case ETemporalQuality.Performance: return 0.58f;
                case ETemporalQuality.Balanced: return 0.67f;
                case ETemporalQuality.Quality: return 0.77f;
                case ETemporalQuality.UltraQuality: return 0.85f;
                case ETemporalQuality.Custom: return math.clamp(customScale, 0.5f, 1.0f);
                default: return math.clamp(customScale, 0.5f, 1.0f);
            }
        }

        internal static float ComputeMipBias(float renderScale)
        {
            return renderScale < 0.999f ? math.log2(math.max(renderScale, 0.25f)) : 0.0f;
        }

        sealed class BuiltinNativeTaaPlanner : ITemporalReconstructionBackend
        {
            static readonly TemporalBackendCapability s_Cap =
                new TemporalBackendCapability(ETemporalProvider.Native, ETemporalBackend.NativeTAA, "Native TAA", true);

            public TemporalBackendCapability Capability => s_Cap;

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input)
            {
                int2 display = input.displaySize;
                return new TemporalFrameRequirements(
                    activeBackend: ETemporalBackend.NativeTAA,
                    provider: ETemporalProvider.Native,
                    mode: ETemporalReconstructionMode.NativeAA,
                    displaySize: display,
                    renderSize: display,
                    renderScale: 1.0f,
                    mipBias: 0.0f,
                    sharpness: math.clamp(input.settings.sharpness, 0.0f, 1.0f),
                    jitterPhaseCount: 8,
                    requiresJitter: true,
                    requiresMotionVectors: true,
                    requiresPreviousDepth: true,
                    requiresReactiveMask: true,
                    requiresTransparencyMask: false,
                    requiresPreExposure: false,
                    requiresHistoryReset: input.forceHistoryReset);
            }
        }

        sealed class BuiltinNativeTaauPlanner : ITemporalReconstructionBackend
        {
            static readonly TemporalBackendCapability s_Cap =
                new TemporalBackendCapability(ETemporalProvider.Native, ETemporalBackend.NativeTAAU, "Native TAAU / TSR", true);

            public TemporalBackendCapability Capability => s_Cap;

            public TemporalFrameRequirements PlanFrame(in TemporalPlanInput input)
            {
                int2 display = input.displaySize;
                float scale = input.settings.resolutionPolicy == ERenderResolutionPolicy.QualityPreset
                    ? ResolveQualityPresetScale(input.settings.quality, input.settings.mode, input.settings.customRenderScale)
                    : (input.settings.mode == ETemporalReconstructionMode.NativeAA ? 1.0f : math.clamp(input.settings.customRenderScale, 0.5f, 1.0f));
                int2 render = new int2(
                    math.max(1, (int)math.ceil(display.x * scale)),
                    math.max(1, (int)math.ceil(display.y * scale)));
                int phases = scale < 0.7f ? 16 : 8;
                return new TemporalFrameRequirements(
                    activeBackend: ETemporalBackend.NativeTAAU,
                    provider: ETemporalProvider.Native,
                    mode: math.any(render != display) ? ETemporalReconstructionMode.Upscaling : input.settings.mode,
                    displaySize: display,
                    renderSize: render,
                    renderScale: scale,
                    mipBias: ComputeMipBias(scale),
                    sharpness: math.clamp(input.settings.sharpness, 0.0f, 1.0f),
                    jitterPhaseCount: phases,
                    requiresJitter: true,
                    requiresMotionVectors: true,
                    requiresPreviousDepth: true,
                    requiresReactiveMask: false,
                    requiresTransparencyMask: false,
                    requiresPreExposure: false,
                    requiresHistoryReset: input.forceHistoryReset);
            }
        }
    }
}
