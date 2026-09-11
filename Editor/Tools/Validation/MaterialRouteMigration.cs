using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    internal static class MaterialRouteMigration
    {
        [Serializable] sealed class Receipt
        {
            public string status, path, identity, beforeHash, afterHash, metaHash, error;
            public bool copyProof, secondSave, reloaded, noOp;
            public bool effectiveAllowLocking;
            public string nativeSchemaDelta;
        }

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Material migration requires idle EditMode.");
            Migrate("Assets/Material/M_BoxC_Instance.mat", "6f29f76a4da334f40a1a43cdc5375d4f",
                "2aed5c8fb83a5f114190b582538290c510a6230b2a6b0d623774d7e7b9bb44b9");
            Migrate("Assets/Material/M_MaskB.mat", "8ad67d0be8ec8c4499e0708bf3a297f7",
                "35ab58d64ee42b135a8ae8c2bcf6738d4a1cab06bae559aea04e33560fd4daeb");
        }

        static void Migrate(string path, string guid, string inspectedHash)
        {
            if (AssetDatabase.AssetPathToGUID(path) != guid)
                throw new InvalidDataException("Material GUID no longer matches inspection: " + path);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material || EditorUtility.IsDirty(material))
                throw new InvalidOperationException("Material is missing or has unsaved user edits: " + path);
            MaterialRouteUtility.Read(material, out int route, out int stage);
            if (route != 0 || stage != 0)
                throw new InvalidDataException("Material route differs from approved inspection: " + path);
            bool noOp = IsCanonical(material);
            if (!noOp && Hash(path) != inspectedHash)
                throw new InvalidDataException("Material source changed after inspection: " + path);

            string run = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "material-route-migration-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(run);
            var receipt = new Receipt { status = "Prepared", path = path, noOp = noOp,
                identity = GlobalObjectId.GetGlobalObjectIdSlow(material).ToString(),
                beforeHash = Hash(path), metaHash = Hash(path + ".meta") };
            SerializedProperty locking = new SerializedObject(material).FindProperty("m_AllowLocking");
            if (locking == null) throw new InvalidDataException("Cannot inspect current native material locking authority.");
            receipt.effectiveAllowLocking = locking.boolValue;
            File.Copy(path, Path.Combine(run, "before.mat"));
            File.Copy(path + ".meta", Path.Combine(run, "before.mat.meta"));
            string scenes = SceneSnapshot();
            File.WriteAllText(Path.Combine(run, "scenes-before.txt"), scenes);
            Write(run, receipt);
            string copy = "Assets/InfinityRouteProof-" + Guid.NewGuid().ToString("N") + ".mat";
            try
            {
                string before = Dump(path, Path.Combine(run, "before.txt"));
                File.WriteAllText(Path.Combine(run, "effective-before.json"), EditorJsonUtility.ToJson(material, true));
                if (!before.Contains("\tm_AllowLocking "))
                    receipt.nativeSchemaDelta = "Serialize previously implicit m_AllowLocking=" + (receipt.effectiveAllowLocking ? "1" : "0") + "; effective value preserved.";
                Write(run, receipt);
                if (!noOp)
                {
                    File.Copy(path, copy);
                    AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                    Material candidate = AssetDatabase.LoadAssetAtPath<Material>(copy);
                    Apply(candidate, before, receipt.effectiveAllowLocking);
                    EditorUtility.SetDirty(candidate);
                    AssetDatabase.SaveAssetIfDirty(candidate);
                    string afterCopy = Dump(copy, Path.Combine(run, "copy-after.txt"));
                    string comparableCopy = afterCopy;
                    if (receipt.nativeSchemaDelta != null)
                        comparableCopy = comparableCopy.Replace("\tm_AllowLocking " + (receipt.effectiveAllowLocking ? "1" : "0") + " (bool)\n", "");
                    if (!IsCanonical(candidate) || WithoutPassList(before) != WithoutPassList(comparableCopy))
                        throw new InvalidDataException("Copy changed fields outside disabledShaderPasses; source untouched.");
                    string copyHash = Hash(copy);
                    EditorUtility.SetDirty(candidate);
                    AssetDatabase.SaveAssetIfDirty(candidate);
                    if (Hash(copy) != copyHash) throw new InvalidDataException("Copy second save changed bytes.");
                    receipt.copyProof = true;
                    Write(run, receipt);
                    if (Hash(path) != receipt.beforeHash || Hash(path + ".meta") != receipt.metaHash ||
                        EditorUtility.IsDirty(material) || SceneSnapshot() != scenes)
                        throw new InvalidDataException("Source or scene changed while proving the copy.");
                    Apply(material, before, receipt.effectiveAllowLocking);
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    if (Dump(path, Path.Combine(run, "source-after.txt")) != afterCopy)
                        throw new InvalidDataException("Source native result differs from proven copy.");
                }
                receipt.afterHash = Hash(path);
                if (!noOp)
                {
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                receipt.secondSave = Hash(path) == receipt.afterHash;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                receipt.reloaded = material && IsCanonical(material) && GlobalObjectId.GetGlobalObjectIdSlow(material).ToString() == receipt.identity;
                if (!receipt.secondSave || !receipt.reloaded || EditorUtility.IsDirty(material) ||
                    Hash(path) != receipt.afterHash || Hash(path + ".meta") != receipt.metaHash || SceneSnapshot() != scenes)
                    throw new InvalidDataException("Identity, reload, scene preservation or idempotence failed.");
                if (noOp && receipt.beforeHash != receipt.afterHash)
                    throw new InvalidDataException("Independent no-op changed bytes.");
                File.Copy(path, Path.Combine(run, "after.mat"));
                Dump(path, Path.Combine(run, "after.txt"));
                receipt.status = noOp ? "VerifiedNoOp" : "VerifiedPendingIndependentCheck";
            }
            catch (Exception error) { receipt.status = "Failed"; receipt.error = error.ToString(); throw; }
            finally
            {
                Write(run, receipt);
                if (File.Exists(copy)) AssetDatabase.DeleteAsset(copy);
                Debug.Log("[InfinityRP] Material route migration " + receipt.status + ": " + run);
            }
        }

        static void Apply(Material material, string before, bool effectiveAllowLocking)
        {
            MaterialRouteUtility.ApplyPassState(material);
            var serialized = new SerializedObject(material);
            SerializedProperty locking = serialized.FindProperty("m_AllowLocking");
            if (locking == null) throw new InvalidDataException("Material locking field is unavailable on the target.");
            locking.boolValue = effectiveAllowLocking;
            foreach (string group in new[] { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" })
            {
                SerializedProperty values = serialized.FindProperty("m_SavedProperties." + group);
                if (values == null) continue;
                for (int i = values.arraySize - 1; i >= 0; --i)
                {
                    string key = values.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                    if (!before.Contains("first \"" + key + "\" (string)")) values.DeleteArrayElementAtIndex(i);
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool IsCanonical(Material material)
        {
            foreach (string pass in new[] { "GBufferPass", "DepthPass", "ShadowPass", "ShadowCaster", "MotionPass" })
                if (material.FindPass(pass) >= 0 && !material.GetShaderPassEnabled(pass)) return false;
            foreach (string pass in new[] { "ForwardPass", "TranslucentDepthPass", "TranslucentT0Pass", "TranslucentT1Pass", "TranslucentT2Pass" })
                if (material.FindPass(pass) >= 0 && material.GetShaderPassEnabled(pass)) return false;
            return true;
        }

        static string WithoutPassList(string native)
        {
            var pattern = new Regex(@"(?ms)^\tdisabledShaderPasses  \(vector\)\n.*?(?=^\t[^\t\r\n])");
            if (pattern.Matches(native).Count != 1) throw new InvalidDataException("Missing or ambiguous native pass list.");
            return pattern.Replace(native, "");
        }

        static string SceneSnapshot()
        {
            string snapshot = SceneManager.GetActiveScene().path + "\n";
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                snapshot += scene.path + "|" + scene.isDirty + "|" + (File.Exists(scene.path) ? Hash(scene.path) : "unsaved") + "\n";
            }
            return snapshot;
        }

        static string Dump(string input, string output)
        {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(EditorApplication.applicationContentsPath, "Helpers/binary2text"),
                "\"" + Path.GetFullPath(input) + "\" \"" + output + "\" -largebinaryhashonly") { UseShellExecute = false, CreateNoWindow = true };
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
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        static void Write(string run, Receipt receipt) => File.WriteAllText(Path.Combine(run, "result.json"), JsonUtility.ToJson(receipt, true));
    }
}
