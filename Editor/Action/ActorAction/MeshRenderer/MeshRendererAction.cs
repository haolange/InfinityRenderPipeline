using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.ProjectWindowCallback;

namespace InfinityTech.Editor.ActorAction
{
    public class GetMaterialListWizard : ScriptableWizard
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

            MeshRenderer[] meshRenderers = GameObject.FindObjectsOfType<MeshRenderer>();
            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                int materiaIndex = Random.Range(-10000, 10000);
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
        [MenuItem("Tool/EntityAction/MeshRenderer/SetRandomMaterial", priority = 9)]
        public static void SetRandomMaterial(MenuCommand menuCommand)
        {
            GameObject[] activeObjects = Selection.gameObjects;

            GetMaterialListWizard getMaterialListWizard = ScriptableWizard.DisplayWizard<GetMaterialListWizard>("GetMaterialListWizard", "Set");
            getMaterialListWizard.SetData(activeObjects);
        }
    }
}
