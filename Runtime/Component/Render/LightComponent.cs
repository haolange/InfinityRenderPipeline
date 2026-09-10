using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("InfinityRenderer/Light Component")]
    public class LightComponent : BaseComponent
    {
        Light m_UnityLight;
        ulong m_RegisteredLightId;
        public Light unityLight => m_UnityLight ? m_UnityLight : (m_UnityLight = GetComponent<Light>());
        public ERenderingLayer lightLayer = ERenderingLayer.LightLayerDefault;

        public float diffuse = 1;
        public float specular = 1;

        public ERenderingLayer shadowLayer
        {
            get => (ERenderingLayer)RenderingLayerUtility.Validate(unchecked((uint)unityLight.renderingLayerMask));
            set => unityLight.renderingLayerMask = checked((int)RenderingLayerUtility.Validate((uint)value));
        }

        public bool enableContactShadow = false;

        public bool enableVolumetric = true;
        public float volumetricIntensity = 1;
        public float volumetricOcclusion = 1;

        public float maxDrawDistance = 128;
        public float maxDrawDistanceFade = 1;

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

        protected override void OnUpdate()
        {
            base.OnUpdate();
        }

        protected override void OnTransformChange()
        {
            base.OnTransformChange();
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
