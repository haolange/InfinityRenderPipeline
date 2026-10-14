using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using InfinityTech.Component;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Component.Editor
{
    // Decal projector inspector: the box is the transform (unit cube), so the size handle edits the scale and keeps the
    // opposite face in place; UVs and angle fade are projector parameters.
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DecalComponent))]
    public sealed class DecalComponentEditor : UnityEditor.Editor
    {
        const string FoldoutKey = "InfinityRP.Decal.Foldout.";

        readonly BoxBoundsHandle m_BoxHandle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (InfinityInspectorGUI.BeginFoldout(FoldoutKey + "General", "General"))
            {
                Field("drawOrder", "Draw Order");
                if (targets.Length == 1)
                {
                    Transform transform = ((DecalComponent)target).transform;
                    EditorGUI.BeginChangeCheck();
                    Vector3 size = EditorGUILayout.Vector3Field(new GUIContent("Size", "Projector box size (the transform scale); projection along -Y."), transform.localScale);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(transform, "Decal Size");
                        transform.localScale = Vector3.Max(size, Vector3.one * 0.001f);
                    }
                }
            }
            InfinityInspectorGUI.EndFoldout();

            if (InfinityInspectorGUI.BeginFoldout(FoldoutKey + "Projection", "Projection"))
            {
                Field("uvScale", "UV Scale");
                Field("uvBias", "UV Bias");
                Field("startAngleFade", "Start Angle Fade");
                Field("endAngleFade", "End Angle Fade");
                EditorGUILayout.HelpBox("Decals apply to every opaque surface inside the box; decal layers are not supported.", MessageType.None);
            }
            InfinityInspectorGUI.EndFoldout();
            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            var decal = (DecalComponent)target;
            Transform transform = decal.transform;
            Matrix4x4 frame = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            using (new Handles.DrawingScope(new Color(1.0f, 0.75f, 0.2f, 1.0f), frame))
            {
                Vector3 size = transform.localScale;
                Handles.DrawLine(Vector3.zero, Vector3.down * size.y * 0.5f);
                m_BoxHandle.center = Vector3.zero;
                m_BoxHandle.size = size;
                EditorGUI.BeginChangeCheck();
                m_BoxHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(transform, "Decal Box");
                    transform.position = frame.MultiplyPoint3x4(m_BoxHandle.center);
                    transform.localScale = Vector3.Max(m_BoxHandle.size, Vector3.one * 0.001f);
                }
            }
        }

        void Field(string name, string label)
        {
            SerializedProperty property = serializedObject.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
