using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    [CustomEditor(typeof(DiffusionProfile))]
    public sealed class DiffusionProfileEditor : UnityEditor.Editor
    {
        const string FoldoutKey = "InfinityRP.Diffusion.Foldout.Scatter";

        SerializedProperty m_ScatterColor;
        SerializedProperty m_ScatterDistance;
        SerializedProperty m_SurfaceAlbedo;
        SerializedProperty m_MaxRadius;
        SerializedProperty m_Thickness;

        void OnEnable()
        {
            m_ScatterColor = serializedObject.FindProperty("scatterColor");
            m_ScatterDistance = serializedObject.FindProperty("scatterDistance");
            m_SurfaceAlbedo = serializedObject.FindProperty("surfaceAlbedo");
            m_MaxRadius = serializedObject.FindProperty("maxRadius");
            m_Thickness = serializedObject.FindProperty("thickness");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutKey, "Scatter"))
            {
                EditorGUILayout.PropertyField(m_ScatterColor, new GUIContent("Scatter Color"));
                EditorGUILayout.PropertyField(m_ScatterDistance, new GUIContent("Scatter Distance"));
                EditorGUILayout.PropertyField(m_SurfaceAlbedo, new GUIContent("Surface Albedo"));
                EditorGUILayout.PropertyField(m_MaxRadius, new GUIContent("Max Radius"));
                EditorGUILayout.PropertyField(m_Thickness, new GUIContent("Thickness", "The share of the skin's diffuse light that scatters: 0 keeps the lit diffuse, 1 replaces it with the scattered diffuse."));
            }
            InfinityInspectorGUI.EndFoldout();

            InfinityRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
            DiffusionProfile[] profiles = asset != null ? asset.ReadDiffusionProfiles() : null;
            if (profiles != null)
            {
                int index = System.Array.IndexOf(profiles, target);
                if (index >= 0)
                    EditorGUILayout.HelpBox("RP Asset list index: " + index, MessageType.Info);
                else
                    EditorGUILayout.HelpBox("This profile is not assigned on the active Infinity RP Asset.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
