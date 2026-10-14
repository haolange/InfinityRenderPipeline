using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // Inspector rows for FFrameSettings (RP Asset defaults) and FFrameSettingsOverride (camera overrides). Both edit
    // the serialized masks directly so multi-object editing and Undo work.
    internal static class InfinityFrameSettingsGUI
    {
        static readonly GUIContent[] s_Labels =
        {
            new GUIContent("Decals", "DBuffer decals."),
            new GUIContent("Ambient Occlusion", "GTAO (when its Volume is active)."),
            new GUIContent("Ray-Traced AO", "RTAO; when off, GTAO owns ambient occlusion."),
            new GUIContent("Contact Shadows", "Screen-space traced shadows of the sun and local lights."),
            new GUIContent("Screen Space Reflection"),
            new GUIContent("Screen Space GI"),
            new GUIContent("Volumetric Fog"),
            new GUIContent("Volumetric Clouds"),
            new GUIContent("Reflection Probes", "Off: only the sky reflects."),
            new GUIContent("Light Cookies", "Cookies and IES profiles."),
        };

        static GUIContent Label(EFrameSetting setting) => (int)setting < s_Labels.Length ? s_Labels[(int)setting] : new GUIContent(setting.ToString());

        public static void DrawSettings(SerializedProperty frameSettings)
        {
            SerializedProperty disabled = frameSettings.FindPropertyRelative("m_Disabled");
            foreach (EFrameSetting setting in FrameSettingsUtility.All)
            {
                uint bit = 1u << (int)setting;
                EditorGUI.showMixedValue = disabled.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.Toggle(Label(setting), (disabled.uintValue & bit) == 0);
                if (EditorGUI.EndChangeCheck())
                    disabled.uintValue = enabled ? disabled.uintValue & ~bit : disabled.uintValue | bit;
                EditorGUI.showMixedValue = false;
            }
        }

        // One row per setting: an override check box, then the value (editable only when overridden).
        public static void DrawOverrides(SerializedProperty frameSettingsOverride, FFrameSettings baseline)
        {
            SerializedProperty overridden = frameSettingsOverride.FindPropertyRelative("m_Overridden");
            SerializedProperty disabled = frameSettingsOverride.FindPropertyRelative("m_Values").FindPropertyRelative("m_Disabled");
            foreach (EFrameSetting setting in FrameSettingsUtility.All)
            {
                uint bit = 1u << (int)setting;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.showMixedValue = overridden.hasMultipleDifferentValues;
                    EditorGUI.BeginChangeCheck();
                    bool isOverridden = EditorGUILayout.Toggle((overridden.uintValue & bit) != 0, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck())
                    {
                        overridden.uintValue = isOverridden ? overridden.uintValue | bit : overridden.uintValue & ~bit;
                        // A new override starts from the value the camera already had.
                        if (isOverridden)
                            disabled.uintValue = baseline.IsEnabled(setting) ? disabled.uintValue & ~bit : disabled.uintValue | bit;
                    }
                    EditorGUI.showMixedValue = disabled.hasMultipleDifferentValues;
                    using (new EditorGUI.DisabledScope(!isOverridden))
                    {
                        bool value = isOverridden ? (disabled.uintValue & bit) == 0 : baseline.IsEnabled(setting);
                        EditorGUI.BeginChangeCheck();
                        value = EditorGUILayout.Toggle(Label(setting), value);
                        if (EditorGUI.EndChangeCheck() && isOverridden)
                            disabled.uintValue = value ? disabled.uintValue & ~bit : disabled.uintValue | bit;
                    }
                    EditorGUI.showMixedValue = false;
                }
            }
        }
    }
}
