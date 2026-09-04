using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Feature;

namespace InfinityTech.Rendering.Editor
{
    [CustomEditor(typeof(AtmosphericalProfile))]
    public sealed class AtmosphericalProfileEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Atmosphere.Foldout.";

        SerializedProperty m_Radius;
        SerializedProperty m_Thickness;
        SerializedProperty m_Brightness;
        SerializedProperty m_DrawGround;
        SerializedProperty m_GroundAlbedo;
        SerializedProperty m_RayleighScatter;
        SerializedProperty m_RayleighStrength;
        SerializedProperty m_RayleighHeight;
        SerializedProperty m_MieStrength;
        SerializedProperty m_MieAbsorption;
        SerializedProperty m_MieHeight;
        SerializedProperty m_MieAnisotropy;
        SerializedProperty m_OzoneAbsorption;
        SerializedProperty m_OzoneStrength;
        SerializedProperty m_OzoneLayerCenter;
        SerializedProperty m_OzoneLayerWidth;
        SerializedProperty m_MultiScatterStrength;
        SerializedProperty m_SunAngle;
        SerializedProperty m_TransmittanceLUTWidth;
        SerializedProperty m_TransmittanceLUTHeight;
        SerializedProperty m_MultiScatteringLUTSize;
        SerializedProperty m_SkyViewLUTWidth;
        SerializedProperty m_SkyViewLUTHeight;
        SerializedProperty m_AerialPerspectiveSize;
        SerializedProperty m_AerialPerspectiveDistance;
        SerializedProperty m_CubemapSize;

