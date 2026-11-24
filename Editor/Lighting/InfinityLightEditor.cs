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
    // Light inspector of InfinityRP: the shape in Unreal form on InfinityAdditionalLightData (synced to the native Light
    // on edit), the native fields Unity owns, and the Infinity-only fields. Scene view handles edit range, angles and
    // the source shape with Undo.
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Light.Foldout.";
        static readonly GUIContent s_UseTemperatureLabel = new GUIContent("Use Temperature", "Tint the light by its color temperature.");
        static readonly GUIContent s_CasterLayersLabel = new GUIContent("Caster Layers");
        static readonly Vector2 s_UnitDragRange = new Vector2(0f, 1f);

        Light[] m_Lights;
        Object[] m_AdditionalTargets;
        InfinityLightInspectorSnapshot[] m_Before;
        InfinityLightInspectorSnapshot[] m_After;
        SerializedObject[] m_BeforeObjects;
        SerializedObject[] m_AfterObjects;
        SerializedObject m_Snapshots;
        SerializedObject m_Additional;
        bool m_AllShapesAuthored;
        readonly BoxBoundsHandle m_RectHandle = new BoxBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };

        void OnEnable()
        {
            int count = targets.Length;
            m_Lights = new Light[count];
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
            SerializedProperty typeProperty = shape.FindPropertyRelative("type");
            SerializedProperty spotShape = shape.FindPropertyRelative("spotShape");
            bool mixedType = typeProperty.hasMultipleDifferentValues;
            var type = (EInfinityLightType)typeProperty.enumValueIndex;

            bool allLocal = true;
            bool allAngledSpots = true;
            bool allCones = true;
            float commonOuterAngle = 179f;
            bool anyCubeCookie = false;
            bool anyPlanarCookie = false;
            bool allShadowed = true;
            bool allDistanceLimited = true;
            bool allAdditionalEnabled = true;
            bool unbakeableShape = false;
            for (int i = 0; i < m_Lights.Length; ++i)
            {
                Light light = m_Lights[i];
                LightSourceShape source = m_Before[i].shape;
                allLocal &= source.type != EInfinityLightType.Directional;
                allAngledSpots &= source.type == EInfinityLightType.Spot && source.spotShape != ESpotShape.Box;
                bool cone = source.type == EInfinityLightType.Spot && source.spotShape == ESpotShape.Cone;
                allCones &= cone;
                if (cone)
                    commonOuterAngle = Mathf.Min(commonOuterAngle, light.spotAngle);
                anyCubeCookie |= source.type == EInfinityLightType.Point;
                anyPlanarCookie |= source.type != EInfinityLightType.Point;
                allShadowed &= light.shadows != LightShadows.None;
                allDistanceLimited &= m_Before[i].maxDrawDistance > 0f;
                allAdditionalEnabled &= m_AdditionalTargets[i] && ((InfinityAdditionalLightData)m_AdditionalTargets[i]).enabled;
                unbakeableShape |= source.type == EInfinityLightType.Point && source.sourceLength > 0f
                    && light.lightmapBakeType != LightmapBakeType.Realtime;
            }

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "General", "General"))
            {
                EditProperty(typeProperty, "Type", true);
                if (!mixedType && type == EInfinityLightType.Spot)
                    EditProperty(spotShape, "Spot Shape", true);
            }

            bool mixedSpot = !mixedType && type == EInfinityLightType.Spot && spotShape.hasMultipleDifferentValues;
            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Shape", "Shape"))
            {
                if (mixedType || mixedSpot)
                    EditorGUILayout.HelpBox("Select lights of the same type and shape to edit dimensions.", MessageType.Info);
                else
                {
                    DrawShape(shape, type, (ESpotShape)spotShape.enumValueIndex);
                    foreach (InfinityLightInspectorSnapshot snapshot in m_Before)
                    {
                        if (!LightShapeUtility.IsValid(snapshot.shape))
                        {
                            EditorGUILayout.HelpBox("The light shape has no valid emitting area.", MessageType.Warning);
                            break;
                        }
                    }
                }
            }

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Emission", "Emission"))
                DrawEmission(additionalOwner, allLocal, allAngledSpots, allCones, commonOuterAngle);

            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Baking", "Baking"))
                DrawBaking(unbakeableShape);

            bool mixedCookieDimensions = anyCubeCookie && anyPlanarCookie;
            if (InfinityLightInspectorGUI.Section(FoldoutPrefix + "Cookie", "Cookie & IES"))
                DrawCookies(additionalOwner, mixedType, type, anyCubeCookie, mixedCookieDimensions, allLocal, allAngledSpots);

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
                    EditField(additionalOwner, "maxDrawDistance", "Maximum Distance");
                if (allLocal && allDistanceLimited)
                    EditField(additionalOwner, "maxDrawDistanceFade", "Distance Fade");
            }
        }

        void DrawEmission(SerializedObject additionalOwner, bool allLocal, bool allAngledSpots, bool allCones, float commonOuterAngle)
        {
            SerializedProperty useTemperature = GetProperty(serializedObject, "m_UseColorTemperature");
            bool temperatureEnabled = !useTemperature.hasMultipleDifferentValues && useTemperature.boolValue;
            InfinityLightInspectorGUI.ColorAndIntensity(
                GetProperty(serializedObject, "m_Color"), GetProperty(serializedObject, "m_Intensity"), temperatureEnabled);
            EditorGUILayout.PropertyField(useTemperature, s_UseTemperatureLabel);
            if (temperatureEnabled || useTemperature.hasMultipleDifferentValues)
            {
                using var disabled = new EditorGUI.DisabledScope(!temperatureEnabled);
                InfinityLightInspectorGUI.Temperature(GetProperty(serializedObject, "m_ColorTemperature"));
            }
            if (allLocal)
                Field(serializedObject, "m_Range", "Range");
            if (allAngledSpots)
                EditorGUILayout.Slider(GetProperty(serializedObject, "m_SpotAngle"), 1f, 179f, new GUIContent("Outer Angle"));
            if (allCones)
                EditorGUILayout.Slider(GetProperty(serializedObject, "m_InnerSpotAngle"), 0f, commonOuterAngle, new GUIContent("Inner Angle"));
            Slider(serializedObject, "m_BounceIntensity", "Indirect Multiplier", new Vector2(0f, 16f));
            serializedObject.ApplyModifiedProperties();

            EditField(additionalOwner, "diffuse", "Diffuse Weight", s_UnitDragRange);
            EditField(additionalOwner, "specular", "Specular Weight", s_UnitDragRange);
        }

        void DrawBaking(bool unbakeableShape)
        {
            using (var change = new EditorGUI.ChangeCheckScope())
            {
                Field(serializedObject, "m_Lightmapping", "Mode");
                if (change.changed)
                {
                    var mode = (LightmapBakeType)GetProperty(serializedObject, "m_Lightmapping").intValue;
                    serializedObject.Update();
                    InfinityLightEditorTransactions.ApplyBakeMode(m_Lights, mode);
                    GUIUtility.ExitGUI();
                }
            }
            if (unbakeableShape)
                EditorGUILayout.HelpBox("Tube and line lights are realtime only. The lightmapper skips them.", MessageType.Warning);
        }

        void DrawCookies(SerializedObject additionalOwner, bool mixedType, EInfinityLightType type,
            bool anyCubeCookie, bool mixedCookieDimensions, bool allLocal, bool supportsPlanarIES)
        {
            SerializedProperty cookie = GetProperty(serializedObject, "m_Cookie");
            bool directionalCookie = !mixedType && type == EInfinityLightType.Directional
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
                DrawCookieWarnings(cookie, anyCubeCookie, !mixedType && (type == EInfinityLightType.Rect || type == EInfinityLightType.Disk));
            if (!allLocal)
                return;

            SerializedProperty ies = GetProperty(additionalOwner, "iesProfile");
            EditProperty(ies, "IES Profile", false);
            if (!ies.hasMultipleDifferentValues && ies.objectReferenceValue is Texture iesTexture
                && iesTexture.dimension == TextureDimension.Tex2D && !supportsPlanarIES)
            {
                EditorGUILayout.HelpBox("A 2D IES profile applies to cone and pyramid spots only. Use the Cube texture for other local lights.", MessageType.Warning);
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
            EditField(additionalOwner, "volumetricIntensity", "Intensity", new Vector2(0f, 32f));
            EditField(additionalOwner, "volumetricOcclusion", "Shadow Weight", s_UnitDragRange);
        }

        void DrawShape(SerializedProperty shape, EInfinityLightType type, ESpotShape spot)
        {
            switch (type)
            {
                case EInfinityLightType.Directional:
                    Relative(shape, "sourceAngle", "Sun Diameter");
                    Relative(shape, "softSourceAngle", "Extra Spread", "Additional angular spread in degrees for realtime specular response and PCSS.");
                    break;
                case EInfinityLightType.Point:
                    Relative(shape, "sourceRadius", "Source Radius");
                    Relative(shape, "sourceLength", "Source Length");
                    Relative(shape, "softSourceRadius", "Extra Radius", "Additional radius in meters for realtime specular response and PCSS.");
                    break;
                case EInfinityLightType.Spot:
                    if (spot == ESpotShape.Box)
                    {
                        SerializedProperty boxSize = shape.FindPropertyRelative("boxSize");
                        Relative(boxSize, "x", "Width");
                        Relative(boxSize, "y", "Height");
                        break;
                    }
                    if (spot == ESpotShape.Pyramid)
                        Relative(shape, "pyramidAspect", "Aspect Ratio");
                    Relative(shape, "sourceRadius", "Source Radius");
                    Relative(shape, "softSourceRadius", "Extra Radius", "Additional radius in meters for realtime specular response and PCSS.");
                    break;
                case EInfinityLightType.Rect:
                    Relative(shape, "sourceWidth", "Source Width");
                    Relative(shape, "sourceHeight", "Source Height");
                    Relative(shape, "barnDoorAngle", "Barn Door Angle");
                    Relative(shape, "barnDoorLength", "Barn Door Length");
                    break;
                case EInfinityLightType.Disk:
                    Relative(shape, "sourceRadius", "Source Radius");
                    break;
            }
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
                switch (shape.type)
                {
                    case EInfinityLightType.Directional:
                        Handles.ArrowHandleCap(0, Vector3.zero, Quaternion.identity, HandleUtility.GetHandleSize(Vector3.zero), EventType.Repaint);
                        break;
                    case EInfinityLightType.Point:
                        range = Handles.RadiusHandle(Quaternion.identity, Vector3.zero, range);
                        shape.sourceRadius = SourceRadiusHandle(shape.sourceRadius);
                        if (shape.sourceLength > 0)
                        {
                            Vector3 end = new Vector3(shape.sourceLength * 0.5f, 0, 0);
                            Handles.DrawLine(-end, end);
                            Vector3 moved = Handles.Slider(end, Vector3.right, HandleUtility.GetHandleSize(end) * 0.05f, Handles.DotHandleCap, 0);
                            shape.sourceLength = Mathf.Max(0, moved.x * 2);
                        }
                        break;
                    case EInfinityLightType.Spot:
                        range = RangeHandle(range);
                        if (shape.spotShape == ESpotShape.Box)
                        {
                            Vector3 size = new Vector3(shape.boxSize.x, shape.boxSize.y, range);
                            Handles.DrawWireCube(new Vector3(0, 0, range * 0.5f), size);
                        }
                        else
                        {
                            spotAngle = ConeHandle(range, spotAngle, shape.spotShape == ESpotShape.Pyramid ? shape.pyramidAspect : 0);
                            shape.sourceRadius = SourceRadiusHandle(shape.sourceRadius);
                        }
                        break;
                    case EInfinityLightType.Rect:
                        m_RectHandle.center = Vector3.zero;
                        m_RectHandle.size = new Vector3(shape.sourceWidth, shape.sourceHeight, 0);
                        m_RectHandle.SetColor(color);
                        m_RectHandle.DrawHandle();
                        shape.sourceWidth = Mathf.Max(0, m_RectHandle.size.x);
                        shape.sourceHeight = Mathf.Max(0, m_RectHandle.size.y);
                        Handles.DrawLine(Vector3.zero, Vector3.forward * Mathf.Min(range, HandleUtility.GetHandleSize(Vector3.zero)));
                        range = RangeHandle(range);
                        break;
                    case EInfinityLightType.Disk:
                        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, shape.sourceRadius);
                        Vector3 radiusPoint = new Vector3(shape.sourceRadius, 0, 0);
                        // An absolute radial slider can grow a zero-radius disk; multiplicative scaling cannot.
                        Vector3 movedRadius = Handles.Slider(radiusPoint, Vector3.right,
                            HandleUtility.GetHandleSize(radiusPoint) * 0.05f, Handles.DotHandleCap, 0);
                        shape.sourceRadius = Mathf.Max(0, movedRadius.x);
                        range = RangeHandle(range);
                        break;
                }
                if (EditorGUI.EndChangeCheck())
                    InfinityLightEditorTransactions.ApplySceneEdit(light, previous, shape, range, spotAngle);
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

        // Cone (aspect 0) or pyramid outline at the range, with a handle on the rim for the outer angle.
        static float ConeHandle(float range, float spotAngle, float aspect)
        {
            float tan = Mathf.Tan(0.5f * spotAngle * Mathf.Deg2Rad);
            Vector3 center = Vector3.forward * range;
            if (aspect <= 0)
            {
                float rim = range * tan;
                Handles.DrawWireDisc(center, Vector3.forward, rim);
                foreach (Vector3 dir in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down })
                    Handles.DrawLine(Vector3.zero, center + dir * rim);
            }
            else
            {
                Vector2 tangents = LightShapeUtility.PyramidTangents(spotAngle, aspect);
                Vector3 a = new Vector3(-tangents.x, -tangents.y, 1) * range, b = new Vector3(tangents.x, -tangents.y, 1) * range;
                Vector3 c = new Vector3(tangents.x, tangents.y, 1) * range, d = new Vector3(-tangents.x, tangents.y, 1) * range;
                Handles.DrawPolyLine(a, b, c, d, a);
                Handles.DrawLine(Vector3.zero, a);
                Handles.DrawLine(Vector3.zero, b);
                Handles.DrawLine(Vector3.zero, c);
                Handles.DrawLine(Vector3.zero, d);
            }
            Vector3 handle = center + Vector3.up * range * tan;
            Vector3 moved = Handles.Slider(handle, Vector3.up, HandleUtility.GetHandleSize(handle) * 0.05f, Handles.DotHandleCap, 0);
            return 2 * Mathf.Atan2(Mathf.Max(0, moved.y), range) * Mathf.Rad2Deg;
        }

        void Relative(SerializedProperty parent, string name, string label, string tooltip = null)
        {
            EditProperty(parent.FindPropertyRelative(name), label, true, tooltip: tooltip);
        }

        void EditField(SerializedObject owner, string name, string label, Vector2? dragRange = null)
        {
            EditProperty(GetProperty(owner, name), label, false, dragRange);
        }

        void EditProperty(SerializedProperty property, string label, bool shape, Vector2? dragRange = null, string tooltip = null)
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

            string path = property.propertyPath;
            InfinityLightEditorTransactions.StageProperty(property, m_AfterObjects);
            if (shape)
                InfinityLightEditorTransactions.ApplyShape(m_Lights, m_Before, m_After);
            else
                InfinityLightEditorTransactions.ApplyAdditional(m_Lights, m_BeforeObjects, m_AfterObjects, path);
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
