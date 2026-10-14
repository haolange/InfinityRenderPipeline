using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    // Material GUI of Infinity Lit Shader Graphs (named by string in Editor/ShaderGraph/InfinityLitSubTarget.cs). The
    // graph's own properties draw as usual; the routing properties the target adds are hidden floats, drawn here with
    // the same choices as InfinityLit, and a route change applies the pass enables through MaterialRouteUtility.
    public class InfinityShaderGraphLitGUI : ShaderGUI
    {
        static readonly string[] s_Routes = { "Deferred", "Forward" };
        static readonly string[] s_Stages = { "None", "T0", "T1", "T2" };

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUILayout.LabelField("Surface Options", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Popup(FindProperty("_SurfaceRoute", properties, false), "Surface Route", s_Routes);
            MaterialProperty stage = FindProperty("_TranslucentStage", properties, false);
            Popup(stage, "Translucent Stage", s_Stages);
            if (stage != null && !stage.hasMixedValue && stage.floatValue == 2)
                Float(materialEditor, FindProperty("_RefractionStrength", properties, false), "Refraction Strength");
            bool routeChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.LabelField("Subsurface", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            MaterialProperty subsurface = FindProperty("_Subsurface", properties, false);
            if (subsurface != null)
            {
                EditorGUI.showMixedValue = subsurface.hasMixedValue;
                bool on = EditorGUILayout.Toggle("Subsurface", subsurface.floatValue > 0.5f);
                EditorGUI.showMixedValue = false;
                if (on != subsurface.floatValue > 0.5f) subsurface.floatValue = on ? 1 : 0;
                if (on)
                {
                    Float(materialEditor, FindProperty("_SSSProfileIndex", properties, false), "SSS Profile Index");
                    Float(materialEditor, FindProperty("_SSSThickness", properties, false), "SSS Thickness");
                    InfinityLitGUI.DrawSssIndexWarning(materialEditor.target as Material);
                }
            }
            routeChanged |= EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Graph Inputs", EditorStyles.boldLabel);
            materialEditor.PropertiesDefaultGUI(properties);

            if (routeChanged)
                InfinityLitGUI.ApplyTargets(materialEditor, true);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            MaterialRouteUtility.ApplyPassState(material);
        }

        public override void ValidateMaterial(Material material)
        {
            MaterialRouteUtility.Read(material, out _, out _);
        }

        static void Popup(MaterialProperty property, string label, string[] options)
        {
            if (property == null) return;
            EditorGUI.showMixedValue = property.hasMixedValue;
            int value = EditorGUILayout.Popup(label, Mathf.Clamp((int)property.floatValue, 0, options.Length - 1), options);
            EditorGUI.showMixedValue = false;
            if (value != (int)property.floatValue) property.floatValue = value;
        }

        static void Float(MaterialEditor materialEditor, MaterialProperty property, string label)
        {
            if (property != null) materialEditor.FloatProperty(property, label);
        }
    }
}
