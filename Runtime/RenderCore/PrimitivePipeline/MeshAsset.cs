using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using InfinityTech.Core.Container;

namespace InfinityTech.Rendering.MeshPipeline
{
    [Serializable]
    public struct MeshLODInfo : IEquatable<MeshLODInfo>
    {
        public float screenSize;
        public int[] materialSlot;

        public bool Equals(MeshLODInfo Target)
        {
            return screenSize.Equals(Target.screenSize) && materialSlot.Equals(Target.materialSlot);
        }

        public override bool Equals(object obj)
        {
            return Equals((MeshLODInfo)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = screenSize.GetHashCode();
            hashCode += materialSlot.GetHashCode();

            return hashCode;
        }
    }

    [Serializable]
    public struct FMesh : IEquatable<FMesh>
    {
        public bool IsCreated;
        public Mesh[] meshes;
        public Material[] materials;
        public MeshLODInfo[] lODInfo;
        
        public FMesh(Mesh[] meshes, Material[] materials, MeshLODInfo[] lODInfo)
        {
            this.IsCreated = true;
            this.meshes = meshes;
            this.materials = materials;
            this.lODInfo = lODInfo;
        }

        public bool Equals(FMesh Target)
        {
            return IsCreated.Equals(Target.IsCreated) && meshes.Equals(Target.meshes) && lODInfo.Equals(Target.lODInfo) && materials.Equals(Target.materials);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMesh)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = IsCreated ? 0 : 1;
            hashCode += meshes.GetHashCode();
            hashCode += lODInfo.GetHashCode();
            hashCode += materials.GetHashCode();

            return hashCode;
        }
    }

    [CreateAssetMenu(menuName = "InfinityRenderPipeline/StaticMeshAsset", order = 358)]
    public class MeshAsset : ScriptableObject
    {
#if UNITY_EDITOR
        [Header("Target")]
        [HideInInspector]
        public GameObject target;
#endif

        [Header("Mesh")]
        public Mesh[] meshes;

        [Header("Material")]
        public Material[] materials;

        [Header("Culling")]
        public MeshLODInfo[] lODInfos;

        [Header("Proxy")]
        [HideInInspector]
        public FMesh Tree;


        public MeshAsset()
        {

        }

        void Awake()
        {
            //Debug.Log("Awake");
            BuildMeshProxy();
        }

        void Reset()
        {
            //Debug.Log("Reset");
            BuildMeshProxy();
        }

        void OnEnable()
        {
            //Debug.Log("OnEnable");
            BuildMeshProxy();
        }

        void OnValidate()
        {
            //Debug.Log("OnValidate");
            BuildMeshProxy();
        }

        void OnDisable()
        {
            //Debug.Log("OnDisable");
        }

        void OnDestroy()
        {
            //Debug.Log("OnDestroy");
        }

        void BuildMeshProxy()
        {
            this.Tree = new FMesh(meshes, materials, lODInfos);
        }

#if UNITY_EDITOR
        void BuildMeshAsset(Mesh[] meshes, Material[] materials, MeshLODInfo[] lODInfos)
        {
            this.meshes = meshes;
            this.materials = materials;
            this.lODInfos = lODInfos;
        }

        internal static void BuildMeshAssetFromLODGroup(GameObject cloneTarget, MeshAsset meshAsset)
        {
            List<Mesh> meshes = new List<Mesh>();
            List<Material> materials = new List<Material>();
            LOD[] lods = cloneTarget.GetComponent<LODGroup>().GetLODs();

            //Collector Meshes&Materials
            for (int j = 0; j < lods.Length; ++j)
            {
                ref LOD lod = ref lods[j];
                Renderer renderer = lod.renderers[0];
                MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();

                meshes.AddUnique(meshFilter.sharedMesh);
                for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
                {
                    materials.AddUnique(renderer.sharedMaterials[k]);
                }
            }

            //Build LODInfo
            MeshLODInfo[] lODInfos = new MeshLODInfo[lods.Length];
            for (int l = 0; l < lods.Length; ++l)
            {
                ref LOD lod = ref lods[l];
                ref MeshLODInfo lODInfo = ref lODInfos[l];
                Renderer renderer = lod.renderers[0];

                lODInfo.screenSize = 1 - (l * 0.125f);
                lODInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                {
                    ref int MaterialSlot = ref lODInfo.materialSlot[m];
                    MaterialSlot = materials.IndexOf(renderer.sharedMaterials[m]);
                }
            }

            meshAsset.BuildMeshAsset(meshes.ToArray(), materials.ToArray(), lODInfos);
            meshAsset.BuildMeshProxy();
            EditorUtility.SetDirty(meshAsset);
        }

        internal static void BuildMeshAssetFromMeshRenderer(GameObject cloneTarget, MeshAsset meshAsset)
        {
            List<Mesh> meshes = new List<Mesh>();
            List<Material> materials = new List<Material>();

            //Collector Meshes&Materials
            Renderer renderer = cloneTarget.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = cloneTarget.GetComponent<MeshFilter>();

            meshes.AddUnique(meshFilter.sharedMesh);
            for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
            {
                materials.AddUnique(renderer.sharedMaterials[k]);
            }

            //Build LODInfo
            MeshLODInfo[] lODInfos = new MeshLODInfo[1];

            ref MeshLODInfo lODInfo = ref lODInfos[0];
            lODInfo.screenSize = 1;
            lODInfo.materialSlot = new int[renderer.sharedMaterials.Length];

            for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
            {
                ref int MaterialSlot = ref lODInfo.materialSlot[m];
                MaterialSlot = materials.IndexOf(renderer.sharedMaterials[m]);
            }

            meshAsset.BuildMeshAsset(meshes.ToArray(), materials.ToArray(), lODInfos);
            meshAsset.BuildMeshProxy();
            EditorUtility.SetDirty(meshAsset);
        }

        public static void BuildMeshAsset(GameObject cloneTarget, MeshAsset meshAsset)
        {
            if (cloneTarget == null)
            {
                Debug.LogWarning("source prefab is null");
                return;
            }

            bool buildOK = false;

            if(cloneTarget.GetComponent<LODGroup>() != null)
            {
                buildOK = true;
                meshAsset.target = cloneTarget;
                BuildMeshAssetFromLODGroup(cloneTarget, meshAsset);
            }

            if (cloneTarget.GetComponent<MeshFilter>() != null && cloneTarget.GetComponent<MeshRenderer>() != null)
            {
                buildOK = true;
                meshAsset.target = cloneTarget;
                BuildMeshAssetFromMeshRenderer(cloneTarget, meshAsset);
            }

            if (!buildOK) { Debug.LogWarning("source prefab doesn't have LODGroup or MeshRenderer"); }
        }
#endif
    }
}
