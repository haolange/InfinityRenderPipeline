using System;
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
            if (!(Selection.activeObject is GameObject prefab))
            {
                Debug.LogWarning("The selected asset is not a prefab.");
                return;
            }

            MeshAsset meshAsset = ScriptableObject.CreateInstance<MeshAsset>();
            try
            {
                MeshAssetBuilder.BuildFromPrefab(prefab, meshAsset);
            }
            catch (InvalidOperationException error)
            {
                UnityEngine.Object.DestroyImmediate(meshAsset);
                Debug.LogError(error.Message, prefab);
                return;
            }

            CreateMeshAsset meshAssetFactory = ScriptableObject.CreateInstance<CreateMeshAsset>();
            meshAssetFactory.meshAsset = meshAsset;

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, meshAssetFactory, "SM_" + prefab.name + ".asset", null, null);
        }

        #endregion //MeshAsset
    }
}
