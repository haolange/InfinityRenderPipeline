using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.MeshPipeline.Editor
{
    /// <summary>
    /// Builds <see cref="MeshAsset"/> data in the Editor. Building validates before it replaces any data. Only
    /// <see cref="GetOrCreateSingleLevel"/> writes a file, and only when it creates the asset.
    /// </summary>
    internal static class MeshAssetBuilder
    {
        internal const string SingleLevelFolder = "Assets/InfinityMeshAssets";

        /// <summary>
        /// With a LODGroup, LOD i is the MeshFilter mesh of the first renderer of LOD i at its
        /// <c>screenRelativeTransitionHeight</c> (one mesh may fill several levels). Without one, the root MeshFilter and
        /// MeshRenderer are one level at screen size 0. Materials are the de-duplicated union of the renderers' materials,
        /// and each level maps its submeshes to that union. The result is validated with <paramref name="fadeWidth"/>.
        /// </summary>
        internal static void Build(GameObject prefab, float fadeWidth, out Mesh[] meshes, out Material[] materials, out MeshLODInfo[] lods)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            var meshList = new List<Mesh>();
            var materialList = new List<Material>();
            var lodList = new List<MeshLODInfo>();
            if (prefab.TryGetComponent(out LODGroup group))
            {
                LOD[] levels = group.GetLODs();
                for (int i = 0; i < levels.Length; ++i)
                {
                    Renderer[] renderers = levels[i].renderers;
                    Renderer renderer = renderers != null && renderers.Length > 0 ? renderers[0] : null;
                    if (renderer == null) throw new InvalidOperationException($"LOD {i} of prefab '{prefab.name}' has no renderer.");
                    AddLevel(prefab, i, renderer, levels[i].screenRelativeTransitionHeight, meshList, materialList, lodList);
                }
            }
            else if (prefab.TryGetComponent(out MeshRenderer renderer))
            {
                AddLevel(prefab, 0, renderer, 0, meshList, materialList, lodList);
            }
            else
            {
                throw new InvalidOperationException($"Prefab '{prefab.name}' needs a LODGroup, or a MeshFilter with a MeshRenderer.");
            }
            meshes = meshList.ToArray();
            materials = materialList.ToArray();
            lods = lodList.ToArray();
            MeshAsset.ThrowIfInvalid(prefab.name, meshes, materials, lods, fadeWidth);
        }

        /// <summary>Replaces the asset's data with <see cref="Build"/> of <paramref name="prefab"/>; the asset keeps its fade width.</summary>
        internal static void BuildFromPrefab(GameObject prefab, MeshAsset asset)
        {
            Build(prefab, asset.FadeWidth, out Mesh[] meshes, out Material[] materials, out MeshLODInfo[] lods);
            asset.Initialize(meshes, materials, lods, asset.FadeWidth);
        }

        /// <summary>Whether the asset already holds exactly this data.</summary>
        internal static bool Holds(MeshAsset asset, Mesh[] meshes, Material[] materials, MeshLODInfo[] lods)
        {
            if (asset.lodCount != lods.Length || !SameObjects(asset.Meshes, meshes) || !SameObjects(asset.Materials, materials)) return false;
            for (int i = 0; i < lods.Length; ++i)
                if (!asset.GetLod(i).Equals(lods[i])) return false;
            return true;
        }

        static bool SameObjects<T>(ReadOnlySpan<T> a, T[] b) where T : UnityEngine.Object
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i]) return false;
            return true;
        }

        static void AddLevel(GameObject prefab, int level, Renderer renderer, float screenSize, List<Mesh> meshes, List<Material> materials, List<MeshLODInfo> lods)
        {
            if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                throw new InvalidOperationException($"LOD {level} renderer '{renderer.name}' of prefab '{prefab.name}' has no MeshFilter mesh.");
            Material[] shared = renderer.sharedMaterials;
            var slots = new int[shared.Length];
            for (int s = 0; s < shared.Length; ++s)
            {
                int index = materials.IndexOf(shared[s]);
                if (index < 0) { index = materials.Count; materials.Add(shared[s]); }
                slots[s] = index;
            }
            meshes.Add(filter.sharedMesh);
            lods.Add(new MeshLODInfo { screenSize = screenSize, materialSlot = slots });
        }

        /// <summary><c>Assets/InfinityMeshAssets/&lt;MeshName&gt;_&lt;first 8 GUID characters&gt;.asset</c>; built-in meshes follow the same rule.</summary>
        internal static string SingleLevelPath(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long _) || string.IsNullOrEmpty(guid) || guid.Length < 8)
                throw new InvalidOperationException($"Mesh '{mesh.name}' is not a saved asset; save it as an asset before creating a MeshAsset.");
            char[] fileName = mesh.name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < fileName.Length; ++i)
                if (Array.IndexOf(invalid, fileName[i]) >= 0) fileName[i] = '_';
            return $"{SingleLevelFolder}/{new string(fileName)}_{guid.Substring(0, 8)}.asset";
        }

        /// <summary>
        /// Returns the shared one-level asset of <paramref name="mesh"/>, creating it with <paramref name="defaults"/> as
        /// its materials when it does not exist. An existing file at the path that is not that asset is an error.
        /// </summary>
        internal static MeshAsset GetOrCreateSingleLevel(Mesh mesh, ReadOnlySpan<Material> defaults, out bool created)
        {
            string path = SingleLevelPath(mesh);
            MeshAsset existing = AssetDatabase.LoadAssetAtPath<MeshAsset>(path);
            if (existing != null)
            {
                if (existing.lodCount != 1 || existing.Meshes.Length != 1 || existing.Meshes[0] != mesh)
                    throw new InvalidOperationException($"'{path}' exists but is not the one-level MeshAsset of mesh '{mesh.name}'.");
                created = false;
                return existing;
            }
            if (File.Exists(path)) throw new InvalidOperationException($"'{path}' exists and is not a MeshAsset.");
            MeshAsset asset = ScriptableObject.CreateInstance<MeshAsset>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            asset.InitializeSingleLevel(mesh, defaults);
            if (!AssetDatabase.IsValidFolder(SingleLevelFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(SingleLevelFolder));
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }
    }
}
