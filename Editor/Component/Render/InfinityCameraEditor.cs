using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Camera))]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityCameraEditor : UnityEditor.Editor
    {
        SerializedObject m_Additional;
        SerializedProperty m_Projection;
        SerializedProperty m_FOV;
        SerializedProperty m_Orthographic;
        SerializedProperty m_Near;
        SerializedProperty m_Far;
        SerializedProperty m_Viewport;
        SerializedProperty m_TargetTexture;
        SerializedProperty m_CullingMask;
        SerializedProperty m_Background;
        SerializedProperty m_ClearFlags;

        void OnEnable()
        {
            EnsureAdditional();
            m_Projection = serializedObject.FindProperty("orthographic");
            m_FOV = serializedObject.FindProperty("field of view");
            m_Orthographic = serializedObject.FindProperty("orthographic size");
            m_Near = serializedObject.FindProperty("near clip plane");
            m_Far = serializedObject.FindProperty("far clip plane");
            m_Viewport = serializedObject.FindProperty("m_NormalizedViewPortRect");
            m_TargetTexture = serializedObject.FindProperty("m_TargetTexture");
            m_CullingMask = serializedObject.FindProperty("m_CullingMask");
            m_Background = serializedObject.FindProperty("m_BackGroundColor");
            m_ClearFlags = serializedObject.FindProperty("m_ClearFlags");
        }

        void EnsureAdditional()
        {
            var extras = new Object[targets.Length];
            for (int i = 0; i < targets.Length; ++i)
            {
                extras[i] = InfinityAdditionalCameraData.GetOrCreate((Camera)targets[i], true);
            }
            m_Additional = new SerializedObject(extras);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (m_Additional == null)
                EnsureAdditional();
            m_Additional.Update();

            EditorGUILayout.LabelField("Projection", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Projection, new GUIContent("Orthographic"));
            if (((Camera)target).orthographic)
                EditorGUILayout.PropertyField(m_Orthographic, new GUIContent("Size"));
            else
                EditorGUILayout.PropertyField(m_FOV, new GUIContent("Field of View"));
            EditorGUILayout.PropertyField(m_Near, new GUIContent("Near"));
            EditorGUILayout.PropertyField(m_Far, new GUIContent("Far"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_CullingMask, new GUIContent("Culling Mask"));
            EditorGUILayout.PropertyField(m_ClearFlags, new GUIContent("Clear Flags"));
            EditorGUILayout.PropertyField(m_Background, new GUIContent("Background"));
            EditorGUILayout.HelpBox("MSAA, Built-in HDR toggle, and command buffers are not InfinityRP features.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Additional.FindProperty("volumeLayerMask"), new GUIContent("Volume Mask"));
            EditorGUILayout.PropertyField(m_Additional.FindProperty("volumeTrigger"), new GUIContent("Volume Trigger"));
            EditorGUILayout.PropertyField(m_Additional.FindProperty("superResolutionOverride"), new GUIContent("Super Resolution"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_TargetTexture, new GUIContent("Target Texture"));
            EditorGUILayout.PropertyField(m_Viewport, new GUIContent("Viewport Rect"));

            serializedObject.ApplyModifiedProperties();
            m_Additional.ApplyModifiedProperties();
        }

        [MenuItem("GameObject/Camera", false, 10)]
        static void CreateCamera(MenuCommand command)
        {
            var go = new GameObject("Camera");
            var camera = go.AddComponent<Camera>();
            InfinityAdditionalCameraData.GetOrCreate(camera, true);
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Camera");
            Selection.activeGameObject = go;
        }
    }
}
