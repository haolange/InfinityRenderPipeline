using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Editor;

namespace InfinityTech.Rendering.Editor.Migration
{
    /// <summary>
    /// Explicit 0.5 migration of MeshComponent geometry and mobility, deleted together with the bridge fields it reads.
    /// It runs only from its menu, never on load or reload. Preconditions are checked and the whole plan is read before
    /// the first write; serialized properties change through <see cref="SerializedObject"/> so nothing else moves.
    /// The verification procedure (backup, reopen, byte-identical second save, no-op) is in
    /// Docs/Verification-Playbook-0.5.0.md.
    /// </summary>
    internal static class MeshComponentMigration05
    {
        const string k_Title = "Migrate MeshComponents to MeshAsset (0.5)";
        // Serialized names of MeshComponent and MeshAsset that this migration reads or writes.
        const string k_Asset = "asset", k_Mobility = "mobility", k_MeshBridge = "meshAsset", k_MobilityBridge = "movebility", k_TargetBridge = "target";

        const string k_Delta =
            "Changes and saves serialized data:\n\n" +
            "- Every MeshComponent in the scenes and prefabs under Assets/ whose legacy 'meshAsset' Mesh is set and that has no " +
            "'asset' of its own: 'asset' = the shared one-level MeshAsset of that Mesh " +
            "(Assets/InfinityMeshAssets/<Mesh>_<GUID8>_<local id>.asset, created when missing), 'mobility' = legacy 'movebility', " +
            "legacy 'meshAsset' = None. On prefab instances only overridden values are written; an instance that overrides the " +
            "legacy Mesh with None gets 'asset' = None.\n" +
            "- Every MeshAsset under Assets/ is re-saved, which drops the removed 'Tree' data. One with a legacy source prefab " +
            "('target') is rebuilt from it first.\n" +
            "- The touched prefabs (sources before the files that contain their instances) and scenes are saved.\n\n" +
            "Capture a backup first (Tools/AssetBaseline.py capture).";

        sealed class Work
        {
            public MeshComponent component;
            public string owner;
            public string hierarchy;
            /// <summary>Index among the prefab root's MeshComponents (prefab work only).</summary>
            public int index;
            /// <summary>Set when this object owns its mesh value: write 'asset' and clear the bridge.</summary>
            public Mesh mesh;
            /// <summary>This object owns its mobility value: write 'mobility' from the bridge.</summary>
            public bool mobility;
            /// <summary>A prefab instance part that overrides the legacy mesh with None: write 'asset' = None.</summary>
            public bool clearAsset;
        }

        sealed class Report
        {
            public readonly List<string> migrated = new List<string>();
            public readonly List<string> created = new List<string>();
            public readonly List<string> rebuilt = new List<string>();
            public readonly List<string> unrebuildable = new List<string>();
            public readonly List<string> invalid = new List<string>();
            public readonly List<string> skipped = new List<string>();
            public readonly List<string> written = new List<string>();

            public void Written(string path, byte[] before)
            {
                byte[] after = File.ReadAllBytes(path);
                string change = before == null ? "created" : SameBytes(before, after) ? "bytes unchanged" : "bytes changed";
                written.Add($"{path} ({change})");
            }

            public string Text()
            {
                var text = new StringBuilder(k_Title).AppendLine();
                Append(text, "Components migrated", migrated);
                Append(text, "MeshAssets created", created);
                Append(text, "MeshAssets rebuilt from their source prefab", rebuilt);
                Append(text, "MeshAssets not rebuildable (data kept)", unrebuildable);
                Append(text, "MeshAssets that do not validate (fix by hand)", invalid);
                Append(text, "Not migrated", skipped);
                Append(text, "Files written", written);
                return text.ToString();
            }

            public string Summary() =>
                $"{migrated.Count} components migrated, {created.Count} MeshAssets created, {rebuilt.Count} rebuilt, " +
                $"{unrebuildable.Count} not rebuildable, {invalid.Count} invalid, {skipped.Count} not migrated, {written.Count} files written.\n\n" +
                "The full report is in the Console.";

            static void Append(StringBuilder text, string title, List<string> lines)
            {
                text.Append(title).Append(": ").Append(lines.Count).AppendLine();
                foreach (string line in lines) text.Append("  ").AppendLine(line);
            }
        }

