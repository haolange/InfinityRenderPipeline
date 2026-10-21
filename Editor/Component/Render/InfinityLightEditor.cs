using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Editor;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component.Editor
{
    // Light inspector of InfinityRP: the shape in Unreal form on InfinityAdditionalLightData (synced to the native Light
    // on edit), the native fields Unity owns, and the Infinity-only fields, in foldout sections. Additional Properties
    // shows the rarely changed fields. Scene view handles edit range, angles and the source shape with Undo.
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Light.Foldout.";
        const string AdditionalKey = "InfinityRP.Light.AdditionalProperties";

        SerializedObject m_Additional;
        readonly BoxBoundsHandle m_RectHandle = new BoxBoundsHandle { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y };

        static bool showAdditional
        {
            get => EditorPrefs.GetBool(AdditionalKey, false);
            set => EditorPrefs.SetBool(AdditionalKey, value);
        }

        void OnEnable() => EnsureAdditional();

        void EnsureAdditional()
        {
            var extras = new Object[targets.Length];
            for (int i = 0; i < targets.Length; ++i)
            {
                extras[i] = InfinityAdditionalLightData.GetOrCreate((Light)targets[i], true);
            }
            m_Additional = new SerializedObject(extras);
            // Components saved before 0.5.0 have no authored shape and render the native one. The inspector edits the
            // Infinity shape, so it adopts the native shape first; rendering does not change.
            foreach (Object extra in extras)
            {
                var data = (InfinityAdditionalLightData)extra;
                if (!data.shapeAuthored)
                {
                    Undo.RecordObject(data, "Adopt Native Light Shape");
                    data.AdoptNativeShape();
                }
            }
            m_Additional.Update();
        }

        public override void OnInspectorGUI()
        {
            if (m_Additional == null)
                EnsureAdditional();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                showAdditional = GUILayout.Toggle(showAdditional, "Additional Properties", EditorStyles.miniButton);
            }

            m_Additional.Update();
            SerializedProperty shape = m_Additional.FindProperty("m_Shape");
            SerializedProperty typeProperty = shape.FindPropertyRelative("type");
            SerializedProperty spotShape = shape.FindPropertyRelative("spotShape");
            bool mixedType = typeProperty.hasMultipleDifferentValues;
            var type = (EInfinityLightType)typeProperty.enumValueIndex;

            // General + Shape (Infinity authority; native fields follow).
            EditorGUI.BeginChangeCheck();
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "General", "General"))
            {
                EditorGUILayout.PropertyField(typeProperty, new GUIContent("Type"));
                mixedType = typeProperty.hasMultipleDifferentValues;
                type = (EInfinityLightType)typeProperty.enumValueIndex;
                if (!mixedType && type == EInfinityLightType.Spot)
                    EditorGUILayout.PropertyField(spotShape, new GUIContent("Spot Shape"));
            }
            InfinityInspectorGUI.EndFoldout();

            if (!mixedType)
            {
                if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Shape", "Shape"))
                {
                    DrawShape(shape, type, spotShape);
                }
                InfinityInspectorGUI.EndFoldout();
            }
            if (EditorGUI.EndChangeCheck())
            {
                m_Additional.FindProperty("m_ShapeAuthored").boolValue = true;
                m_Additional.ApplyModifiedProperties();
                SyncNativeShape();
            }

            // Native Light fields.
            serializedObject.Update();
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Emission", "Emission"))
            {
                Field(serializedObject, "m_Color", "Color");
                Field(serializedObject, "m_UseColorTemperature", "Use Color Temperature");
                Field(serializedObject, "m_ColorTemperature", "Color Temperature");
                Field(serializedObject, "m_Intensity", "Intensity");
                if (mixedType || type != EInfinityLightType.Directional) Field(serializedObject, "m_Range", "Range");
                if (!mixedType && type == EInfinityLightType.Spot && !spotShape.hasMultipleDifferentValues && (ESpotShape)spotShape.enumValueIndex != ESpotShape.Box)
                {
                    Field(serializedObject, "m_SpotAngle", "Outer Angle");
                    if ((ESpotShape)spotShape.enumValueIndex == ESpotShape.Cone)
                        Field(serializedObject, "m_InnerSpotAngle", "Inner Angle");
                }
                if (showAdditional) Field(serializedObject, "m_BounceIntensity", "Indirect Multiplier");
            }
            InfinityInspectorGUI.EndFoldout();

            // Commit other native edits before the mode transaction. A legacy native Disc is converted only
            // by this explicit edit, never while adopting it on inspector open.
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Baking", "Baking"))
            {
                EditorGUI.BeginChangeCheck();
                Field(serializedObject, "m_Lightmapping", "Mode");
                if (EditorGUI.EndChangeCheck())
                {
                    var mode = (LightmapBakeType)serializedObject.FindProperty("m_Lightmapping").intValue;
                    serializedObject.Update();
                    foreach (Object extra in m_Additional.targetObjects)
                    {
                        var data = (InfinityAdditionalLightData)extra;
                        ApplyBakeMode(data.attachedLight, data, mode);
                    }
                    serializedObject.Update();
                }
                if (!mixedType && type == EInfinityLightType.Point && shape.FindPropertyRelative("sourceLength").floatValue > 0 && ((Light)target).lightmapBakeType != LightmapBakeType.Realtime)
                    EditorGUILayout.HelpBox("Tube and line lights are not baked; the lightmapper skips them.", MessageType.Warning);
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Cookie", "Cookie & IES"))
            {
                Field(serializedObject, "m_Cookie", "Cookie");
                if (!mixedType && type == EInfinityLightType.Directional) Field(serializedObject, "m_CookieSize2D", "Cookie Size");
                DrawCookieWarnings(mixedType, type);
                m_Additional.Update();
                if (mixedType || type != EInfinityLightType.Directional)
                {
                    Field(m_Additional, "iesProfile", "IES Profile");
                    SerializedProperty iesProperty = m_Additional.FindProperty("iesProfile");
                    if (!mixedType && iesProperty != null && !iesProperty.hasMultipleDifferentValues && iesProperty.objectReferenceValue is Texture iesTexture
                        && iesTexture.dimension == TextureDimension.Tex2D && type != EInfinityLightType.Spot)
                        EditorGUILayout.HelpBox("A 2D IES profile applies to cone and pyramid spots only; use the profile's Cube texture.", MessageType.Warning);
                }
                m_Additional.ApplyModifiedProperties();
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Shadows", "Shadows"))
            {
                Field(serializedObject, "m_Shadows.m_Type", "Mode");
                Field(serializedObject, "m_Shadows.m_Strength", "Strength");
                if (showAdditional)
                {
                    Field(serializedObject, "m_Shadows.m_NearPlane", "Near Plane");
                    Field(serializedObject, "m_Shadows.m_Bias", "Depth Bias");
                    Field(serializedObject, "m_Shadows.m_NormalBias", "Normal Bias");
                }
                DrawCasterLayers();
                serializedObject.ApplyModifiedProperties();

                m_Additional.Update();
                Field(m_Additional, "m_ShadowFilter", "Filter");
                Field(m_Additional, "m_ShadowUpdateMode", "Update Mode");
                SerializedProperty updateMode = m_Additional.FindProperty("m_ShadowUpdateMode");
                if (updateMode != null && !updateMode.hasMultipleDifferentValues && updateMode.enumValueIndex == (int)EShadowUpdateMode.OnDemand
                    && GUILayout.Button("Request Shadow Map Rendering"))
                {
                    foreach (Object extra in m_Additional.targetObjects)
                        ((InfinityAdditionalLightData)extra).RequestShadowMapRendering();
                    SceneView.RepaintAll();
                }
                if (updateMode != null && updateMode.enumValueIndex != (int)EShadowUpdateMode.EveryFrame)
                    EditorGUILayout.HelpBox("Cached shadows keep the casters of their last render; moving objects do not update them. Directional lights always render every frame.", MessageType.None);
                Field(m_Additional, "enableContactShadow", "Contact Shadows");
                m_Additional.ApplyModifiedProperties();
            }
            InfinityInspectorGUI.EndFoldout();
            serializedObject.ApplyModifiedProperties();

            m_Additional.Update();
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Volumetric", "Volumetric"))
            {
                Field(m_Additional, "enableVolumetric", "Volumetric");
                Field(m_Additional, "volumetricIntensity", "Intensity");
                if (showAdditional) Field(m_Additional, "volumetricOcclusion", "Shadow Weight");
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Layers", "Layers & Distance"))
            {
                Field(m_Additional, "lightLayer", "Light Layers");
                Field(m_Additional, "maxDrawDistance", "Maximum Distance");
                if (showAdditional)
                {
                    Field(m_Additional, "maxDrawDistanceFade", "Distance Fade");
                    Field(m_Additional, "diffuse", "Diffuse Weight");
                    Field(m_Additional, "specular", "Specular Weight");
                }
            }
            InfinityInspectorGUI.EndFoldout();
            m_Additional.ApplyModifiedProperties();
        }

        void DrawShape(SerializedProperty shape, EInfinityLightType type, SerializedProperty spotShape)
        {
            switch (type)
            {
                case EInfinityLightType.Directional:
                    Relative(shape, "sourceAngle", "Source Angle");
                    if (showAdditional) Relative(shape, "softSourceAngle", "Soft Source Angle");
                    break;
                case EInfinityLightType.Point:
                    Relative(shape, "sourceRadius", "Source Radius");
                    Relative(shape, "sourceLength", "Source Length");
                    if (showAdditional) Relative(shape, "softSourceRadius", "Soft Source Radius");
                    if (shape.FindPropertyRelative("sourceLength").floatValue > 0)
                        EditorGUILayout.HelpBox("A length makes a tube (a line when the radius is 0). Tubes and lines are realtime only.", MessageType.Info);
                    break;
                case EInfinityLightType.Spot:
                    var spot = (ESpotShape)spotShape.enumValueIndex;
                    if (spot == ESpotShape.Box)
                    {
                        Relative(shape, "boxSize", "Size");
                        break;
                    }
                    if (spot == ESpotShape.Pyramid) Relative(shape, "pyramidAspect", "Aspect Ratio");
                    Relative(shape, "sourceRadius", "Source Radius");
                    if (showAdditional) Relative(shape, "softSourceRadius", "Soft Source Radius");
                    break;
                case EInfinityLightType.Rect:
                    Relative(shape, "sourceWidth", "Source Width");
                    Relative(shape, "sourceHeight", "Source Height");
                    if (showAdditional)
                    {
                        Relative(shape, "barnDoorAngle", "Barn Door Angle");
                        Relative(shape, "barnDoorLength", "Barn Door Length");
                    }
                    break;
                case EInfinityLightType.Disk:
                    Relative(shape, "sourceRadius", "Source Radius");
                    break;
            }
        }

        void DrawCookieWarnings(bool mixedType, EInfinityLightType type)
        {
            SerializedProperty cookieProperty = serializedObject.FindProperty("m_Cookie");
            if (mixedType || cookieProperty == null || cookieProperty.hasMultipleDifferentValues || !(cookieProperty.objectReferenceValue is Texture cookieTexture))
                return;
            bool cube = cookieTexture.dimension == TextureDimension.Cube;
            if (type == EInfinityLightType.Point && !cube)
                EditorGUILayout.HelpBox("Point lights take a Cubemap cookie; this 2D cookie is ignored.", MessageType.Warning);
            else if (type != EInfinityLightType.Point && cube)
                EditorGUILayout.HelpBox("Only point lights take a Cubemap cookie; this one is ignored.", MessageType.Warning);
            if (type == EInfinityLightType.Rect || type == EInfinityLightType.Disk)
                EditorGUILayout.HelpBox("On rect and disk lights the cookie is the emitter texture.", MessageType.None);
        }

        void DrawCasterLayers()
        {
            SerializedProperty layer = serializedObject.FindProperty("m_RenderingLayerMask");
            if (layer == null) return;
            if ((layer.uintValue & ~0xFFu) != 0)
            {
                EditorGUILayout.HelpBox("Caster layers contain unsupported high bits.", MessageType.Error);
                return;
            }
            EditorGUI.showMixedValue = layer.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var value = (ERenderingLayer)EditorGUILayout.EnumFlagsField("Caster Layers", (ERenderingLayer)layer.uintValue);
            if (EditorGUI.EndChangeCheck()) layer.uintValue = RenderingLayerUtility.Validate((uint)value);
            EditorGUI.showMixedValue = false;
        }

        static void ApplyBakeMode(Light light, InfinityAdditionalLightData data, LightmapBakeType mode)
        {
            Undo.RecordObjects(new Object[] { light, data }, "Light Baking Mode");
            data.SetShape(data.shape);
            light.lightmapBakeType = mode;
            if (light.lightmapBakeType != mode)
                throw new System.InvalidOperationException("Unity rejected the requested light baking mode.");
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(data);
        }

        void SyncNativeShape()
        {
            foreach (Object extra in m_Additional.targetObjects)
            {
                var data = (InfinityAdditionalLightData)extra;
                Light light = data.attachedLight;
                Undo.RecordObject(light, "Light Shape");
                if (LightShapeUtility.ApplyToUnityLight(light, data.shape))
                    EditorUtility.SetDirty(light);
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Scene view handles (in the light's local frame; forward = +Z, right = +X)

        void OnSceneGUI()
        {
            var light = (Light)target;
            if (light == null || !light.TryGetComponent(out InfinityAdditionalLightData data)) return;

            FLightShape shape = data.shape;
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
                        shape.sourceRadius = Mathf.Max(0, Handles.ScaleValueHandle(shape.sourceRadius, new Vector3(shape.sourceRadius, 0, 0), Quaternion.identity,
                            HandleUtility.GetHandleSize(Vector3.zero) * 0.5f, Handles.DotHandleCap, 0));
                        range = RangeHandle(range);
                        break;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(new Object[] { data, light }, "Edit Light");
                    light.range = Mathf.Max(0.01f, range);
                    light.spotAngle = Mathf.Clamp(spotAngle, 1, 179);
                    data.SetShape(shape);
                    EditorUtility.SetDirty(data);
                    EditorUtility.SetDirty(light);
                }
            }
        }

        static float SourceRadiusHandle(float radius)
        {
            if (radius <= 0) return radius;
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
                Handles.DrawLine(Vector3.zero, a); Handles.DrawLine(Vector3.zero, b); Handles.DrawLine(Vector3.zero, c); Handles.DrawLine(Vector3.zero, d);
            }
            Vector3 handle = center + Vector3.up * range * tan;
            Vector3 moved = Handles.Slider(handle, Vector3.up, HandleUtility.GetHandleSize(handle) * 0.05f, Handles.DotHandleCap, 0);
            return 2 * Mathf.Atan2(Mathf.Max(0, moved.y), range) * Mathf.Rad2Deg;
        }

        static void Relative(SerializedProperty parent, string name, string label)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
        }

        static void Field(SerializedObject owner, string name, string label)
        {
            SerializedProperty property = owner.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
