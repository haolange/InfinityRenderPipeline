using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.MeshPipeline.Editor
{
    public class MeshAssetWizard : ScriptableWizard
    {
        private MeshAsset meshAsset;

        public GameObject target;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            MeshAsset.BuildMeshAsset(target, meshAsset);
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {
            
        }

        public void SetMeshAsset(MeshAsset meshAsset)
        {
            this.meshAsset = meshAsset;
            this.target = meshAsset.target != null ? meshAsset.target : null;
        }
    }
}
