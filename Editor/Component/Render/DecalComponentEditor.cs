using UnityEditor;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DecalComponent))]
    public sealed class DecalComponentEditor : UnityEditor.Editor
    {
        const string FoldoutKey = "InfinityRP.Decal.Foldout.General";

        SerializedProperty m_DrawOrder;

        void OnEnable()
        {
            m_DrawOrder = serializedObject.FindProperty("drawOrder");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutKey, "General"))
            {
                EditorGUILayout.PropertyField(m_DrawOrder, new GUIContent("Draw Order"));
            }
            InfinityInspectorGUI.EndFoldout();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
