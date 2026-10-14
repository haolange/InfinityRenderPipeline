using UnityEditor;
using UnityEngine;
using UnityEditor.ProjectWindowCallback;

namespace InfinityTech.Rendering.MeshPipeline.Editor
{
    public class MeshAssetAction
    {
        #region MeshAsset
        internal class CreateMeshAsset : AssetCreationEndAction
        {
            public MeshAsset meshAsset;

            public override void Action(EntityId entityId, string pathName, string resourceFile)
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

            GameObject prefab = (GameObject)activeObject;
            // A MeshAsset is built either from a LODGroup or from a MeshFilter + MeshRenderer pair.
            bool buildOK = prefab.GetComponent<LODGroup>() != null
                || (prefab.GetComponent<MeshFilter>() != null && prefab.GetComponent<MeshRenderer>() != null);

            if (buildOK)
            {
                MeshAsset meshAsset = ScriptableObject.CreateInstance<MeshAsset>();
                meshAsset.target = prefab;
                MeshAsset.BuildMeshAsset(prefab, meshAsset);

                CreateMeshAsset meshAssetFactory = ScriptableObject.CreateInstance<CreateMeshAsset>();
                meshAssetFactory.meshAsset = meshAsset;

                ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, meshAssetFactory, "SM_" + prefab.name + ".asset", null, null);
            } else {
                Debug.LogWarning("The selected prefab needs a LODGroup, or a MeshFilter with a MeshRenderer.");
            }
        }

        #endregion //MeshAsset
    }
}
