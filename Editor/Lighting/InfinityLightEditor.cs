using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component.Editor
{
    // Light inspector of InfinityRP: Emitter -> Distribution -> Emission layout backed by LightSourceShape on
    // InfinityAdditionalLightData (lazily created on Infinity edits) and photometric LightIntensityUtility conversions.
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Light.Foldout.";
        static readonly GUIContent s_UseTemperatureLabel = new GUIContent("Use Temperature", "Tint the light by its color temperature.");
        static readonly GUIContent s_CasterLayersLabel = new GUIContent("Caster Layers");
        static readonly GUIContent s_OuterAngleLabel = new GUIContent("Outer Angle", "Outer cone angle in degrees.");
        static readonly GUIContent s_InnerAngleLabel = new GUIContent("Inner Angle", "Inner cone falloff start angle in degrees.");
        static readonly GUIContent s_LuxAtDistanceLabel = new GUIContent("Lux At Distance", "Reference distance in meters for local Lux intensity display.");
        static readonly GUIContent s_SourceLengthDisabledLabel = new GUIContent("Source Length", "Only Omnidirectional Round lights support Source Length.");
        static readonly Vector2 s_UnitDragRange = new Vector2(0f, 1f);
        static readonly Vector2 s_SunAngleDragRange = new Vector2(0f, 15f);
        static readonly Vector2 s_BarnDoorAngleDragRange = new Vector2(0f, 90f);
        static readonly Vector3[] s_ConeHandleAxes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down };

        Light[] m_Lights;
        LightSourceShape[] m_ShapeBuffer;
        Object[] m_AdditionalTargets;
        InfinityLightInspectorSnapshot[] m_Before;
        InfinityLightInspectorSnapshot[] m_After;
        SerializedObject[] m_BeforeObjects;
        SerializedObject[] m_AfterObjects;
        SerializedObject m_Snapshots;
        SerializedObject m_Additional;
        bool m_AllShapesAuthored;
        readonly List<LightUnit> m_CommonUnits = new List<LightUnit>(4);
        readonly BoxBoundsHandle m_RectHandle = new BoxBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };
        readonly BoxBoundsHandle m_BoxBeamHandle = new BoxBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };

        void OnEnable()
        {
            int count = targets.Length;
            m_Lights = new Light[count];
            m_ShapeBuffer = new LightSourceShape[count];
            m_AdditionalTargets = new Object[count];
            m_Before = new InfinityLightInspectorSnapshot[count];
            m_After = new InfinityLightInspectorSnapshot[count];
            m_BeforeObjects = new SerializedObject[count];
            m_AfterObjects = new SerializedObject[count];
            var snapshots = new Object[count];
            for (int i = 0; i < count; ++i)
            {
                m_Lights[i] = (Light)targets[i];
                m_Before[i] = CreateInstance<InfinityLightInspectorSnapshot>();
                m_After[i] = CreateInstance<InfinityLightInspectorSnapshot>();
                m_Before[i].hideFlags = HideFlags.DontSave;
                m_After[i].hideFlags = HideFlags.DontSave;
                m_BeforeObjects[i] = new SerializedObject(m_Before[i]);
                m_AfterObjects[i] = new SerializedObject(m_After[i]);
                snapshots[i] = m_After[i];
            }
            m_Snapshots = new SerializedObject(snapshots);
            Undo.undoRedoPerformed += Repaint;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            m_Additional?.Dispose();
            m_Snapshots?.Dispose();
            if (m_Before == null)
                return;
            for (int i = 0; i < m_Before.Length; ++i)
            {
                m_BeforeObjects[i]?.Dispose();
                m_AfterObjects[i]?.Dispose();
                DestroyImmediate(m_Before[i]);
                DestroyImmediate(m_After[i]);
            }
        }

        void RefreshBindings()
        {
            bool allPresent = true;
            bool changed = false;
            m_AllShapesAuthored = true;
            for (int i = 0; i < m_Lights.Length; ++i)
            {
                Light light = m_Lights[i];
                light.TryGetComponent(out InfinityAdditionalLightData data);
                changed |= m_AdditionalTargets[i] != data;
                m_AdditionalTargets[i] = data;
                allPresent &= data;
                m_AllShapesAuthored &= data && data.shapeAuthored;
                m_Before[i].Read(light);
                m_After[i].Read(light);
                m_ShapeBuffer[i] = m_Before[i].shape;
                m_BeforeObjects[i].Update();
                m_AfterObjects[i].Update();
            }
            if (!allPresent)
            {
                m_Additional?.Dispose();
                m_Additional = null;
            }
            else if (changed || m_Additional == null)
            {
                m_Additional?.Dispose();
                m_Additional = new SerializedObject(m_AdditionalTargets);
            }
            m_Additional?.Update();
            m_Snapshots.Update();
        }

        public override void OnInspectorGUI()
        {
            InfinityLightEditorTransactions.SyncDragState();
            RefreshBindings();
            serializedObject.Update();
            SerializedObject shapeOwner = m_AllShapesAuthored ? m_Additional : m_Snapshots;
            SerializedObject additionalOwner = m_Additional ?? m_Snapshots;

            foreach (Object additional in m_AdditionalTargets)
            {
                if (additional && !((InfinityAdditionalLightData)additional).enabled)
                {
                    EditorGUILayout.HelpBox("Realtime lighting ignores disabled Additional Light Data. Baking can still use its shape.", MessageType.Warning);
                    break;
                }
            }

            SerializedProperty shape = shapeOwner.FindProperty("m_Shape");
            SerializedProperty kindProperty = shape.FindPropertyRelative("kind");
            SerializedProperty emitterProperty = shape.FindPropertyRelative("emitter");
            SerializedProperty distributionProperty = shape.FindPropertyRelative("distribution");
            bool mixedKind = kindProperty.hasMultipleDifferentValues;
            var kind = (ELightKind)kindProperty.enumValueIndex;
            bool mixedEmitter = !mixedKind && kind == ELightKind.Local && emitterProperty.hasMultipleDifferentValues;
            var emitter = (ELocalLightEmitter)emitterProperty.enumValueIndex;
            bool mixedDistribution = !mixedKind && kind == ELightKind.Local && distributionProperty.hasMultipleDifferentValues;
            var distribution = (ELocalLightDistribution)distributionProperty.enumValueIndex;

            bool allLocal = true;
            bool allAngledSpots = true;
            bool allCones = true;
            float commonOuterAngle = 179f;
            bool anyCubeCookie = false;
            bool anyPlanarCookie = false;
            bool allPlanarEmitterCookie = true;
            bool all2DIesSupported = true;
            bool allShadowed = true;
            bool allDistanceLimited = true;
            bool allAdditionalEnabled = true;
            bool allRealtimeMode = true;
            bool anyBakeDisabled = false;
            bool anyLocalLux = false;
            string bakeDisabledReason = null;
            EResolvedLightShape commonResolved = default;
            bool mixedResolved = false;

            m_CommonUnits.Clear();
            IReadOnlyList<LightUnit> firstUnits = LightIntensityUtility.GetSupportedUnits(m_ShapeBuffer[0]);
            for (int u = 0; u < firstUnits.Count; ++u)
                m_CommonUnits.Add(firstUnits[u]);

            for (int i = 0; i < m_Lights.Length; ++i)
            {
                Light light = m_Lights[i];
                LightSourceShape source = m_Before[i].shape;
                LightCapabilities capabilities = LightShapeUtility.GetCapabilities(source);
                allLocal &= source.kind == ELightKind.Local;
                allAngledSpots &= capabilities.hasSpotAngle;
                allCones &= capabilities.hasInnerSpotAngle;
                if (capabilities.hasInnerSpotAngle)
                    commonOuterAngle = Mathf.Min(commonOuterAngle, light.spotAngle);
                anyCubeCookie |= capabilities.supportsCubeCookie;
                anyPlanarCookie |= !capabilities.supportsCubeCookie;
                allPlanarEmitterCookie &= capabilities.isPlanar;
                all2DIesSupported &= capabilities.supports2DIes;
                allShadowed &= light.shadows != LightShadows.None;
                allDistanceLimited &= m_Before[i].maxDrawDistance > 0f;
                allAdditionalEnabled &= m_AdditionalTargets[i] && ((InfinityAdditionalLightData)m_AdditionalTargets[i]).enabled;
                allRealtimeMode &= light.lightmapBakeType == LightmapBakeType.Realtime;
                if (!capabilities.baking)
                {
                    anyBakeDisabled = true;
                    bakeDisabledReason ??= capabilities.bakeDisabledReason;
                }

                bool validCombination = LightShapeUtility.IsValidCombination(source.kind, source.emitter, source.distribution);
                EResolvedLightShape resolved = validCombination ? LightShapeUtility.Resolve(source) : EResolvedLightShape.Point;
                if (i == 0)
                    commonResolved = resolved;
                else if (commonResolved != resolved)
                    mixedResolved = true;

                LightUnit displayUnit = LightIntensityUtility.ResolveDisplayUnit(light, source);
                if (source.kind == ELightKind.Local && resolved != EResolvedLightShape.SpotBox && displayUnit == LightUnit.Lux)
                    anyLocalLux = true;

                for (int u = m_CommonUnits.Count - 1; u >= 0; --u)
                {
                    if (!LightIntensityUtility.IsUnitSupported(source, m_CommonUnits[u]))
                        m_CommonUnits.RemoveAt(u);
                }
            }

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Emitter", "Emitter"))
                DrawEmitterSection(shape, kindProperty, emitterProperty, mixedKind, kind, mixedEmitter, emitter, mixedDistribution, distribution, commonResolved, mixedResolved);

            if (allLocal || mixedKind)
            {
                if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Distribution", "Distribution"))
                    DrawDistributionSection(shape, emitterProperty, distributionProperty, mixedKind, kind, mixedEmitter, mixedDistribution, emitter, distribution, allLocal, allAngledSpots, allCones, commonOuterAngle);
            }

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Emission", "Emission"))
                DrawEmission(additionalOwner, anyLocalLux);

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Baking", "Baking"))
                DrawBaking(anyBakeDisabled, allRealtimeMode, bakeDisabledReason);

            bool mixedCookieDimensions = anyCubeCookie && anyPlanarCookie;
            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Cookie", "Cookie & IES"))
                DrawCookies(additionalOwner, !mixedKind && kind == ELightKind.Directional, allPlanarEmitterCookie, anyCubeCookie, mixedCookieDimensions, allLocal, all2DIesSupported);

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Shadows", "Shadows"))
                DrawShadows(additionalOwner, allLocal, allShadowed, allAdditionalEnabled);

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Volumetric", "Volumetric"))
                DrawVolumetric(additionalOwner);

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Layers", allLocal ? "Layers & Distance" : "Layers"))
            {
                Field(serializedObject, "m_CullingMask", "Culling Mask");
                serializedObject.ApplyModifiedProperties();
                EditField(additionalOwner, "lightLayer", "Light Layers");
                if (allLocal)
                    EditField(additionalOwner, "maxDrawDistance", "Maximum Distance", clampMinimum: 0f);
                if (allLocal && allDistanceLimited)
                    EditField(additionalOwner, "maxDrawDistanceFade", "Distance Fade", clampMinimum: 0f);
            }
        }

        void DrawEmitterSection(
            SerializedProperty shape,
            SerializedProperty kindProperty,
            SerializedProperty emitterProperty,
            bool mixedKind,
            ELightKind kind,
            bool mixedEmitter,
            ELocalLightEmitter emitter,
            bool mixedDistribution,
            ELocalLightDistribution distribution,
            EResolvedLightShape commonResolved,
            bool mixedResolved)
        {
            EditShapeEnum(kindProperty, "Kind");
            if (mixedKind)
            {
                EditorGUILayout.HelpBox("Select lights with the same kind to edit emitter properties.", MessageType.Info);
                return;
            }

            if (kind == ELightKind.Directional)
            {
                LightCapabilities directionalCapabilities = LightShapeUtility.GetCapabilities(m_Before[0].shape);
                if (directionalCapabilities.hasSunAngle)
                    Relative(shape, "sourceAngle", "Sun Diameter", s_SunAngleDragRange, 0f, 90f);
                return;
            }

            DrawEmitterPopup(emitterProperty, mixedEmitter, emitter);
            if (!mixedEmitter && !mixedResolved)
                EditorGUILayout.LabelField("Resolved Shape", FormatResolvedShape(commonResolved), EditorStyles.miniLabel);

            if (mixedEmitter || mixedDistribution)
            {
                EditorGUILayout.HelpBox("Select lights with the same emitter and distribution to edit dimensions.", MessageType.Info);
                return;
            }

            LightCapabilities commonCapabilities = LightShapeUtility.GetCapabilities(m_Before[0].shape);
            if (!commonCapabilities.validCombination)
            {
                EditorGUILayout.HelpBox(commonCapabilities.bakeDisabledReason, MessageType.Warning);
                return;
            }

            if (commonCapabilities.hasSourceRadius)
                Relative(shape, "sourceRadius", "Source Radius", clampMinimum: 0f);
            if (emitter == ELocalLightEmitter.Round)
            {
                if (commonCapabilities.hasSourceLength)
                {
                    Relative(shape, "sourceLength", "Source Length", clampMinimum: 0f);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.FloatField(s_SourceLengthDisabledLabel, 0f);
                }
            }
            if (commonCapabilities.hasRectSize)
            {
                Relative(shape, "sourceWidth", "Source Width", clampMinimum: 0f);
                Relative(shape, "sourceHeight", "Source Height", clampMinimum: 0f);
            }
            if (commonCapabilities.hasBarnDoors)
            {
                Relative(shape, "barnDoorAngle", "Barn Door Angle", s_BarnDoorAngleDragRange, 0f, 90f);
                Relative(shape, "barnDoorLength", "Barn Door Length", clampMinimum: 0f);
            }

            foreach (InfinityLightInspectorSnapshot snapshot in m_Before)
            {
                if (!LightShapeUtility.IsValid(snapshot.shape))
                {
                    EditorGUILayout.HelpBox("The light shape has no valid emitting area.", MessageType.Warning);
                    break;
                }
            }
        }

        void DrawDistributionSection(
            SerializedProperty shape,
            SerializedProperty emitterProperty,
            SerializedProperty distributionProperty,
            bool mixedKind,
            ELightKind kind,
            bool mixedEmitter,
            bool mixedDistribution,
            ELocalLightEmitter emitter,
            ELocalLightDistribution distribution,
            bool allLocal,
            bool allAngledSpots,
            bool allCones,
            float commonOuterAngle)
        {
            if (mixedKind || kind != ELightKind.Local)
            {
                EditorGUILayout.HelpBox("Select Local lights to edit distribution settings.", MessageType.Info);
                return;
            }

            DrawDistributionPopup(mixedEmitter, mixedDistribution, emitter, distribution);
            if (!mixedEmitter && emitter == ELocalLightEmitter.Rectangle)
                EditorGUILayout.HelpBox("Rectangle emitters only support Hemisphere distribution.", MessageType.Info);

            if (allLocal)
            {
                SerializedProperty range = GetProperty(serializedObject, "m_Range");
                using (var change = new EditorGUI.ChangeCheckScope())
                {
                    EditorGUILayout.PropertyField(range, new GUIContent("Range"));
                    if (change.changed)
                    {
                        range.floatValue = Mathf.Max(0.01f, range.floatValue);
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            }

            if (allAngledSpots)
            {
                SerializedProperty spotAngleProperty = GetProperty(serializedObject, "m_SpotAngle");
                InfinityLightInspectorGUI.FloatSliderValue(
                    s_OuterAngleLabel,
                    spotAngleProperty.floatValue,
                    spotAngleProperty.hasMultipleDifferentValues,
                    new Vector2(1f, 179f),
                    new Vector2(1f, 179f),
                    out float nextOuter,
                    out bool outerChanged);
                if (outerChanged)
                {
                    InfinityLightEditorTransactions.ApplySpotAngles(m_Lights, m_Before, nextOuter, null, mergeDrag: true);
                    serializedObject.Update();
                    commonOuterAngle = nextOuter;
                }
            }

            if (allCones)
            {
                SerializedProperty innerAngleProperty = GetProperty(serializedObject, "m_InnerSpotAngle");
                InfinityLightInspectorGUI.FloatSliderValue(
                    s_InnerAngleLabel,
                    innerAngleProperty.floatValue,
                    innerAngleProperty.hasMultipleDifferentValues,
                    new Vector2(0f, commonOuterAngle),
                    new Vector2(0f, commonOuterAngle),
                    out float nextInner,
                    out bool innerChanged);
                if (innerChanged)
                {
                    InfinityLightEditorTransactions.ApplySpotAngles(m_Lights, m_Before, null, nextInner, mergeDrag: true);
                    serializedObject.Update();
                }
            }

            if (!mixedEmitter && !mixedDistribution)
            {
                LightCapabilities commonCapabilities = LightShapeUtility.GetCapabilities(m_Before[0].shape);
                if (commonCapabilities.hasPyramidAspect)
                    Relative(shape, "pyramidAspect", "Aspect Ratio", clampMinimum: 0.01f);
                if (commonCapabilities.hasBoxSize)
                {
                    SerializedProperty boxSize = shape.FindPropertyRelative("boxSize");
                    Relative(boxSize, "x", "Beam Width", clampMinimum: 0.001f);
                    Relative(boxSize, "y", "Beam Height", clampMinimum: 0.001f);
                }
            }
        }

        void DrawEmission(SerializedObject additionalOwner, bool anyLocalLux)
        {
            SerializedProperty useTemperature = GetProperty(serializedObject, "m_UseColorTemperature");
            bool temperatureEnabled = !useTemperature.hasMultipleDifferentValues && useTemperature.boolValue;

            LightUnit firstUnit = LightIntensityUtility.ResolveDisplayUnit(m_Lights[0], m_ShapeBuffer[0]);
            bool mixedUnit = false;
            float firstDisplayIntensity = LightIntensityUtility.ToDisplayIntensity(m_Lights[0], m_ShapeBuffer[0], firstUnit);
            bool mixedIntensity = false;
            for (int i = 1; i < m_Lights.Length; ++i)
            {
                LightUnit candidateUnit = LightIntensityUtility.ResolveDisplayUnit(m_Lights[i], m_ShapeBuffer[i]);
                if (candidateUnit != firstUnit)
                    mixedUnit = true;
                float candidateDisplay = LightIntensityUtility.ToDisplayIntensity(m_Lights[i], m_ShapeBuffer[i], candidateUnit);
                if (Mathf.Abs(candidateDisplay - firstDisplayIntensity) > 1e-4f * Mathf.Max(1f, Mathf.Abs(firstDisplayIntensity)))
                    mixedIntensity = true;
            }

            InfinityLightInspectorGUI.ColorAndIntensity(
                GetProperty(serializedObject, "m_Color"),
                firstDisplayIntensity,
                mixedIntensity,
                firstUnit,
                mixedUnit,
                m_CommonUnits,
                temperatureEnabled,
                out float nextIntensity,
                out bool intensityChanged,
                out LightUnit nextUnit,
                out bool unitChanged);
            serializedObject.ApplyModifiedProperties();

            if (unitChanged)
            {
                InfinityLightEditorTransactions.ApplyDisplayUnit(m_Lights, m_ShapeBuffer, nextUnit);
                serializedObject.Update();
            }
            if (intensityChanged)
            {
                InfinityLightEditorTransactions.ApplyDisplayIntensity(m_Lights, m_ShapeBuffer, nextIntensity, mixedUnit ? null : firstUnit, mergeDrag: true);
                serializedObject.Update();
            }

            if (anyLocalLux)
            {
                SerializedProperty luxDistance = GetProperty(serializedObject, "m_LuxAtDistance");
                using var luxChange = new EditorGUI.ChangeCheckScope();
                EditorGUILayout.PropertyField(luxDistance, s_LuxAtDistanceLabel);
                if (luxChange.changed)
                {
                    luxDistance.floatValue = Mathf.Max(0.01f, luxDistance.floatValue);
                    serializedObject.ApplyModifiedProperties();
                }
            }

            EditField(additionalOwner, "m_IntensityEditMode", "Intensity Edit Mode");

            EditorGUILayout.PropertyField(useTemperature, s_UseTemperatureLabel);
            if (temperatureEnabled || useTemperature.hasMultipleDifferentValues)
            {
                using var disabled = new EditorGUI.DisabledScope(!temperatureEnabled);
                InfinityLightInspectorGUI.Temperature(GetProperty(serializedObject, "m_ColorTemperature"));
            }
            Slider(serializedObject, "m_BounceIntensity", "Indirect Multiplier", new Vector2(0f, 16f));
            serializedObject.ApplyModifiedProperties();

            EditField(additionalOwner, "diffuse", "Diffuse Weight", s_UnitDragRange);
            EditField(additionalOwner, "specular", "Specular Weight", s_UnitDragRange);
        }

        void DrawEmitterPopup(SerializedProperty emitterProperty, bool mixedEmitter, ELocalLightEmitter emitter)
        {
            using var change = new EditorGUI.ChangeCheckScope();
            EditorGUI.showMixedValue = mixedEmitter;
            var next = (ELocalLightEmitter)EditorGUILayout.EnumPopup("Emitter", emitter);
            EditorGUI.showMixedValue = false;
            if (!change.changed)
                return;

            ApplyLocalShapeSelector((LightSourceShape s) =>
            {
                var normalized = LightShapeUtility.NormalizeCombination(next, s.distribution, keepEmitter: true);
                s.kind = ELightKind.Local;
                s.emitter = normalized.emitter;
                s.distribution = normalized.distribution;
                return s;
            });
            GUIUtility.ExitGUI();
        }

        void DrawDistributionPopup(bool mixedEmitter, bool mixedDistribution, ELocalLightEmitter emitter, ELocalLightDistribution distribution)
        {
            Rect row = EditorGUILayout.GetControlRect();
            Rect field = EditorGUI.PrefixLabel(row, new GUIContent("Distribution"));
            string buttonText = mixedDistribution ? "\u2014" : ObjectNames.NicifyVariableName(distribution.ToString());
            if (!EditorGUI.DropdownButton(field, new GUIContent(buttonText), FocusType.Keyboard))
                return;

            Light[] selectedLights = (Light[])m_Lights.Clone();
            var menu = new GenericMenu();
            foreach (ELocalLightDistribution candidate in (ELocalLightDistribution[])System.Enum.GetValues(typeof(ELocalLightDistribution)))
            {
                string label = ObjectNames.NicifyVariableName(candidate.ToString());
                bool allowed = mixedEmitter || LightShapeUtility.IsValidCombination(ELightKind.Local, emitter, candidate);
                if (allowed)
                {
                    bool on = !mixedDistribution && distribution == candidate;
                    menu.AddItem(new GUIContent(label), on, () => ApplyLocalShapeSelectorForLights(selectedLights, (LightSourceShape s) =>
                    {
                        var normalized = LightShapeUtility.NormalizeCombination(s.emitter, candidate, keepEmitter: false);
                        s.kind = ELightKind.Local;
                        s.emitter = normalized.emitter;
                        s.distribution = normalized.distribution;
                        return s;
                    }));
                }
                else
                {
                    string reason = emitter == ELocalLightEmitter.Rectangle
                        ? $"{label} (Rectangle supports Hemisphere only)"
                        : $"{label} (Unsupported for {emitter})";
                    menu.AddDisabledItem(new GUIContent(reason));
                }
            }
            menu.ShowAsContext();
        }

        static string FormatResolvedShape(EResolvedLightShape resolved) => resolved switch
        {
            EResolvedLightShape.SpotCone => "Cone",
            EResolvedLightShape.SpotPyramid => "Pyramid",
            EResolvedLightShape.SpotBox => "Parallel Box",
            EResolvedLightShape.DiskCone => "Disk + Cone (Realtime Only)",
            EResolvedLightShape.Line => "Line (Realtime Only)",
            EResolvedLightShape.Tube => "Tube (Realtime Only)",
            _ => resolved.ToString()
        };

        void EditShapeEnum(SerializedProperty property, string label)
        {
            using var change = new EditorGUI.ChangeCheckScope();
            EditorGUILayout.PropertyField(property, new GUIContent(label));
            if (!change.changed)
                return;
            InfinityLightEditorTransactions.StageProperty(property, m_AfterObjects);
            if (!InfinityLightEditorTransactions.ApplyShape(m_Lights, m_Before, m_After))
            {
                property.serializedObject.Update();
                return;
            }
            GUIUtility.ExitGUI();
        }

        void ApplyLocalShapeSelector(System.Func<LightSourceShape, LightSourceShape> transform)
        {
            ApplyLocalShapeSelectorForLights(m_Lights, transform);
        }

        static void ApplyLocalShapeSelectorForLights(Light[] lights, System.Func<LightSourceShape, LightSourceShape> transform)
        {
            var before = new InfinityLightInspectorSnapshot[lights.Length];
            var after = new InfinityLightInspectorSnapshot[lights.Length];
            try
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    before[i] = CreateInstance<InfinityLightInspectorSnapshot>();
                    after[i] = CreateInstance<InfinityLightInspectorSnapshot>();
                    before[i].hideFlags = HideFlags.DontSave;
                    after[i].hideFlags = HideFlags.DontSave;
                    before[i].Read(lights[i]);
                    after[i].Read(lights[i]);
                    LightSourceShape updated = transform(after[i].shape);
                    using var serialized = new SerializedObject(after[i]);
                    SerializedProperty shapeProperty = serialized.FindProperty("m_Shape");
                    shapeProperty.FindPropertyRelative("kind").enumValueIndex = (int)updated.kind;
                    shapeProperty.FindPropertyRelative("emitter").enumValueIndex = (int)updated.emitter;
                    shapeProperty.FindPropertyRelative("distribution").enumValueIndex = (int)updated.distribution;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                InfinityLightEditorTransactions.ApplyShape(lights, before, after);
            }
            finally
            {
                for (int i = 0; i < lights.Length; ++i)
                {
                    if (before[i]) DestroyImmediate(before[i]);
                    if (after[i]) DestroyImmediate(after[i]);
                }
            }
        }

        void DrawBaking(bool anyBakeDisabled, bool allRealtimeMode, string bakeDisabledReason)
        {
            using (new EditorGUI.DisabledScope(anyBakeDisabled && allRealtimeMode))
            using (var change = new EditorGUI.ChangeCheckScope())
            {
                Field(serializedObject, "m_Lightmapping", "Mode");
                if (change.changed)
                {
                    var mode = (LightmapBakeType)GetProperty(serializedObject, "m_Lightmapping").intValue;
                    serializedObject.Update();
                    if (!anyBakeDisabled || mode == LightmapBakeType.Realtime)
                    {
                        InfinityLightEditorTransactions.ApplyBakeMode(m_Lights, mode);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (anyBakeDisabled)
            {
                EditorGUILayout.HelpBox(bakeDisabledReason, MessageType.Info);
                if (!allRealtimeMode && GUILayout.Button("Switch to Realtime"))
                {
                    InfinityLightEditorTransactions.ApplyBakeMode(m_Lights, LightmapBakeType.Realtime);
                    GUIUtility.ExitGUI();
                }
            }
        }

        void DrawCookies(SerializedObject additionalOwner, bool isDirectional, bool isPlanarEmitter,
            bool anyCubeCookie, bool mixedCookieDimensions, bool allLocal, bool supportsPlanarIES)
        {
            SerializedProperty cookie = GetProperty(serializedObject, "m_Cookie");
            bool directionalCookie = isDirectional
                && !cookie.hasMultipleDifferentValues && cookie.objectReferenceValue is Texture directionalTexture
                && directionalTexture.dimension == TextureDimension.Tex2D;
            using (new EditorGUI.DisabledScope(mixedCookieDimensions))
                EditorGUILayout.PropertyField(cookie);
            if (directionalCookie)
                Field(serializedObject, "m_CookieSize2D", "Cookie Size");
            serializedObject.ApplyModifiedProperties();
            if (mixedCookieDimensions)
                EditorGUILayout.HelpBox("Edit point-light Cubemap cookies separately from 2D cookies.", MessageType.Info);
            else
                DrawCookieWarnings(cookie, anyCubeCookie, isPlanarEmitter);
            if (!allLocal)
                return;

            SerializedProperty ies = GetProperty(additionalOwner, "iesProfile");
            EditProperty(ies, "IES Profile", false);
            if (!ies.hasMultipleDifferentValues && ies.objectReferenceValue is Texture iesTexture
                && iesTexture.dimension == TextureDimension.Tex2D && !supportsPlanarIES)
            {
                EditorGUILayout.HelpBox("A 2D IES profile applies to Cone, Pyramid, and Disk + Cone lights only. Use a Cube IES texture for other local lights.", MessageType.Warning);
            }
        }

        void DrawShadows(SerializedObject additionalOwner, bool allLocal, bool allShadowed, bool allAdditionalEnabled)
        {
            Field(serializedObject, "m_Shadows.m_Type", "Mode");
            using (new EditorGUI.DisabledScope(!allShadowed))
            {
                EditorGUILayout.Slider(GetProperty(serializedObject, "m_Shadows.m_Strength"), 0f, 1f, new GUIContent("Strength"));
                Slider(serializedObject, "m_Shadows.m_NearPlane", "Near Plane", new Vector2(0.01f, 10f));
                Slider(serializedObject, "m_Shadows.m_Bias", "Depth Bias", new Vector2(0f, 2f));
                Slider(serializedObject, "m_Shadows.m_NormalBias", "Normal Bias", new Vector2(0f, 3f));
                DrawCasterLayers();
                serializedObject.ApplyModifiedProperties();
                EditField(additionalOwner, "m_ShadowFilter", "Filter");
                if (allLocal)
                {
                    SerializedProperty updateMode = GetProperty(additionalOwner, "m_ShadowUpdateMode");
                    EditProperty(updateMode, "Update Mode", false);
                    if (!updateMode.hasMultipleDifferentValues && updateMode.enumValueIndex != (int)EShadowUpdateMode.EveryFrame)
                    {
                        if (updateMode.enumValueIndex == (int)EShadowUpdateMode.OnDemand)
                        {
                            using var disabled = new EditorGUI.DisabledScope(!allAdditionalEnabled);
                            if (GUILayout.Button("Request Shadow Map Rendering"))
                            {
                                foreach (Object extra in m_AdditionalTargets)
                                    ((InfinityAdditionalLightData)extra).RequestShadowMapRendering();
                                SceneView.RepaintAll();
                            }
                        }
                        EditorGUILayout.HelpBox("Cached shadows keep the casters from the last render.", MessageType.Info);
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
            EditField(additionalOwner, "enableContactShadow", "Contact Shadows");
        }

        void DrawVolumetric(SerializedObject additionalOwner)
        {
            SerializedProperty enabled = GetProperty(additionalOwner, "enableVolumetric");
            EditProperty(enabled, "Enabled", false);
            using var disabled = new EditorGUI.DisabledScope(enabled.hasMultipleDifferentValues || !enabled.boolValue);
            EditField(additionalOwner, "volumetricIntensity", "Intensity", new Vector2(0f, 32f), clampMinimum: 0f);
            EditField(additionalOwner, "volumetricOcclusion", "Shadow Weight", s_UnitDragRange);
        }

        static void DrawCookieWarnings(SerializedProperty cookie, bool usesCube, bool isEmitterTexture)
        {
            if (cookie.hasMultipleDifferentValues || !(cookie.objectReferenceValue is Texture texture))
                return;
            TextureDimension dimension = usesCube ? TextureDimension.Cube : TextureDimension.Tex2D;
            if (texture.dimension != dimension)
            {
                string message = usesCube
                    ? "Point lights use Cubemap cookies. This texture is ignored."
                    : "This light requires a 2D cookie. The assigned texture is ignored.";
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
            else if (isEmitterTexture)
                EditorGUILayout.HelpBox("The cookie is used as the emitter texture.", MessageType.Info);
        }

        void DrawCasterLayers()
        {
            SerializedProperty layer = GetProperty(serializedObject, "m_RenderingLayerMask");
            foreach (Light light in m_Lights)
            {
                if (((uint)light.renderingLayerMask & ~0xFFu) != 0)
                {
                    EditorGUILayout.HelpBox("Caster layers contain unsupported high bits.", MessageType.Error);
                    return;
                }
            }
            Rect row = EditorGUILayout.GetControlRect();
            using var scope = new EditorGUI.PropertyScope(row, s_CasterLayersLabel, layer);
            using var change = new EditorGUI.ChangeCheckScope();
            var value = (ERenderingLayer)EditorGUI.EnumFlagsField(row, scope.content, (ERenderingLayer)layer.uintValue);
            if (change.changed)
                layer.uintValue = RenderingLayerUtility.Validate((uint)value);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Scene view handles (in the light's local frame; forward = +Z, right = +X)

        void OnSceneGUI()
        {
            InfinityLightEditorTransactions.SyncDragState();
            var light = (Light)target;
            if (light == null)
                return;
            light.TryGetComponent(out InfinityAdditionalLightData data);
            LightSourceShape shape = data ? data.shape : LightShapeUtility.FromUnityLight(light);
            LightSourceShape previous = shape;
            float range = light.range;
            float spotAngle = light.spotAngle;
            Color color = light.color;
            color.a = 1;
            Matrix4x4 frame = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one);
            using (new Handles.DrawingScope(color, frame))
            {
                EditorGUI.BeginChangeCheck();
                EResolvedLightShape resolved = LightShapeUtility.IsValidCombination(shape.kind, shape.emitter, shape.distribution)
                    ? LightShapeUtility.Resolve(shape)
                    : EResolvedLightShape.Point;
                switch (resolved)
                {
                    case EResolvedLightShape.Directional:
                        Handles.ArrowHandleCap(0, Vector3.zero, Quaternion.identity, HandleUtility.GetHandleSize(Vector3.zero), EventType.Repaint);
                        break;
                    case EResolvedLightShape.Point:
                    case EResolvedLightShape.Sphere:
                    case EResolvedLightShape.Line:
                    case EResolvedLightShape.Tube:
                        range = Handles.RadiusHandle(Quaternion.identity, Vector3.zero, range);
                        shape.sourceRadius = SourceRadiusHandle(shape.sourceRadius);
                        if (shape.sourceLength > 0)
                        {
                            Vector3 end = new Vector3(shape.sourceLength * 0.5f, 0, 0);
                            Handles.DrawLine(-end, end);
                            if (shape.sourceRadius > 0)
                            {
                                Handles.DrawWireDisc(-end, Vector3.right, shape.sourceRadius);
                                Handles.DrawWireDisc(end, Vector3.right, shape.sourceRadius);
                                Handles.DrawLine(-end + Vector3.up * shape.sourceRadius, end + Vector3.up * shape.sourceRadius);
                                Handles.DrawLine(-end - Vector3.up * shape.sourceRadius, end - Vector3.up * shape.sourceRadius);
                                Handles.DrawLine(-end + Vector3.forward * shape.sourceRadius, end + Vector3.forward * shape.sourceRadius);
                                Handles.DrawLine(-end - Vector3.forward * shape.sourceRadius, end - Vector3.forward * shape.sourceRadius);
                            }
                            Vector3 moved = Handles.Slider(end, Vector3.right, HandleUtility.GetHandleSize(end) * 0.05f, Handles.DotHandleCap, 0);
                            shape.sourceLength = Mathf.Max(0, moved.x * 2);
                        }
                        break;
                    case EResolvedLightShape.SpotBox:
                        m_BoxBeamHandle.center = Vector3.zero;
                        m_BoxBeamHandle.size = new Vector3(shape.boxSize.x, shape.boxSize.y, 0);
                        m_BoxBeamHandle.SetColor(color);
                        m_BoxBeamHandle.DrawHandle();
                        shape.boxSize = new Vector2(Mathf.Max(0.01f, m_BoxBeamHandle.size.x), Mathf.Max(0.01f, m_BoxBeamHandle.size.y));
                        range = RangeHandle(range);
                        Handles.DrawWireCube(new Vector3(0, 0, range * 0.5f), new Vector3(shape.boxSize.x, shape.boxSize.y, range));
                        break;
                    case EResolvedLightShape.SpotCone:
                        range = RangeHandle(range);
                        spotAngle = ConeHandle(range, spotAngle, 0f);
                        shape.sourceRadius = SourceRadiusHandle(shape.sourceRadius);
                        break;
                    case EResolvedLightShape.SpotPyramid:
                        range = RangeHandle(range);
                        PyramidHandle(range, ref spotAngle, ref shape.pyramidAspect);
                        shape.sourceRadius = SourceRadiusHandle(shape.sourceRadius);
                        break;
                    case EResolvedLightShape.Rect:
                        m_RectHandle.center = Vector3.zero;
                        m_RectHandle.size = new Vector3(shape.sourceWidth, shape.sourceHeight, 0);
                        m_RectHandle.SetColor(color);
                        m_RectHandle.DrawHandle();
                        shape.sourceWidth = Mathf.Max(0, m_RectHandle.size.x);
                        shape.sourceHeight = Mathf.Max(0, m_RectHandle.size.y);
                        if (shape.barnDoorLength > 0)
                            DrawBarnDoors(shape.sourceWidth * 0.5f, shape.sourceHeight * 0.5f, shape.barnDoorAngle, shape.barnDoorLength);
                        Handles.DrawLine(Vector3.zero, Vector3.forward * Mathf.Min(range, HandleUtility.GetHandleSize(Vector3.zero)));
                        range = RangeHandle(range);
                        break;
                    case EResolvedLightShape.Disk:
                    case EResolvedLightShape.DiskCone:
                        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, shape.sourceRadius);
                        Vector3 radiusPoint = new Vector3(shape.sourceRadius, 0, 0);
                        Vector3 movedRadius = Handles.Slider(radiusPoint, Vector3.right,
                            HandleUtility.GetHandleSize(radiusPoint) * 0.05f, Handles.DotHandleCap, 0);
                        shape.sourceRadius = Mathf.Max(0, movedRadius.x);
                        if (resolved == EResolvedLightShape.DiskCone)
                            spotAngle = ConeHandle(range, spotAngle, shape.sourceRadius);
                        range = RangeHandle(range);
                        break;
                }
                if (EditorGUI.EndChangeCheck())
                    InfinityLightEditorTransactions.ApplySceneEdit(light, previous, shape, range, spotAngle, mergeDrag: true);
            }
        }

        static float SourceRadiusHandle(float radius)
        {
            if (radius <= 0)
            {
                return radius;
            }
            return Handles.RadiusHandle(Quaternion.identity, Vector3.zero, radius, true);
        }

        static float RangeHandle(float range)
        {
            Vector3 tip = Vector3.forward * range;
            Vector3 moved = Handles.Slider(tip, Vector3.forward, HandleUtility.GetHandleSize(tip) * 0.05f, Handles.DotHandleCap, 0);
            return Mathf.Max(0.01f, moved.z);
        }

        // Cone outline at range, optionally originating from a disk rim of diskRadius.
        static float ConeHandle(float range, float spotAngle, float diskRadius)
        {
            float tan = Mathf.Tan(0.5f * spotAngle * Mathf.Deg2Rad);
            Vector3 center = Vector3.forward * range;
            float rim = diskRadius + range * tan;
            Handles.DrawWireDisc(center, Vector3.forward, rim);
            foreach (Vector3 direction in s_ConeHandleAxes)
                Handles.DrawLine(direction * diskRadius, center + direction * rim);
            Vector3 handle = center + Vector3.up * rim;
            Vector3 moved = Handles.Slider(handle, Vector3.up, HandleUtility.GetHandleSize(handle) * 0.05f, Handles.DotHandleCap, 0);
            return 2 * Mathf.Atan2(Mathf.Max(0, moved.y - diskRadius), range) * Mathf.Rad2Deg;
        }

        static void PyramidHandle(float range, ref float spotAngle, ref float aspect)
        {
            Vector2 tangents = LightShapeUtility.PyramidTangents(spotAngle, aspect);
            Vector3 a = new Vector3(-tangents.x, -tangents.y, 1) * range;
            Vector3 b = new Vector3(tangents.x, -tangents.y, 1) * range;
            Vector3 c = new Vector3(tangents.x, tangents.y, 1) * range;
            Vector3 d = new Vector3(-tangents.x, tangents.y, 1) * range;
            Handles.DrawPolyLine(a, b, c, d, a);
            Handles.DrawLine(Vector3.zero, a);
            Handles.DrawLine(Vector3.zero, b);
            Handles.DrawLine(Vector3.zero, c);
            Handles.DrawLine(Vector3.zero, d);

            Vector3 center = Vector3.forward * range;
            Vector3 upHandle = center + Vector3.up * (range * tangents.y);
            Vector3 movedUp = Handles.Slider(upHandle, Vector3.up, HandleUtility.GetHandleSize(upHandle) * 0.05f, Handles.DotHandleCap, 0);
            Vector3 rightHandle = center + Vector3.right * (range * tangents.x);
            Vector3 movedRight = Handles.Slider(rightHandle, Vector3.right, HandleUtility.GetHandleSize(rightHandle) * 0.05f, Handles.DotHandleCap, 0);

            if ((movedUp - upHandle).sqrMagnitude <= 1e-10f && (movedRight - rightHandle).sqrMagnitude <= 1e-10f)
                return;

            float nextTanY = Mathf.Max(0.005f, movedUp.y / range);
            float nextTanX = Mathf.Max(0.005f, movedRight.x / range);
            spotAngle = Mathf.Clamp(2f * Mathf.Atan(Mathf.Max(nextTanX, nextTanY)) * Mathf.Rad2Deg, 1f, 179f);
            aspect = Mathf.Clamp(nextTanX / nextTanY, 0.01f, 100f);
        }

        static void DrawBarnDoors(float halfWidth, float halfHeight, float angleDegrees, float length)
        {
            float radians = Mathf.Clamp(angleDegrees, 0f, 90f) * Mathf.Deg2Rad;
            float forward = Mathf.Cos(radians) * length;
            float flare = Mathf.Sin(radians) * length;

            Vector3 c00 = new Vector3(-halfWidth, -halfHeight, 0f);
            Vector3 c10 = new Vector3(halfWidth, -halfHeight, 0f);
            Vector3 c11 = new Vector3(halfWidth, halfHeight, 0f);
            Vector3 c01 = new Vector3(-halfWidth, halfHeight, 0f);

            Handles.DrawPolyLine(c00, c00 + new Vector3(-flare, 0f, forward), c01 + new Vector3(-flare, 0f, forward), c01);
            Handles.DrawPolyLine(c10, c10 + new Vector3(flare, 0f, forward), c11 + new Vector3(flare, 0f, forward), c11);
            Handles.DrawPolyLine(c00, c00 + new Vector3(0f, -flare, forward), c10 + new Vector3(0f, -flare, forward), c10);
            Handles.DrawPolyLine(c01, c01 + new Vector3(0f, flare, forward), c11 + new Vector3(0f, flare, forward), c11);
        }

        void Relative(SerializedProperty parent, string name, string label, Vector2? dragRange = null, float? clampMinimum = null, float? clampMaximum = null, string tooltip = null)
        {
            EditProperty(parent.FindPropertyRelative(name), label, true, dragRange, clampMinimum, clampMaximum, tooltip);
        }

        void EditField(SerializedObject owner, string name, string label, Vector2? dragRange = null, float? clampMinimum = null, float? clampMaximum = null)
        {
            EditProperty(GetProperty(owner, name), label, false, dragRange, clampMinimum, clampMaximum);
        }

        void EditProperty(SerializedProperty property, string label, bool shape, Vector2? dragRange = null, float? clampMinimum = null, float? clampMaximum = null, string tooltip = null)
        {
            if (property == null)
                throw new System.InvalidOperationException($"Light property '{label}' is unavailable.");
            using var change = new EditorGUI.ChangeCheckScope();
            var content = new GUIContent(label, tooltip ?? property.tooltip);
            if (dragRange is Vector2 range)
                InfinityLightInspectorGUI.FloatSlider(property, content, range);
            else
                EditorGUILayout.PropertyField(property, content);
            if (!change.changed)
                return;

            if (property.propertyType == SerializedPropertyType.Float)
            {
                float value = property.floatValue;
                if (clampMinimum.HasValue) value = Mathf.Max(clampMinimum.Value, value);
                if (clampMaximum.HasValue) value = Mathf.Min(clampMaximum.Value, value);
                property.floatValue = value;
            }

            string path = property.propertyPath;
            InfinityLightEditorTransactions.StageProperty(property, m_AfterObjects);
            bool mergeDrag = dragRange.HasValue;
            if (shape)
            {
                if (!InfinityLightEditorTransactions.ApplyShape(m_Lights, m_Before, m_After, mergeDrag: mergeDrag))
                {
                    property.serializedObject.Update();
                    return;
                }
            }
            else
                InfinityLightEditorTransactions.ApplyAdditional(m_Lights, m_BeforeObjects, m_AfterObjects, path, mergeDrag);
            GUIUtility.ExitGUI();
        }

        static void Field(SerializedObject owner, string name, string label)
        {
            EditorGUILayout.PropertyField(GetProperty(owner, name), new GUIContent(label));
        }

        static void Slider(SerializedObject owner, string name, string label, Vector2 dragRange)
        {
            InfinityLightInspectorGUI.FloatSlider(GetProperty(owner, name), new GUIContent(label), dragRange);
        }

        static SerializedProperty GetProperty(SerializedObject owner, string name)
        {
            return owner.FindProperty(name)
                ?? throw new System.InvalidOperationException($"Light property '{name}' is unavailable.");
        }
    }
}
