using UnityEditor;
using UnityEngine;
using UnityEditor.ProjectWindowCallback;

namespace InfinityTech.Rendering.MeshPipeline.Editor
{
    public class MeshAssetAction
    {
        #region MeshAsset
        internal class CreateMeshAsset : EndNameEditAction
        {
            public MeshAsset meshAsset;

            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                AssetDatabase.CreateAsset(meshAsset, pathName);
            }
        }

        [MenuItem("Assets/AssetActions/Primitive/BuildMeshAssetFromPrefab", priority = 32)]
        public static void BuildMeshAssetFromPrefab(MenuCommand menuCommand)
        {
            Object activeObject = Selection.activeObject;
            if (activeObject.GetType() != typeof(GameObject))
            {
                Debug.LogWarning("select asset is not Prefab");
                return;
            }

            bool buildOK = false;
            GameObject prefab = (GameObject)activeObject;

            if (prefab.GetComponent<LODGroup>() == null)
            {
                buildOK = true;
            }

            if (prefab.GetComponent<MeshFilter>() == null || prefab.GetComponent<MeshRenderer>() == null)
            {
                buildOK = true;
            }

            if (buildOK)
            {
                MeshAsset meshAsset = ScriptableObject.CreateInstance<MeshAsset>();
                meshAsset.target = prefab;
                MeshAsset.BuildMeshAsset(prefab, meshAsset);

                CreateMeshAsset meshAssetFactory = ScriptableObject.CreateInstance<CreateMeshAsset>();
                meshAssetFactory.meshAsset = meshAsset;

                ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, meshAssetFactory, "SM_" + prefab.name + ".asset", null, null);
            } else {
                Debug.LogWarning("select prefab doesn't have LODGroup or MeshRenderer");
            }
        }

        [MenuItem("Assets/AssetActions/Primitive/UpdateMeshAssetFromPrefab", priority = 32)]
        public static void UpdateMeshAssetFromPrefab(MenuCommand menuCommand)
        {
            Object activeObject = Selection.activeObject;
            if (activeObject.GetType() != typeof(MeshAsset)) 
            {
                Debug.LogWarning("select asset type is not MeshAsset");
                return; 
            }

            MeshAssetWizard meshAssetWizard = ScriptableWizard.DisplayWizard<MeshAssetWizard>("Build MeshAsset", "Build");
            meshAssetWizard.SetMeshAsset((MeshAsset)activeObject);
        }
        #endregion //MeshAsset
    }
}
