using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("InfinityRenderer/Additional Light Data")]
    public sealed class InfinityAdditionalLightData : BaseComponent
    {
        Light m_UnityLight;
        ulong m_RegisteredLightId;

        public Light attachedLight => m_UnityLight ? m_UnityLight : (m_UnityLight = GetComponent<Light>());
        public ERenderingLayer lightLayer = ERenderingLayer.LightLayerDefault;
        public float diffuse = 1;
        public float specular = 1;
        public bool enableContactShadow = false;
        public bool enableVolumetric = true;
        public float volumetricIntensity = 1;
        public float volumetricOcclusion = 1;
        public float maxDrawDistance = 128;
        public float maxDrawDistanceFade = 1;

        public ERenderingLayer shadowLayer
        {
            get => (ERenderingLayer)RenderingLayerUtility.Validate(unchecked((uint)attachedLight.renderingLayerMask));
            set => attachedLight.renderingLayerMask = checked((int)RenderingLayerUtility.Validate((uint)value));
        }

        public static InfinityAdditionalLightData GetOrCreate(Light light, bool undo)
        {
            if (light == null)
            {
                throw new System.ArgumentNullException(nameof(light));
            }

            if (light.TryGetComponent(out InfinityAdditionalLightData data))
            {
                return data;
            }

#if UNITY_EDITOR
            if (undo)
            {
                return UnityEditor.Undo.AddComponent<InfinityAdditionalLightData>(light.gameObject);
            }
#endif
            return light.gameObject.AddComponent<InfinityAdditionalLightData>();
        }

        protected override void OnRegister()
        {
            m_UnityLight = GetComponent<Light>();
            m_RegisteredLightId = UnityEntityId.ToUInt64(m_UnityLight);
            ulong lightId = m_RegisteredLightId;
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                if (this && m_UnityLight) renderContext.AddWorldLight(lightId, this);
            });
        }

        protected override void UnRegister()
        {
            ulong lightId = m_RegisteredLightId;
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.RemoveWorldLight(lightId);
            });
        }
    }
}
