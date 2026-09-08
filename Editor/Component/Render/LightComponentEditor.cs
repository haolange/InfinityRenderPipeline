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
    public class LightComponentEditor : LightEditor
    {
        SerializedObject m_Extensions;

        protected override void OnEnable()
        {
            base.OnEnable();
            BindExtensions();
        }

        void BindExtensions()
        {
            var extensions = new Object[targets.Length];
            for (int i = 0; i < targets.Length; ++i)
            {
                extensions[i] = ((Light)targets[i]).GetComponent<LightComponent>();
                if (!extensions[i]) { m_Extensions = null; return; }
            }
            m_Extensions = new SerializedObject(extensions);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Light", EditorStyles.boldLabel);
            Field(serializedObject, "m_Type", "Type");
            Field(serializedObject, "m_Color", "Color");
            Field(serializedObject, "m_Intensity", "Intensity");
            Field(serializedObject, "m_UseColorTemperature", "Use Color Temperature");
            Field(serializedObject, "m_ColorTemperature", "Color Temperature");
            LightType type = ((Light)target).type;
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
                    EditorGUILayout.HelpBox("Caster layers contain unsupported high bits. Run the explicit scene migration before editing this mask.", MessageType.Error);
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

            if (m_Extensions == null)
            {
                if (GUILayout.Button("Add Infinity Light Settings"))
                {
                    foreach (Object item in targets)
                    {
                        var light = (Light)item;
                        if (!light.GetComponent<LightComponent>()) Undo.AddComponent<LightComponent>(light.gameObject);
                    }
                    BindExtensions();
                }
                return;
            }
            m_Extensions.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Surface Lighting", EditorStyles.boldLabel);
            Field(m_Extensions, "lightLayer", "Light Layers");
            Field(m_Extensions, "diffuse", "Diffuse Weight");
            Field(m_Extensions, "specular", "Specular Weight");
            Field(m_Extensions, "enableContactShadow", "Contact Shadows");
            Field(m_Extensions, "maxDrawDistance", "Maximum Distance");
            Field(m_Extensions, "maxDrawDistanceFade", "Distance Fade Fraction");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Volumetric Lighting", EditorStyles.boldLabel);
            Field(m_Extensions, "enableVolumetric", "Enabled");
            Field(m_Extensions, "volumetricIntensity", "Intensity");
            Field(m_Extensions, "volumetricOcclusion", "Shadow Weight");
            m_Extensions.ApplyModifiedProperties();
        }

        static void Field(SerializedObject owner, string name, string label)
        {
            SerializedProperty property = owner.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
