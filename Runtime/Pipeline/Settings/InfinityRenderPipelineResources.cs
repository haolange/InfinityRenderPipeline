using System;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed class InfinityRenderPipelineResources
    {
        public InfinityRenderPipelineRuntimeShaders shaders
        {
            get;
        }
        public InfinityRenderPipelineRuntimeTextures textures
        {
            get;
        }
        public InfinityRenderPipelineRuntimeMaterials materials
        {
            get;
        }
        public VolumeProfile defaultVolumeProfile
        {
            get;
        }
        internal PipelineKernels kernels => m_ShaderResources.kernels;
        internal BlitShaderPasses blitPasses => m_ShaderResources.blitPasses;
        // Why each feature cannot record for reasons that depend only on these resources (null when it can).
        internal string[] featureResourceReasons => m_ShaderResources.featureResourceReasons;
        internal TemporalReconstructionSystem temporalPlanner => m_ShaderResources.temporalPlanner;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal CaptureKernels captureKernels => m_ShaderResources.captureKernels;
#endif

        ShaderResourceRevision m_ShaderResources;

        sealed class ShaderResourceRevision
        {
            internal readonly PipelineKernels kernels;
            internal readonly BlitShaderPasses blitPasses;
            internal readonly string[] featureResourceReasons;
            internal readonly TemporalReconstructionSystem temporalPlanner;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            internal readonly CaptureKernels captureKernels;
#endif
#if UNITY_EDITOR
            internal readonly int importEpoch;
#endif

            internal ShaderResourceRevision(InfinityRenderPipelineResources resources)
            {
#if UNITY_EDITOR
                importEpoch = s_ImportEpoch;
#endif
                kernels = new PipelineKernels(resources.shaders);
                kernels.RequireUngatedKernels();
                blitPasses = new BlitShaderPasses(resources.materials.blitMaterial);
                featureResourceReasons = FeatureRegistry.ResolveResourceSupport(kernels, resources.textures);
                FeatureRegistry.RequireProducerSupport(featureResourceReasons, resources.m_RequiredProducers);
                temporalPlanner = new TemporalReconstructionSystem(BuildTemporalCapabilities(kernels, resources.textures));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                captureKernels = new CaptureKernels(resources.shaders);
#endif
            }
        }

        // The pipeline's required producers: every resolution, including the one after a shader import, fails when the
        // resources cannot support one of them.
        readonly EFrameFeature[] m_RequiredProducers;

        public InfinityRenderPipelineResources(EFrameFeature[] requiredProducers)
        {
            if (requiredProducers == null)
                throw new ArgumentNullException(nameof(requiredProducers));
            m_RequiredProducers = requiredProducers;
            InfinityRenderPipelineGlobalSettings.Require();

            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeShaders resolvedShaders) || resolvedShaders == null)
                throw new InvalidOperationException("InfinityRP: RuntimeShaders are not registered on GlobalSettings.");
            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeTextures resolvedTextures) || resolvedTextures == null)
                throw new InvalidOperationException("InfinityRP: RuntimeTextures are not registered on GlobalSettings.");
            if (!GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeMaterials resolvedMaterials) || resolvedMaterials == null)
                throw new InvalidOperationException("InfinityRP: RuntimeMaterials are not registered on GlobalSettings.");

            shaders = resolvedShaders;
            textures = resolvedTextures;
            materials = resolvedMaterials;
            defaultVolumeProfile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            if (materials.blitMaterial == null)
                throw new InvalidOperationException("InfinityRP: GlobalSettings RuntimeMaterials.blitMaterial is required.");
            if (!DefaultVolumeProfileFactory.HasRequiredDefaultComponents(defaultVolumeProfile))
                throw new InvalidOperationException("InfinityRP: default Volume profile is incomplete or its required component overrides are disabled.");
            ResolveShaders();
        }

        // A failed resolution keeps the previous revision intact.
        void ResolveShaders() => m_ShaderResources = new ShaderResourceRevision(this);

