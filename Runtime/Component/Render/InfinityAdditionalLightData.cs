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
        [Tooltip("Lights farther than this from the camera are not rendered (meters).")]
        public float maxDrawDistance = 128;
        [Tooltip("Distance over which the light fades out before Maximum Distance (meters).")]
        public float maxDrawDistanceFade = 1;
        [Tooltip("IES profile from an imported .ies file: the Cube texture applies to every local light by direction, the 2D texture to cone and pyramid spots. The cookie is the Light's own Cookie.")]
        public Texture iesProfile;

        // Source shape in Unreal form. Authority over the native shape fields (type, shapeRadius, areaSize,
        // shadowAngle) once authored; until then the shape is read from the native Light.
        [SerializeField] bool m_ShapeAuthored;
        [SerializeField] FLightShape m_Shape = FLightShape.Default(EInfinityLightType.Point);
        [SerializeField] EShadowFilter m_ShadowFilter = EShadowFilter.PCF;
        [SerializeField] EShadowUpdateMode m_ShadowUpdateMode = EShadowUpdateMode.EveryFrame;
        int m_ShadowRequestCount;
        int m_EnableGeneration;

        public bool shapeAuthored => m_ShapeAuthored;

        // The shape the renderer uses: the authored shape, or the shape derived from the native Light.
        public FLightShape shape => m_ShapeAuthored ? m_Shape : LightShapeUtility.FromUnityLight(attachedLight);

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
        public void SetShape(in FLightShape value)
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
            if (light == null) return;
            m_Shape = LightShapeUtility.FromUnityLight(light);
            m_ShapeAuthored = true;
            m_ShadowFilter = light.shadows == LightShadows.Hard ? EShadowFilter.Hard : EShadowFilter.PCF;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
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