        [MenuItem("Window/Infinity/Migrate MeshComponents to MeshAsset (0.5)", false, 21)]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(k_Title, "Exit Play mode before migrating.", "OK");
                return;
            }
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorUtility.DisplayDialog(k_Title, "Close Prefab Mode before migrating.", "OK");
                return;
            }

            var report = new Report();
            var blockers = new List<string>();
            var scenes = new List<(Scene scene, List<Work> work)>();
            var prefabs = new List<(string path, List<Work> work)>();
            PlanScenes(scenes, blockers, report);
            PlanUnloadedScenes(blockers);
            PlanPrefabs(prefabs, report);
            PlanSharedAssets(prefabs, scenes, blockers);
            if (blockers.Count > 0)
            {
                EditorUtility.DisplayDialog(k_Title, "Nothing was changed:\n\n" + string.Join("\n", blockers), "OK");
                return;
            }
            int componentCount = 0;
            foreach (var entry in scenes) componentCount += entry.work.Count;
            foreach (var entry in prefabs) componentCount += entry.work.Count;
            string plan = $"{componentCount} MeshComponents in {scenes.Count} scenes and {prefabs.Count} prefabs.\n\n";
            if (!EditorUtility.DisplayDialog(k_Title, plan + k_Delta, "Migrate", "Cancel")) return;

            try
            {
                Dictionary<Mesh, MeshAsset> shared = CreateSharedAssets(prefabs, scenes, report);
                ResaveMeshAssets(report);
                // A source prefab is saved before every file that holds one of its instances, so an instance override
                // written afterwards differs from the migrated source value and is recorded.
                foreach (var entry in prefabs) WritePrefab(entry.path, entry.work, shared, report);
                foreach (var entry in scenes) WriteScene(entry.scene, entry.work, shared, report);
            }
            finally
            {
                Debug.Log(report.Text());
                EditorUtility.DisplayDialog(k_Title, report.Summary(), "OK");
            }
        }

        #region Plan
        static void PlanScenes(List<(Scene, List<Work>)> scenes, List<string> blockers, Report report)
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                var work = new List<Work>();
                string owner = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (MeshComponent component in root.GetComponentsInChildren<MeshComponent>(true))
                        Plan(component, owner, -1, work, report);
                if (work.Count == 0) continue;
                if (string.IsNullOrEmpty(scene.path)) blockers.Add($"Scene '{scene.name}' is untitled; save it first.");
                else if (scene.isDirty) blockers.Add($"Scene '{scene.path}' has unsaved changes; save or discard them so the migration saves only its own delta.");
                scenes.Add((scene, work));
            }
        }

        /// <summary>
        /// Every scene under Assets/ that may hold MeshComponents must be loaded: an instance in an unloaded scene would
        /// silently inherit its migrated source prefab's values instead of its own legacy overrides.
        /// </summary>
        static void PlanUnloadedScenes(List<string> blockers)
        {
            string scriptGuid = null;
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript MeshComponent"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(MeshComponent)) scriptGuid = guid;
            }
            if (scriptGuid == null)
            {
                blockers.Add("The MeshComponent script asset was not found, so unloaded scenes cannot be checked.");
                return;
            }
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) loaded.Add(scene.path);
            }
            string[] paths = Array.ConvertAll(AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }), AssetDatabase.GUIDToAssetPath);
            Array.Sort(paths, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (loaded.Contains(path)) continue;
                string text = File.ReadAllText(path);
                if (!text.StartsWith("%YAML", StringComparison.Ordinal))
                    blockers.Add($"Scene '{path}' is not text-serialized and not loaded; open it (additively) before migrating.");
                else if (text.Contains("guid: " + scriptGuid))
                    blockers.Add($"Scene '{path}' has MeshComponents and is not loaded; open it (additively) before migrating.");
            }
        }

        /// <summary>Prefabs under Assets/ with work, each after the planned prefabs it contains instances of.</summary>
        static void PlanPrefabs(List<(string, List<Work>)> prefabs, Report report)
        {
            string[] paths = Array.ConvertAll(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }), AssetDatabase.GUIDToAssetPath);
            Array.Sort(paths, StringComparer.Ordinal);
            var planned = new Dictionary<string, List<Work>>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                MeshComponent[] components = root.GetComponentsInChildren<MeshComponent>(true);
                var work = new List<Work>();
                for (int i = 0; i < components.Length; ++i) Plan(components[i], path, i, work, report);
                if (work.Count > 0) planned.Add(path, work);
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths) Visit(path);

            void Visit(string path)
            {
                if (!planned.ContainsKey(path) || !visited.Add(path)) return;
                string[] dependencies = AssetDatabase.GetDependencies(path, true);
                Array.Sort(dependencies, StringComparer.Ordinal);
                foreach (string dependency in dependencies)
                    if (dependency != path) Visit(dependency);
                prefabs.Add((path, planned[path]));
            }
        }

        /// <summary>Every shared asset path must be free or already hold that asset before anything is written.</summary>
        static void PlanSharedAssets(List<(string path, List<Work> work)> prefabs, List<(Scene scene, List<Work> work)> scenes, List<string> blockers)
        {
            var checkedMeshes = new HashSet<Mesh>();
            var all = new List<Work>();
            foreach (var entry in prefabs) all.AddRange(entry.work);
            foreach (var entry in scenes) all.AddRange(entry.work);
            foreach (Work item in all)
            {
                if (item.mesh == null || !checkedMeshes.Add(item.mesh)) continue;
                try { MeshAssetBuilder.ThrowIfSingleLevelBlocked(item.mesh); }
                catch (InvalidOperationException error) { blockers.Add($"Mesh '{item.mesh.name}': {error.Message}"); }
            }
        }

        /// <summary>
        /// Selects a component whose effective legacy mesh is set. A prefab instance part writes only the values it
        /// overrides; the rest come from its source prefab, which is migrated as its own file.
        /// </summary>
        static void Plan(MeshComponent component, string owner, int index, List<Work> work, Report report)
        {
            using (var serialized = new SerializedObject(component))
            {
                SerializedProperty meshBridge = serialized.FindProperty(k_MeshBridge);
                SerializedProperty mobilityBridge = serialized.FindProperty(k_MobilityBridge);
                SerializedProperty assetProperty = serialized.FindProperty(k_Asset);
                string hierarchy = HierarchyPath(component.transform);
                var mesh = meshBridge.objectReferenceValue as Mesh;
                bool instance = PrefabUtility.IsPartOfPrefabInstance(component);
                bool ownsMobility = !instance || mobilityBridge.prefabOverride;
                if (mesh == null)
                {
                    if (instance && meshBridge.prefabOverride && assetProperty.objectReferenceValue == null && SourceHasLegacyMesh(component))
                    {
                        // The instance deliberately had no mesh; its migrated source will have an asset to override.
                        work.Add(new Work { component = component, owner = owner, hierarchy = hierarchy, index = index, clearAsset = true, mobility = ownsMobility });
                        return;
                    }
                    if (mobilityBridge.intValue == (int)EMeshMobility.Dynamic && serialized.FindProperty(k_Mobility).intValue != mobilityBridge.intValue
                        && assetProperty.objectReferenceValue == null)
                        report.skipped.Add($"{owner}: {hierarchy} has no legacy mesh; its legacy Dynamic mobility is not migrated (set Mobility by hand).");
                    return;
                }
                if (assetProperty.objectReferenceValue != null && (!instance || assetProperty.prefabOverride))
                {
                    report.skipped.Add($"{owner}: {hierarchy} already has MeshAsset '{AssetDatabase.GetAssetPath(assetProperty.objectReferenceValue)}'; " +
                        "its asset and mobility are left unchanged (the legacy fields are ignored).");
                    return;
                }
                bool ownsMesh = !instance || meshBridge.prefabOverride;
                if (!ownsMesh)
                {
                    string source = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(component));
                    if (!source.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        report.skipped.Add($"{owner}: {hierarchy} inherits its legacy mesh from '{source}', which is outside Assets/.");
                        return;
                    }
                    if (!ownsMobility) return;
                }
                work.Add(new Work { component = component, owner = owner, hierarchy = hierarchy, index = index, mesh = ownsMesh ? mesh : null, mobility = ownsMobility });
            }
        }

        static bool SourceHasLegacyMesh(MeshComponent component)
        {
            MeshComponent source = PrefabUtility.GetCorrespondingObjectFromSource(component);
            if (source == null) return false;
            using (var serialized = new SerializedObject(source)) return serialized.FindProperty(k_MeshBridge).objectReferenceValue != null;
        }

        static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
        #endregion

        #region Write
        /// <summary>
        /// A MeshAsset with a legacy source prefab is rebuilt with the current builder unless it already holds that data:
        /// the legacy builder wrote placeholder screen sizes (1 - 0.125 i, or 1 for a single renderer) and de-duplicated
        /// levels, which the lower-bound LOD chain cannot use. Every MeshAsset is then saved in the current schema.
        /// </summary>
        static void ResaveMeshAssets(Report report)
        {
            string[] paths = Array.ConvertAll(AssetDatabase.FindAssets("t:MeshAsset", new[] { "Assets" }), AssetDatabase.GUIDToAssetPath);
            Array.Sort(paths, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                MeshAsset asset = AssetDatabase.LoadAssetAtPath<MeshAsset>(path);
                if (asset == null) continue;
                GameObject source;
                using (var serialized = new SerializedObject(asset)) source = serialized.FindProperty(k_TargetBridge).objectReferenceValue as GameObject;
                bool built = false;
                if (source != null)
                {
                    try
                    {
                        MeshAssetBuilder.Build(source, asset.FadeWidth, out Mesh[] meshes, out Material[] materials, out MeshLODInfo[] lods);
                        built = true;
                        if (!MeshAssetBuilder.Holds(asset, meshes, materials, lods))
                        {
                            asset.Initialize(meshes, materials, lods, asset.FadeWidth);
                            report.rebuilt.Add($"{path} from '{AssetDatabase.GetAssetPath(source)}'");
                        }
                    }
                    catch (InvalidOperationException error) { report.unrebuildable.Add($"{path}: {error.Message}"); }
                }
                else if (asset.Meshes.Length != asset.lodCount)
                {
                    report.unrebuildable.Add($"{path}: {asset.Meshes.Length} meshes for {asset.lodCount} LOD levels and no source prefab.");
                }
                if (!built)
                {
                    try { asset.ThrowIfInvalid(); }
                    catch (InvalidOperationException error) { report.invalid.Add($"{path}: {error.Message}"); }
                }
                byte[] before = File.ReadAllBytes(path);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                report.Written(path, before);
            }
        }

        static Dictionary<Mesh, MeshAsset> CreateSharedAssets(List<(string path, List<Work> work)> prefabs, List<(Scene scene, List<Work> work)> scenes, Report report)
        {
            var shared = new Dictionary<Mesh, MeshAsset>();
            var failed = new HashSet<Mesh>();
            var all = new List<Work>();
            foreach (var entry in prefabs) all.AddRange(entry.work);
            foreach (var entry in scenes) all.AddRange(entry.work);
            foreach (Work item in all)
            {
                if (item.mesh == null || shared.ContainsKey(item.mesh) || failed.Contains(item.mesh)) continue;
                try
                {
                    // The first component that references the mesh supplies the asset's default materials.
                    MeshAsset asset = MeshAssetBuilder.GetOrCreateSingleLevel(item.mesh, item.component.Materials, out bool created);
                    shared.Add(item.mesh, asset);
                    if (created)
                    {
                        string path = AssetDatabase.GetAssetPath(asset);
                        report.created.Add(path);
                        report.Written(path, null);
                    }
                }
                catch (InvalidOperationException error)
                {
                    failed.Add(item.mesh);
                    report.skipped.Add($"Mesh '{item.mesh.name}': {error.Message}");
                }
            }
            return shared;
        }

        static void WriteScene(Scene scene, List<Work> work, Dictionary<Mesh, MeshAsset> shared, Report report)
        {
            bool touched = false;
            foreach (Work item in work) touched |= Write(item.component, item, shared, report);
            if (!touched) return;
            byte[] before = File.ReadAllBytes(scene.path);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"Scene '{scene.path}' was not saved.");
            report.Written(scene.path, before);
        }

        static void WritePrefab(string path, List<Work> work, Dictionary<Mesh, MeshAsset> shared, Report report)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MeshComponent[] components = root.GetComponentsInChildren<MeshComponent>(true);
                bool touched = false;
                foreach (Work item in work)
                {
                    MeshComponent component = item.index < components.Length ? components[item.index] : null;
                    if (component == null || HierarchyPath(component.transform) != item.hierarchy)
                        throw new InvalidOperationException($"Prefab '{path}' changed while migrating: {item.hierarchy} was not found.");
                    touched |= Write(component, item, shared, report);
                }
                if (!touched) return;
                byte[] before = File.ReadAllBytes(path);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new InvalidOperationException($"Prefab '{path}' was not saved.");
                report.Written(path, before);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Writes only the planned properties whose value differs, so a second run writes nothing; the mobility value is the
        /// object's legacy value. Returns whether anything changed.
        /// </summary>
        static bool Write(MeshComponent component, Work item, Dictionary<Mesh, MeshAsset> shared, Report report)
        {
            MeshAsset asset = null;
            if (item.mesh != null && !shared.TryGetValue(item.mesh, out asset)) return false;
            using (var serialized = new SerializedObject(component))
            {
                var change = new StringBuilder($"{item.owner}: {item.hierarchy}");
                bool changed = false;
                SerializedProperty assetProperty = serialized.FindProperty(k_Asset);
                SerializedProperty meshBridge = serialized.FindProperty(k_MeshBridge);
                if (asset != null && (assetProperty.objectReferenceValue != asset || meshBridge.objectReferenceValue != null))
                {
                    assetProperty.objectReferenceValue = asset;
                    meshBridge.objectReferenceValue = null;
                    change.Append(" asset=").Append(AssetDatabase.GetAssetPath(asset));
                    changed = true;
                }
                if (item.clearAsset && assetProperty.objectReferenceValue != null)
                {
                    assetProperty.objectReferenceValue = null;
                    change.Append(" asset=None (instance override)");
                    changed = true;
                }
                if (item.mobility)
                {
                    int value = serialized.FindProperty(k_MobilityBridge).intValue;
                    SerializedProperty mobility = serialized.FindProperty(k_Mobility);
                    if (mobility.intValue != value)
                    {
                        mobility.intValue = value;
                        change.Append(" mobility=").Append((EMeshMobility)value);
                        changed = true;
                    }
                }
                if (!changed) return false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                report.migrated.Add(change.ToString());
            }
            return true;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i]) return false;
            return true;
        }
        #endregion
    }
}
