using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using InfinityTech.Rendering.Pipeline;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Editor
{
    internal static class PipelineAssetConfiguration
    {
        [Serializable] sealed class ConfigurationEvidence
        {
            public string utc;
            public string asset;
            public string guid;
            public string beforeJson;
            public string afterJson;
            public List<string> changes = new List<string>();
        }

        [MenuItem("Infinity/Validation/Configure Selected Pipeline Compute Shaders")]
        public static void ConfigureSelected()
        {
            var asset = Selection.activeObject as InfinityRenderPipelineAsset;
            if (asset == null) throw new InvalidOperationException("Select an InfinityRenderPipelineAsset to configure its missing compute references explicitly.");
            if (EditorUtility.IsDirty(asset)) throw new InvalidOperationException("The selected asset has unsaved edits. Preserve those edits before explicit configuration.");
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("The selected asset must be a saved project asset.");
            string run = Path.Combine(Path.GetTempPath(), "InfinityRP-AssetConfiguration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
            Directory.CreateDirectory(run);
            File.Copy(path, Path.Combine(run, Path.GetFileName(path)), false);
            File.Copy(path + ".meta", Path.Combine(run, Path.GetFileName(path) + ".meta"), false);
            var evidence = new ConfigurationEvidence { utc = DateTime.UtcNow.ToString("O"), asset = path,
                guid = AssetDatabase.AssetPathToGUID(path), beforeJson = EditorJsonUtility.ToJson(asset) };
            var candidate = Object.Instantiate(asset);
            candidate.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                // Validate the complete candidate before changing any property on the user's asset.
                candidate.AssignDefaultComputeShadersExplicitly();
                FieldInfo[] fields = typeof(InfinityRenderPipelineAsset).GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(field => field.FieldType == typeof(ComputeShader)).ToArray();
                foreach (FieldInfo field in fields)
                {
                    var before = (ComputeShader)field.GetValue(asset);
                    var after = (ComputeShader)field.GetValue(candidate);
                    if (before == after) continue;
                    if (before != null) throw new InvalidOperationException("Explicit default configuration must preserve existing shader references: " + field.Name);
                    evidence.changes.Add(field.Name + " -> " + AssetDatabase.GetAssetPath(after));
                }
                if (evidence.changes.Count > 0)
                {
                    foreach (FieldInfo field in fields) field.SetValue(asset, field.GetValue(candidate));
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
                evidence.afterJson = EditorJsonUtility.ToJson(asset);
            }
            finally
            {
                Object.DestroyImmediate(candidate);
                File.WriteAllText(Path.Combine(run, "configuration.json"), JsonUtility.ToJson(evidence, true));
            }
            Debug.Log("[InfinityRP] Explicit pipeline configuration: " + evidence.changes.Count + " changes; " + run);
        }

        [MenuItem("Infinity/Validation/Configure Selected Pipeline Compute Shaders", true)]
        static bool CanConfigureSelected() => Selection.activeObject is InfinityRenderPipelineAsset;
    }
}
