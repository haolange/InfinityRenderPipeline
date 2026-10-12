using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // Base editor for Volume components with a quality level. Quality-driven parameters are editable only in Custom;
    // for Low..Ultra the inspector shows the hardcoded preset value read-only, and "Copy Preset To Custom" writes the
    // preset into the parameters so experts can start from it.
    abstract class InfinityQualityVolumeEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Quality;

        protected void SetQualityParameter(SerializedDataParameter quality) => m_Quality = quality;

        protected EQualityLevel QualityLevel => m_Quality == null ? EQualityLevel.Custom : (EQualityLevel)m_Quality.value.intValue;

        protected bool IsCustom => m_Quality == null || m_Quality.value.hasMultipleDifferentValues || QualityLevel == EQualityLevel.Custom;

        protected void DrawQuality()
        {
            if (m_Quality == null)
            {
                return;
            }

            PropertyField(m_Quality);
            if (!IsCustom)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Copy Preset To Custom", EditorStyles.miniButton))
                    {
                        CopyPresetToCustom(QualityLevel);
                        m_Quality.overrideState.boolValue = true;
                        m_Quality.value.intValue = (int)EQualityLevel.Custom;
                    }
                }
            }
        }

        // Draws a quality-driven parameter: editable in Custom, otherwise the preset value is shown read-only.
        protected void QualityField(SerializedDataParameter parameter, Func<EQualityLevel, object> presetValue)
        {
            if (IsCustom)
            {
                PropertyField(parameter);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent(parameter.displayName, "Set by the quality preset. Choose Custom to edit."), Convert.ToString(presetValue(QualityLevel), System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected abstract void CopyPresetToCustom(EQualityLevel level);

        protected static void Set(SerializedDataParameter parameter, int value)
        {
            parameter.overrideState.boolValue = true;
            parameter.value.intValue = value;
        }

        protected static void Set(SerializedDataParameter parameter, float value)
        {
            parameter.overrideState.boolValue = true;
            parameter.value.floatValue = value;
        }
    }
}
