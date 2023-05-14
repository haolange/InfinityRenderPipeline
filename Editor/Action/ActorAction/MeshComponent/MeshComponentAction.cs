using UnityEditor;
using UnityEngine;
using InfinityTech.Component;

namespace InfinityTech.ActorAction.Editor
{
    public class SetMeshComponentRandomMeshWizard : ScriptableWizard
    {
        public Mesh[] meshs;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            MeshComponent[] meshComponents = FindObjectsByType<MeshComponent>(FindObjectsSortMode.None);
            foreach (MeshComponent meshComponent in meshComponents)
            {
                int meshIndex = Random.Range(0, meshs.Length);
                meshIndex = Mathf.Clamp(meshIndex, 0, meshs.Length - 1);
                meshComponent.meshAsset = meshs[meshIndex];
                meshComponent.UpdateMaterial();
            }
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {

        }
    }

    public class SetMeshComponentRandomMaterialWizard : ScriptableWizard
    {
        public Material[] materials;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            MeshComponent[] meshComponents = FindObjectsByType<MeshComponent>(FindObjectsSortMode.None);
            foreach (MeshComponent meshComponent in meshComponents)
            {
                int materiaIndex = Random.Range(0, materials.Length);
                materiaIndex = Mathf.Clamp(materiaIndex, 0, materials.Length - 1);

                for (int i = 0; i < meshComponent.materials.Length; ++i)
                {
                    meshComponent.materials[i] = materials[materiaIndex];
                }
            }
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {

        }
    }

    public class MeshComponentAction
    {
        [MenuItem("Tool/EntityAction/MeshComponent/SetRandomMesh", priority = 10)]
        public static void SetRandomMesh(MenuCommand menuCommand)
        {
            ScriptableWizard.DisplayWizard<SetMeshComponentRandomMeshWizard>("SetRadomMesh", "Set");
        }

        [MenuItem("Tool/EntityAction/MeshComponent/SetRandomMaterial", priority = 10)]
        public static void SetRandomMaterial(MenuCommand menuCommand)
        {
            ScriptableWizard.DisplayWizard<SetMeshComponentRandomMaterialWizard>("SetRadomMaterial", "Set");
        }
    }
}
