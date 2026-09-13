using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityDefaultVolumeProfileSettings : IDefaultVolumeProfileSettings
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        public bool isAvailableInPlayerBuild => true;

        public VolumeProfile volumeProfile;

        VolumeProfile IDefaultVolumeProfileSettings.volumeProfile
        {
            get => volumeProfile;
            set => volumeProfile = value;
        }
    }
}
