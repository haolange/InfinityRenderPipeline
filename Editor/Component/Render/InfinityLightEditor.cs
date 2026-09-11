using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Light))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityLightEditor : UnityEditor.Editor
    {
        SerializedObject m_Additional;

        void OnEnable() => BindAdditional();

        void BindAdditional()
        {
            var extras = new Object[targets.Length];
            for (int i = 0; i < targets.Length; ++i)
            {
                extras[i] = ((Light)targets[i]).GetComponent<InfinityAdditionalLightData>();
                if (extras[i] == null)
                {
                    m_Additional = null;
                    return;
                }
            }
            m_Additional = new SerializedObject(extras);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            Field(serializedObject, "m_Type", "Type");
            LightType type = ((Light)target).type;
            if (type == LightType.Disc)
                EditorGUILayout.HelpBox("Disc lights are not implemented. Use Spot or Rectangle.", MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Emission", EditorStyles.boldLabel);
            Field(serializedObject, "m_Color", "Color");
            Field(serializedObject, "m_Intensity", "Intensity");
            Field(serializedObject, "m_UseColorTemperature", "Use Color Temperature");
            Field(serializedObject, "m_ColorTemperature", "Color Temperature");
            if (type != LightType.Directional) Field(serializedObject, "m_Range", "Range");
            if (type == LightType.Spot)
            {
                Field(serializedObject, "m_SpotAngle", "Outer Angle");
                Field(serializedObject, "m_InnerSpotAngle", "Inner Angle");
            }
            if (type == LightType.Rectangle) Field(serializedObject, "m_AreaSize", "Size");

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

            if (m_Additional == null)
            {
                if (GUILayout.Button("Add Infinity Light Settings"))
                {
                    foreach (Object item in targets)
                    {
                        InfinityAdditionalLightData.GetOrCreate((Light)item, true);
                    }
                    BindAdditional();
                }
                return;
            }

            m_Additional.Update();
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

        static void Field(SerializedObject owner, string name, string label)
        {
            SerializedProperty property = owner.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
