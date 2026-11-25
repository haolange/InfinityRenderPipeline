using UnityEngine;
using InfinityTech.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component.Editor
{
    internal sealed class InfinityLightInspectorSnapshot : ScriptableObject
    {
        internal const float UnlimitedDistance = 0f;
        [SerializeField] LightSourceShape m_Shape;
        [SerializeField] EShadowFilter m_ShadowFilter;
        [SerializeField] EShadowUpdateMode m_ShadowUpdateMode;
        [SerializeField] ELightIntensityEditMode m_IntensityEditMode = ELightIntensityEditMode.PhysicallyBased;

        public ERenderingLayer lightLayer;
        public float diffuse;
        public float specular;
        public bool enableContactShadow;
        public bool enableVolumetric;
        public float volumetricIntensity;
        public float volumetricOcclusion;
        [Tooltip(InfinityAdditionalLightData.MaximumDistanceTooltip)] public float maxDrawDistance;
        [Tooltip(InfinityAdditionalLightData.DistanceFadeTooltip)] public float maxDrawDistanceFade;
        [Tooltip(InfinityAdditionalLightData.IESTooltip)] public Texture iesProfile;

        internal LightSourceShape shape => m_Shape;
        internal ELightIntensityEditMode intensityEditMode => m_IntensityEditMode;

        internal void Read(Light light)
        {
            light.TryGetComponent(out InfinityAdditionalLightData data);
            m_Shape = data ? data.shape : LightShapeUtility.FromUnityLight(light);
            m_ShadowFilter = data ? data.shadowFilter : ResolveNativeShadowFilter(light.shadows);
            m_ShadowUpdateMode = data ? data.shadowUpdateMode : EShadowUpdateMode.EveryFrame;
            m_IntensityEditMode = data ? data.intensityEditMode : ELightIntensityEditMode.PhysicallyBased;
            lightLayer = data ? data.lightLayer : ERenderingLayer.LightLayerDefault;
            diffuse = data ? data.diffuse : InfinityAdditionalLightData.DefaultDiffuse;
            specular = data ? data.specular : InfinityAdditionalLightData.DefaultSpecular;
            enableContactShadow = data ? data.enableContactShadow : InfinityAdditionalLightData.DefaultContactShadow;
            enableVolumetric = data ? data.enableVolumetric : InfinityAdditionalLightData.DefaultVolumetric;
            volumetricIntensity = data ? data.volumetricIntensity : InfinityAdditionalLightData.DefaultVolumetricIntensity;
            volumetricOcclusion = data ? data.volumetricOcclusion : InfinityAdditionalLightData.DefaultVolumetricOcclusion;
            maxDrawDistance = data ? data.maxDrawDistance : UnlimitedDistance;
            maxDrawDistanceFade = data ? data.maxDrawDistanceFade : InfinityAdditionalLightData.DefaultDistanceFade;
            iesProfile = data ? data.iesProfile : null;
        }

        internal static EShadowFilter ResolveNativeShadowFilter(LightShadows shadows) =>
            shadows == LightShadows.Hard ? EShadowFilter.Hard : EShadowFilter.PCF;
    }
}
