using UnityEngine;

namespace InfinityTech.Component
{
    public enum ELightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
        Rect = 3
    }

    public enum ELightState
    {
        Static = 0,
        Mixed = 1,
        Dynamic = 2
    }

    public enum ELightUnit
    {
        Lumen,      // lm = total power/flux emitted by the light
        Candela,    // lm/sr = flux per steradian
        Lux,        // lm/m² = flux per unit area
        Luminance,  // lm/m²/sr = flux per unit area and per steradian
        Ev100,      // ISO 100 Exposure Value (https://en.wikipedia.org/wiki/Exposure_value)
    }

    public enum ELightLayer
    {
        Nothing = 0,   // Custom name for "Nothing" option
        LightLayerDefault = 1 << 0,
        LightLayer1 = 1 << 1,
        LightLayer2 = 1 << 2,
        LightLayer3 = 1 << 3,
        LightLayer4 = 1 << 4,
        LightLayer5 = 1 << 5,
        LightLayer6 = 1 << 6,
        LightLayer7 = 1 << 7,
        Everything = 0xFF, // Custom name for "Everything" option
    }

    public enum EShadowResolution
    {
        X512 = 0,
        X1024 = 1,
        X2048 = 2,
        X4096 = 3,
        X8192 = 4
    }

    public enum EShadowType
    {
        Hard = 0,
        PCF = 1,
        PCSS = 2
    }

    public enum EShadowCascade
    {
        One = 0,
        Two = 1,
        Three = 2,
        Four = 3
    }

    public struct FLightBatch
    {
        public ELightState LightState;
        public ELightType LightType;
        public ELightLayer LightLayer;

        ///Emission Property
        public float LightIntensity;
        public Color LightColor;
        public float Temperature;
        public float LightRange;
        public float LightDiffuse;
        public float LightSpecular;
        public float SourceRadius;
        public float SourceLength;
        public float SourceInnerAngle;
        public float SourceOuterAngle;
        public float SourceWidth;
        public float SourceHeight;

        ///Globalillumination Property
        public int EnableGlobalIllumination;
        public float GlobalIlluminationIntensity;

        ///IES and Cookie Property
        public float IESIntensity;
        public int IESTextureIndex;
        public int CookieTextureIndex;

        ///Shadow Property
        public int EnableShadow;
        public ELightLayer ShadowLayer;
        public EShadowType ShadowType;
        public EShadowResolution ShadowResolution;
        public Color ShadowColor;
        public float ShadowIntensity;
        public float ShadowBias;
        public float ShadowNormalBias;
        public float ShadowNearPlane;
        public float MinSoftness;
        public float MaxSoftness;
        public EShadowCascade CascadeType;
        public float ShadowDistance;

        ///Contact Shadow Property
        public int EnableContactShadow;
        public float ContactShadowLength;

        ///VolumetricFog Property
        public int EnableVolumetric;
        public float VolumetricScatterIntensity;
        public float VolumetricScatterOcclusion;

        ///Performance Property
        public float MaxDrawDistance;
        public float MaxDrawDistanceFade;
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
        public float LightIntensity = 10;
        public Color LightColor = Color.white;
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
        public bool EnableIES = false;
        public float IESIntensity = 1;
        public Texture2D IESTexture;
        public int IESTextureIndex = 0;
        public bool EnableCookie = false;
        public Texture2D CookieTexture;
        public int CookieTextureIndex = 0;

        ///Shadow Property
        public bool EnableShadow = true;
        public ELightLayer ShadowLayer = ELightLayer.LightLayerDefault;
        public EShadowType ShadowType = EShadowType.PCF;
        public EShadowResolution ShadowResolution = EShadowResolution.X1024;
        public Color ShadowColor = Color.black;
        public float ShadowIntensity = 1;
        public float ShadowBias = 0.01f;
        public float ShadowNormalBias = 0.01f;
        public float ShadowNearPlane = 0.05f;
        public float MinSoftness = 0.1f;
        public float MaxSoftness = 1;
        public EShadowCascade CascadeType = EShadowCascade.Four;
        public float ShadowDistance = 128;

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

        protected override void OnRigister()
        {
            base.OnRigister();
            GetWorld().AddWorldLight(this);

            UnityLight = GetComponent<Light>();
            InitLightType();
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

        protected override void UnRigister()
        {
            GetWorld().RemoveWorldLight(this);
        }

        public void OnGUIChange()
        {
            UpdateUnityLightParameters();
        }

        public void InitLightType() {
            switch (UnityLight.type)
            {
                case UnityEngine.LightType.Directional:
                    LightType = ELightType.Directional;
                    break;

                case UnityEngine.LightType.Point:
                    LightType = ELightType.Point;
                    break;

                case UnityEngine.LightType.Spot:
                    LightType = ELightType.Spot;
                    break;

                case UnityEngine.LightType.Disc:
                    LightType = ELightType.Spot;
                    break;

                case UnityEngine.LightType.Rectangle:
                    LightType = ELightType.Rect;
                    break;
            }
        }

        public void UpdateUnityLightParameters()
        {
            //Update UnityLightType
            switch (LightType)
            {
                case ELightType.Directional:
                    UnityLight.type = UnityEngine.LightType.Directional;
                    break;

                case ELightType.Point:
                    UnityLight.type = UnityEngine.LightType.Point;
                    break;

                case ELightType.Spot:
                    UnityLight.type = UnityEngine.LightType.Spot;
                    if(SourceRadius > 0) {
                        UnityLight.type = UnityEngine.LightType.Disc;
                    }
                    break;

                case ELightType.Rect:
                    UnityLight.type = UnityEngine.LightType.Rectangle;
                    break;
            }
        }

        public FLightBatch GetLightBatchElement() {
            FLightBatch LightBatch;
            {
                LightBatch.LightState = LightState;
                LightBatch.LightType = LightType;
                LightBatch.LightLayer = LightLayer;
                LightBatch.LightIntensity = LightIntensity;
                LightBatch.LightColor = LightColor;
                LightBatch.Temperature = Temperature;
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
                LightBatch.IESIntensity = IESIntensity;
                LightBatch.IESTextureIndex = IESTextureIndex;
                LightBatch.CookieTextureIndex = CookieTextureIndex;
                LightBatch.EnableShadow = EnableShadow ? 1 : 0;
                LightBatch.ShadowLayer = ShadowLayer;
                LightBatch.ShadowType = ShadowType;
                LightBatch.ShadowResolution = ShadowResolution;
                LightBatch.ShadowColor = ShadowColor;
                LightBatch.ShadowIntensity = ShadowIntensity;
                LightBatch.ShadowBias = ShadowBias;
                LightBatch.ShadowNormalBias = ShadowNormalBias;
                LightBatch.ShadowNearPlane = ShadowNearPlane;
                LightBatch.MinSoftness = MinSoftness;
                LightBatch.MaxSoftness = MaxSoftness;
                LightBatch.CascadeType = CascadeType;
                LightBatch.ShadowDistance = ShadowDistance;
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
