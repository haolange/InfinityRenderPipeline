using System;
using UnityEngine;
using UnityEngine.Rendering;

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
        internal PipelineKernels kernels
        {
            get;
            private set;
        }
        internal BlitShaderPasses blitPasses
        {
            get;
            private set;
        }
        // Why each feature cannot record for reasons that depend only on these resources (null when it can).
        internal string[] featureResourceReasons
        {
            get;
            private set;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal CaptureKernels captureKernels
        {
            get;
            private set;
        }
#endif
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

        // Resolves kernels, blit passes and resource-only feature support together, then publishes them at once.
        void ResolveShaders()
        {
            var resolvedKernels = new PipelineKernels(shaders);
            resolvedKernels.RequireUngatedKernels();
            var resolvedPasses = new BlitShaderPasses(materials.blitMaterial);
            string[] reasons = FeatureRegistry.ResolveResourceSupport(resolvedKernels, textures);
            FeatureRegistry.RequireProducerSupport(reasons, m_RequiredProducers);
            kernels = resolvedKernels;
            blitPasses = resolvedPasses;
            featureResourceReasons = reasons;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            captureKernels = new CaptureKernels(shaders);
#endif
#if UNITY_EDITOR
            m_ResolvedImportEpoch = s_ImportEpoch;
#endif
        }

#if UNITY_EDITOR
        static int s_ImportEpoch;
        int m_ResolvedImportEpoch;

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
            if (m_ResolvedImportEpoch != s_ImportEpoch)
            {
                ResolveShaders();
            }
        }
#endif
    }
}
