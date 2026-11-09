using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using InfinityTech.Rendering.Editor;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Editor;

namespace InfinityTech.Component.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MeshComponent))]
    public class MeshComponentEditor : UnityEditor.Editor
    {
        const string FoldoutPrefix = "InfinityRP.Mesh.Foldout.";

        SerializedProperty m_Mobility;
        SerializedProperty m_Asset;
        SerializedProperty m_Materials;
        SerializedProperty m_CastShadow;
        SerializedProperty m_ReceiveShadow;
        SerializedProperty m_AffectIndirectLighting;
        SerializedProperty m_Visible;
        SerializedProperty m_RenderingLayer;
        SerializedProperty m_RenderPriority;
        SerializedProperty m_MotionVector;
        // Inspector-only source of the explicit "create from Mesh" action; never serialized.
        Mesh m_SourceMesh;

        void OnEnable()
        {
            m_Mobility = serializedObject.FindProperty("mobility");
            m_Asset = serializedObject.FindProperty("asset");
            m_Materials = serializedObject.FindProperty("materials");
            m_CastShadow = serializedObject.FindProperty("castShadow");
            m_ReceiveShadow = serializedObject.FindProperty("receiveShadow");
            m_AffectIndirectLighting = serializedObject.FindProperty("affectIndirectLighting");
            m_Visible = serializedObject.FindProperty("visible");
            m_RenderingLayer = serializedObject.FindProperty("renderingLayer");
            m_RenderPriority = serializedObject.FindProperty("renderPriority");
            m_MotionVector = serializedObject.FindProperty("motionVector");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            #region State
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "State", "State"))
            {
                EditorGUILayout.PropertyField(m_Mobility, new GUIContent("Mobility"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Mesh
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Mesh", "Mesh"))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(m_Asset, new GUIContent("Mesh Asset"));
                // Assigning an asset copies its default materials once; the component's materials stay the authority.
                if (EditorGUI.EndChangeCheck() && m_Asset.objectReferenceValue is MeshAsset assigned)
                {
                    CopyMaterials(assigned);
                }
                if (m_Asset.objectReferenceValue == null && !m_Asset.hasMultipleDifferentValues && !serializedObject.isEditingMultipleObjects)
                    DrawCreateFromMesh();
                EditorGUILayout.PropertyField(m_Materials, new GUIContent("Materials"), true);
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Lighting
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Lighting", "Lighting"))
            {
                EditorGUILayout.PropertyField(m_CastShadow, new GUIContent("Cast Shadow"));
                EditorGUILayout.PropertyField(m_ReceiveShadow, new GUIContent("Receive Shadow"));
                EditorGUILayout.PropertyField(m_AffectIndirectLighting, new GUIContent("Affect Indirect"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            #region Rendering
            if (InfinityInspectorGUI.BeginFoldout(FoldoutPrefix + "Rendering", "Rendering"))
            {
                EditorGUILayout.PropertyField(m_Visible, new GUIContent("Visible"));
                EditorGUILayout.PropertyField(m_RenderingLayer, new GUIContent("Rendering Layer"));
                EditorGUILayout.PropertyField(m_RenderPriority, new GUIContent("Render Priority"));
                EditorGUILayout.PropertyField(m_MotionVector, new GUIContent("Motion Vector"));
            }
            InfinityInspectorGUI.EndFoldout();
            #endregion

            serializedObject.ApplyModifiedProperties();
        }

        void CopyMaterials(MeshAsset source)
        {
            System.ReadOnlySpan<Material> defaults = source.Materials;
            m_Materials.arraySize = defaults.Length;
            for (int i = 0; i < defaults.Length; ++i)
            {
                m_Materials.GetArrayElementAtIndex(i).objectReferenceValue = defaults[i];
            }
        }

        /// <summary>
        /// Explicit action: the shared one-level asset of a Mesh, created with this component's materials when it does not
        /// exist, is assigned without changing the materials. CreateAsset is the only file write.
        /// </summary>
        void DrawCreateFromMesh()
        {
            m_SourceMesh = (Mesh)EditorGUILayout.ObjectField(new GUIContent("Create MeshAsset from Mesh"), m_SourceMesh, typeof(Mesh), false);
            using (new EditorGUI.DisabledScope(m_SourceMesh == null))
            {
                if (!GUILayout.Button("Create MeshAsset"))
                {
                    return;
                }
                try
                {
                    MeshAsset created = MeshAssetBuilder.GetOrCreateSingleLevel(m_SourceMesh, ((MeshComponent)target).Materials, out _);
                    m_Asset.objectReferenceValue = created;
                    m_SourceMesh = null;
                }
                catch (System.InvalidOperationException error)
                {
                    Debug.LogError(error.Message, target);
                }
            }
        }

        [MenuItem("GameObject/3D Object/Infinity/MeshEntity", false, -1000)]
        public static void CreatePrimitiveEntity(MenuCommand menuCommand)
        {
            GameObject MeshEntity = new GameObject("MeshEntity");
            MeshEntity.AddComponent<MeshComponent>();
            GameObjectUtility.SetParentAndAlign(MeshEntity, menuCommand.context as GameObject);
            StageUtility.PlaceGameObjectInCurrentStage(MeshEntity);
            GameObjectUtility.EnsureUniqueNameForSibling(MeshEntity);
            Undo.RegisterCreatedObjectUndo(MeshEntity, "Create " + MeshEntity.name);
            Selection.activeObject = MeshEntity;
        }
    }
}
