using UnityEditor;
using UnityEngine;

namespace InfinityTech.ActorAction.Editor
{
    public class SetMeshRendererRandomMeshWizard : ScriptableWizard
    {
        public Mesh[] meshs;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            MeshFilter[] meshRenderers = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            foreach (MeshFilter meshRenderer in meshRenderers)
            {
                int meshIndex = Random.Range(0, meshs.Length);
                meshIndex = Mathf.Clamp(meshIndex, 0, meshs.Length - 1);
                meshRenderer.sharedMesh = meshs[meshIndex];
            }
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {

        }
    }

    public class SetMeshRendererMaterialWizard : ScriptableWizard
    {
        public Material[] materials;
        private GameObject[] activeObjects;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            /*foreach(GameObject gameObject in activeObjects)
            {
                MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                {
                    Debug.LogWarning("select game object : " + gameObject.name + " doesn't have MeshRenderer component");
                    return;
                }

                int materiaIndex = Random.Range(-10000, 10000);
                materiaIndex = Mathf.Clamp(materiaIndex, 0, materials.Length - 1);

                //for (int i = 0; i < meshRenderer.sharedMaterials.Length; ++i)
                {
                    meshRenderer.sharedMaterial = materials[materiaIndex];
                }
            }*/

            MeshRenderer[] meshRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                int materiaIndex = Random.Range(0, materials.Length);
                materiaIndex = Mathf.Clamp(materiaIndex, 0, materials.Length - 1);

                //for (int i = 0; i < meshRenderer.sharedMaterials.Length; ++i)
                {
                    meshRenderer.sharedMaterial = materials[materiaIndex];
                }
            }
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {

        }

        public void SetData(GameObject[] activeObjects)
        {
            this.activeObjects = activeObjects;
        }
    }

    public class MeshRendererAction
    {
        [MenuItem("Tool/EntityAction/MeshRenderer/SetRandomMesh", priority = 10)]
        public static void SetRandomMesh(MenuCommand menuCommand)
        {
            ScriptableWizard.DisplayWizard<SetMeshRendererRandomMeshWizard>("SetRadomMesh", "Set");
        }

        [MenuItem("Tool/EntityAction/MeshRenderer/SetRandomMaterial", priority = 9)]
        public static void SetRandomMaterial(MenuCommand menuCommand)
        {
            GameObject[] activeObjects = Selection.gameObjects;

            SetMeshRendererMaterialWizard setMaterialWizard = ScriptableWizard.DisplayWizard<SetMeshRendererMaterialWizard>("SetRandomMaterial", "Set");
            setMaterialWizard.SetData(activeObjects);
        }
    }
}
