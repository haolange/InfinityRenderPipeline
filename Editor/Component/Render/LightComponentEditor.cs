using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public class LightComponentEditor : LightEditor
    {
        SerializedObject m_UnityLightObject;
        SerializedObject m_ExtensionObject;
        LightComponent m_LightComponent;

        SerializedProperty m_Type;
        SerializedProperty m_Color;
        SerializedProperty m_Intensity;
        SerializedProperty m_Range;
        SerializedProperty m_SpotAngle;
        SerializedProperty m_InnerSpotAngle;
        SerializedProperty m_AreaSize;
        SerializedProperty m_Shadows;
        SerializedProperty m_UseColorTemperature;
        SerializedProperty m_ColorTemperature;
        SerializedProperty m_BounceIntensity;

        SerializedProperty m_State;
        SerializedProperty m_LightLayer;
        SerializedProperty m_Diffuse;
        SerializedProperty m_Specular;
        SerializedProperty m_Width;
        SerializedProperty m_Height;
        SerializedProperty m_EnableIndirect;
        SerializedProperty m_IndirectIntensity;
        SerializedProperty m_EnableShadow;
        SerializedProperty m_ShadowLayer;
        SerializedProperty m_ShadowType;
        SerializedProperty m_Resolution;
        SerializedProperty m_NearPlane;
        SerializedProperty m_MinSoftness;
        SerializedProperty m_MaxSoftness;
        SerializedProperty m_EnableContactShadow;
        SerializedProperty m_ContactShadowLength;
        SerializedProperty m_EnableVolumetric;
        SerializedProperty m_VolumetricIntensity;
        SerializedProperty m_VolumetricOcclusion;
        SerializedProperty m_IESIndex;
        SerializedProperty m_IESTexture;
        SerializedProperty m_CookieIndex;
        SerializedProperty m_CookieTexture;
        SerializedProperty m_MaxDrawDistance;
        SerializedProperty m_MaxDrawDistanceFade;

        protected override void OnEnable()
        {
            base.OnEnable();
            BindTargets();
        }

        public override void OnInspectorGUI()
        {
            BindTargets();
            if (m_UnityLightObject == null)
            {
                return;
            }

            m_UnityLightObject.Update();
            DrawUnityLight();
            m_UnityLightObject.ApplyModifiedProperties();

            if (m_ExtensionObject == null)
            {
                return;
            }

            m_ExtensionObject.Update();
            DrawExtensions();
            m_ExtensionObject.ApplyModifiedProperties();
        }

        void BindTargets()
        {
            Light light = target as Light;
            if (light == null)
            {
                return;
            }

            m_UnityLightObject = serializedObject;
            m_Type = m_UnityLightObject.FindProperty("m_Type");
            m_Color = m_UnityLightObject.FindProperty("m_Color");
            m_Intensity = m_UnityLightObject.FindProperty("m_Intensity");
            m_Range = m_UnityLightObject.FindProperty("m_Range");
            m_SpotAngle = m_UnityLightObject.FindProperty("m_SpotAngle");
            m_InnerSpotAngle = m_UnityLightObject.FindProperty("m_InnerSpotAngle");
            m_AreaSize = m_UnityLightObject.FindProperty("m_AreaSize");
            m_Shadows = m_UnityLightObject.FindProperty("m_Shadows");
            m_UseColorTemperature = m_UnityLightObject.FindProperty("m_UseColorTemperature");
            m_ColorTemperature = m_UnityLightObject.FindProperty("m_ColorTemperature");
            m_BounceIntensity = m_UnityLightObject.FindProperty("m_BounceIntensity");

            m_LightComponent = light.GetComponent<LightComponent>();
            if (m_LightComponent == null)
            {
                m_LightComponent = light.gameObject.AddComponent<LightComponent>();
            }

            m_ExtensionObject = new SerializedObject(m_LightComponent);
            m_State = m_ExtensionObject.FindProperty("state");
            m_LightLayer = m_ExtensionObject.FindProperty("lightLayer");
            m_Diffuse = m_ExtensionObject.FindProperty("diffuse");
            m_Specular = m_ExtensionObject.FindProperty("specular");
            m_Width = m_ExtensionObject.FindProperty("width");
            m_Height = m_ExtensionObject.FindProperty("height");
            m_EnableIndirect = m_ExtensionObject.FindProperty("enableIndirect");
            m_IndirectIntensity = m_ExtensionObject.FindProperty("indirectIntensity");
            m_EnableShadow = m_ExtensionObject.FindProperty("enableShadow");
            m_ShadowLayer = m_ExtensionObject.FindProperty("shadowLayer");
            m_ShadowType = m_ExtensionObject.FindProperty("shadowType");
            m_Resolution = m_ExtensionObject.FindProperty("resolution");
            m_NearPlane = m_ExtensionObject.FindProperty("nearPlane");
            m_MinSoftness = m_ExtensionObject.FindProperty("minSoftness");
            m_MaxSoftness = m_ExtensionObject.FindProperty("maxSoftness");
            m_EnableContactShadow = m_ExtensionObject.FindProperty("enableContactShadow");
            m_ContactShadowLength = m_ExtensionObject.FindProperty("contactShadowLength");
            m_EnableVolumetric = m_ExtensionObject.FindProperty("enableVolumetric");
            m_VolumetricIntensity = m_ExtensionObject.FindProperty("volumetricIntensity");
            m_VolumetricOcclusion = m_ExtensionObject.FindProperty("volumetricOcclusion");
            m_IESIndex = m_ExtensionObject.FindProperty("IESIndex");
            m_IESTexture = m_ExtensionObject.FindProperty("IESTexture");
            m_CookieIndex = m_ExtensionObject.FindProperty("cookieIndex");
            m_CookieTexture = m_ExtensionObject.FindProperty("cookieTexture");
            m_MaxDrawDistance = m_ExtensionObject.FindProperty("maxDrawDistance");
            m_MaxDrawDistanceFade = m_ExtensionObject.FindProperty("maxDrawDistanceFade");
        }

        void DrawUnityLight()
        {
            EditorGUILayout.LabelField("Unity Light", EditorStyles.boldLabel);
            if (m_Type != null)
            {
                EditorGUILayout.PropertyField(m_Type, new GUIContent("Type"));
            }

            if (m_Color != null)
            {
                EditorGUILayout.PropertyField(m_Color, new GUIContent("Color"));
            }

            if (m_Intensity != null)
            {
                EditorGUILayout.PropertyField(m_Intensity, new GUIContent("Intensity"));
            }

            if (m_UseColorTemperature != null)
            {
                EditorGUILayout.PropertyField(m_UseColorTemperature, new GUIContent("Use Color Temperature"));
            }

            if (m_ColorTemperature != null && (m_UseColorTemperature == null || m_UseColorTemperature.boolValue))
            {
                EditorGUILayout.PropertyField(m_ColorTemperature, new GUIContent("Temperature"));
            }

            LightType type = m_Type != null ? (LightType)m_Type.enumValueIndex : LightType.Directional;
            bool local = type != LightType.Directional;
            if (local && m_Range != null)
            {
                EditorGUILayout.PropertyField(m_Range, new GUIContent("Range"));
            }

            if (type == LightType.Spot)
            {
                if (m_SpotAngle != null)
                {
                    EditorGUILayout.PropertyField(m_SpotAngle, new GUIContent("Spot Angle"));
                }

                if (m_InnerSpotAngle != null)
                {
                    EditorGUILayout.PropertyField(m_InnerSpotAngle, new GUIContent("Inner Spot Angle"));
                }
            }

            if (type == LightType.Rectangle)
            {
                if (m_AreaSize != null)
                {
                    EditorGUILayout.PropertyField(m_AreaSize, new GUIContent("Area Size"));
                }

                EditorGUILayout.HelpBox("Rect lights do not cast shadows in InfinityRP.", MessageType.Info);
            }

            if (m_Shadows != null && type != LightType.Rectangle)
            {
                EditorGUILayout.PropertyField(m_Shadows, new GUIContent("Shadows"));
            }

            if (m_BounceIntensity != null)
            {
                EditorGUILayout.PropertyField(m_BounceIntensity, new GUIContent("Bounce Intensity"));
            }
        }

        void DrawExtensions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Infinity Extensions", EditorStyles.boldLabel);
            const string foldoutPrefix = "InfinityRP.Light.Foldout.";

            #region General
            if (InfinityInspectorGUI.BeginFoldout(foldoutPrefix + "General", "General"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_State, new GUIContent("State"));
                EditorGUILayout.PropertyField(m_LightLayer, new GUIContent("Layer"));
                EditorGUI.indentLevel--;
            }

            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Shading
            if (InfinityInspectorGUI.BeginFoldout(foldoutPrefix + "Shading", "Shading"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.Slider(m_Diffuse, 0, 1, new GUIContent("Diffuse"));
                EditorGUILayout.Slider(m_Specular, 0, 1, new GUIContent("Specular"));
                LightType type = m_Type != null ? (LightType)m_Type.enumValueIndex : LightType.Directional;
                if (type == LightType.Rectangle)
                {
                    EditorGUILayout.PropertyField(m_Width, new GUIContent("Width (fallback)"));
                    EditorGUILayout.PropertyField(m_Height, new GUIContent("Height (fallback)"));
                }

                EditorGUILayout.PropertyField(m_IESIndex, new GUIContent("IES Index"));
                EditorGUILayout.PropertyField(m_IESTexture, new GUIContent("IES Texture"));
                EditorGUILayout.PropertyField(m_CookieIndex, new GUIContent("Cookie Index"));
                EditorGUILayout.PropertyField(m_CookieTexture, new GUIContent("Cookie Texture"));
                EditorGUI.indentLevel--;
            }

            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Indirect
            if (InfinityInspectorGUI.BeginFoldout(foldoutPrefix + "Indirect", "Indirect"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_EnableIndirect, new GUIContent("Enable"));
                using (new EditorGUI.DisabledScope(!m_EnableIndirect.boolValue))
                {
                    EditorGUILayout.Slider(m_IndirectIntensity, 0, 16, new GUIContent("Intensity"));
                }

                EditorGUI.indentLevel--;
            }

            InfinityInspectorGUI.EndFoldout();
            #endregion

            LightType lightType = m_Type != null ? (LightType)m_Type.enumValueIndex : LightType.Directional;
            #region Shadow
            if (InfinityInspectorGUI.BeginFoldout(foldoutPrefix + "Shadow", "Shadow", false))
            {
                EditorGUI.indentLevel++;
                if (lightType == LightType.Rectangle)
                {
                    EditorGUILayout.HelpBox("Rect lights do not receive a shadow atlas allocation.", MessageType.None);
                }
                else
                {
                    EditorGUILayout.PropertyField(m_EnableShadow, new GUIContent("Enable (overlay)"));
                    EditorGUILayout.HelpBox("Unity Light.shadows is the cull authority.", MessageType.None);
                    using (new EditorGUI.DisabledScope(!m_EnableShadow.boolValue))
                    {
                        EditorGUILayout.PropertyField(m_ShadowType, new GUIContent("Type"));
                        EditorGUILayout.PropertyField(m_ShadowLayer, new GUIContent("Layer"));
                        EditorGUILayout.PropertyField(m_Resolution, new GUIContent("Resolution"));
                        EditorGUILayout.Slider(m_NearPlane, 0, 10, new GUIContent("Near Plane"));
                        if (m_ShadowType.enumValueIndex == (int)EShadowType.PCSS)
                        {
                            EditorGUILayout.Slider(m_MinSoftness, 0, 2, new GUIContent("Min Softness"));
                            EditorGUILayout.Slider(m_MaxSoftness, 0, 2, new GUIContent("Max Softness"));
                        }

                        bool contactOpen = SessionState.GetBool(foldoutPrefix + "ContactShadow", true);
                        bool contactNext = EditorGUILayout.Foldout(contactOpen, "Contact Shadow", true, EditorStyles.foldoutHeader);
                        if (contactNext != contactOpen)
                        {
                            SessionState.SetBool(foldoutPrefix + "ContactShadow", contactNext);
                        }

                        if (contactNext)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(m_EnableContactShadow, new GUIContent("Enable"));
                            using (new EditorGUI.DisabledScope(!m_EnableContactShadow.boolValue))
                            {
                                EditorGUILayout.Slider(m_ContactShadowLength, 0, 1, new GUIContent("Length"));
                            }

                            EditorGUI.indentLevel--;
                        }
                    }
                }

                EditorGUI.indentLevel--;
            }

            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Volumetric
            if (InfinityInspectorGUI.BeginFoldout(foldoutPrefix + "Volumetric", "Volumetric"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_EnableVolumetric, new GUIContent("Enable"));
                using (new EditorGUI.DisabledScope(!m_EnableVolumetric.boolValue))
                {
                    EditorGUILayout.Slider(m_VolumetricIntensity, 0, 32, new GUIContent("Intensity"));
                    EditorGUILayout.Slider(m_VolumetricOcclusion, 0, 1, new GUIContent("Occlusion"));
                }

                EditorGUI.indentLevel--;
            }

            InfinityInspectorGUI.EndFoldout();
            #endregion

            if (lightType != LightType.Directional)
            {
                #region Performance
                if (InfinityInspectorGUI.BeginFoldout(foldoutPrefix + "Performance", "Performance"))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(m_MaxDrawDistance, new GUIContent("Max Draw Distance"));
                    EditorGUILayout.PropertyField(m_MaxDrawDistanceFade, new GUIContent("Fade"));
                    EditorGUI.indentLevel--;
                }

                InfinityInspectorGUI.EndFoldout();
                #endregion
            }
        }
    }

    [CustomEditor(typeof(LightComponent))]
    public class LightComponentOnlyEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Infinity light extensions are edited on the Unity Light inspector.", MessageType.Info);
        }
    }
}
