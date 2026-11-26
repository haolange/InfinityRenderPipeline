using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Atmosphere;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.Temporal;

namespace InfinityTech.Rendering.Pipeline
{
    public sealed partial class InfinityRenderPipelineAsset
    {
        // The asset's values as the pipeline reads them, normalized once. Each pipeline instance takes one snapshot at
        // construction; an Inspector edit recreates the pipeline (RenderPipelineAsset.OnValidate), so it never goes stale.
        internal readonly struct Settings
        {
            // Editor only: refresh static renderer poses when the pipeline is created.
            internal readonly bool updateProxy;
            internal readonly bool enableRayTrace, enableSuperResolution, enableSRPBatch, enableInstanceBatch;
            internal readonly EMeshDrawBackendPolicy meshBackendPolicy;
            internal readonly float renderScale;
            internal readonly AtmosphericalProfile atmosphericalProfile;
            internal readonly DiffusionProfile[] diffusionProfiles;
            internal readonly bool hasDiffusionProfiles;
            internal readonly EOutputMode outputMode;
            internal readonly EHDREncoding hdrEncoding;
            internal readonly int cascadeShadowMapResolution, localShadowMapResolution;
            internal readonly float shadowDistance;
            internal readonly Vector3 cascadeSplitRatios;
            internal readonly int maxShadowedLocalLights, cachedShadowAtlasWidth, cascadeCount;
            internal readonly float cascadeBlendWidth;
            internal readonly ShadowFilterSettings shadowFilter;
            internal readonly ELightProbeSystem lightProbeSystem;
            internal readonly ProbeVolumeTextureMemoryBudget probeVolumeMemoryBudget;
            internal readonly ProbeVolumeBlendingTextureMemoryBudget probeVolumeBlendingMemoryBudget;
            internal readonly ProbeVolumeSHBands probeVolumeSHBands;
            internal readonly bool supportProbeVolumeGPUStreaming, supportProbeVolumeDiskStreaming, supportProbeVolumeScenarios, supportProbeVolumeScenarioBlending;
            internal readonly FrameSettings defaultFrameSettings, reflectionFrameSettings;
            internal readonly int reflectionProbeResolution, maxReflectionProbes, cookieAtlasResolution, cookieMaxSize;
            internal readonly EGBufferLayout gBufferLayout;
            internal readonly ESceneColorFormat sceneColorFormat;
            internal readonly EDepthStencilFormat depthStencilFormat;
            internal readonly EMotionVectorFormat motionVectorFormat;
            internal readonly EIndirectLightingFormat indirectLightingFormat;
            internal readonly ESubsurfaceLightingMode subsurfaceLightingMode;
            internal readonly bool enableRenderingLayers;
            internal readonly bool enableAnalyticShadow;
            internal readonly bool enableAnalyticIndirectOcclusion;
            internal readonly bool enableAnalyticDirectShadow;
            internal readonly TemporalSettings temporal;
            internal readonly EffectivePipelineConfiguration config;

            internal Settings(InfinityRenderPipelineAsset asset)
            {
                updateProxy = asset.updateProxy;
                enableRayTrace = asset.enableRayTrace;
                enableSuperResolution = asset.enableSuperResolution;
                enableSRPBatch = asset.enableSRPBatch;
                enableInstanceBatch = asset.enableInstanceBatch;
                meshBackendPolicy = asset.meshBackendPolicy;
                renderScale = asset.m_RenderScale;
                atmosphericalProfile = asset.atmosphericalProfile;
                diffusionProfiles = asset.diffusionProfiles;
                hasDiffusionProfiles = diffusionProfiles != null && System.Array.Exists(diffusionProfiles, profile => profile != null);
                outputMode = asset.outputMode;
                hdrEncoding = asset.hdrEncoding;
                cascadeShadowMapResolution = asset.cascadeShadowMapResolution;
                localShadowMapResolution = asset.localShadowMapResolution;
                shadowDistance = Mathf.Max(0.0f, asset.shadowDistance);
                cascadeSplitRatios = ShadowAllocator.ValidateCascadeRatios(asset.cascadeSplitRatios);
                maxShadowedLocalLights = Mathf.Clamp(asset.maxShadowedLocalLights, 1, 64);
                cachedShadowAtlasWidth = Mathf.Max(0, asset.cachedShadowAtlasWidth);
                cascadeCount = Mathf.Clamp(asset.cascadeCount, 1, ShadowAllocator.CascadeCount);
                cascadeBlendWidth = asset.cascadeBlendWidth;
                shadowFilter = asset.shadowFilterQuality == EQualityLevel.Custom
                    ? asset.customShadowFilter
                    : QualityPresets.Select(QualityPresets.ShadowFilter, asset.shadowFilterQuality);
                lightProbeSystem = asset.lightProbeSystem;
                probeVolumeMemoryBudget = asset.probeVolumeMemoryBudget;
                probeVolumeBlendingMemoryBudget = asset.probeVolumeBlendingMemoryBudget;
                probeVolumeSHBands = asset.probeVolumeSHBands;
                supportProbeVolumeGPUStreaming = asset.supportProbeVolumeGPUStreaming;
                supportProbeVolumeDiskStreaming = asset.supportProbeVolumeDiskStreaming;
                supportProbeVolumeScenarios = asset.supportProbeVolumeScenarios;
                supportProbeVolumeScenarioBlending = asset.supportProbeVolumeScenarioBlending;
                defaultFrameSettings = asset.defaultFrameSettings;
                reflectionFrameSettings = asset.reflectionFrameSettings;
                reflectionProbeResolution = asset.reflectionProbeResolution;
                maxReflectionProbes = asset.maxReflectionProbes;
                cookieAtlasResolution = asset.cookieAtlasResolution;
                cookieMaxSize = asset.cookieMaxSize;
                gBufferLayout = asset.m_GBufferLayout;
                sceneColorFormat = asset.m_SceneColorFormat;
                depthStencilFormat = asset.m_DepthStencilFormat;
                motionVectorFormat = asset.m_MotionVectorFormat;
                indirectLightingFormat = asset.m_IndirectLightingFormat;
                subsurfaceLightingMode = asset.m_SubsurfaceLightingMode;
                enableRenderingLayers = asset.m_EnableRenderingLayers;
                enableAnalyticShadow = asset.m_EnableAnalyticShadow;
                enableAnalyticIndirectOcclusion = asset.m_EnableAnalyticIndirectOcclusion;
                enableAnalyticDirectShadow = asset.m_EnableAnalyticDirectShadow;
                ETemporalBackend authoredBackend = asset.m_TemporalBackend;
                if (authoredBackend == ETemporalBackend.NativeTAA && asset.enableSuperResolution)
                {
                    authoredBackend = ETemporalBackend.NativeTAAU;
                }
                ETemporalReconstructionMode authoredMode = asset.enableSuperResolution && asset.m_TemporalMode == ETemporalReconstructionMode.NativeAA
                    ? ETemporalReconstructionMode.Upscaling
                    : asset.m_TemporalMode;
                temporal = new TemporalSettings(authoredBackend, authoredMode, asset.m_TemporalQuality, asset.m_RenderResolutionPolicy, asset.m_RenderScale, asset.m_TemporalSharpness);
                config = default;
                config = PipelineConfigurationResolver.Evaluate(in this, DeviceCapabilitySnapshot.FromCurrentDevice());
            }

            internal bool supportProbeVolume => lightProbeSystem == ELightProbeSystem.ProbeVolumes;
        }

        internal Settings ResolveSettings() => new Settings(this);

        // Editor inspectors read single serialized values without normalizing the whole asset, so an invalid shadow
        // setting never breaks an unrelated inspector.
        internal DiffusionProfile[] ReadDiffusionProfiles() => diffusionProfiles;
        internal FrameSettings ReadDefaultFrameSettings() => defaultFrameSettings;
    }
}
