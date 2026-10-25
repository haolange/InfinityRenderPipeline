using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TerrainComponent))]
    internal class TerrainComponentEditor : UnityEditor.Editor
    {
        [System.NonSerialized] SerializedProperty lOD0ScreenSize;
        [System.NonSerialized] SerializedProperty lOD0Distribution;
        [System.NonSerialized] SerializedProperty lODXDistribution;

        void OnEnable()
        {
            lOD0ScreenSize = serializedObject.FindProperty("lod0ScreenSize");
            lOD0Distribution = serializedObject.FindProperty("lod0Distribution");
            lODXDistribution = serializedObject.FindProperty("lodXDistribution");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (InfinityInspectorGUI.BeginFoldout("InfinityRP.Terrain.Foldout.General", "General"))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(lOD0ScreenSize, new GUIContent("LOD0 ScreenSize"));
                EditorGUILayout.PropertyField(lOD0Distribution, new GUIContent("LOD0 Distribution"));
                EditorGUILayout.PropertyField(lODXDistribution, new GUIContent("LODX Distribution"));
                EditorGUI.indentLevel--;
            }
            InfinityInspectorGUI.EndFoldout();

            serializedObject.ApplyModifiedProperties();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Rebuild Sector From Unity Terrain"))
                foreach (Object item in targets)
                {
                    var terrain = (TerrainComponent)item;
                    Undo.RecordObject(terrain, "Rebuild Infinity Terrain Sector");
                    terrain.Serialize();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
                    EditorUtility.SetDirty(terrain);
                }
        }

    }
}
