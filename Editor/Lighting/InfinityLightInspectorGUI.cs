using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Component.Editor
{
    internal static class InfinityLightInspectorGUI
    {
        const float MinimumKelvin = 1000f;
        const float NeutralKelvin = 6500f;
        const float MaximumKelvin = 20000f;
        const float NarrowIntensityStackWidth = 320f;
        const float TemperatureStackWidth = 350f;
        const float ControlGap = 4f;
        const float UnitPopupWidth = 68f;

        static readonly GUIContent s_ColorLabel = new GUIContent("Color / Intensity", "Light color and physical intensity.");
        static readonly GUIContent s_ColorFilterLabel = new GUIContent("Color / Intensity", "The swatch filters the temperature color; intensity sits to the right.");
        static readonly GUIContent s_IntensityLabel = new GUIContent("Intensity", "Light intensity in the selected physical display unit.");
        static readonly GUIContent s_IntensityTooltip = new GUIContent("", "Intensity");
        static readonly GUIContent s_TemperatureLabel = new GUIContent("Color Temperature", "The light's color temperature in Kelvin.");
        static readonly GUIContent s_KelvinLabel = new GUIContent("K", "Kelvin");
        static Texture2D s_TemperatureGradient;
        static GUIStyle s_SectionTitle;
        static bool s_DarkSectionTitle;
        static readonly string[] s_UnitBuffer1 = new string[1];
        static readonly string[] s_UnitBuffer2 = new string[2];
        static readonly string[] s_UnitBuffer3 = new string[3];
        static readonly string[] s_UnitBuffer4 = new string[4];

        internal static bool Section(string key, string title)
        {
            GUILayout.Space(5f);
            Rect header = EditorGUILayout.GetControlRect(false, 22f);
            bool dark = EditorGUIUtility.isProSkin;
            if (Event.current.type == EventType.Repaint)
            {
                Rect background = header;
                background.xMin -= 10f;
                EditorGUI.DrawRect(background, dark ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.80f, 0.80f, 0.80f));
                Rect separator = background;
                separator.height = 1f / EditorGUIUtility.pixelsPerPoint;
                EditorGUI.DrawRect(separator, dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.64f, 0.64f, 0.64f));
            }

            if (s_SectionTitle == null || s_DarkSectionTitle != dark)
            {
                s_SectionTitle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
                s_DarkSectionTitle = dark;
            }
            bool previous = SessionState.GetBool(key, true);
            bool expanded = EditorGUI.Foldout(header, previous, title, true, s_SectionTitle);
            if (expanded != previous)
                SessionState.SetBool(key, expanded);
            if (expanded)
                GUILayout.Space(3f);
            return expanded;
        }

        internal static void ColorAndIntensity(
            SerializedProperty color,
            float displayIntensity,
            bool mixedIntensity,
            LightUnit displayUnit,
            bool mixedUnit,
            IReadOnlyList<LightUnit> supportedUnits,
            bool useTemperature,
            out float nextIntensity,
            out bool intensityChanged,
            out LightUnit nextUnit,
            out bool unitChanged)
        {
            nextIntensity = displayIntensity;
            intensityChanged = false;
            nextUnit = displayUnit;
            unitChanged = false;

            bool narrow = EditorGUIUtility.currentViewWidth < NarrowIntensityStackWidth;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect unitRect;
            Rect intensityRect;
            if (narrow)
            {
                EditorGUILayout.PropertyField(color, useTemperature ? s_ColorFilterLabel : s_ColorLabel);
                Rect secondRow = EditorGUILayout.GetControlRect(true, lineHeight);
                Rect indented = EditorGUI.PrefixLabel(secondRow, s_IntensityLabel);
                unitRect = new Rect(indented.xMax - UnitPopupWidth, indented.y, UnitPopupWidth, lineHeight);
                intensityRect = new Rect(indented.x, indented.y, Mathf.Max(40f, unitRect.xMin - ControlGap - indented.x), lineHeight);
            }
            else
            {
                Rect row = EditorGUILayout.GetControlRect(true, lineHeight);
                unitRect = new Rect(row.xMax - UnitPopupWidth, row.y, UnitPopupWidth, lineHeight);
                Rect colorAndIntensity = new Rect(row.x, row.y, Mathf.Max(60f, unitRect.xMin - ControlGap - row.x), lineHeight);
                intensityRect = SplitNumericField(ref colorAndIntensity);
                EditorGUI.PropertyField(colorAndIntensity, color, useTemperature ? s_ColorFilterLabel : s_ColorLabel);
            }

            using var indent = new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel);
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = mixedIntensity;
            using (var change = new EditorGUI.ChangeCheckScope())
            {
                float entered = EditorGUI.DelayedFloatField(intensityRect, GUIContent.none, displayIntensity);
                if (change.changed && float.IsFinite(entered))
                {
                    nextIntensity = Mathf.Max(0f, entered);
                    intensityChanged = true;
                }
            }
            if (!narrow)
                GUI.Label(intensityRect, s_IntensityTooltip);

            int unitCount = supportedUnits != null ? supportedUnits.Count : 0;
            using (new EditorGUI.DisabledScope(unitCount <= 1))
            {
                if (unitCount > 0)
                {
                    string[] labels = FillUnitLabels(supportedUnits);
                    int selectedIndex = -1;
                    if (!mixedUnit)
                    {
                        for (int i = 0; i < unitCount; ++i)
                        {
                            if (supportedUnits[i] == displayUnit)
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                    }

                    EditorGUI.showMixedValue = mixedUnit || selectedIndex < 0;
                    using var unitScope = new EditorGUI.ChangeCheckScope();
                    int chosen = EditorGUI.Popup(unitRect, selectedIndex, labels);
                    if (unitScope.changed && chosen >= 0 && chosen < unitCount)
                    {
                        nextUnit = supportedUnits[chosen];
                        unitChanged = true;
                    }
                }
                else
                {
                    EditorGUI.showMixedValue = true;
                    EditorGUI.Popup(unitRect, -1, s_UnitBuffer1);
                }
            }
            EditorGUI.showMixedValue = previousMixed;
        }

        internal static void FloatSlider(SerializedProperty property, GUIContent label, Vector2 dragRange)
        {
            Rect row = EditorGUILayout.GetControlRect();
            using var scope = new EditorGUI.PropertyScope(row, label, property);
            Rect track = EditorGUI.PrefixLabel(row, scope.content);
            Rect field = SplitNumericField(ref track);
            using var indent = new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel);

            bool mixed = property.hasMultipleDifferentValues;
            float value = property.floatValue;
            if (track.width > 2f)
            {
                float position = mixed ? -1f : Mathf.InverseLerp(dragRange.x, dragRange.y, value);
                GUIStyle thumb = mixed ? GUIStyle.none : GUI.skin.horizontalSliderThumb;
                using var sliderChange = new EditorGUI.ChangeCheckScope();
                float next = GUI.HorizontalSlider(track, position, 0f, 1f, GUI.skin.horizontalSlider, thumb);
                if (sliderChange.changed && next >= 0f)
                    property.floatValue = value = Mathf.Round(Mathf.Lerp(dragRange.x, dragRange.y, next) * 1000f) / 1000f;
            }

            // The drag range is an authoring convenience. The numeric field retains the property's full domain.
            using var fieldChange = new EditorGUI.ChangeCheckScope();
            float entered = EditorGUI.DelayedFloatField(field, value);
            if (fieldChange.changed && !float.IsNaN(entered) && !float.IsInfinity(entered))
                property.floatValue = entered;
        }

        internal static void FloatSliderValue(
            GUIContent label,
            float value,
            bool mixed,
            Vector2 dragRange,
            Vector2 clampRange,
            out float nextValue,
            out bool changed)
        {
            nextValue = value;
            changed = false;
            Rect row = EditorGUILayout.GetControlRect();
            Rect track = EditorGUI.PrefixLabel(row, label);
            Rect field = SplitNumericField(ref track);
            using var indent = new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel);

            bool previousMixed = EditorGUI.showMixedValue;
            if (track.width > 2f)
            {
                float position = mixed ? -1f : Mathf.InverseLerp(dragRange.x, dragRange.y, value);
                GUIStyle thumb = mixed ? GUIStyle.none : GUI.skin.horizontalSliderThumb;
                using var sliderChange = new EditorGUI.ChangeCheckScope();
                float next = GUI.HorizontalSlider(track, position, 0f, 1f, GUI.skin.horizontalSlider, thumb);
                if (sliderChange.changed && next >= 0f)
                {
                    nextValue = Mathf.Clamp(Mathf.Round(Mathf.Lerp(dragRange.x, dragRange.y, next) * 1000f) / 1000f, clampRange.x, clampRange.y);
                    changed = true;
                }
            }

            EditorGUI.showMixedValue = mixed;
            using (var fieldChange = new EditorGUI.ChangeCheckScope())
            {
                float entered = EditorGUI.DelayedFloatField(field, value);
                if (fieldChange.changed && float.IsFinite(entered))
                {
                    nextValue = Mathf.Clamp(entered, clampRange.x, clampRange.y);
                    changed = true;
                }
            }
            EditorGUI.showMixedValue = previousMixed;
        }

        static string[] FillUnitLabels(IReadOnlyList<LightUnit> units)
        {
            string[] labels = units.Count switch
            {
                1 => s_UnitBuffer1,
                2 => s_UnitBuffer2,
                3 => s_UnitBuffer3,
                4 => s_UnitBuffer4,
                _ => new string[units.Count]
            };
            for (int i = 0; i < units.Count; ++i)
                labels[i] = FormatUnitLabel(units[i]);
            return labels;
        }

        internal static string FormatUnitLabel(LightUnit unit) => unit switch
        {
            LightUnit.Lumen => "Lumen",
            LightUnit.Candela => "Candela",
            LightUnit.Nits => "Nits",
            LightUnit.Lux => "Lux",
            _ => unit.ToString()
        };

        static Rect SplitNumericField(ref Rect controls)
        {
            float width = Mathf.Max(54f, EditorGUIUtility.fieldWidth);
            var field = new Rect(controls.xMax - width, controls.y, width, controls.height);
            controls.xMax = field.xMin - ControlGap;
            return field;
        }

        internal static void Temperature(SerializedProperty property)
        {
            bool stackLabel = EditorGUIUtility.currentViewWidth < TemperatureStackWidth;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row = EditorGUILayout.GetControlRect(true, stackLabel ? 2f * lineHeight + spacing : lineHeight);
            using var propertyScope = new EditorGUI.PropertyScope(row, s_TemperatureLabel, property);

            Rect controls;
            if (stackLabel)
            {
                Rect label = row;
                label.height = lineHeight;
                EditorGUI.LabelField(label, propertyScope.content);
                controls = EditorGUI.IndentedRect(row);
                controls.y = label.yMax + spacing;
                controls.height = lineHeight;
            }
            else
            {
                controls = EditorGUI.PrefixLabel(row, propertyScope.content);
            }

            const float unitWidth = 12f;
            Rect unit = new Rect(controls.xMax - unitWidth, controls.y, unitWidth, lineHeight);
            Rect slider = controls;
            slider.xMax = unit.xMin - ControlGap;
            Rect field = SplitNumericField(ref slider);

            using var indent = new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel);
            float kelvin = property.floatValue;
            if (slider.width > 2f)
            {
                bool mixed = property.hasMultipleDifferentValues;
                float position = mixed ? -1f : PositionFromKelvin(kelvin);
                GUIStyle thumb = mixed ? GUIStyle.none : GUI.skin.horizontalSliderThumb;
                if (Event.current.type == EventType.Repaint)
                    DrawTemperatureTrack(slider);

                using var sliderChange = new EditorGUI.ChangeCheckScope();
                float next = GUI.HorizontalSlider(slider, position, 0f, 1f, GUIStyle.none, thumb);
                if (sliderChange.changed && next >= 0f)
                    property.floatValue = kelvin = Mathf.Round(KelvinFromPosition(next));
            }

            using (var fieldChange = new EditorGUI.ChangeCheckScope())
            {
                float next = EditorGUI.DelayedFloatField(field, kelvin);
                if (fieldChange.changed && !float.IsNaN(next))
                    property.floatValue = Mathf.Clamp(next, MinimumKelvin, MaximumKelvin);
            }
            EditorGUI.LabelField(unit, s_KelvinLabel, EditorStyles.miniLabel);
        }

        internal static float KelvinFromPosition(float position) => position <= 0.5f
            ? Mathf.Lerp(MinimumKelvin, NeutralKelvin, position * 2f)
            : Mathf.Lerp(NeutralKelvin, MaximumKelvin, (position - 0.5f) * 2f);

        internal static float PositionFromKelvin(float kelvin) => kelvin <= NeutralKelvin
            ? 0.5f * Mathf.InverseLerp(MinimumKelvin, NeutralKelvin, kelvin)
            : 0.5f + 0.5f * Mathf.InverseLerp(NeutralKelvin, MaximumKelvin, kelvin);

        static void DrawTemperatureTrack(Rect slider)
        {
            if (!s_TemperatureGradient)
            {
                const int samples = 128;
                var colors = new Color[samples];
                for (int x = 0; x < samples; ++x)
                    colors[x] = Mathf.CorrelatedColorTemperatureToRGB(KelvinFromPosition(x / (samples - 1f))).gamma;

                s_TemperatureGradient = new Texture2D(samples, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp
                };
                s_TemperatureGradient.SetPixels(colors);
                s_TemperatureGradient.Apply(false, true);
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseTemperatureGradient;
            }

            const float trackHeight = 8f;
            Rect border = new Rect(slider.x, slider.center.y - trackHeight * 0.5f, slider.width, trackHeight);
            EditorGUI.DrawRect(border, EditorGUIUtility.isProSkin ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.46f, 0.46f, 0.46f));
            Rect track = new Rect(border.x + 1f, border.y + 1f, border.width - 2f, border.height - 2f);
            Color tint = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(track, s_TemperatureGradient, ScaleMode.StretchToFill, true, 0f, tint, 0f, 0f);
        }

        static void ReleaseTemperatureGradient()
        {
            Object.DestroyImmediate(s_TemperatureGradient);
            s_TemperatureGradient = null;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseTemperatureGradient;
        }
    }
}
