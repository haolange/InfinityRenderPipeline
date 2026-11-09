using UnityEditor;

namespace InfinityTech.Rendering.Editor
{
    public static class InfinityInspectorGUI
    {
        public static bool BeginFoldout(string key, string title, bool defaultOpen = true)
        {
            bool open = SessionState.GetBool(key, defaultOpen);
            EditorGUILayout.BeginVertical();
            bool next = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (next != open)
            {
                SessionState.SetBool(key, next);
            }

            return next;
        }

        public static void EndFoldout()
        {
            EditorGUILayout.EndVertical();
        }
    }
}