        void OnEnable()
        {
            m_Radius = serializedObject.FindProperty("radius");
            m_Thickness = serializedObject.FindProperty("thickness");
            m_Brightness = serializedObject.FindProperty("brightness");
            m_DrawGround = serializedObject.FindProperty("drawGround");
            m_GroundAlbedo = serializedObject.FindProperty("groundAlbedo");
            m_RayleighScatter = serializedObject.FindProperty("rayleighScatter");
            m_RayleighStrength = serializedObject.FindProperty("rayleighStrength");
            m_RayleighHeight = serializedObject.FindProperty("rayleighHeight");
            m_MieStrength = serializedObject.FindProperty("mieStrength");
            m_MieAbsorption = serializedObject.FindProperty("mieAbsorption");
            m_MieHeight = serializedObject.FindProperty("mieHeight");
            m_MieAnisotropy = serializedObject.FindProperty("mieAnisotropy");
            m_OzoneAbsorption = serializedObject.FindProperty("ozoneAbsorption");
            m_OzoneStrength = serializedObject.FindProperty("ozoneStrength");
            m_OzoneLayerCenter = serializedObject.FindProperty("ozoneLayerCenter");
            m_OzoneLayerWidth = serializedObject.FindProperty("ozoneLayerWidth");
            m_MultiScatterStrength = serializedObject.FindProperty("multiScatterStrength");
            m_SunAngle = serializedObject.FindProperty("sunAngle");
            m_TransmittanceLUTWidth = serializedObject.FindProperty("transmittanceLUTWidth");
            m_TransmittanceLUTHeight = serializedObject.FindProperty("transmittanceLUTHeight");
            m_MultiScatteringLUTSize = serializedObject.FindProperty("multiScatteringLUTSize");
            m_SkyViewLUTWidth = serializedObject.FindProperty("skyViewLUTWidth");
            m_SkyViewLUTHeight = serializedObject.FindProperty("skyViewLUTHeight");
            m_AerialPerspectiveSize = serializedObject.FindProperty("aerialPerspectiveSize");
            m_AerialPerspectiveDistance = serializedObject.FindProperty("aerialPerspectiveDistance");
            m_CubemapSize = serializedObject.FindProperty("cubemapSize");
            UpgradeIfNeeded();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            #region Planet
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Planet", "Planet"))
            {
                EditorGUILayout.PropertyField(m_Radius, new GUIContent("Radius (m)"));
                EditorGUILayout.PropertyField(m_Thickness, new GUIContent("Atmosphere Thickness (m)"));
                EditorGUILayout.PropertyField(m_DrawGround, new GUIContent("Draw Ground"));
                EditorGUILayout.PropertyField(m_GroundAlbedo, new GUIContent("Ground Albedo"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Scatter
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Scatter", "Scatter"))
            {
                EditorGUILayout.PropertyField(m_Brightness, new GUIContent("Brightness"));
                EditorGUILayout.PropertyField(m_SunAngle, new GUIContent("Sun Angle (rad)"));
                EditorGUILayout.PropertyField(m_RayleighScatter, new GUIContent("Rayleigh Scatter (km^-1)"));
                EditorGUILayout.PropertyField(m_RayleighStrength, new GUIContent("Rayleigh Strength"));
                EditorGUILayout.PropertyField(m_RayleighHeight, new GUIContent("Rayleigh Height (m)"));
                EditorGUILayout.PropertyField(m_MieStrength, new GUIContent("Mie Strength (km^-1)"));
                EditorGUILayout.PropertyField(m_MieAbsorption, new GUIContent("Mie Absorption (km^-1)"));
                EditorGUILayout.PropertyField(m_MieHeight, new GUIContent("Mie Height (m)"));
                EditorGUILayout.PropertyField(m_MieAnisotropy, new GUIContent("Mie Anisotropy"));
                EditorGUILayout.PropertyField(m_OzoneAbsorption, new GUIContent("Ozone Absorption (km^-1)"));
                EditorGUILayout.PropertyField(m_OzoneStrength, new GUIContent("Ozone Strength"));
                EditorGUILayout.PropertyField(m_OzoneLayerCenter, new GUIContent("Ozone Layer Center (m)"));
                EditorGUILayout.PropertyField(m_OzoneLayerWidth, new GUIContent("Ozone Layer Width (m)"));
                EditorGUILayout.PropertyField(m_MultiScatterStrength, new GUIContent("Multi-Scatter Strength"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Quality
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Quality", "Quality", false))
            {
                EditorGUILayout.PropertyField(m_TransmittanceLUTWidth, new GUIContent("Transmittance LUT Width"));
                EditorGUILayout.PropertyField(m_TransmittanceLUTHeight, new GUIContent("Transmittance LUT Height"));
                EditorGUILayout.PropertyField(m_MultiScatteringLUTSize, new GUIContent("Multi-Scatter LUT Size"));
                EditorGUILayout.PropertyField(m_SkyViewLUTWidth, new GUIContent("Sky View LUT Width"));
                EditorGUILayout.PropertyField(m_SkyViewLUTHeight, new GUIContent("Sky View LUT Height"));
                EditorGUILayout.PropertyField(m_AerialPerspectiveSize, new GUIContent("Aerial Perspective Size"));
                EditorGUILayout.PropertyField(m_AerialPerspectiveDistance, new GUIContent("Aerial Perspective Distance (m)"));
                EditorGUILayout.PropertyField(m_CubemapSize, new GUIContent("Sky Cubemap Size"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            if (GUILayout.Button("Reset to Earth (Hillaire)"))
            {
                serializedObject.ApplyModifiedProperties();
                AtmosphericalProfile profile = (AtmosphericalProfile)target;
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Reset Atmospherical Profile to Earth");
                    profile.ResetToEarth();
                    EditorUtility.SetDirty(profile);
                    serializedObject.Update();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        void UpgradeIfNeeded()
        {
            AtmosphericalProfile profile = (AtmosphericalProfile)target;
            if (profile == null)
            {
                return;
            }

            Undo.RecordObject(profile, "Upgrade Atmospherical Profile");
            if (profile.UpgradeOutOfRangeToEarth())
            {
                EditorUtility.SetDirty(profile);
            }
        }
    }
}
