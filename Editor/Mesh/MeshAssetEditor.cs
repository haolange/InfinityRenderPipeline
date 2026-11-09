using UnityEditor;

namespace InfinityTech.Rendering.MeshPipeline.Editor
{
    [CustomEditor(typeof(MeshAsset))]
    public class MeshAssetEditor : UnityEditor.Editor
    {
        private MeshAsset assetTarget
        {
            get
            {
                return target as MeshAsset;
            }
        }

        private void OnEnable()
        {
        }

        private void OnValidate()
        {
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnDisable()
        {
        }
    }
}
