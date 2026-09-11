using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using InfinityTech.Rendering.Pipeline;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Editor
{
    internal static class DefaultVolumeRegistryMigration
    {
        [Serializable] sealed class ComponentEvidence
        {
            public string identity, type, json;
        }
        [Serializable] sealed class Evidence
        {
            public string status, path, guid, beforeHash, afterHash, metaHash, error;
            public List<ComponentEvidence> existing = new List<ComponentEvidence>();
            public List<string> added = new List<string>();
        }

        [MenuItem("Window/Infinity/Migrate/Complete Default Volume Registry")]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Default Volume migration requires idle EditMode.");
            VolumeProfile profile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            if (profile == null)
                throw new InvalidOperationException("Active Infinity default profile is required.");
            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path) || EditorUtility.IsDirty(profile) || profile.components.Any(EditorUtility.IsDirty))
                throw new InvalidOperationException("Save or resolve existing default-profile edits before migration.");
            string run = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "default-volume-registry-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(run);
            var evidence = new Evidence { status = "Prepared", path = path, guid = AssetDatabase.AssetPathToGUID(path),
                beforeHash = Hash(path), metaHash = Hash(path + ".meta") };
            File.Copy(path, Path.Combine(run, "before.asset"));
            File.Copy(path + ".meta", Path.Combine(run, "before.asset.meta"));
            var original = profile.components.ToArray();
            foreach (VolumeComponent component in original)
                evidence.existing.Add(new ComponentEvidence { identity = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                    type = component.GetType().AssemblyQualifiedName, json = EditorJsonUtility.ToJson(component) });
            Write(run, evidence);
            try
            {
                foreach (Type type in DefaultVolumeProfileFactory.OptionalComponentTypes)
                {
                    if (profile.TryGet(type, out VolumeComponent existing)) continue;
                    VolumeComponent component = profile.Add(type, false);
                    AssetDatabase.AddObjectToAsset(component, profile);
                    evidence.added.Add(type.FullName);
                }
                if (evidence.added.Count > 0)
                {
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
                for (int i = 0; i < original.Length; i++)
                    if (profile.components[i] != original[i] ||
                        GlobalObjectId.GetGlobalObjectIdSlow(original[i]).ToString() != evidence.existing[i].identity ||
                        EditorJsonUtility.ToJson(original[i]) != evidence.existing[i].json)
                        throw new InvalidDataException("An existing default component changed during registry completion.");
                if (Hash(path + ".meta") != evidence.metaHash || AssetDatabase.AssetPathToGUID(path) != evidence.guid)
                    throw new InvalidDataException("Default profile identity changed.");
                if (!DefaultVolumeProfileFactory.HasRequiredDefaultComponents(profile))
                    throw new InvalidDataException("Default profile registry or required parameter contract is incomplete.");
                evidence.afterHash = Hash(path);
                if (evidence.added.Count > 0)
                {
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                    if (Hash(path) != evidence.afterHash) throw new InvalidDataException("Second save changed default profile bytes.");
                }
                else if (evidence.afterHash != evidence.beforeHash)
                    throw new InvalidDataException("No-op changed default profile bytes.");
                evidence.status = evidence.added.Count == 0 ? "VerifiedNoOp" : "VerifiedPendingIndependentNativeCheck";
            }
            catch (Exception exception)
            {
                evidence.status = "Failed";
                evidence.error = exception.ToString();
                throw;
            }
            finally { Write(run, evidence); Debug.Log("[InfinityRP] Default Volume registry " + evidence.status + ": " + run); }
        }
        static void Write(string run, Evidence evidence) => File.WriteAllText(Path.Combine(run, "result.json"), JsonUtility.ToJson(evidence, true));
        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
