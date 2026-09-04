using UnityEditor;

namespace InfinityTech.Rendering.Editor
{
    public static class InfinityInspectorGUI
    {
        public static bool BeginFoldout(string key, string title, bool defaultOpen = true)
        {
            bool open = SessionState.GetBool(key, defaultOpen);
            bool next = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            if (next != open)
            {
                SessionState.SetBool(key, next);
            }

            return next;
        }

        public static void EndFoldout()
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
