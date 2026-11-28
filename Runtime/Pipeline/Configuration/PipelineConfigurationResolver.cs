using InfinityTech.Rendering.Temporal;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Single authority turning authored pipeline settings and device capabilities into the effective depth format and
    // per-field diagnostics, shared by runtime pipeline creation, Asset Inspector, build validation and stripping (P1, R8).
    internal static class PipelineConfigurationResolver
    {
        static readonly TemporalReconstructionSystem s_DefaultTemporalPlanner = new TemporalReconstructionSystem();

        const string k_TemporalLockedUpscalingReason = "The selected backend supports Upscaling only; effective mode is locked to Upscaling.";
        const string k_TemporalLockedNativeAAReason = "The selected backend supports NativeAA only; effective mode is locked to NativeAA.";

        internal static EffectivePipelineConfiguration Evaluate(in InfinityRenderPipelineAsset.Settings authored, in DeviceCapabilitySnapshot caps,
            TemporalReconstructionSystem temporalSystem = null)
        {
            string blockingConflict = null;
            TemporalReconstructionSystem planner = temporalSystem ?? s_DefaultTemporalPlanner;

            // 1. Opaque color path.
            ConfigFieldState colorPathState = ConfigFieldState.Supported;
            string colorPathFailure = !caps.supportsR8G8B8A8SRGB ? "R8G8B8A8_SRGB render target (GBuffer A) is not supported on this device."
                : !caps.supportsR11G11B10FLoadStore ? "B10G11R11_UFloatPack32 with load/store (Scene Color) is not supported on this device."
                : !caps.supportsR8G8B8A8LoadStore ? "R8G8B8A8_UNorm with load/store (sideband) is not supported on this device."
                : !caps.supportsRG16F ? "R16G16_SFloat render target (motion vectors) is not supported on this device."
                : null;
            if (colorPathFailure != null)
            {
                colorPathState = new ConfigFieldState(EConfigStatus.Unsupported, colorPathFailure);
                blockingConflict = blockingConflict ?? colorPathFailure;
            }

            // 2. Depth / stencil.
            ConfigFieldState depthState = ConfigFieldState.Supported;
            GraphicsFormat depthFormat;
            bool preferD24 = authored.depthStencilFormat == EDepthStencilFormat.D24S8;
            bool preferredSupported = preferD24 ? caps.supportsD24S8 : caps.supportsD32FS8;
            bool otherSupported = preferD24 ? caps.supportsD32FS8 : caps.supportsD24S8;
            GraphicsFormat preferredFormat = preferD24 ? GraphicsFormat.D24_UNorm_S8_UInt : GraphicsFormat.D32_SFloat_S8_UInt;
            GraphicsFormat otherFormat = preferD24 ? GraphicsFormat.D32_SFloat_S8_UInt : GraphicsFormat.D24_UNorm_S8_UInt;
            if (preferredSupported)
            {
                depthFormat = preferredFormat;
            }
            else if (otherSupported)
            {
                depthFormat = otherFormat;
                depthState = new ConfigFieldState(EConfigStatus.PlatformOverride, preferD24
                    ? "D24_UNorm_S8_UInt is unsupported on this device; the depth buffer uses D32_SFloat_S8_UInt."
                    : "D32_SFloat_S8_UInt is unsupported on this device; the depth buffer uses D24_UNorm_S8_UInt.");
            }
            else
            {
                depthFormat = preferredFormat;
                depthState = new ConfigFieldState(EConfigStatus.Unsupported, "Neither D24_UNorm_S8_UInt nor D32_SFloat_S8_UInt is supported with a readable R8_UInt stencil plane on this device.");
                blockingConflict = blockingConflict ?? depthState.reason;
            }

            // 3. Subsurface profiles.
            ConfigFieldState subsurfaceState = ConfigFieldState.Supported;
            if (authored.diffusionProfileCount > EffectivePipelineConfiguration.MaxDiffusionProfiles)
            {
                subsurfaceState = new ConfigFieldState(EConfigStatus.Conflict, "At most 15 Diffusion Profiles fit the sideband's four-bit profile; remove the extra profiles from the pipeline asset.");
                blockingConflict = blockingConflict ?? subsurfaceState.reason;
            }
            else if (!authored.hasDiffusionProfiles)
            {
                subsurfaceState = new ConfigFieldState(EConfigStatus.Inactive, "Assign at least one Diffusion Profile to enable Subsurface Scattering.");
            }

            // 4. Reflection and Analytic Shadow capabilities.
            ConfigFieldState reflectionState = authored.reflectionProbeResolution > 0 && authored.maxReflectionProbes > 0
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Inactive, "Reflection probe atlas is disabled (resolution or max probe slots == 0); sky & raster reflections remain active.");

            ConfigFieldState analyticShadowState = authored.enableAnalyticShadow
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Inactive, "Enable Analytic Shadow to activate primitive indirect occlusion and direct soft shadows.");
            ConfigFieldState analyticIndirectState = !authored.enableAnalyticShadow
                ? new ConfigFieldState(EConfigStatus.Inactive, "Analytic Shadow master toggle is disabled.")
                : ConfigFieldState.Supported;
            ConfigFieldState analyticDirectState = !authored.enableAnalyticShadow
                ? new ConfigFieldState(EConfigStatus.Inactive, "Analytic Shadow master toggle is disabled.")
                : ConfigFieldState.Supported;

            // 5. Temporal Reconstruction (TRC) backend, capability-driven mode lock, quality preset & mip bias.
            TemporalBackendCapability requestedTrc = planner.GetCapability(authored.temporal.backend);
            ConfigFieldState temporalBackendState = requestedTrc.supported
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Unsupported, planner.GetBackendFieldMessage(authored.temporal.backend));
            TemporalFrameRequirements previewReq = planner.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, ETemporalCameraOverride.FollowPipeline, authored.temporal, false),
                out TemporalBackendCapability effectiveCap);

            ConfigFieldState temporalModeState;
            if (!previewReq.isActive)
            {
                temporalModeState = new ConfigFieldState(EConfigStatus.Inactive, "Temporal Reconstruction is disabled.");
            }
            else if (effectiveCap.HasSingleSupportedMode(out ETemporalReconstructionMode onlyMode))
            {
                temporalModeState = new ConfigFieldState(
                    EConfigStatus.Required,
                    onlyMode == ETemporalReconstructionMode.Upscaling ? k_TemporalLockedUpscalingReason : k_TemporalLockedNativeAAReason);
            }
            else
            {
                temporalModeState = ConfigFieldState.Supported;
            }

            // Bind Quality availability strictly to effective mode so Upscaling-only backends never deadlock with an authored NativeAA value.
            ConfigFieldState temporalQualityState = !previewReq.isActive
                ? new ConfigFieldState(EConfigStatus.Inactive, "Temporal Reconstruction is disabled.")
                : previewReq.mode != ETemporalReconstructionMode.Upscaling
                    ? new ConfigFieldState(EConfigStatus.Inactive, "NativeAA renders at fixed 1.0x resolution; switch Reconstruction Mode to Upscaling to select a Quality tier.")
                    : ConfigFieldState.Supported;

            ConfigFieldState temporalSharpnessState = !previewReq.isActive
                ? new ConfigFieldState(EConfigStatus.Inactive, "Temporal Reconstruction is disabled.")
                : ConfigFieldState.Supported;

            float qualityScale = previewReq.isActive
                ? planner.GetQualityScale(previewReq.activeBackend, authored.temporal.quality)
                : 1.0f;

            int geometryColorBits = BitsPerPixel(EffectivePipelineConfiguration.GBufferAFormat) + BitsPerPixel(EffectivePipelineConfiguration.GBufferBFormat)
                + BitsPerPixel(EffectivePipelineConfiguration.SceneColorFormat) + BitsPerPixel(EffectivePipelineConfiguration.SidebandFormat);

            return new EffectivePipelineConfiguration(
                enableRenderingLayers: authored.enableRenderingLayers,
                depthStencilFormat: depthFormat,
                geometryColorBitsPerPixel: geometryColorBits,
                requestedTemporalBackend: authored.temporal.backend,
                effectiveTemporalBackend: previewReq.activeBackend,
                effectiveTemporalMode: previewReq.mode,
                effectiveRenderScale: previewReq.renderScale,
                effectiveTemporalMipBias: previewReq.mipBias,
                effectiveQualityScale: qualityScale,
                colorPathState: colorPathState,
                depthStencilState: depthState,
                subsurfaceState: subsurfaceState,
                reflectionState: reflectionState,
                analyticShadowState: analyticShadowState,
                analyticIndirectState: analyticIndirectState,
                analyticDirectState: analyticDirectState,
                temporalBackendState: temporalBackendState,
                temporalModeState: temporalModeState,
                temporalQualityState: temporalQualityState,
                temporalSharpnessState: temporalSharpnessState,
                blockingConflict: blockingConflict);
        }

        static int BitsPerPixel(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R8G8B8A8_SRGB:
                case GraphicsFormat.R8G8B8A8_UNorm:
                case GraphicsFormat.B10G11R11_UFloatPack32:
                    return 32;
                default:
                    return 32;
            }
        }
    }
}
