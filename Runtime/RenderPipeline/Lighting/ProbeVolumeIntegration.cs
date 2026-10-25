using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    // Which light probe system feeds dynamic objects and unlightmapped surfaces.
    public enum ELightProbeSystem
    {
        [InspectorName("Light Probe Groups")]
        LegacyLightProbes = 0,
        [InspectorName("Adaptive Probe Volumes")]
        ProbeVolumes = 1,
    }

    /// <summary>
    /// Adaptive Probe Volume lifecycle of one pipeline instance. ProbeReferenceVolume is a CoreRP singleton that owns
    /// persistent GPU resources; it is initialized with the pipeline, and per camera its streaming, bindings and
    /// constants are set on the camera command buffer before culling and before the RenderGraph records (AGENTS.md:
    /// the one CoreRP-owned global binding outside the graph). Surfaces sample it under PROBE_VOLUMES_L1 / L2.
    /// SupportedRenderingFeatures.overridesLightProbeSystem / skyOcclusion are installed with the pipeline's features.
    /// </summary>
    internal sealed class ProbeVolumeIntegration : IDisposable
    {
        static readonly GlobalKeyword s_ProbeVolumesL1 = GlobalKeyword.Create("PROBE_VOLUMES_L1");
        static readonly GlobalKeyword s_ProbeVolumesL2 = GlobalKeyword.Create("PROBE_VOLUMES_L2");
        static readonly int s_EnableProbeVolumes = Shader.PropertyToID("_EnableProbeVolumes");

        static ProbeVolumeIntegration s_ResourceOwner;
        readonly bool m_Enabled;
        readonly ProbeVolumeSHBands m_Bands;

        public bool Enabled => m_Enabled;

        public ProbeVolumeIntegration(InfinityRenderPipelineAsset asset)
        {
            m_Enabled = asset != null && asset.lightProbeSystem == ELightProbeSystem.ProbeVolumes;
            m_Bands = asset != null ? asset.probeVolumeSHBands : ProbeVolumeSHBands.SphericalHarmonicsL1;
            if (m_Enabled)
            {
                if (ProbeReferenceVolume.instance.isInitialized)
                    throw new InvalidOperationException("APV already has a pipeline owner; dispose it before initializing another integration.");
                ProbeReferenceVolume.instance.Initialize(new ProbeVolumeSystemParameters
                {
                    memoryBudget = asset.probeVolumeMemoryBudget,
                    blendingMemoryBudget = asset.probeVolumeBlendingMemoryBudget,
                    shBands = asset.probeVolumeSHBands,
                    supportGPUStreaming = asset.supportProbeVolumeGPUStreaming,
                    supportDiskStreaming = asset.supportProbeVolumeDiskStreaming,
                    supportScenarios = asset.supportProbeVolumeScenarios,
                    supportScenarioBlending = asset.supportProbeVolumeScenarioBlending,
                });
                s_ResourceOwner = this;
            }
        }

        // Per camera, on the camera command buffer before culling (RenderDebug emits intermediate renderers).
        // Reflection and preview cameras do not drive streaming.
        public void SetupCamera(CommandBuffer cmd, Camera camera, VolumeStack stack, bool temporal)
        {
            ProbeReferenceVolume volume = ProbeReferenceVolume.instance;
            volume.SetEnableStateFromSRP(m_Enabled);
            bool active = m_Enabled && volume.isInitialized;
            cmd.SetKeyword(s_ProbeVolumesL1, active && m_Bands == ProbeVolumeSHBands.SphericalHarmonicsL1);
            cmd.SetKeyword(s_ProbeVolumesL2, active && m_Bands == ProbeVolumeSHBands.SphericalHarmonicsL2);
            if (!active)
            {
                cmd.SetGlobalInt(s_EnableProbeVolumes, 0);
                return;
            }

            ProbeVolumesOptions options = stack != null ? stack.GetComponent<ProbeVolumesOptions>() : null;
            volume.PerformPendingOperations();
            if (camera.cameraType != CameraType.Reflection && camera.cameraType != CameraType.Preview)
            {
                volume.UpdateCellStreaming(cmd, camera, options);
            }
            volume.BindAPVRuntimeResources(cmd, true);
            volume.RenderDebug(camera, options, Texture2D.whiteTexture);
            bool hasData = volume.UpdateShaderVariablesProbeVolumes(cmd, options, temporal ? Time.frameCount : 0, true);
            cmd.SetGlobalInt(s_EnableProbeVolumes, hasData ? 1 : 0);
        }

        public void Dispose()
        {
            if (ReferenceEquals(s_ResourceOwner, this))
            {
                ProbeReferenceVolume.instance.Cleanup();
                s_ResourceOwner = null;
            }
        }
    }
}
