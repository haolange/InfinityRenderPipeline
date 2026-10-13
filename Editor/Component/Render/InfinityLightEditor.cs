using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Component;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightEditor : UnityEditor.Editor
    {
        SerializedObject m_Additional;

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
                    data.AdoptNativeShape();
            }
            m_Additional.Update();
        }

        public override void OnInspectorGUI()
        {
            if (m_Additional == null)
                EnsureAdditional();

            // Shape: Infinity owns the shape in Unreal form and writes the native Light fields it derives.
            m_Additional.Update();
            SerializedProperty shape = m_Additional.FindProperty("m_Shape");
            SerializedProperty typeProperty = shape.FindPropertyRelative("type");
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(typeProperty, new GUIContent("Type"));
            bool mixedType = typeProperty.hasMultipleDifferentValues;
            var type = (EInfinityLightType)typeProperty.enumValueIndex;
            SerializedProperty spotShape = shape.FindPropertyRelative("spotShape");
            if (!mixedType)
            {
                EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
                switch (type)
                {
                    case EInfinityLightType.Directional:
                        Relative(shape, "sourceAngle", "Source Angle");
                        Relative(shape, "softSourceAngle", "Soft Source Angle");
                        break;
                    case EInfinityLightType.Point:
                        Relative(shape, "sourceRadius", "Source Radius");
                        Relative(shape, "softSourceRadius", "Soft Source Radius");
                        Relative(shape, "sourceLength", "Source Length");
                        if (shape.FindPropertyRelative("sourceLength").floatValue > 0)
                            EditorGUILayout.HelpBox("A length makes a tube (a line when the radius is 0). Tubes and lines are realtime only.", MessageType.Info);
                        break;
                    case EInfinityLightType.Spot:
                        EditorGUILayout.PropertyField(spotShape, new GUIContent("Spot Shape"));
                        var spot = (ESpotShape)spotShape.enumValueIndex;
                        if (spot == ESpotShape.Box)
                        {
                            Relative(shape, "boxSize", "Size");
                        }
                        else
                        {
                            if (spot == ESpotShape.Pyramid) Relative(shape, "pyramidAspect", "Aspect Ratio");
                            Relative(shape, "sourceRadius", "Source Radius");
                            Relative(shape, "softSourceRadius", "Soft Source Radius");
                        }
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
            if (EditorGUI.EndChangeCheck())
            {
                m_Additional.FindProperty("m_ShapeAuthored").boolValue = true;
                m_Additional.ApplyModifiedProperties();
                SyncNativeShape();
            }

            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Emission", EditorStyles.boldLabel);
            Field(serializedObject, "m_Color", "Color");
            Field(serializedObject, "m_Intensity", "Intensity");
            Field(serializedObject, "m_UseColorTemperature", "Use Color Temperature");
            Field(serializedObject, "m_ColorTemperature", "Color Temperature");
            if (mixedType || type != EInfinityLightType.Directional) Field(serializedObject, "m_Range", "Range");
            if (!mixedType && type == EInfinityLightType.Spot && !spotShape.hasMultipleDifferentValues && (ESpotShape)spotShape.enumValueIndex != ESpotShape.Box)
            {
                Field(serializedObject, "m_SpotAngle", "Outer Angle");
                if ((ESpotShape)spotShape.enumValueIndex == ESpotShape.Cone)
                    Field(serializedObject, "m_InnerSpotAngle", "Inner Angle");
            }
            Field(serializedObject, "m_Lightmapping", "Mode");
            if (!mixedType && type == EInfinityLightType.Point && shape.FindPropertyRelative("sourceLength").floatValue > 0 && ((Light)target).lightmapBakeType != LightmapBakeType.Realtime)
                EditorGUILayout.HelpBox("Tube and line lights are not baked; the lightmapper skips them.", MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);
            Field(serializedObject, "m_Shadows.m_Type", "Mode");
            Field(serializedObject, "m_Shadows.m_NearPlane", "Near Plane");
            Field(serializedObject, "m_Shadows.m_Bias", "Depth Bias");
            Field(serializedObject, "m_Shadows.m_NormalBias", "Normal Bias");
            SerializedProperty layer = serializedObject.FindProperty("m_RenderingLayerMask");
            if (layer != null)
            {
                if ((layer.uintValue & ~0xFFu) != 0)
                    EditorGUILayout.HelpBox("Caster layers contain unsupported high bits.", MessageType.Error);
                else
                {
                    EditorGUI.showMixedValue = layer.hasMultipleDifferentValues;
                    EditorGUI.BeginChangeCheck();
                    var value = (ERenderingLayer)EditorGUILayout.EnumFlagsField("Caster Layers", (ERenderingLayer)layer.uintValue);
                    if (EditorGUI.EndChangeCheck()) layer.uintValue = RenderingLayerUtility.Validate((uint)value);
                    EditorGUI.showMixedValue = false;
                }
            }
            serializedObject.ApplyModifiedProperties();

            m_Additional.Update();
            Field(m_Additional, "m_ShadowFilter", "Shadow Filter");
            Field(m_Additional, "m_ShadowUpdateMode", "Shadow Update Mode");
            SerializedProperty updateMode = m_Additional.FindProperty("m_ShadowUpdateMode");
            if (updateMode != null && !updateMode.hasMultipleDifferentValues && updateMode.enumValueIndex == (int)EShadowUpdateMode.OnDemand)
            {
                if (GUILayout.Button("Request Shadow Map Rendering"))
                {
                    foreach (Object extra in m_Additional.targetObjects)
                        ((InfinityAdditionalLightData)extra).RequestShadowMapRendering();
                    SceneView.RepaintAll();
                }
            }
            if (updateMode != null && updateMode.enumValueIndex != (int)EShadowUpdateMode.EveryFrame)
            {
                EditorGUILayout.HelpBox("Cached shadows keep the casters of their last render; moving objects do not update them. Directional lights always render every frame.", MessageType.None);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Infinity", EditorStyles.boldLabel);
            Field(m_Additional, "lightLayer", "Light Layers");
            Field(m_Additional, "diffuse", "Diffuse Weight");
            Field(m_Additional, "specular", "Specular Weight");
            Field(m_Additional, "enableContactShadow", "Contact Shadows");
            Field(m_Additional, "maxDrawDistance", "Maximum Distance");
            Field(m_Additional, "maxDrawDistanceFade", "Distance Fade");
            Field(m_Additional, "enableVolumetric", "Volumetric");
            Field(m_Additional, "volumetricIntensity", "Volumetric Intensity");
            Field(m_Additional, "volumetricOcclusion", "Volumetric Shadow Weight");
            m_Additional.ApplyModifiedProperties();
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
