using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.Feature;
using InfinityTech.Rendering.Pipeline;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Editor
{
    internal static partial class AtmosphereEffectiveProfileDiagnosis
    {
        const string Source = "Assets/Profile/AtmosphericalProfile.asset";
        const string SourceGuid = "1cc7578f54406fb43a8bbfd029e3de1b";
        const string SourceHash = "01dee56c77bad81614468a723d3e41297d84c0456a6e6bbc1ad49866fe313c2e";
        const string SourceMetaHash = "9c7cdd3ff5bac36c96943dccf6f8222541fad2a05cbac8458831fe65ecd00167";
        const string Root = "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation";
        static bool s_Running;
        [Serializable] sealed class Report
        {
            public string utc, status, error, stage, stageGuid, copyGuid, sourceIdentity, pipelineIdentity;
            public string sourceBeforeHash, sourceAfterHash, sourceBeforeMetaHash, sourceAfterMetaHash;
            public string pipelineBeforeHash, pipelineAfterHash, pipelineBeforeMetaHash, pipelineAfterMetaHash;
            public string sourceNativeHash, sourceNativeFloatBitsHash, canonicalNativeHash, canonicalNativeFloatBitsHash;
            public string sourceProfileSnapshotHash, sourceParameterSnapshotHash, beforeSceneState, afterSceneState;
            public int sourceDirtyCountBefore, sourceDirtyCountAfter, pipelineDirtyCountBefore, pipelineDirtyCountAfter;
            public bool currentPipelineUsesSource, sourceValid, importedCopyEffectiveEquals, canonicalCopyEffectiveEquals;
            public bool secondSaveByteIdentical, sourcePreserved, stageDeleted;
            public string savedCopyHash, secondSaveHash;
        }

        public static void Diagnose()
        {
            if (s_Running) throw new InvalidOperationException("Atmosphere diagnosis is already running.");
            s_Running = true;
            string token = Guid.NewGuid().ToString("N");
            string stage = "Assets/InfinityRP_AtmosphereDiagnosis_" + token;
            string directory = Path.Combine(Root, "atmosphere-effective-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + token);
            string sentinel = stage + "/owner.txt";
            bool owned = false;
            AtmosphericalProfile source = null;
            InfinityRenderPipelineAsset pipeline = null;
            string sourceSnapshot = null, parameterSnapshot = null, pipelineSnapshot = null, pipelinePath = null, pipelineHash = null, pipelineMetaHash = null;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), status = "Preparing", stage = stage };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Use the existing idle EditMode editor for this explicit diagnosis.");
                if (!Directory.Exists(Root) || Directory.Exists(directory)) throw new IOException("Persistent root is missing or unique output exists.");
                Directory.CreateDirectory(directory);
                report.beforeSceneState = SceneState();
                report.sourceBeforeHash = Hash(Source); report.sourceBeforeMetaHash = Hash(Source + ".meta");
                if (report.sourceBeforeHash != SourceHash || report.sourceBeforeMetaHash != SourceMetaHash || AssetDatabase.AssetPathToGUID(Source) != SourceGuid)
                    throw new InvalidOperationException("The diagnosed source bytes/metadata/GUID changed.");
                source = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(Source);
                if (source == null) throw new InvalidOperationException("Source AtmosphericalProfile did not load as its current type.");
                pipeline = GraphicsSettings.currentRenderPipeline as InfinityRenderPipelineAsset;
                if (pipeline == null) throw new InvalidOperationException("The current rendering pipeline is not InfinityRP.");
                report.sourceIdentity = Identity(source); report.pipelineIdentity = Identity(pipeline);
                pipelinePath = AssetDatabase.GetAssetPath(pipeline);
                pipelineHash = Hash(pipelinePath); pipelineMetaHash = Hash(pipelinePath + ".meta");
                report.pipelineBeforeHash = pipelineHash; report.pipelineBeforeMetaHash = pipelineMetaHash;
                report.currentPipelineUsesSource = pipeline.atmosphericalProfile == source;
                report.sourceDirtyCountBefore = EditorUtility.GetDirtyCount(source);
                report.pipelineDirtyCountBefore = EditorUtility.GetDirtyCount(pipeline);
                sourceSnapshot = Snapshot(source); pipelineSnapshot = Snapshot(pipeline);
                parameterSnapshot = Parameters(source);
                Write(directory, "source-effective.txt", sourceSnapshot);
                Write(directory, "source-parameters.txt", parameterSnapshot);
                Write(directory, "pipeline-effective.txt", pipelineSnapshot);
                report.sourceProfileSnapshotHash = TextHash(sourceSnapshot); report.sourceParameterSnapshotHash = TextHash(parameterSnapshot);
                AtmosphereParameter.FromProfile(source).ThrowIfInvalid(); report.sourceValid = true;
                if (!report.currentPipelineUsesSource) throw new InvalidOperationException("Current RP Asset does not reference this source; no active-runtime equivalence claim is allowed.");
                WriteBytes(Path.Combine(directory, "source.before.asset"), File.ReadAllBytes(Source));
                WriteBytes(Path.Combine(directory, "source.before.asset.meta"), File.ReadAllBytes(Source + ".meta"));
                if (Hash(Path.Combine(directory, "source.before.asset")) != SourceHash || Hash(Path.Combine(directory, "source.before.asset.meta")) != SourceMetaHash)
                    throw new IOException("Fresh source backup verification failed.");
                Native(directory, "source-before", Path.Combine(directory, "source.before.asset"));
                report.sourceNativeHash = Hash(Path.Combine(directory, "source-before.native.txt"));
                report.sourceNativeFloatBitsHash = Hash(Path.Combine(directory, "source-before.floatbits.txt"));
                Write(directory, "intent.json", JsonUtility.ToJson(report, true));
                CheckCancel("Source captured; before creating owned copy", 0.2f);
                if (Directory.Exists(stage) || File.Exists(stage + ".meta") || AssetDatabase.IsValidFolder(stage)) throw new IOException("Staging collision.");
                report.stageGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(stage));
                if (string.IsNullOrEmpty(report.stageGuid) || AssetDatabase.AssetPathToGUID(stage) != report.stageGuid)
                    throw new IOException("Owned stage creation failed.");
                WriteBytes(sentinel, Encoding.UTF8.GetBytes(token)); owned = true;
                string copy = stage + "/AtmosphericalProfile.asset";
                File.Copy(Path.Combine(directory, "source.before.asset"), copy, false);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                report.copyGuid = AssetDatabase.AssetPathToGUID(copy);
                if (string.IsNullOrEmpty(report.copyGuid) || report.copyGuid == SourceGuid) throw new IOException("Copy GUID did not establish independent ownership.");
                AtmosphericalProfile imported = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(copy);
                CompareEffective(imported, sourceSnapshot, parameterSnapshot, directory, "copy-import");
                report.importedCopyEffectiveEquals = true;
                RequireSource();
                CheckCancel("Imported copy verified; before canonical copy save", 0.5f);
                AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                imported = AssetDatabase.LoadAssetAtPath<AtmosphericalProfile>(copy);
                CompareEffective(imported, sourceSnapshot, parameterSnapshot, directory, "copy-canonical");
                report.canonicalCopyEffectiveEquals = true;
                Native(directory, "copy-canonical", copy);
                report.canonicalNativeHash = Hash(Path.Combine(directory, "copy-canonical.native.txt"));
                report.canonicalNativeFloatBitsHash = Hash(Path.Combine(directory, "copy-canonical.floatbits.txt"));
                WriteBytes(Path.Combine(directory, "canonical-copy.asset"), File.ReadAllBytes(copy));
                WriteBytes(Path.Combine(directory, "canonical-copy.asset.meta"), File.ReadAllBytes(copy + ".meta"));
                report.savedCopyHash = Hash(copy);
                CheckCancel("Canonical effective values verified; before second copy save", 0.8f);
                AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);
                report.secondSaveHash = Hash(copy);
                if (report.savedCopyHash != report.secondSaveHash) throw new InvalidOperationException("Canonical copy second save is not byte-identical.");
                report.secondSaveByteIdentical = true;
                RequireSource();
                report.status = "EffectiveValuesEqualPendingTerra";
            }
            catch (OperationCanceledException error) { report.status = "Cancelled"; report.error = error.ToString(); }
            catch (Exception error) { report.status = "Failed"; report.error = error.ToString(); }
            finally
            {
                try
                {
                    if (owned)
                    {
                        if (AssetDatabase.AssetPathToGUID(stage) != report.stageGuid || !File.Exists(sentinel) || File.ReadAllText(sentinel) != token)
                            throw new IOException("Stage ownership changed; refusing cleanup.");
                        if (!AssetDatabase.DeleteAsset(stage) || Directory.Exists(stage) || File.Exists(stage + ".meta")) throw new IOException("Owned stage cleanup failed.");
                        report.stageDeleted = true;
                    }
                    if (source != null && sourceSnapshot != null) RequireSource();
                    report.afterSceneState = SceneState();
                    if (report.beforeSceneState != null && report.afterSceneState != report.beforeSceneState) throw new InvalidOperationException("Scene setup or dirty state changed.");
                }
                catch (Exception error) { report.status = "Failed"; report.error += "\nCleanup/preservation: " + error; }
                EditorUtility.ClearProgressBar(); s_Running = false;
                if (Directory.Exists(directory)) Write(directory, "report.json", JsonUtility.ToJson(report, true));
                Debug.Log("[InfinityRP] Atmosphere effective-profile diagnosis " + report.status + ": " + directory);
            }
            void RequireSource()
            {
                report.sourceAfterHash = Hash(Source); report.sourceAfterMetaHash = Hash(Source + ".meta");
                report.pipelineAfterHash = Hash(pipelinePath); report.pipelineAfterMetaHash = Hash(pipelinePath + ".meta");
                report.sourceDirtyCountAfter = EditorUtility.GetDirtyCount(source); report.pipelineDirtyCountAfter = EditorUtility.GetDirtyCount(pipeline);
                if (report.sourceAfterHash != report.sourceBeforeHash || report.sourceAfterMetaHash != report.sourceBeforeMetaHash ||
                    report.sourceDirtyCountBefore != report.sourceDirtyCountAfter || report.pipelineDirtyCountBefore != report.pipelineDirtyCountAfter ||
                    Snapshot(source) != sourceSnapshot || Parameters(source) != parameterSnapshot || Snapshot(pipeline) != pipelineSnapshot ||
                    Hash(pipelinePath) != pipelineHash || Hash(pipelinePath + ".meta") != pipelineMetaHash ||
                    GraphicsSettings.currentRenderPipeline != pipeline || pipeline.atmosphericalProfile != source || SceneState() != report.beforeSceneState)
                    throw new InvalidOperationException("Source effective values, reference authority, metadata, bytes, dirty state or scene state changed.");
                report.sourcePreserved = true;
            }
        }

        static void CompareEffective(AtmosphericalProfile copy, string expected, string parameters, string directory, string prefix)
        {
            if (copy == null) throw new InvalidOperationException("Isolated profile did not load.");
            string actual = Snapshot(copy), derived = Parameters(copy);
            Write(directory, prefix + "-effective.txt", actual); Write(directory, prefix + "-parameters.txt", derived);
            AtmosphereParameter.FromProfile(copy).ThrowIfInvalid();
            if (actual != expected || derived != parameters) throw new InvalidOperationException("Current effective profile/FromProfile values differ at " + prefix);
        }
        static string Parameters(AtmosphericalProfile profile)
        {
            object parameters = AtmosphereParameter.FromProfile(profile);
            var text = new StringBuilder();
            foreach (FieldInfo field in typeof(AtmosphereParameter).GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(field => field.Name, StringComparer.Ordinal))
                text.AppendLine(field.Name + "|" + Value(field.GetValue(parameters)));
            foreach (PropertyInfo property in typeof(AtmosphereParameter).GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(property => property.Name, StringComparer.Ordinal))
                text.AppendLine(property.Name + "|" + Value(property.GetValue(parameters)));
            return text.ToString();
        }
        static string Snapshot(Object value)
        {
            var text = new StringBuilder(value.GetType().AssemblyQualifiedName + "\n");
            using (var serialized = new SerializedObject(value))
            {
                var property = serialized.GetIterator(); bool enter = true;
                while (property.Next(enter))
                {
                    enter = property.propertyType != SerializedPropertyType.ObjectReference && property.propertyType != SerializedPropertyType.String;
                    text.Append(property.propertyPath).Append('|').Append(property.type).Append('|');
                    switch (property.propertyType)
                    {
                        case SerializedPropertyType.Generic: text.Append("container"); break;
                        case SerializedPropertyType.ObjectReference:
                            if (property.objectReferenceValue != null) text.Append(Identity(property.objectReferenceValue));
                            else if (property.objectReferenceEntityIdValue == default(EntityId)) text.Append("null");
                            else throw new InvalidOperationException("Unresolved effective reference " + property.propertyPath);
                            break;
                        case SerializedPropertyType.Float: text.Append(Value(property.doubleValue)); break;
                        case SerializedPropertyType.Integer: text.Append(property.longValue); break;
                        case SerializedPropertyType.Enum:
                        case SerializedPropertyType.ArraySize:
                        case SerializedPropertyType.LayerMask: text.Append(property.intValue); break;
                        case SerializedPropertyType.Boolean: text.Append(property.boolValue); break;
                        case SerializedPropertyType.String: text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(property.stringValue))); break;
                        case SerializedPropertyType.Color: text.Append(Value(property.colorValue)); enter = false; break;
                        case SerializedPropertyType.Hash128: text.Append(property.hash128Value.ToString()); break;
                        default: throw new InvalidOperationException("Unsupported effective field " + property.propertyPath + ": " + property.propertyType);
                    }
                    text.AppendLine();
                }
            }
            return text.ToString();
        }
        static string Value(object value)
        {
            if (value is float single) return BitConverter.ToString(BitConverter.GetBytes(single));
            if (value is double number) return BitConverter.ToString(BitConverter.GetBytes(number));
            if (value is Color color) return Value(color.r) + "," + Value(color.g) + "," + Value(color.b) + "," + Value(color.a);
            if (value is Vector4 vector) return Value(vector.x) + "," + Value(vector.y) + "," + Value(vector.z) + "," + Value(vector.w);
            if (value is bool || value is int) return Convert.ToString(value, CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Unsupported derived atmosphere value: " + value?.GetType());
        }
        static string Identity(Object value)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId) || string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("Effective reference lacks a persistent identity: " + value.name);
            return guid + ":" + localId;
        }
        static string SceneState() => string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(index =>
        { Scene scene = SceneManager.GetSceneAt(index); return scene.path + "|" + scene.handle + "|" + scene.isLoaded + "|" + scene.isDirty + "|active=" + (scene == SceneManager.GetActiveScene()); }));
        static void CheckCancel(string message, float progress)
        { if (EditorUtility.DisplayCancelableProgressBar("Atmosphere effective profile diagnosis", message, progress)) throw new OperationCanceledException("Explicit atmosphere diagnosis cancelled."); }
        static void Native(string directory, string prefix, string input)
        {
            MethodInfo reader = typeof(KnownSceneReferenceRepairs).GetMethod("RunReader", BindingFlags.Static | BindingFlags.NonPublic);
            reader.Invoke(null, new object[] { input, Path.Combine(directory, prefix + ".native.txt"), false });
            reader.Invoke(null, new object[] { input, Path.Combine(directory, prefix + ".floatbits.txt"), true });
        }
        static void Write(string directory, string name, string text) => WriteBytes(Path.Combine(directory, name), Encoding.UTF8.GetBytes(text));
        static void WriteBytes(string path, byte[] bytes)
        { using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); } }
        static string TextHash(string value) { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static string Hash(string path) { using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return Hex(hash.ComputeHash(file)); }
        static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
    }
}
