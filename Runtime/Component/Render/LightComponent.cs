using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component
{
    internal static class FLightUtility
    {
        public static void InitLightType(this LightComponent light, Light unityLight) 
        {
            switch (unityLight.type)
            {
                case UnityEngine.LightType.Directional:
                    light.lightType = ELightType.Directional;
                    break;

                case UnityEngine.LightType.Point:
                    light.lightType = ELightType.Point;
                    break;

                case UnityEngine.LightType.Spot:
                    light.lightType = ELightType.Spot;
                    break;

                case UnityEngine.LightType.Disc:
                    light.lightType = ELightType.Spot;
                    break;

                case UnityEngine.LightType.Rectangle:
                    light.lightType = ELightType.Rect;
                    break;
            }
        }

        public static void UpdateLightShadowParameters(this LightComponent light, Light unityLight)
        {
            if(light.enableShadow)
            {
                switch (light.shadowType)
                {
                    case EShadowType.Hard:
                        unityLight.shadows = LightShadows.Hard;
                        break;

                    case EShadowType.PCF:
                        unityLight.shadows = LightShadows.Soft;
                        break;

                    case EShadowType.PCSS:
                        unityLight.shadows = LightShadows.Soft;
                        break;
                }
            } else {
                unityLight.shadows = LightShadows.None;
            }
        }

        public static void UpdateUnityDirecitonLightParameters(this LightComponent light, Light unityLight)
        {
            unityLight.type = UnityEngine.LightType.Directional;
            unityLight.intensity = light.intensity;
            unityLight.bounceIntensity = light.indirectIntensity;

            UpdateLightShadowParameters(light, unityLight);
        }

        public static void UpdateUnityPointLightParameters(this LightComponent light, Light unityLight)
        {
            unityLight.type = UnityEngine.LightType.Point;
            unityLight.intensity = light.intensity;
            unityLight.bounceIntensity = light.indirectIntensity;
            UpdateLightShadowParameters(light, unityLight);
        }

        public static void UpdateUnitySpotLightParameters(this LightComponent light, Light unityLight)
        {
            unityLight.type = UnityEngine.LightType.Spot;
            unityLight.intensity = light.intensity;
            unityLight.bounceIntensity = light.indirectIntensity;

            if(light.radius > 0) {
                unityLight.type = UnityEngine.LightType.Disc;
            }

            UpdateLightShadowParameters(light, unityLight);
        }

        public static void UpdateUnityRectLightParameters(this LightComponent light, Light unityLight)
        {
            unityLight.type = UnityEngine.LightType.Rectangle;
            unityLight.intensity = light.intensity;
            unityLight.bounceIntensity = light.indirectIntensity;

            UpdateLightShadowParameters(light, unityLight);
        }

        public static void UpdateUnityLightParameters(this LightComponent light, Light unityLight, in ELightType lightType)
        {
            switch (lightType)
            {
                case ELightType.Directional:
                    UpdateUnityDirecitonLightParameters(light, unityLight);
                    break;

                case ELightType.Point:
                    UpdateUnityPointLightParameters(light, unityLight);
                    break;

                case ELightType.Spot:
                    UpdateUnitySpotLightParameters(light, unityLight);
                    break;

                case ELightType.Rect:
                    UpdateUnityRectLightParameters(light, unityLight);
                    break;
            }
        }
    }

    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("InfinityRenderer/Light Component")]
    public class LightComponent : BaseComponent
    {
        ///General Property
        public Light unityLight;
        public ELightState state = ELightState.Dynamic;
        public ELightType lightType = ELightType.Directional;
        public ELightLayer lightLayer = ELightLayer.LightLayerDefault;

        ///Emission Property
        public Color color = Color.white;
        public float intensity = 10;
        public float temperature = 7000;
        public float range = 10;
        public float diffuse = 1;
        public float specular = 1;
        public float radius = 0;
        public float length = 0;
        public float innerAngle = 32;
        public float outerAngle = 90;
        public float width = 0.5f;
        public float height = 0.5f;

        ///Globalillumination Property
        public bool enableIndirect = true;
        public float indirectIntensity = 1;

        ///IES and Cookie Property
        public int IESIndex = 0;
        public Texture2D IESTexture;
        public int cookieIndex = 0;
        public Texture2D cookieTexture;

        ///Shadow Property
        public bool enableShadow = true;
        public float nearPlane = 0.05f;
        public float minSoftness = 0.1f;
        public float maxSoftness = 1;
        public EShadowType shadowType = EShadowType.PCF;
        public ELightLayer shadowLayer = ELightLayer.LightLayerDefault;
        public EShadowResolution resolution = EShadowResolution.X1024;

        ///Contact Shadow Property
        public bool enableContactShadow = false;
        public float contactShadowLength = 0.05f;

        ///VolumetricFog Property
        public bool enableVolumetric = true;
        public float volumetricIntensity = 1;
        public float volumetricOcclusion = 1;

        ///Performance Property
        public float maxDrawDistance = 128;
        public float maxDrawDistanceFade = 1;

        protected override void OnRegister()
        {
            GetWorld().AddWorldLight(this);

            unityLight = GetComponent<Light>();
            FLightUtility.InitLightType(this, unityLight);
        }

        protected override void EventPlay()
        {
            base.EventPlay();
        }

        protected override void EventTick()
        {
            base.EventTick();
        }

        protected override void OnTransformChange()
        {
            base.OnTransformChange();
        }

        protected override void UnRegister()
        {
            GetWorld().RemoveWorldLight(this);
        }

#if UNITY_EDITOR
        public void OnGUIChange()
        {
            FLightUtility.UpdateUnityLightParameters(this, unityLight, lightType);
        }
#endif

        public FLightElement GetLightElementElement() 
        {
            FLightElement lightElement;
            {
                lightElement.state = state;
                lightElement.lightType = lightType;
                lightElement.lightLayer = lightLayer;
                lightElement.color = color;
                lightElement.temperature = temperature;
                lightElement.intensity = intensity;
                lightElement.range = range;
                lightElement.diffuse = diffuse;
                lightElement.specular = specular;
                lightElement.radius = radius;
                lightElement.length = length;
                lightElement.innerAngle = innerAngle;
                lightElement.outerAngle = outerAngle;
                lightElement.width = width;
                lightElement.height = height;
                lightElement.enableIndirect = enableIndirect ? 1 : 0;
                lightElement.indirectIntensity = indirectIntensity;
                lightElement.IESIndex = IESIndex;
                lightElement.cookieIndex = cookieIndex;
                lightElement.enableShadow = enableShadow ? 1 : 0;
                lightElement.shadowLayer = shadowLayer;
                lightElement.shadowType = shadowType;
                lightElement.resolution = resolution;
                lightElement.nearPlane = nearPlane;
                lightElement.minSoftness = minSoftness;
                lightElement.maxSoftness = maxSoftness;
                lightElement.enableContactShadow = enableContactShadow ? 1 : 0;
                lightElement.contactShadowLength = contactShadowLength;
                lightElement.enableVolumetric = enableVolumetric ? 1 : 0;
                lightElement.volumetricIntensity = volumetricIntensity;
                lightElement.volumetricOcclusion = volumetricOcclusion;
                lightElement.maxDrawDistance = maxDrawDistance;
                lightElement.maxDrawDistanceFade = maxDrawDistanceFade;
            }
            return lightElement;
        }
    }
}
