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
        /// With a LODGroup, LOD i is the MeshFilter mesh of the one renderer of LOD i at its
        /// <c>screenRelativeTransitionHeight</c> (one mesh may fill several levels). Without one, the root MeshFilter and
        /// MeshRenderer are one level at screen size 0. Materials are the de-duplicated union of the renderers' materials,
        /// and each level maps its submeshes to that union. The result is validated with <paramref name="fadeWidth"/>.
        /// A level with several renderers, or a renderer away from the prefab root's transform, throws: a level is one mesh
        /// drawn in the component's space, and dropping geometry or a transform would draw something else.
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
                var lodRenderers = new HashSet<Renderer>();
                foreach (LOD level in levels)
                    foreach (Renderer candidate in level.renderers ?? Array.Empty<Renderer>())
                        if (candidate != null) lodRenderers.Add(candidate);
                foreach (Renderer any in prefab.GetComponentsInChildren<Renderer>(true))
                    if (!lodRenderers.Contains(any))
                        throw new InvalidOperationException($"Renderer '{any.name}' of prefab '{prefab.name}' is outside every LOD; a MeshAsset would drop it.");
                for (int i = 0; i < levels.Length; ++i)
                {
                    Renderer renderer = null;
                    int count = 0;
                    foreach (Renderer candidate in levels[i].renderers ?? Array.Empty<Renderer>())
                        if (candidate != null) { renderer = candidate; ++count; }
                    if (count != 1) throw new InvalidOperationException($"LOD {i} of prefab '{prefab.name}' has {count} renderers; a MeshAsset level holds exactly one mesh.");
                    AddLevel(prefab, i, renderer, levels[i].screenRelativeTransitionHeight, meshList, materialList, lodList);
                }
            }
            else if (prefab.TryGetComponent(out MeshRenderer renderer))
            {
                int count = prefab.GetComponentsInChildren<Renderer>(true).Length;
                if (count != 1)
                    throw new InvalidOperationException($"Prefab '{prefab.name}' has {count} renderers and no LODGroup; a one-level MeshAsset holds the root renderer only.");
                AddLevel(prefab, 0, renderer, 0, meshList, materialList, lodList);
            }
            else
            {
                throw new InvalidOperationException($"Prefab '{prefab.name}' needs a LODGroup, or a MeshFilter with a MeshRenderer.");
            }
            meshes = meshList.ToArray();
            materials = materialList.ToArray();
            lods = lodList.ToArray();
            MeshAsset.ThrowIfInvalid(prefab, meshes, materials, lods, fadeWidth);
        }

        /// <summary>Replaces the asset's data with <see cref="Build"/> of <paramref name="prefab"/>; the asset keeps its fade width.</summary>
        internal static void BuildFromPrefab(GameObject prefab, MeshAsset asset)
        {
            Build(prefab, asset.FadeWidth, out Mesh[] meshes, out Material[] materials, out MeshLODInfo[] lods);
            asset.Initialize(meshes, materials, lods, asset.FadeWidth);
        }

        static void AddLevel(GameObject prefab, int level, Renderer renderer, float screenSize, List<Mesh> meshes, List<Material> materials, List<MeshLODInfo> lods)
        {
            if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                throw new InvalidOperationException($"LOD {level} renderer '{renderer.name}' of prefab '{prefab.name}' has no MeshFilter mesh.");
            Matrix4x4 relative = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int e = 0; e < 16; ++e)
                if (Mathf.Abs(relative[e] - Matrix4x4.identity[e]) > 1e-4f)
                    throw new InvalidOperationException($"LOD {level} renderer '{renderer.name}' of prefab '{prefab.name}' is not at the prefab root's transform; a MeshAsset level draws in the component's space.");
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

        /// <summary>
        /// <c>Assets/InfinityMeshAssets/&lt;MeshName&gt;_&lt;first 8 GUID characters&gt;_&lt;local file id, hex&gt;.asset</c>.
        /// GUID and local id identify the mesh inside its file, so several meshes of one model and the built-in meshes get
        /// distinct paths. Characters outside ASCII letters, digits, '-' and '_' become '_', the same on every OS.
        /// </summary>
        internal static string SingleLevelPath(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId) || string.IsNullOrEmpty(guid) || guid.Length < 8)
                throw new InvalidOperationException($"Mesh '{mesh.name}' is not a saved asset; save it as an asset before creating a MeshAsset.");
            char[] fileName = mesh.name.ToCharArray();
            for (int i = 0; i < fileName.Length; ++i)
            {
                char c = fileName[i];
                bool keep = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!keep) fileName[i] = '_';
            }
            return $"{SingleLevelFolder}/{new string(fileName)}_{guid.Substring(0, 8)}_{unchecked((ulong)localId):x}.asset";
        }

        /// <summary>Throws when <see cref="GetOrCreateSingleLevel"/> could not return the shared asset of <paramref name="mesh"/>.</summary>
        internal static void ThrowIfSingleLevelBlocked(Mesh mesh)
        {
            string path = SingleLevelPath(mesh);
            MeshAsset existing = AssetDatabase.LoadAssetAtPath<MeshAsset>(path);
            if (existing != null)
            {
                if (existing.lodCount != 1 || existing.Meshes.Length != 1 || existing.Meshes[0] != mesh)
                    throw new InvalidOperationException($"'{path}' exists but is not the one-level MeshAsset of mesh '{mesh.name}'.");
            }
            else if (File.Exists(path)) throw new InvalidOperationException($"'{path}' exists and is not a MeshAsset.");
        }

        /// <summary>
        /// Returns the shared one-level asset of <paramref name="mesh"/>, creating it with <paramref name="defaults"/> as
        /// its materials when it does not exist. An existing file at the path that is not that asset is an error.
        /// </summary>
        internal static MeshAsset GetOrCreateSingleLevel(Mesh mesh, ReadOnlySpan<Material> defaults, out bool created)
        {
            ThrowIfSingleLevelBlocked(mesh);
            string path = SingleLevelPath(mesh);
            MeshAsset existing = AssetDatabase.LoadAssetAtPath<MeshAsset>(path);
            if (existing != null)
            {
                created = false;
                return existing;
            }
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
