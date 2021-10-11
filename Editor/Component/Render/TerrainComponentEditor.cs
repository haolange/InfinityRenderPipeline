using UnityEditor;
using UnityEngine;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TerrainComponent))]
    internal class TerrainComponentEditor : UnityEditor.Editor
    {
        public bool showGeneral = true;
        public SerializedProperty lOD0ScreenSize;
        public SerializedProperty lOD0Distribution;
        public SerializedProperty lODXDistribution;
        public SerializedObject serializedTerrain;

        TerrainComponent m_Terrain { get { return target as TerrainComponent; } }

        void OnEnable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSave;

            serializedTerrain = new SerializedObject(m_Terrain);
            lOD0ScreenSize = serializedTerrain.FindProperty("lod0ScreenSize");
            lOD0Distribution = serializedTerrain.FindProperty("lod0Distribution");
            lODXDistribution = serializedTerrain.FindProperty("lodXDistribution");
        }

        void OnValidate()
        {

        }

        public override void OnInspectorGUI()
        {
            serializedTerrain.Update();

            showGeneral = EditorGUILayout.BeginFoldoutHeaderGroup(showGeneral, "General");
            if (showGeneral)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(lOD0ScreenSize, new GUIContent("LOD0 ScreenSize"));
                EditorGUILayout.PropertyField(lOD0Distribution, new GUIContent("LOD0 Distribution"));
                EditorGUILayout.PropertyField(lODXDistribution, new GUIContent("LODX Distribution"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedTerrain.ApplyModifiedProperties();
        }

        void OnDisable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSave;
        }

        void OnSave(UnityEngine.SceneManagement.Scene InScene, string InPath)
        {
            if (m_Terrain.gameObject.activeSelf == false) { return; }
            if (m_Terrain.enabled == false) { return; }

            m_Terrain.Serialize();
        }
    }
}

