using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component
{
    public static class FLightUtility
    {
        public static void InitLightType(this LightComponent Light, Light UnityLight) 
        {
            switch (UnityLight.type)
            {
                case UnityEngine.LightType.Directional:
                    Light.LightType = ELightType.Directional;
                    break;

                case UnityEngine.LightType.Point:
                    Light.LightType = ELightType.Point;
                    break;

                case UnityEngine.LightType.Spot:
                    Light.LightType = ELightType.Spot;
                    break;

                case UnityEngine.LightType.Disc:
                    Light.LightType = ELightType.Spot;
                    break;

                case UnityEngine.LightType.Rectangle:
                    Light.LightType = ELightType.Rect;
                    break;
            }
        }

        public static void UpdateLightShadowParameters(this LightComponent Light, Light UnityLight)
        {
            if(Light.EnableShadow)
            {
                switch (Light.ShadowType)
                {
                    case EShadowType.Hard:
                        UnityLight.shadows = LightShadows.Hard;
                        break;

                    case EShadowType.PCF:
                        UnityLight.shadows = LightShadows.Soft;
                        break;

                    case EShadowType.PCSS:
                        UnityLight.shadows = LightShadows.Soft;
                        break;
                }
            } else {
                UnityLight.shadows = LightShadows.None;
            }
        }

        public static void UpdateUnityDirecitonLightParameters(this LightComponent Light, Light UnityLight)
        {
            UnityLight.type = UnityEngine.LightType.Directional;
            UnityLight.intensity = Light.LightIntensity;
            UnityLight.bounceIntensity = Light.GlobalIlluminationIntensity;

            UpdateLightShadowParameters(Light, UnityLight);
        }

        public static void UpdateUnityPointLightParameters(this LightComponent Light, Light UnityLight)
        {
            UnityLight.type = UnityEngine.LightType.Point;
            UnityLight.intensity = Light.LightIntensity;
            UnityLight.bounceIntensity = Light.GlobalIlluminationIntensity;
            UpdateLightShadowParameters(Light, UnityLight);
        }

        public static void UpdateUnitySpotLightParameters(this LightComponent Light, Light UnityLight)
        {
            UnityLight.type = UnityEngine.LightType.Spot;
            UnityLight.intensity = Light.LightIntensity;
            UnityLight.bounceIntensity = Light.GlobalIlluminationIntensity;

            if(Light.SourceRadius > 0) {
                UnityLight.type = UnityEngine.LightType.Disc;
            }

            UpdateLightShadowParameters(Light, UnityLight);
        }

        public static void UpdateUnityRectLightParameters(this LightComponent Light, Light UnityLight)
        {
            UnityLight.type = UnityEngine.LightType.Rectangle;
            UnityLight.intensity = Light.LightIntensity;
            UnityLight.bounceIntensity = Light.GlobalIlluminationIntensity;

            UpdateLightShadowParameters(Light, UnityLight);
        }

        public static void UpdateUnityLightParameters(this LightComponent Light, Light UnityLight, in ELightType LightType)
        {
            //Update UnityLightType
            switch (LightType)
            {
                case ELightType.Directional:
                    UpdateUnityDirecitonLightParameters(Light, UnityLight);
                    break;

                case ELightType.Point:
                    UpdateUnityPointLightParameters(Light, UnityLight);
                    break;

                case ELightType.Spot:
                    UpdateUnitySpotLightParameters(Light, UnityLight);
                    break;

                case ELightType.Rect:
                    UpdateUnityRectLightParameters(Light, UnityLight);
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
        public ELightState LightState = ELightState.Dynamic;
        public ELightType LightType = ELightType.Directional;
        public ELightLayer LightLayer = ELightLayer.LightLayerDefault;

        ///Emission Property
        public Color LightColor = Color.white;
        public float LightIntensity = 10;
        public float Temperature = 7000;
        public float LightRange = 10;
        public float LightDiffuse = 1;
        public float LightSpecular = 1;
        public float SourceRadius = 0;
        public float SourceLength = 0;
        public float SourceInnerAngle = 32;
        public float SourceOuterAngle = 90;
        public float SourceWidth = 0.5f;
        public float SourceHeight = 0.5f;

        ///Globalillumination Property
        public bool EnableGlobalIllumination = true;
        public float GlobalIlluminationIntensity = 1;

        ///IES and Cookie Property
        public Texture2D IESTexture;
        public int IESTextureIndex = 0;
        public Texture2D CookieTexture;
        public int CookieTextureIndex = 0;

        ///Shadow Property
        public bool EnableShadow = true;
        public float NearPlane = 0.05f;
        public float MinSoftness = 0.1f;
        public float MaxSoftness = 1;
        public EShadowType ShadowType = EShadowType.PCF;
        public ELightLayer ShadowLayer = ELightLayer.LightLayerDefault;
        public EShadowResolution Resolution = EShadowResolution.X1024;

        ///Contact Shadow Property
        public bool EnableContactShadow = false;
        public float ContactShadowLength = 0.05f;

        ///VolumetricFog Property
        public bool EnableVolumetric = true;
        public float VolumetricScatterIntensity = 1;
        public float VolumetricScatterOcclusion = 1;

        ///Performance Property
        public float MaxDrawDistance = 128;
        public float MaxDrawDistanceFade = 1;

        public Light UnityLight;


        // Function
        public LightComponent() : base()
        {

        }

        protected override void OnRegister()
        {
            GetWorld().AddWorldLight(this);

            UnityLight = GetComponent<Light>();
            FLightUtility.InitLightType(this, UnityLight);
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
            FLightUtility.UpdateUnityLightParameters(this, UnityLight, LightType);
        }
#endif

        public FLightBatch GetLightBatchElement() 
        {
            FLightBatch LightBatch;
            {
                LightBatch.LightState = LightState;
                LightBatch.LightType = LightType;
                LightBatch.LightLayer = LightLayer;
                LightBatch.LightColor = LightColor;
                LightBatch.Temperature = Temperature;
                LightBatch.LightIntensity = LightIntensity;
                LightBatch.LightRange = LightRange;
                LightBatch.LightDiffuse = LightDiffuse;
                LightBatch.LightSpecular = LightSpecular;
                LightBatch.SourceRadius = SourceRadius;
                LightBatch.SourceLength = SourceLength;
                LightBatch.SourceInnerAngle = SourceInnerAngle;
                LightBatch.SourceOuterAngle = SourceOuterAngle;
                LightBatch.SourceWidth = SourceWidth;
                LightBatch.SourceHeight = SourceHeight;
                LightBatch.EnableGlobalIllumination = EnableGlobalIllumination ? 1 : 0;
                LightBatch.GlobalIlluminationIntensity = GlobalIlluminationIntensity;
                LightBatch.IESTextureIndex = IESTextureIndex;
                LightBatch.CookieTextureIndex = CookieTextureIndex;
                LightBatch.EnableShadow = EnableShadow ? 1 : 0;
                LightBatch.ShadowLayer = ShadowLayer;
                LightBatch.ShadowType = ShadowType;
                LightBatch.Resolution = Resolution;
                LightBatch.NearPlane = NearPlane;
                LightBatch.MinSoftness = MinSoftness;
                LightBatch.MaxSoftness = MaxSoftness;
                LightBatch.EnableContactShadow = EnableContactShadow ? 1 : 0;
                LightBatch.ContactShadowLength = ContactShadowLength;
                LightBatch.EnableVolumetric = EnableVolumetric ? 1 : 0;
                LightBatch.VolumetricScatterIntensity = VolumetricScatterIntensity;
                LightBatch.VolumetricScatterOcclusion = VolumetricScatterOcclusion;
                LightBatch.MaxDrawDistance = MaxDrawDistance;
                LightBatch.MaxDrawDistanceFade = MaxDrawDistanceFade;
            }
            return LightBatch;
        }
    }
}