#if UNITY_EDITOR
        static int s_ImportEpoch;

        static TemporalReconstructionSystem s_EditorTemporalPlanner;
        static int s_EditorTemporalImportEpoch;
        static TemporalResourceBindings s_EditorTemporalBindings;

        readonly struct TemporalResourceBindings
        {
            readonly ComputeShader m_Native, m_Stp, m_Fsr, m_Sgsr;
            readonly Texture2D m_StpNoise;

            internal TemporalResourceBindings(InfinityRenderPipelineRuntimeShaders shaders, InfinityRenderPipelineRuntimeTextures textures)
            {
                m_Native = shaders?.taaShader;
                m_Stp = shaders?.temporalStpShader;
                m_Fsr = shaders?.temporalFsrShader;
                m_Sgsr = shaders?.temporalSgsrShader;
                m_StpNoise = textures?.blueNoiseOwenScrambledSobol;
            }

            internal bool Matches(in TemporalResourceBindings other)
            {
                return m_Native == other.m_Native && m_Stp == other.m_Stp && m_Fsr == other.m_Fsr
                    && m_Sgsr == other.m_Sgsr && m_StpNoise == other.m_StpNoise;
            }
        }

        internal static TemporalReconstructionSystem ResolveEditorTemporalPlanner()
        {
            GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeShaders shaders);
            GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeTextures textures);
            var bindings = new TemporalResourceBindings(shaders, textures);
            if (s_EditorTemporalPlanner != null && s_EditorTemporalImportEpoch == s_ImportEpoch
                && s_EditorTemporalBindings.Matches(bindings))
                return s_EditorTemporalPlanner;

            var kernels = new PipelineKernels(shaders ?? new InfinityRenderPipelineRuntimeShaders());
            s_EditorTemporalPlanner = new TemporalReconstructionSystem(BuildTemporalCapabilities(kernels, textures));
            s_EditorTemporalBindings = bindings;
            s_EditorTemporalImportEpoch = s_ImportEpoch;
            return s_EditorTemporalPlanner;
        }

        // An import can renumber kernels and passes or remove them; the next frame resolves the tables again.
        internal static void NotifyShadersImported()
        {
            unchecked
            {
                ++s_ImportEpoch;
            }
        }

        internal void ResolveShadersAfterImport()
        {
            if (m_ShaderResources.importEpoch != s_ImportEpoch)
            {
                ResolveShaders();
            }
        }
#endif

        static TemporalDeviceCapabilities BuildTemporalCapabilities(PipelineKernels kernels, InfinityRenderPipelineRuntimeTextures textures)
        {
            string nativeReason = !kernels.temporalAntiAliasing.isValid ? kernels.temporalAntiAliasing.missingReason
                : null;
            bool stpApiSupported = SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3;
            string stpReason = !stpApiSupported ? "STP does not support OpenGLES3."
                : !kernels.stpSetup.isValid ? kernels.stpSetup.missingReason
                : !kernels.stpPreTaa.isValid ? kernels.stpPreTaa.missingReason
                : !kernels.stpTaa.isValid ? kernels.stpTaa.missingReason
                : !kernels.stpSharpen.isValid ? kernels.stpSharpen.missingReason
                : textures == null ? "RuntimeTextures are not registered in Graphics > Infinity RP."
                : textures.blueNoiseOwenScrambledSobol == null ? "RuntimeTextures is missing blueNoiseOwenScrambledSobol."
                : null;
            string fsrReason = !kernels.fsrClear.isValid ? kernels.fsrClear.missingReason
                : !kernels.fsrPrepareInputs.isValid ? kernels.fsrPrepareInputs.missingReason
                : !kernels.fsrLumaPyramid.isValid ? kernels.fsrLumaPyramid.missingReason
                : !kernels.fsrFinalizeLuma.isValid ? kernels.fsrFinalizeLuma.missingReason
                : !kernels.fsrShadingPyramid.isValid ? kernels.fsrShadingPyramid.missingReason
                : !kernels.fsrShadingChange.isValid ? kernels.fsrShadingChange.missingReason
                : !kernels.fsrPrepareReactivity.isValid ? kernels.fsrPrepareReactivity.missingReason
                : !kernels.fsrLumaInstability.isValid ? kernels.fsrLumaInstability.missingReason
                : !kernels.fsrAccumulate.isValid ? kernels.fsrAccumulate.missingReason
                : !kernels.fsrRcas.isValid ? kernels.fsrRcas.missingReason
                : null;
            string sgsrReason = !kernels.sgsrConvert.isValid ? kernels.sgsrConvert.missingReason
                : !kernels.sgsrActivate.isValid ? kernels.sgsrActivate.missingReason
                : !kernels.sgsrUpscale.isValid ? kernels.sgsrUpscale.missingReason
                : !kernels.sgsrSharpen.isValid ? kernels.sgsrSharpen.missingReason
                : null;
            return new TemporalDeviceCapabilities(
                nativeSupported: nativeReason == null,
                stpSupported: stpReason == null,
                fsrSupported: fsrReason == null,
                sgsrSupported: sgsrReason == null,
                xessSupported: false,
                dlssSupported: false,
                nativeUnavailableReason: nativeReason,
                stpUnavailableReason: stpReason,
                fsrUnavailableReason: fsrReason,
                sgsrUnavailableReason: sgsrReason);
        }
    }
}
