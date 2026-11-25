using UnityEngine;
using InfinityTech.Core;
using InfinityTech.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("InfinityRenderer/Additional Light Data")]
    [HelpURL(InfinityTech.Core.InfinityDocumentation.BaseUrl + "lights.md")]
    public sealed class InfinityAdditionalLightData : EntityComponent
    {
        // The live set of light extensions by light entity id: the authority the scene world's light table derives from.
        internal static readonly ComponentRegistry<InfinityAdditionalLightData> s_Registry = new ComponentRegistry<InfinityAdditionalLightData>();
        internal const float DefaultDiffuse = 1;
        internal const float DefaultSpecular = 1;
        internal const bool DefaultContactShadow = false;
        internal const bool DefaultVolumetric = true;
        internal const float DefaultVolumetricIntensity = 1;
        internal const float DefaultVolumetricOcclusion = 1;
        internal const float DefaultDistanceFade = 1;
        internal const string MaximumDistanceTooltip = "Lights farther than this from the camera are not rendered (meters).";
        internal const string DistanceFadeTooltip = "Distance over which the light fades out before Maximum Distance (meters).";
        internal const string IESTooltip = "IES profile from an imported .ies file: the Cube texture applies to every local light by direction, the 2D texture to Cone, Pyramid, and Disk + Cone lights. The cookie is the Light's own Cookie.";

        Light m_UnityLight;
        ulong m_RegisteredLightId;

        public Light attachedLight => m_UnityLight ? m_UnityLight : (m_UnityLight = GetComponent<Light>());
        public ERenderingLayer lightLayer = ERenderingLayer.LightLayerDefault;
        public float diffuse = DefaultDiffuse;
        public float specular = DefaultSpecular;
        public bool enableContactShadow = DefaultContactShadow;
        public bool enableVolumetric = DefaultVolumetric;
        public float volumetricIntensity = DefaultVolumetricIntensity;
        public float volumetricOcclusion = DefaultVolumetricOcclusion;
        [Tooltip(MaximumDistanceTooltip)]
        public float maxDrawDistance = 128;
        [Tooltip(DistanceFadeTooltip)]
        public float maxDrawDistanceFade = DefaultDistanceFade;
        [Tooltip(IESTooltip)]
        public Texture iesProfile;

        // Source shape in Infinity two-dimensional (Emitter x Distribution) form. Authority over the native shape
        // fields (type, shapeRadius, areaSize, shadowAngle) once authored; until then read from the native Light.
        [SerializeField] bool m_ShapeAuthored;
        [SerializeField] LightSourceShape m_Shape = LightSourceShape.DefaultLocal(ELocalLightEmitter.Round, ELocalLightDistribution.Omnidirectional);
        [SerializeField] ELightIntensityEditMode m_IntensityEditMode = ELightIntensityEditMode.PhysicallyBased;
        [SerializeField] EShadowFilter m_ShadowFilter = EShadowFilter.PCF;
        [SerializeField] EShadowUpdateMode m_ShadowUpdateMode = EShadowUpdateMode.EveryFrame;
        int m_ShadowRequestCount;
        int m_EnableGeneration;

        public bool shapeAuthored => m_ShapeAuthored;

        // The shape the renderer uses: the authored shape, or the shape derived from the native Light.
        public LightSourceShape shape => m_ShapeAuthored ? m_Shape : LightShapeUtility.FromUnityLight(attachedLight);

        public ELightIntensityEditMode intensityEditMode
        {
            get => m_IntensityEditMode;
            set => m_IntensityEditMode = value;
        }

        public EShadowFilter shadowFilter
        {
            get => m_ShadowFilter;
            set => m_ShadowFilter = value;
        }

        public EShadowUpdateMode shadowUpdateMode
        {
            get => m_ShadowUpdateMode;
            set => m_ShadowUpdateMode = value;
        }

        // OnDemand lights re-render their cached shadow tiles on the next frame that draws them.
        public void RequestShadowMapRendering()
        {
            m_ShadowRequestCount++;
        }

        // Version the shadow cache compares against: requests for OnDemand, enables for OnEnable.
        internal int shadowCacheVersion => m_ShadowUpdateMode == EShadowUpdateMode.OnDemand ? m_ShadowRequestCount : m_EnableGeneration;

        // Sets the authored shape and writes the derived native Light fields. The Editor records Undo on both
        // objects before calling this.
        public void SetShape(in LightSourceShape value)
        {
            m_Shape = value;
            m_ShapeAuthored = true;
            LightShapeUtility.ApplyToUnityLight(attachedLight, m_Shape);
        }

        // Makes the native shape the authored shape. Rendering does not change; afterwards this component is the
        // shape authority.
        public void AdoptNativeShape()
        {
            Light light = attachedLight;
            if (light == null)
            {
                return;
            }
            FreezeNativeShape();
            m_ShadowFilter = light.shadows == LightShadows.Hard ? EShadowFilter.Hard : EShadowFilter.PCF;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // Makes the shape derived from the native Light the authored shape without writing anything else, so the
        // light renders the same where the native extent fields do not exist (a player).
        internal void FreezeNativeShape()
        {
            m_Shape = LightShapeUtility.FromUnityLight(attachedLight);
            m_ShapeAuthored = true;
        }

        // Adopts the native shape when the component is added, so adding it never changes how the light renders.
        void Reset()
        {
            AdoptNativeShape();
        }

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
            m_EnableGeneration++;
            m_UnityLight = GetComponent<Light>();
            m_RegisteredLightId = UnityEntityId.ToUInt64(m_UnityLight);
            s_Registry.Register(m_RegisteredLightId, this);
        }

        protected override void UnRegister()
        {
            s_Registry.Unregister(m_RegisteredLightId);
        }
    }
}
