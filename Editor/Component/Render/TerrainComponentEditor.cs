using UnityEditor;
using InfinityTech.Component;

namespace InfinityTech.Editor.Component
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TerrainComponent))]
    public class TerrainComponentEditor : UnityEditor.Editor
    {
        TerrainComponent Terrain { get { return target as TerrainComponent; } }


        void OnEnable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += PreSave;
        }

        void OnValidate()
        {

        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            serializedObject.ApplyModifiedProperties();
        }

        void OnDisable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= PreSave;
        }

        void PreSave(UnityEngine.SceneManagement.Scene InScene, string InPath)
        {
            if (Terrain.gameObject.activeSelf == false) { return; }
            if (Terrain.enabled == false) { return; }

            Terrain.Serialize();
        }
    }
}

