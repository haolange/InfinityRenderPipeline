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

        internal static EffectivePipelineConfiguration Evaluate(in InfinityRenderPipelineAsset.Settings authored, in DeviceCapabilitySnapshot caps,
            TemporalReconstructionSystem temporalSystem = null)
        {
            string blockingConflict = null;
            TemporalReconstructionSystem planner = temporalSystem ?? s_DefaultTemporalPlanner;

            // 1. Opaque color path: fixed RGBA8 sRGB + RGBA8 GBuffer, R11G11B10F Scene Color read and written in place by
            // the atmosphere, RGBA8 sideband written by geometry, RG16F motion.
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

            // 2. Depth / stencil: the stencil holds the motion, deferred and subsurface admission bits.
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

            // 3. Subsurface profiles: skin pixels store profile 1..15 in the sideband.
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

            // 5. Temporal Reconstruction (TRC) backend, mode, quality and render scale resolution.
            TemporalBackendCapability requestedTrc = planner.GetCapability(authored.temporal.backend);
            ConfigFieldState temporalBackendState = requestedTrc.supported
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Unsupported, requestedTrc.unsupportedReason);
            TemporalFrameRequirements previewReq = planner.PlanFrame(
                new TemporalPlanInput(new int2(1920, 1080), true, true, authored.temporal, false),
                out _);

            ConfigFieldState temporalModeState = !previewReq.isActive
                ? new ConfigFieldState(EConfigStatus.Inactive, "Temporal Reconstruction is disabled.")
                : (authored.temporal.backend == ETemporalBackend.NativeTAA && authored.temporal.mode != ETemporalReconstructionMode.NativeAA)
                    ? new ConfigFieldState(EConfigStatus.Inactive, "Native TAA executes at NativeAA resolution; switch to Native TAAU for Upscaling.")
                    : ConfigFieldState.Supported;

            bool scaleDrivenByQuality = authored.temporal.resolutionPolicy == ERenderResolutionPolicy.QualityPreset && previewReq.activeBackend == ETemporalBackend.NativeTAAU;
            ConfigFieldState temporalQualityState = !previewReq.isActive || previewReq.activeBackend != ETemporalBackend.NativeTAAU
                ? new ConfigFieldState(EConfigStatus.Inactive, "Quality Preset applies when an upscaling backend (Native TAAU / external upscaler) is selected.")
                : ConfigFieldState.Supported;
            ConfigFieldState renderScaleState = !previewReq.isActive
                ? new ConfigFieldState(EConfigStatus.Inactive, "Temporal Reconstruction is disabled.")
                : previewReq.activeBackend == ETemporalBackend.NativeTAA
                    ? new ConfigFieldState(EConfigStatus.Inactive, "Native TAA runs at fixed 1.0× display resolution.")
                    : scaleDrivenByQuality
                        ? new ConfigFieldState(EConfigStatus.Inactive, "Render Scale is driven automatically by Quality Preset; switch Resolution Policy to FixedScale to customize.")
                        : ConfigFieldState.Supported;

            int geometryColorBits = BitsPerPixel(EffectivePipelineConfiguration.GBufferAFormat) + BitsPerPixel(EffectivePipelineConfiguration.GBufferBFormat)
                + BitsPerPixel(EffectivePipelineConfiguration.SceneColorFormat) + BitsPerPixel(EffectivePipelineConfiguration.SidebandFormat);

            return new EffectivePipelineConfiguration(
                enableRenderingLayers: authored.enableRenderingLayers,
                depthStencilFormat: depthFormat,
                geometryColorBitsPerPixel: geometryColorBits,
                effectiveTemporalBackend: previewReq.activeBackend,
                effectiveTemporalMode: previewReq.mode,
                effectiveRenderScale: previewReq.renderScale,
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
                renderScaleState: renderScaleState,
                blockingConflict: blockingConflict);
        }

        internal static int BitsPerPixel(GraphicsFormat format)
        {
            return format == GraphicsFormat.None ? 0 : (int)GraphicsFormatUtility.GetBlockSize(format) * 8;
        }
    }
}
