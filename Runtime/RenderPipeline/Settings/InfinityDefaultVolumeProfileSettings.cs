using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityDefaultVolumeProfileSettings : IRenderPipelineGraphicsSettings
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        public VolumeProfile volumeProfile;
    }
}
