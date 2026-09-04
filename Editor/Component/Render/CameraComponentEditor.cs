using UnityEditor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(CameraComponent))]
    public class CameraComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
