using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Explicit, bounded source operation. Never invoked by import or script reload.
    internal static class SpazonEmissionMigration
    {
        const string k_Path = "Assets/Animation/Anim_MAT.mat";
        const string k_Guid = "bb7f4bc1163abbf4182cc43243c7c515";
        const string k_Old = "first \"_EmissionColor\" (string)\n\t\t\t\tsecond (8 8 8 1) (ColorRGBA)";
        const string k_New = "first \"_EmissionColor\" (string)\n\t\t\t\tsecond (0 0 0 1) (ColorRGBA)";

        [Serializable] sealed class Receipt
        {
            public string status, error, identity, beforeHash, afterHash, metaHash;
            public bool copyProof, secondSave, reloaded, noOp;
        }

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Emission migration requires idle EditMode.");
            if (AssetDatabase.AssetPathToGUID(k_Path) != k_Guid)
                throw new InvalidDataException("Spazon material identity does not match the approved target.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_Path);
            if (!material || EditorUtility.IsDirty(material))
                throw new InvalidOperationException("Target material is missing or has existing unsaved edits.");
            Color emission = material.GetColor("_EmissionColor");
            bool noOp = emission == new Color(0, 0, 0, 1);
            if (!noOp && emission != new Color(8, 8, 8, 1))
                throw new InvalidDataException("Unexpected emission value; refusing stale intent.");
            string run = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "spazon-emission-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(run);
            var receipt = new Receipt { status = "Prepared", noOp = noOp,
                identity = GlobalObjectId.GetGlobalObjectIdSlow(material).ToString(),
                beforeHash = Hash(k_Path), metaHash = Hash(k_Path + ".meta") };
            File.Copy(k_Path, Path.Combine(run, "before.mat"));
            File.Copy(k_Path + ".meta", Path.Combine(run, "before.mat.meta"));
            string scenes = SceneSnapshot();
            File.WriteAllText(Path.Combine(run, "scenes-before.txt"), scenes);
            Write(run, receipt);
            string copy = "Assets/InfinityEmissionProof-" + Guid.NewGuid().ToString("N") + ".mat";
            try
            {
                string before = Dump(k_Path, Path.Combine(run, "before.txt"));
                string expected = noOp ? before : before.Replace(k_Old, k_New);
                if ((!noOp && expected == before) || (noOp && !before.Contains(k_New)))
                    throw new InvalidDataException("Native emission field does not match the approved delta.");
                if (!noOp)
                {
                    File.Copy(k_Path, copy);
                    AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                    var candidate = AssetDatabase.LoadAssetAtPath<Material>(copy);
                    ApplyEmissionOnly(candidate, before);
                    EditorUtility.SetDirty(candidate);
                    AssetDatabase.SaveAssetIfDirty(candidate);
                    if (Dump(copy, Path.Combine(run, "copy-after.txt")) != expected)
                        throw new InvalidDataException("Copy proof changed non-emission native fields; source untouched.");
                    string copyHash = Hash(copy);
                    EditorUtility.SetDirty(candidate);
                    AssetDatabase.SaveAssetIfDirty(candidate);
                    if (Hash(copy) != copyHash) throw new InvalidDataException("Copy second save is not byte-identical.");
                    receipt.copyProof = true;
                    Write(run, receipt);
                    if (Hash(k_Path) != receipt.beforeHash || Hash(k_Path + ".meta") != receipt.metaHash ||
                        EditorUtility.IsDirty(material) || SceneSnapshot() != scenes)
                        throw new InvalidDataException("Source or scene changed while proving the copy.");
                    ApplyEmissionOnly(material, before);
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                receipt.afterHash = Hash(k_Path);
                if (Dump(k_Path, Path.Combine(run, "after.txt")) != expected)
                    throw new InvalidDataException("Saved source contains an unapproved native delta.");
                if (!noOp)
                {
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                receipt.secondSave = Hash(k_Path) == receipt.afterHash;
                AssetDatabase.ImportAsset(k_Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                material = AssetDatabase.LoadAssetAtPath<Material>(k_Path);
                receipt.reloaded = material && material.GetColor("_EmissionColor") == new Color(0, 0, 0, 1) &&
                    GlobalObjectId.GetGlobalObjectIdSlow(material).ToString() == receipt.identity;
                if (!receipt.secondSave || !receipt.reloaded || EditorUtility.IsDirty(material) ||
                    Hash(k_Path) != receipt.afterHash || Hash(k_Path + ".meta") != receipt.metaHash || SceneSnapshot() != scenes)
                    throw new InvalidDataException("Identity, reload, scene preservation or idempotence check failed.");
                if (noOp && receipt.beforeHash != receipt.afterHash)
                    throw new InvalidDataException("No-op changed source bytes.");
                File.Copy(k_Path, Path.Combine(run, "after.mat"));
                receipt.status = noOp ? "VerifiedNoOp" : "VerifiedPendingVisualAndIndependentCheck";
            }
            catch (Exception error) { receipt.status = "Failed"; receipt.error = error.ToString(); throw; }
            finally
            {
                Write(run, receipt);
                if (File.Exists(copy)) AssetDatabase.DeleteAsset(copy);
                Debug.Log("[InfinityRP] Spazon emission " + receipt.status + ": " + run);
            }
        }

        static void ApplyEmissionOnly(Material material, string nativeBefore)
        {
            material.SetColor("_EmissionColor", new Color(0, 0, 0, 1));
            // Import may materialize previously implicit shader defaults. This explicit migration
            // preserves the original serialized schema; the full native comparison remains the gate.
            var serialized = new SerializedObject(material);
            var floats = serialized.FindProperty("m_SavedProperties.m_Floats");
            for (int i = floats.arraySize - 1; i >= 0; i--)
            {
                string key = floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                bool generated = key == "_RefractionStrength" || key == "_SSSProfileIndex" ||
                    key == "_SSSThickness" || key == "_Subsurface";
                if (generated && !nativeBefore.Contains("first \"" + key + "\" (string)"))
                    floats.DeleteArrayElementAtIndex(i);
            }
            if (nativeBefore.Contains("stringTagMap  (map)\n\t\tsize 0 (int)"))
            {
                var tags = serialized.FindProperty("stringTagMap");
                for (int i = tags.arraySize - 1; i >= 0; i--)
                    if (tags.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "RenderType")
                        tags.DeleteArrayElementAtIndex(i);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static string SceneSnapshot()
        {
            string snapshot = SceneManager.GetActiveScene().path + "\n";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                snapshot += scene.path + "|" + scene.isDirty + "|" + (File.Exists(scene.path) ? Hash(scene.path) : "unsaved") + "\n";
            }
            return snapshot;
        }
        static string Dump(string input, string output)
        {
            string reader = Path.Combine(EditorApplication.applicationContentsPath, "Helpers/binary2text");
            var start = new System.Diagnostics.ProcessStartInfo(reader,
                "\"" + Path.GetFullPath(input) + "\" \"" + output + "\" -largebinaryhashonly")
                { UseShellExecute = false, CreateNoWindow = true };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                if (process == null) throw new IOException("Could not start native reader.");
                if (!process.WaitForExit(30000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Native reader timed out."); }
                if (process.ExitCode != 0) throw new IOException("Native reader failed.");
            }
            return File.ReadAllText(output).Replace("\r\n", "\n");
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static void Write(string run, Receipt receipt) => File.WriteAllText(Path.Combine(run, "result.json"), JsonUtility.ToJson(receipt, true));
    }
}
