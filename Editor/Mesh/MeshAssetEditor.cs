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

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
