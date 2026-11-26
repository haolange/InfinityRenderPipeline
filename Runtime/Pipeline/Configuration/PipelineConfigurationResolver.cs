using InfinityTech.Rendering.Temporal;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Single authority turning authored pipeline settings and device capabilities into effective attachment formats and
    // per-field diagnostics, shared by runtime pipeline creation, Asset Inspector, build validation and stripping (P1, R8).
    internal static class PipelineConfigurationResolver
    {
        static readonly TemporalReconstructionSystem s_DefaultTemporalPlanner = new TemporalReconstructionSystem();

        internal static EffectivePipelineConfiguration Evaluate(in InfinityRenderPipelineAsset.Settings authored, in DeviceCapabilitySnapshot caps,
            TemporalReconstructionSystem temporalSystem = null)
        {
            bool hasBlockingConflict = false;
            TemporalReconstructionSystem planner = temporalSystem ?? s_DefaultTemporalPlanner;

            // 1. GBuffer layout & normal encoding.
            EGBufferLayout effectiveLayout = authored.gBufferLayout;
            ConfigFieldState gBufferState = ConfigFieldState.Supported;
            if (effectiveLayout == EGBufferLayout.HighPrecision96Bit && !caps.supportsRGBA16UnormRT)
            {
                gBufferState = new ConfigFieldState(EConfigStatus.Unsupported, "R16G16B16A16_UNorm render target is not supported on this device; effective layout uses Compact64Bit.");
                effectiveLayout = EGBufferLayout.Compact64Bit;
            }
            if ((effectiveLayout == EGBufferLayout.Compact64Bit || effectiveLayout == EGBufferLayout.HighPrecision96Bit) && !caps.supportsR8G8B8A8SRGB)
            {
                gBufferState = new ConfigFieldState(EConfigStatus.Unsupported, "R8G8B8A8_SRGB render target is not supported on this device.");
                hasBlockingConflict = true;
            }

            GraphicsFormat gBufferAFormat = effectiveLayout == EGBufferLayout.BestFitYCoCg64Bit
                ? GraphicsFormat.R8G8B8A8_UNorm
                : (caps.supportsR8G8B8A8SRGB ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm);
            GraphicsFormat gBufferBFormat = effectiveLayout == EGBufferLayout.HighPrecision96Bit
                ? GraphicsFormat.R16G16B16A16_UNorm
                : GraphicsFormat.R8G8B8A8_UNorm;

            // 2. Surface metadata (Routing/Flags + optional Rendering Layers).
            bool enableRenderingLayers = authored.enableRenderingLayers;
            ConfigFieldState renderingLayersState = ConfigFieldState.Supported;
            GraphicsFormat surfaceMetadataFormat;
            if (enableRenderingLayers)
            {
                if (caps.supportsR8G8UnormRT)
                {
                    surfaceMetadataFormat = GraphicsFormat.R8G8_UNorm;
                }
                else
                {
                    surfaceMetadataFormat = GraphicsFormat.R8G8B8A8_UNorm;
                    renderingLayersState = new ConfigFieldState(EConfigStatus.Unsupported, "R8G8_UNorm is unsupported; promoting SurfaceMetadata to R8G8B8A8_UNorm.");
                }
            }
            else
            {
                surfaceMetadataFormat = caps.supportsR8UnormRT ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;
            }

            // 3. Depth / Stencil format (MotionPass requires Stencil bit 0).
            ConfigFieldState depthState = ConfigFieldState.Supported;
            GraphicsFormat depthFormat;
            switch (authored.depthStencilFormat)
            {
                case EDepthStencilFormat.D24S8:
                    if (caps.supportsD24S8)
                    {
                        depthFormat = GraphicsFormat.D24_UNorm_S8_UInt;
                    }
                    else
                    {
                        depthState = new ConfigFieldState(EConfigStatus.Unsupported, "D24_UNorm_S8_UInt is unsupported on this device; effective format uses D32_SFloat_S8_UInt.");
                        depthFormat = caps.supportsD32FS8 ? GraphicsFormat.D32_SFloat_S8_UInt : GraphicsFormat.D32_SFloat;
                    }
                    break;
                case EDepthStencilFormat.D32FS8:
                    if (caps.supportsD32FS8)
                    {
                        depthFormat = GraphicsFormat.D32_SFloat_S8_UInt;
                    }
                    else
                    {
                        depthState = new ConfigFieldState(EConfigStatus.Unsupported, "D32_SFloat_S8_UInt is unsupported on this device.");
                        depthFormat = caps.supportsD24S8 ? GraphicsFormat.D24_UNorm_S8_UInt : GraphicsFormat.D32_SFloat;
                    }
                    break;
                case EDepthStencilFormat.D32F:
                case EDepthStencilFormat.D16:
                    depthState = new ConfigFieldState(EConfigStatus.Conflict, "Main depth buffer requires a stencil channel for MotionPass object/camera separation.");
                    hasBlockingConflict = true;
                    depthFormat = caps.supportsD32FS8 ? GraphicsFormat.D32_SFloat_S8_UInt
                        : caps.supportsD24S8 ? GraphicsFormat.D24_UNorm_S8_UInt
                        : GraphicsFormat.D32_SFloat;
                    break;
                default:
                    depthFormat = GraphicsFormat.D32_SFloat_S8_UInt;
                    break;
            }

            // 4. SceneColor & Subsurface Scattering representation contract.
            ConfigFieldState sceneColorState = ConfigFieldState.Supported;
            ConfigFieldState sssModeState = ConfigFieldState.Supported;
            ESubsurfaceLightingMode effectiveSSSMode = authored.subsurfaceLightingMode;
            GraphicsFormat sceneColorFormat = authored.sceneColorFormat == ESceneColorFormat.R11G11B10F
                ? (caps.supportsR11G11B10FUAV ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R16G16B16A16_SFloat)
                : GraphicsFormat.R16G16B16A16_SFloat;

            if (authored.sceneColorFormat == ESceneColorFormat.R11G11B10F && !caps.supportsR11G11B10FUAV)
            {
                sceneColorState = new ConfigFieldState(EConfigStatus.Unsupported, "B10G11R11_UFloatPack32 UAV is unsupported on this device; effective format uses RGBA16F.");
            }

            if (!authored.hasDiffusionProfiles)
            {
                sssModeState = new ConfigFieldState(EConfigStatus.Inactive, "Assign at least one Diffusion Profile to enable Subsurface Scattering.");
            }
            else if (effectiveSSSMode == ESubsurfaceLightingMode.SceneColorAlpha && authored.sceneColorFormat == ESceneColorFormat.R11G11B10F)
            {
                sssModeState = new ConfigFieldState(EConfigStatus.Conflict, "SceneColorAlpha SSS requires RGBA16F SceneColor (R11G11B10F has no alpha channel).");
                sceneColorState = new ConfigFieldState(EConfigStatus.Required, "RGBA16F is required by SceneColorAlpha Subsurface Scattering.");
                hasBlockingConflict = true;
                sceneColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            }

            // 5. Motion vector, 64-bit MotionMetadata & Indirect lighting formats.
            ConfigFieldState motionState = ConfigFieldState.Supported;
            GraphicsFormat motionFormat = authored.motionVectorFormat == EMotionVectorFormat.RG32F
                ? (caps.supportsRG32F ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R16G16_SFloat)
                : (caps.supportsRG16F ? GraphicsFormat.R16G16_SFloat : GraphicsFormat.R32G32_SFloat);
            if (authored.motionVectorFormat == EMotionVectorFormat.RG32F && !caps.supportsRG32F)
            {
                motionState = new ConfigFieldState(EConfigStatus.Unsupported, "R32G32_SFloat is unsupported on this device; effective format uses RG16F.");
            }
            GraphicsFormat motionMetadataFormat = caps.supportsRG32F ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R32G32B32A32_SFloat;

            ConfigFieldState indirectState = ConfigFieldState.Supported;
            GraphicsFormat indirectFormat = authored.indirectLightingFormat == EIndirectLightingFormat.R11G11B10F
                ? (caps.supportsR11G11B10FUAV ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R16G16B16A16_SFloat)
                : GraphicsFormat.R16G16B16A16_SFloat;
            if (authored.indirectLightingFormat == EIndirectLightingFormat.R11G11B10F && !caps.supportsR11G11B10FUAV)
            {
                indirectState = new ConfigFieldState(EConfigStatus.Unsupported, "B10G11R11_UFloatPack32 UAV is unsupported on this device; effective format uses RGBA16F.");
            }

            // 6. Reflection, Decal & Analytic Shadow capabilities.
            ConfigFieldState reflectionState = authored.reflectionProbeResolution > 0 && authored.maxReflectionProbes > 0
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Inactive, "Reflection probe atlas is disabled (resolution or max probe slots == 0); sky & raster reflections remain active.");

            bool useBestFitYCoCg = effectiveLayout == EGBufferLayout.BestFitYCoCg64Bit;
            bool directDecalSupported = !useBestFitYCoCg;
            ConfigFieldState directDecalState = directDecalSupported
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Inactive, "Direct GBuffer decals fall back to DBuffer when BestFitYCoCg64Bit GBuffer encoding is active.");

            ConfigFieldState analyticShadowState = authored.enableAnalyticShadow
                ? ConfigFieldState.Supported
                : new ConfigFieldState(EConfigStatus.Inactive, "Enable Analytic Shadow to activate primitive indirect occlusion and direct soft shadows.");
            ConfigFieldState analyticIndirectState = !authored.enableAnalyticShadow
                ? new ConfigFieldState(EConfigStatus.Inactive, "Analytic Shadow master toggle is disabled.")
                : ConfigFieldState.Supported;
            ConfigFieldState analyticDirectState = !authored.enableAnalyticShadow
                ? new ConfigFieldState(EConfigStatus.Inactive, "Analytic Shadow master toggle is disabled.")
                : ConfigFieldState.Supported;

            // 7. Temporal Reconstruction (TRC) backend, mode, quality and render scale resolution.
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

            GraphicsFormat compactHDR = caps.supportsR11G11B10FUAV ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R16G16B16A16_SFloat;
            GraphicsFormat customDataSSSFormat = caps.supportsR8G8UnormRT ? GraphicsFormat.R8G8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;

            int coreBits = BitsPerPixel(gBufferAFormat) + BitsPerPixel(gBufferBFormat);
            int metaBits = BitsPerPixel(surfaceMetadataFormat);
            int motionMetaBits = BitsPerPixel(motionMetadataFormat);
            int sceneBits = BitsPerPixel(sceneColorFormat);
            int commonBits = coreBits + metaBits + sceneBits;

            return new EffectivePipelineConfiguration(
                gBufferLayout: effectiveLayout,
                useBestFitYCoCg: useBestFitYCoCg,
                subsurfaceLightingMode: effectiveSSSMode,
                enableRenderingLayers: enableRenderingLayers,
                hasStencil: depthFormat == GraphicsFormat.D32_SFloat_S8_UInt || depthFormat == GraphicsFormat.D24_UNorm_S8_UInt || depthFormat == GraphicsFormat.S8_UInt,
                directDecalSupported: directDecalSupported,
                gBufferAFormat: gBufferAFormat,
                gBufferBFormat: gBufferBFormat,
                surfaceMetadataFormat: surfaceMetadataFormat,
                sceneColorFormat: sceneColorFormat,
                depthStencilFormat: depthFormat,
                motionVectorFormat: motionFormat,
                motionMetadataFormat: motionMetadataFormat,
                indirectLightingFormat: indirectFormat,
                bakedDiffuseFormat: compactHDR,
                scatterDiffuseFormat: compactHDR,
                customDataSSSFormat: customDataSSSFormat,
                effectiveTemporalBackend: previewReq.activeBackend,
                effectiveTemporalMode: previewReq.mode,
                effectiveRenderScale: previewReq.renderScale,
                coreGBufferBitsPerPixel: coreBits,
                surfaceMetadataBitsPerPixel: metaBits,
                motionMetadataBitsPerPixel: motionMetaBits,
                commonGBufferPassBitsPerPixel: commonBits,
                gBufferLayoutState: gBufferState,
                sceneColorState: sceneColorState,
                depthStencilState: depthState,
                motionVectorState: motionState,
                indirectLightingState: indirectState,
                subsurfaceModeState: sssModeState,
                renderingLayersState: renderingLayersState,
                reflectionState: reflectionState,
                directDecalState: directDecalState,
                analyticShadowState: analyticShadowState,
                analyticIndirectState: analyticIndirectState,
                analyticDirectState: analyticDirectState,
                temporalBackendState: temporalBackendState,
                temporalModeState: temporalModeState,
                temporalQualityState: temporalQualityState,
                renderScaleState: renderScaleState,
                hasBlockingConflict: hasBlockingConflict);
        }

        internal static int BitsPerPixel(GraphicsFormat format)
        {
            return format == GraphicsFormat.None ? 0 : (int)GraphicsFormatUtility.GetBlockSize(format) * 8;
        }
    }
}
