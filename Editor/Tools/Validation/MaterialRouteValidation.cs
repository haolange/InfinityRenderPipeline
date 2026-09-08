using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor
{
    public static class MaterialRouteValidation
    {
        [Serializable]
        sealed class MaterialEvidence
        {
            public string path;
            public string guid;
            public long localId;
            public string shader;
            public int route;
            public int stage;
            public bool previouslyDirty;
            public bool dirtyAfter;
            public string beforeHash;
            public string afterHash;
            public string error;
            public string[] enabledShadingPasses;
            public bool routeMatches;
        }

        [Serializable]
        sealed class Report
        {
            public string status;
            public string unity;
            public int inspected;
            public List<MaterialEvidence> materials = new List<MaterialEvidence>();
        }

        [MenuItem("Infinity/Validation/Materials/Inspect Route States")]
        static void Inspect()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation"));
            string directory = Path.Combine(root, "material-route-inspect-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var report = new Report { status = "Inspecting", unity = Application.unityVersion };
            var dirty = new HashSet<string>(StringComparer.Ordinal);
            foreach (Material loaded in Resources.FindObjectsOfTypeAll<Material>())
                if (EditorUtility.IsDirty(loaded)) dirty.Add(AssetDatabase.GetAssetPath(loaded));

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            Array.Sort(guids, StringComparer.Ordinal);
            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (EditorUtility.DisplayCancelableProgressBar("Inspect material routes", path, (float)report.inspected / guids.Length))
                    {
                        report.status = "Cancelled";
                        return;
                    }
                    ++report.inspected;
                    string before = Hash(path);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || !material.HasProperty("_SurfaceRoute") || !material.HasProperty("_TranslucentStage"))
                        continue;
                    var item = new MaterialEvidence
                    {
                        path = path, guid = guid, shader = material.shader.name,
                        beforeHash = before, previouslyDirty = dirty.Contains(path)
                    };
                    report.materials.Add(item);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string _, out item.localId);
                    try
                    {
                        MaterialRouteUtility.Read(material, out item.route, out item.stage);
                        string expected = item.stage == 0 ? (item.route == 0 ? "GBufferPass" : "ForwardPass") : $"TranslucentT{item.stage - 1}Pass";
                        var enabled = new List<string>();
                        foreach (string pass in new[] { "GBufferPass", "ForwardPass", "TranslucentT0Pass", "TranslucentT1Pass", "TranslucentT2Pass" })
                            if (material.FindPass(pass) >= 0 && material.GetShaderPassEnabled(pass)) enabled.Add(pass);
                        item.enabledShadingPasses = enabled.ToArray();
                        item.routeMatches = enabled.Count == 1 && enabled[0] == expected;
                    }
                    catch (Exception error) { item.error = error.ToString(); }
                    item.afterHash = Hash(path);
                    item.dirtyAfter = EditorUtility.IsDirty(material);
                    if (item.beforeHash != item.afterHash)
                        throw new InvalidOperationException("Material bytes changed during read-only route inspection: " + path);
                }
                report.status = "Completed";
            }
            catch
            {
                report.status = "Failed";
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[InfinityRP] Material route inspection: " + directory);
            }
        }

        static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
