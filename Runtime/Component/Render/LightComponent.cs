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
        public Light unityLight;
        public ELightState state = ELightState.Dynamic;
        public ERenderingLayer lightLayer = ERenderingLayer.LightLayerDefault;

        public float diffuse = 1;
        public float specular = 1;
        public float width = 0.5f;
        public float height = 0.5f;

        public bool enableIndirect = true;
        public float indirectIntensity = 1;

        public int IESIndex = 0;
        public Texture2D IESTexture;
        public int cookieIndex = 0;
        public Texture2D cookieTexture;

        public bool enableShadow = true;
        public float nearPlane = 0.05f;
        public float minSoftness = 0.1f;
        public float maxSoftness = 1;
        public EShadowType shadowType = EShadowType.PCF;
        public ERenderingLayer shadowLayer = ERenderingLayer.LightLayerDefault;
        public EShadowResolution resolution = EShadowResolution.X1024;

        public bool enableContactShadow = false;
        public float contactShadowLength = 0.05f;

        public bool enableVolumetric = true;
        public float volumetricIntensity = 1;
        public float volumetricOcclusion = 1;

        public float maxDrawDistance = 128;
        public float maxDrawDistanceFade = 1;

        protected override void OnRegister()
        {
            unityLight = GetComponent<Light>();
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.AddWorldLight(UnityEntityId.ToInt32(unityLight), this);
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
            FGraphics.AddTask((RenderContext renderContext) =>
            {
                renderContext.RemoveWorldLight(UnityEntityId.ToInt32(unityLight));
            });
        }
    }
}
