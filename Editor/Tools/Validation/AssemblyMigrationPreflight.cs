using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace InfinityTech.Rendering.Editor
{
    // Explicit, cancellable preflight. Never opens scenes, instantiates prefabs or changes identities.
    internal static partial class AssemblyMigrationPreflight
    {
        const int WorkerDeadlineSeconds = 120;
        const string Run = "/private/tmp/InfinityRP-validation-20260905T164500Z-T02";
        const string Package = "Packages/com.infinity.render-pipeline/";
        const string OldAssembly = "Unity.RenderPipelines.HighDefinition.";
        static PreflightSession s_Active;
        static readonly HashSet<string> s_NativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".asset", ".mat", ".prefab", ".unity", ".anim", ".controller", ".overrideController",
            ".playable", ".preset", ".rendertexture", ".cubemap", ".guiskin", ".flare",
            ".physicmaterial", ".physicsmaterial2d", ".terrainlayer", ".lighting"
        };
        static readonly Dictionary<string, string> s_AssemblyMapping = new Dictionary<string, string>
        {
            { OldAssembly + "Runtime", "Unity.RenderPipelines.Infinity.Runtime" },
            { OldAssembly + "Editor", "Unity.RenderPipelines.Infinity.Editor" },
            { OldAssembly + "Shaders", "Unity.RenderPipelines.Infinity.Shaders" }
        };

        [Serializable] internal sealed class Report
        {
            public string utc;
            public string unity;
            public string implementationHash;
            public string mode;
            public string status = "Running";
            public int workerDeadlineSeconds = WorkerDeadlineSeconds;
            public string workerKind;
            public string workerStartedUtc;
            public string workerDeadlineUtc;
            public int ownedReaderPid;
            public string stage;
            public string currentPath;
            public string phase;
            public long objectsRead;
            public int inputCount;
            public string sceneStateBefore;
            public string sceneStateAfter;
            public bool passed;
            public bool stageCreated;
            public bool stageRemoved;
            public List<AssetRecord> assets = new List<AssetRecord>();
            public List<string> coveredImporters = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable] internal sealed class AssetRecord
        {
            public string path;
            public string guid;
            public string importer;
            public string mainType;
            public string classification;
            public bool native;
            public bool selected;
            public bool needsObjectScan;
            public bool affected;
            public bool unchanged;
            public string sourceHash;
            public string metaHash;
            public string backup;
            public string semanticHash;
            public string initialCopyHash;
            public string roundtripHash;
            public string savedBytesHash;
            public string secondSaveBytesHash;
            public List<string> scriptDependencies = new List<string>();
            public List<string> objectKeys = new List<string>();
            public List<string> scriptReferences = new List<string>();
            public List<string> classIdentifiers = new List<string>();
            public List<string> managedReferences = new List<string>();
            public List<string> identityMappings = new List<string>();
            public List<string> types = new List<string>();
        }

        [Serializable] sealed class ProgressEvidence
        {
            public string utc;
            public string mode;
            public string phase;
            public string path;
            public long objectsRead;
            public string cancelFile;
            public string status;
            public int workerDeadlineSeconds;
            public string workerKind;
            public string workerStartedUtc;
            public string workerDeadlineUtc;
            public int ownedReaderPid;
        }

        [Serializable] sealed class SnapshotLine { public string value; }
        [Serializable] sealed class SmokeReceipt
        {
            public string implementationHash;
            public string reportPath;
            public string reportHash;
        }

        [MenuItem("Infinity/Validation/Migration/Run Assembly Preflight")]
        public static void RunPreflight()
        {
            if (s_Active != null) throw new InvalidOperationException("A preflight is already running; use its progress bar or cancel.request file to cancel it.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Assembly preflight requires an idle EditMode editor.");
            s_Active = new PreflightSession();
            s_Active.Start();
        }

        sealed partial class PreflightSession
        {
            readonly string m_Token = Guid.NewGuid().ToString("N");
            readonly string m_Output;
            readonly Report m_Report;
            readonly Dictionary<Object, int> m_LoadedSourceDirty = new Dictionary<Object, int>();
            readonly CancellationTokenSource m_Cancel = new CancellationTokenSource();
            Process m_ReaderProcess;
            Task<ReaderResult> m_ParseTask;
            Task<string> m_ReaderStdout;
            Task<string> m_ReaderStderr;
            CancellationTokenSource m_ParseCancel;
            Stopwatch m_WorkerClock;
            bool m_ReloadLocked;
            string m_FinalStatus;
            string m_LastDrainError;
            IEnumerator<int> m_Work;
            double m_LastProgress;
            bool m_Finishing;

            public PreflightSession(bool checkpoint = false)
            {
                m_IsCheckpoint = checkpoint;
                m_Output = Path.Combine(checkpoint ? "/Volumes/DataDisk/Projects/Unity/InfinityRP-Validation" : Run, "serialization-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + m_Token);
                string implementationHash = HashFile(Package + "Editor/Tools/Validation/AssemblyMigrationPreflight.cs");
                m_Report = new Report
                {
                    utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                    implementationHash = implementationHash,
                    mode = checkpoint || HasMatchingSmoke(implementationHash) ? "full" : "smoke",
                    stage = "Assets/InfinityAssemblyPreflight_" + m_Token,
                    sceneStateBefore = SceneState()
                };
            }

            public void Start()
            {
                try
                {
                    Directory.CreateDirectory(m_Output);
                    // Acquire before any owned work; domain reload cannot discard a draining session.
                    EditorApplication.LockReloadAssemblies();
                    m_ReloadLocked = true;
                    EditorApplication.update += Tick;
                    CompilationPipeline.compilationStarted += CancelForCompilation;
                    EditorApplication.wantsToQuit += CancelForQuit;
                    SaveProgress();
                    File.WriteAllText(Path.Combine(m_Output, "report.json"), JsonUtility.ToJson(m_Report, true));
                    m_Work = (m_IsCheckpoint ? CheckpointSteps() : Steps()).GetEnumerator();
                    Debug.Log("[InfinityRP] Assembly preflight " + m_Report.mode + " started: " + m_Output + "; cancel by creating cancel.request in that directory.");
                }
                catch (Exception error) { Finish("Failed", error); }
            }

            void Tick()
            {
                if (m_Finishing) { Drain(); return; }
                try
                {
                    if (EditorApplication.isCompiling) { Finish("CancelledBeforeReload", null); return; }
                    if (File.Exists(Path.Combine(m_Output, "cancel.request")) || EditorUtility.DisplayCancelableProgressBar(
                        "InfinityRP assembly preflight (" + m_Report.mode + ")", m_Report.phase + " | " + m_Report.currentPath + " | objects " + m_Report.objectsRead, CurrentProgress()))
                    { Finish("Cancelled", null); return; }
                    if (EditorApplication.isPlayingOrWillChangePlaymode || SceneState() != m_Report.sceneStateBefore)
                        throw new InvalidOperationException("Editor scene setup/dirty state or play mode changed. Preflight stopped without saving or restoring user state.");
                    double deadline = EditorApplication.timeSinceStartup + 0.008;
                    int count = 0;
                    do
                    {
                        if (!m_Work.MoveNext()) { Finish("Completed", null); return; }
                    }
                    while (++count < 16 && EditorApplication.timeSinceStartup < deadline);
                    if (EditorApplication.timeSinceStartup - m_LastProgress > 0.5) SaveProgress();
                }
                catch (Exception error) { Finish("Failed", error); }
            }

            float CurrentProgress()
            {
                int index = m_Report.assets.FindIndex(record => record.path == m_Report.currentPath);
                return m_Report.inputCount == 0 ? 0f : Mathf.Clamp01((index + 1f) / m_Report.inputCount);
            }

            void CancelForCompilation(object context) => Finish("CancelledBeforeReload", null);

            bool CancelForQuit()
            {
                // Cancel this quit attempt; the user can quit again after ownership has drained.
                Finish("CancelledBeforeQuit", null);
                Debug.Log("[InfinityRP] Quit cancelled while assembly preflight drains owned work. Quit again after its final report is written.");
                return false;
            }

            void SaveProgress()
            {
                m_LastProgress = EditorApplication.timeSinceStartup;
                File.WriteAllText(Path.Combine(m_Output, "progress.json"), JsonUtility.ToJson(new ProgressEvidence
                {
                    utc = DateTime.UtcNow.ToString("O"), mode = m_Report.mode, phase = m_Report.phase,
                    path = m_Report.currentPath, objectsRead = m_Report.objectsRead,
                    cancelFile = Path.Combine(m_Output, "cancel.request"), status = m_Report.status,
                    workerDeadlineSeconds = WorkerDeadlineSeconds, workerKind = m_Report.workerKind,
                    workerStartedUtc = m_Report.workerStartedUtc, workerDeadlineUtc = m_Report.workerDeadlineUtc,
                    ownedReaderPid = m_Report.ownedReaderPid
                }, true));
            }

            IEnumerable<int> Steps()
            {
                m_Report.phase = "Capture already-loaded source dirty state";
                foreach (ScriptableObject value in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                {
                    string path = AssetDatabase.GetAssetPath(value);
                    if (IsProjectInput(path)) m_LoadedSourceDirty[value] = EditorUtility.GetDirtyCount(value);
                    yield return 0;
                }
                m_Report.phase = "Classify inputs";
                string[] inputs = AssetDatabase.GetAllAssetPaths().Where(IsProjectInput).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                m_Report.inputCount = inputs.Length;
                foreach (string path in inputs)
                {
                    m_Report.currentPath = path;
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    if (importer == null) throw new InvalidDataException("No importer; assembly-bearing metadata cannot be ruled out.");
                    Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    var record = new AssetRecord
                    {
                        path = path, guid = AssetDatabase.AssetPathToGUID(path), importer = importer.GetType().FullName,
                        mainType = mainType?.AssemblyQualifiedName,
                        native = importer.GetType().Name == "NativeFormatImporter" || importer.GetType().Name == "PrefabImporter" ||
                            s_NativeExtensions.Contains(Path.GetExtension(path)) || IsYaml(path)
                    };
                    if (importer is MonoImporter || Path.GetExtension(path) == ".asmdef" || Path.GetExtension(path) == ".asmref") record.native = false;
                    m_Report.assets.Add(record);
                    // Dependency information is read without loading or instantiating scene/prefab objects.
                    record.scriptDependencies = AssetDatabase.GetDependencies(path, true).Where(dependency => dependency.EndsWith(".cs", StringComparison.Ordinal)).ToList();
                    record.needsObjectScan = record.scriptDependencies.Count > 0 ||
                        (mainType != null && (typeof(MonoBehaviour).IsAssignableFrom(mainType) || typeof(ScriptableObject).IsAssignableFrom(mainType)));
                    record.classification = record.native && !record.needsObjectScan
                        ? "Native built-in data: no MonoScript dependency and no managed host; preserve bytes, no reserialization"
                        : record.native ? "Native managed metadata: source/copy roundtrip required" : "Imported data/build input: importer metadata inspection";
                    yield return 0;
                }
                m_Report.coveredImporters = m_Report.assets.Select(record => record.importer).Distinct().OrderBy(value => value, StringComparer.Ordinal).ToList();
                // The first run for this exact implementation is a small smoke, never full acceptance.
                var smokePaths = new HashSet<string>(StringComparer.Ordinal)
                {
                    Package + "Runtime/Resources/InfinityDefaultVolumeProfile.asset",
                    "Assets/Profile/RenderPipelineAsset.asset"
                };
                AssetRecord material = m_Report.assets.FirstOrDefault(record => Path.GetExtension(record.path) == ".mat" && new FileInfo(record.path).Length < 65536);
                AssetRecord script = m_Report.assets.FirstOrDefault(record => record.importer.EndsWith(".MonoImporter", StringComparison.Ordinal) && new FileInfo(record.path).Length < 65536);
                if (material != null) smokePaths.Add(material.path);
                if (script != null) smokePaths.Add(script.path);
                foreach (AssetRecord record in m_Report.assets) record.selected = m_Report.mode == "full" || smokePaths.Contains(record.path);
                if (!m_Report.assets.Any(record => record.selected && record.native && record.needsObjectScan))
                    throw new InvalidDataException("Smoke selection has no small managed native asset; choose a safe representative before proceeding.");
                m_Report.phase = "Back up selected sources";
                foreach (AssetRecord record in m_Report.assets.Where(record => record.selected))
                {
                    m_Report.currentPath = record.path;
                    record.sourceHash = HashFile(record.path);
                    record.metaHash = HashFile(record.path + ".meta");
                    record.backup = Path.Combine(m_Output, "bytes", record.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(record.backup));
                    File.Copy(record.path, record.backup, false);
                    File.Copy(record.path + ".meta", record.backup + ".meta", false);
                    if (record.sourceHash != HashFile(record.backup) || record.metaHash != HashFile(record.backup + ".meta"))
                        throw new IOException("Input changed during backup.");
                    yield return 0;
                }
                m_Report.phase = "Read source metadata";
                foreach (AssetRecord record in m_Report.assets.Where(record => record.selected))
                {
                    m_Report.currentPath = record.path;
                    var importerValues = new List<string>();
                    AppendObject(AssetImporter.GetAtPath(record.path), importerValues, record, false, new HashSet<string>(), record.guid);
                    File.WriteAllText(Path.Combine(m_Output, Index(record) + "-importer.snapshot"), string.Join("\n", importerValues));
                    yield return 0;
                    if (record.native)
                    {
                        // Read the byte backup with the matching Editor's standalone native reader.
                        // No source scene, prefab, ScriptableObject or generated object is loaded here.
                        foreach (int step in ReaderSteps(record.backup, record.guid, record.guid, record, Index(record) + "-source")) yield return step;
                        record.semanticHash = m_ParseTask.Result.semanticHash;
                        record.needsObjectScan = record.affected;
                        record.classification = record.affected ? "Official native data contains mapped script/identity; canonical copy roundtrip required"
                            : "Official native object/field inventory has no affected script/identity; bytes preserved without reserialization";
                    }
                    else record.semanticHash = record.sourceHash;
                    if (!record.native && record.affected)
                        throw new InvalidDataException("Affected imported managed metadata requires an explicit importer migration.");
                }
                m_Report.phase = "Create owned staging";
                string stage = m_Report.stage;
                if (Directory.Exists(stage) || AssetDatabase.IsValidFolder(stage)) throw new IOException("Staging collision.");
                string guid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(stage));
                if (string.IsNullOrEmpty(guid) || AssetDatabase.GUIDToAssetPath(guid) != stage) throw new IOException("Unexpected staging creation result.");
                m_Report.stageCreated = true;
                using (var stream = new FileStream(stage + "/owner.txt", FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(m_Token);
                yield return 0;
                foreach (AssetRecord record in m_Report.assets.Where(record => record.selected && record.native && record.needsObjectScan))
                {
                    foreach (int step in CopyAssetSteps(record)) yield return step;
                }
                m_Report.phase = "Verify source and backup preservation";
                foreach (AssetRecord record in m_Report.assets.Where(record => record.selected))
                {
                    m_Report.currentPath = record.path;
                    VerifyPreservation(record);
                    yield return 0;
                }
            }

            IEnumerable<int> ReaderSteps(string input, string assetGuid, string sourceGuid, AssetRecord record, string prefix)
            {
                string reader = Path.Combine(EditorApplication.applicationContentsPath, "Helpers/binary2text");
                if (!File.Exists(reader)) throw new FileNotFoundException("The current editor's official binary2text reader is required.", reader);
                string dump = Path.Combine(m_Output, prefix + ".native.txt");
                string preciseDump = Path.Combine(m_Output, prefix + ".floatbits.txt");
                string inputHash = HashFile(input);
                foreach (int step in RunReaderProcess(reader, input, dump, false)) yield return step;
                foreach (int step in RunReaderProcess(reader, input, preciseDump, true)) yield return step;
                if (HashFile(input) != inputHash) throw new IOException("Reader input changed between metadata and precision captures.");
                m_ParseTask?.Dispose();
                m_ParseCancel?.Dispose();
                m_ParseCancel = CancellationTokenSource.CreateLinkedTokenSource(m_Cancel.Token);
                BeginWorker("Parse " + prefix);
                m_ParseCancel.CancelAfter(TimeSpan.FromSeconds(WorkerDeadlineSeconds));
                CancellationToken parseToken = m_ParseCancel.Token;
                m_ParseTask = Task.Run(() => ReadNativeDump(dump, preciseDump, assetGuid, sourceGuid, parseToken), parseToken);
                while (!m_ParseTask.IsCompleted) { CheckWorkerDeadline(); yield return 0; }
                CheckWorkerDeadline();
                ReaderResult parsed = m_ParseTask.GetAwaiter().GetResult();
                m_ParseCancel.Dispose(); m_ParseCancel = null;
                EndWorker();
                File.WriteAllText(Path.Combine(m_Output, prefix + ".summary.json"), JsonUtility.ToJson(parsed, true));
                m_Report.objectsRead += parsed.objectCount;
                record.objectKeys = parsed.objectIds;
                record.classIdentifiers = parsed.classIdentifiers;
                record.managedReferences = parsed.managedMetadata;
                foreach (string identifier in parsed.classIdentifiers) RecordIdentity(identifier, record);
                foreach (string script in parsed.scripts)
                {
                    record.scriptReferences.Add(script);
                    string[] parts = script.Split('|');
                    string scriptPath = AssetDatabase.GUIDToAssetPath(parts[0]);
                    MonoScript monoScript = string.IsNullOrEmpty(scriptPath) ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                    if (monoScript == null || monoScript.GetClass() == null)
                        throw new InvalidDataException("Unresolved MonoScript GUID/localID " + script + " in " + input);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(monoScript, out string guid, out long localId) || guid != parts[0] || localId.ToString() != parts[1])
                        throw new InvalidDataException("MonoScript local file identity mismatch: " + script);
                    record.types.Add(monoScript.GetClass().AssemblyQualifiedName);
                    RecordTypeMapping(monoScript.GetClass(), record, true);
                    yield return 0;
                }
                if (parsed.managedMetadata.Count > 0)
                    throw new InvalidDataException("Managed-reference registry metadata requires explicit review before migration; retained in " + prefix + ".native.txt");
            }

            IEnumerable<int> RunReaderProcess(string reader, string input, string dump, bool preciseFloats)
            {
                var start = new ProcessStartInfo(reader)
                {
                    Arguments = QuoteArgument(Path.GetFullPath(input)) + " " + QuoteArgument(dump) + " -largebinaryhashonly" + (preciseFloats ? " -hexfloat" : ""),
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                };
                BeginWorker("binary2text " + (preciseFloats ? "floatbits " : "metadata ") + input);
                m_ReaderProcess = Process.Start(start);
                if (m_ReaderProcess == null) throw new IOException("Could not start binary2text.");
                m_Report.ownedReaderPid = m_ReaderProcess.Id;
                m_ReaderStdout = m_ReaderProcess.StandardOutput.ReadToEndAsync();
                m_ReaderStderr = m_ReaderProcess.StandardError.ReadToEndAsync();
                SaveProgress();
                while (!m_ReaderProcess.HasExited || !m_ReaderStdout.IsCompleted || !m_ReaderStderr.IsCompleted)
                { CheckWorkerDeadline(); yield return 0; }
                CheckWorkerDeadline();
                int exitCode = m_ReaderProcess.ExitCode;
                File.WriteAllText(dump + ".reader.log", m_ReaderStdout.GetAwaiter().GetResult() + m_ReaderStderr.GetAwaiter().GetResult());
                ReleaseReader();
                EndWorker();
                if (exitCode != 0 || !File.Exists(dump)) throw new InvalidDataException("Official reader failed; see " + dump + ".reader.log");
            }

            string Index(AssetRecord record) => m_Report.assets.IndexOf(record).ToString("D5");

            void VerifyPreservation(AssetRecord record)
            {
                if (string.IsNullOrEmpty(record.backup)) return;
                record.unchanged = record.sourceHash == HashFile(record.path) && record.metaHash == HashFile(record.path + ".meta") &&
                    record.sourceHash == HashFile(record.backup) && record.metaHash == HashFile(record.backup + ".meta");
                if (!record.unchanged) throw new IOException("Source/backup bytes changed: " + record.path);

            }

            void BeginWorker(string kind)
            {
                DateTime started = DateTime.UtcNow;
                m_WorkerClock = Stopwatch.StartNew();
                m_Report.workerKind = kind;
                m_Report.workerStartedUtc = started.ToString("O");
                m_Report.workerDeadlineUtc = started.AddSeconds(WorkerDeadlineSeconds).ToString("O");
                SaveProgress();
            }

            void CheckWorkerDeadline()
            {
                if (m_WorkerClock.Elapsed.TotalSeconds >= WorkerDeadlineSeconds ||
                    (m_ParseCancel != null && m_ParseCancel.IsCancellationRequested && !m_Cancel.IsCancellationRequested))
                    throw new TimeoutException(m_Report.workerKind + " exceeded its " + WorkerDeadlineSeconds + " second deadline.");
                m_Cancel.Token.ThrowIfCancellationRequested();
            }

            void EndWorker()
            {
                m_WorkerClock = null;
                m_Report.workerKind = null;
                m_Report.workerStartedUtc = null;
                m_Report.workerDeadlineUtc = null;
            }

            void ReleaseReader()
            {
                // Call only after exit AND both redirected stream tasks have completed.
                m_ReaderProcess?.Dispose(); m_ReaderProcess = null;
                m_ReaderStdout?.Dispose(); m_ReaderStdout = null;
                m_ReaderStderr?.Dispose(); m_ReaderStderr = null;
                m_Report.ownedReaderPid = 0;
            }

            void Finish(string status, Exception error)
            {
                if (m_Finishing) return;
                m_Finishing = true;
                m_FinalStatus = status;
                if (error != null) m_Report.errors.Add(m_Report.currentPath + " | " + m_Report.phase + " | " + error);
                m_Report.status = "Cancelling";
                m_Cancel.Cancel();
                Drain();
            }

            void Drain()
            {
                try
                {
                    // Never Wait on the Editor thread or surrender ownership after a grace period.
                    if (m_ReaderProcess != null && !m_ReaderProcess.HasExited) m_ReaderProcess.Kill();
                    m_Report.status = "Draining";
                    if (EditorApplication.timeSinceStartup - m_LastProgress > 0.5) SaveProgress();
                    if ((m_ReaderProcess != null && !m_ReaderProcess.HasExited) ||
                        (m_ReaderStdout != null && !m_ReaderStdout.IsCompleted) ||
                        (m_ReaderStderr != null && !m_ReaderStderr.IsCompleted) ||
                        (m_ParseTask != null && !m_ParseTask.IsCompleted)) return;
                    ObserveWorkerFailure(m_ReaderStdout);
                    ObserveWorkerFailure(m_ReaderStderr);
                    ObserveWorkerFailure(m_ParseTask);
                    ReleaseReader();
                    m_ParseTask?.Dispose(); m_ParseTask = null;
                    m_ParseCancel?.Dispose(); m_ParseCancel = null;
                    EndWorker();
                }
                catch (Exception workerError)
                {
                    string message = "Owned reader drain | " + workerError;
                    if (message != m_LastDrainError) { m_Report.errors.Add(message); Debug.LogError(message); m_LastDrainError = message; }
                    return; // Keep update, reload lock, and s_Active until ownership is actually resolved.
                }
                CompleteFinish();
            }

            void ObserveWorkerFailure(Task task)
            {
                if (task != null && task.IsFaulted)
                    foreach (Exception failure in task.Exception.Flatten().InnerExceptions)
                        if (!(failure is OperationCanceledException)) m_Report.errors.Add("Owned worker | " + failure);
            }

            void CompleteFinish()
            {
                try { m_Work?.Dispose(); }
                catch (Exception disposeError) { m_Report.errors.Add("Dispose | " + disposeError); }
                try
                {
                    string sentinel = m_Report.stage + "/owner.txt";
                    if (m_Report.stageCreated && File.Exists(sentinel) && File.ReadAllText(sentinel) == m_Token)
                    {
                        m_Report.stageRemoved = AssetDatabase.DeleteAsset(m_Report.stage);
                        if (!m_Report.stageRemoved) m_Report.errors.Add("Owned staging cleanup failed.");
                    }
                    else if (m_Report.stageCreated) m_Report.errors.Add("Staging cleanup refused: owner sentinel mismatch.");
                }
                catch (Exception cleanupError) { m_Report.errors.Add("Cleanup | " + cleanupError); }
                // Cancellation also checks every captured input; no source is saved or automatically restored.
                foreach (AssetRecord record in m_Report.assets.Where(record => record.selected))
                {
                    try { VerifyPreservation(record); }
                    catch (Exception preservationError) { m_Report.errors.Add(preservationError.ToString()); }
                }
                try
                {
                    foreach (var pair in m_LoadedSourceDirty)
                        if (pair.Key == null || EditorUtility.GetDirtyCount(pair.Key) != pair.Value)
                            m_Report.errors.Add("Already-loaded source asset dirty state changed: " + (pair.Key == null ? "destroyed object" : AssetDatabase.GetAssetPath(pair.Key)));
                    m_Report.sceneStateAfter = SceneState();
                    if (m_Report.sceneStateAfter != m_Report.sceneStateBefore) m_Report.errors.Add("Editor scene setup/dirty state changed; preserved for explicit inspection, not overwritten.");
                    m_Report.status = m_FinalStatus;
                    m_Report.passed = m_FinalStatus == "Completed" && m_Report.errors.Count == 0;
                    string reportPath = Path.Combine(m_Output, "report.json");
                    File.WriteAllText(reportPath, JsonUtility.ToJson(m_Report, true));
                    if (m_Report.passed && m_Report.mode == "smoke")
                        File.WriteAllText(Path.Combine(Run, "smoke-pass-" + m_Report.implementationHash + "-" + m_Token + ".json"),
                            JsonUtility.ToJson(new SmokeReceipt { implementationHash = m_Report.implementationHash,
                                reportPath = reportPath, reportHash = HashFile(reportPath) }, true));
                    SaveProgress();
                    Debug.Log("[InfinityRP] Assembly preflight " + m_Report.mode + " " + m_FinalStatus + ", passed=" + m_Report.passed + ": " + m_Output);
                }
                finally
                {
                    EditorApplication.update -= Tick;
                    CompilationPipeline.compilationStarted -= CancelForCompilation;
                    EditorApplication.wantsToQuit -= CancelForQuit;
                    EditorUtility.ClearProgressBar();
                    m_Cancel.Dispose();
                    s_Active = null;
                    if (m_ReloadLocked) { m_ReloadLocked = false; EditorApplication.UnlockReloadAssemblies(); }
                }
            }
        }

        [Serializable] sealed class ReaderResult
        {
            public int objectCount;
            public string semanticHash;
            public List<string> objectIds = new List<string>();
            public List<string> scripts = new List<string>();
            public List<string> classIdentifiers = new List<string>();
            public List<string> managedMetadata = new List<string>();
        }

        static string QuoteArgument(string value)
        {
            if (value.IndexOfAny(new[] { '\"', '\r', '\n' }) >= 0) throw new ArgumentException("Unsupported control/quote character in reader path.");
            return "\"" + value + "\"";
        }

        static ReaderResult ReadNativeDump(string path, string precisePath, string assetGuid, string sourceGuid, CancellationToken cancellation)
        {
            // The official reader owns decoding, primitive formatting, blob hashes and GUID interpretation.
            // This small indexer retains complete raw dumps and only normalizes PPtr external-table indices.
            cancellation.ThrowIfCancellationRequested();
            var result = new ReaderResult();
            using var precise = new StreamReader(precisePath);
            string pendingPrecisionHeader = null;
            var externals = new Dictionary<int, string>();
            var objects = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var internalReferences = new HashSet<string>();
            var scripts = new SortedSet<string>(StringComparer.Ordinal);
            string objectId = null;
            int classId = 0;
            bool hasScript = false;
            using (var input = new StreamReader(path))
            using (SHA256 objectHash = SHA256.Create())
            {
                Action<string> append = value =>
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(value + "\n");
                    objectHash.TransformBlock(bytes, 0, bytes.Length, null, 0);
                };
                Action finishObject = () =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (objectId == null) return;
                    if (classId == 114 && !hasScript) throw new InvalidDataException("MonoBehaviour " + objectId + " has no non-null MonoScript reference.");
                    objectHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    // ID/reference metadata comes only from decimal output. The official -hexfloat mode
                    // leaves later integers in hex; use that second capture solely for exact float bit patterns.
                    string bits = ReadPrecisionObject(precise, ref pendingPrecisionHeader, objectId, classId, cancellation);
                    objects.Add(objectId, classId + "|" + Hex(objectHash.Hash) + "|" + bits);
                    objectHash.Initialize();
                };
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line) || line == "External References") continue;
                    Match external = Regex.Match(line, @"^path\((\d+)\): .* GUID: ([0-9a-fA-F]{32}) Type: ");
                    if (external.Success) { externals.Add(int.Parse(external.Groups[1].Value), external.Groups[2].Value.ToLowerInvariant()); continue; }
                    Match header = Regex.Match(line, @"^ID: (\S+) \(ClassID: (\d+)\)");
                    if (header.Success)
                    {
                        finishObject(); objectId = header.Groups[1].Value; classId = int.Parse(header.Groups[2].Value); hasScript = false;
                        continue;
                    }
                    if (objectId == null) throw new InvalidDataException("Unexpected official reader preamble: " + line);
                    if (line.Contains("(PPtr<"))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string fileLine = input.ReadLine();
                        cancellation.ThrowIfCancellationRequested();
                        string idLine = input.ReadLine();
                        Match file = Regex.Match(fileLine ?? "", @"^\s*m_FileID (\d+) \(int\)$");
                        Match local = Regex.Match(idLine ?? "", @"^\s*m_PathID (\S+) \(SInt64\)$");
                        if (!file.Success || !local.Success) throw new InvalidDataException("Unexpected PPtr shape at object " + objectId + ": " + line);
                        int fileId = int.Parse(file.Groups[1].Value);
                        string localId = local.Groups[1].Value;
                        string guid = fileId == 0 ? sourceGuid : externals.TryGetValue(fileId, out string externalGuid) ? externalGuid : throw new InvalidDataException("Unknown external table index " + fileId);
                        if (guid == assetGuid) guid = sourceGuid;
                        append(line.TrimEnd()); append("GUID " + (localId == "0" ? "null" : guid)); append("LocalID " + localId);
                        if (fileId == 0 && localId != "0") internalReferences.Add(localId);
                        if (line.TrimStart().StartsWith("m_Script ", StringComparison.Ordinal) && localId != "0")
                        { hasScript = true; scripts.Add(guid + "|" + localId); }
                        continue;
                    }
                    Match identifier = Regex.Match(line, "^\\s*m_EditorClassIdentifier \"(.*)\" \\(string\\)$");
                    if (identifier.Success && identifier.Groups[1].Value.Length > 0) result.classIdentifiers.Add(identifier.Groups[1].Value);
                    if (line.IndexOf("managedReference", StringComparison.OrdinalIgnoreCase) >= 0 || line.Contains("ReferencedManagedType"))
                        result.managedMetadata.Add(objectId + " | " + line.Trim());
                    append(line.TrimEnd());
                }
                finishObject();
            }
            if (objects.Count == 0) throw new InvalidDataException("Official reader produced no native objects.");
            if (pendingPrecisionHeader != null) throw new InvalidDataException("Precision capture contains unmatched trailing objects.");
            string trailingLine;
            while ((trailingLine = precise.ReadLine()) != null)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(trailingLine)) throw new InvalidDataException("Precision capture contains unmatched trailing objects.");
            }
            foreach (string reference in internalReferences)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!objects.ContainsKey(reference)) throw new InvalidDataException("Missing same-file object localID " + reference + " in " + path);
            }
            foreach (string key in objects.Keys)
            {
                cancellation.ThrowIfCancellationRequested();
                result.objectIds.Add(key);
            }
            result.objectCount = objects.Count;
            foreach (string script in scripts)
            {
                cancellation.ThrowIfCancellationRequested();
                result.scripts.Add(script);
            }
            string indexPath = path + ".objects.txt";
            using (var writer = new StreamWriter(indexPath, false, new UTF8Encoding(false)))
                foreach (var item in objects)
                {
                    cancellation.ThrowIfCancellationRequested();
                    writer.WriteLine(item.Key + "|" + item.Value);
                }
            result.semanticHash = HashFile(indexPath, cancellation);
            return result;
        }

        static string ReadPrecisionObject(StreamReader input, ref string pendingHeader, string decimalId, int classId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string line = pendingHeader;
            pendingHeader = null;
            while (line == null || !line.StartsWith("ID: ", StringComparison.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                line = input.ReadLine();
                if (line == null) throw new InvalidDataException("Precision capture ended before object " + decimalId);
            }
            Match header = Regex.Match(line, @"^ID: (\S+) \(ClassID: (\d+)\)");
            long numericId = long.Parse(decimalId, System.Globalization.CultureInfo.InvariantCulture);
            string hexId = unchecked((ulong)numericId).ToString("x");
            if (!header.Success || (header.Groups[1].Value != decimalId && header.Groups[1].Value != hexId) || int.Parse(header.Groups[2].Value) != classId)
                throw new InvalidDataException("Metadata/precision captures do not identify the same object: " + decimalId + " / " + line);
            using (SHA256 bits = SHA256.Create())
            {
                while ((line = input.ReadLine()) != null)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (line.StartsWith("ID: ", StringComparison.Ordinal)) { pendingHeader = line; break; }
                    foreach (Match value in Regex.Matches(line, @"0x([0-9a-fA-F]+)"))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        byte[] bytes = Encoding.ASCII.GetBytes(value.Groups[1].Value.ToLowerInvariant() + "\n");
                        bits.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    }
                }
                bits.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Hex(bits.Hash);
            }
        }

        static bool HasMatchingSmoke(string implementationHash)
        {
            if (!Directory.Exists(Run)) return false;
            // Old failed full inventories may be large; never parse them on the menu's main-thread entry.
            foreach (string receiptPath in Directory.GetFiles(Run, "smoke-pass-" + implementationHash + "-*.json"))
            {
                SmokeReceipt receipt = JsonUtility.FromJson<SmokeReceipt>(File.ReadAllText(receiptPath));
                if (receipt == null || receipt.implementationHash != implementationHash || !File.Exists(receipt.reportPath) ||
                    HashFile(receipt.reportPath) != receipt.reportHash) continue;
                Report report = JsonUtility.FromJson<Report>(File.ReadAllText(receipt.reportPath));
                if (report != null && report.passed && report.mode == "smoke" && report.implementationHash == implementationHash) return true;
            }
            return false;
        }

        static string SceneState()
        {
            var scenes = new List<string>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                scenes.Add(scene.handle + "|" + scene.path + "|" + scene.isLoaded + "|" + scene.isDirty + "|" + (scene == SceneManager.GetActiveScene()));
            }
            return string.Join("\n", scenes);
        }

        static bool IsProjectInput(string path)
        {
            if ((!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith(Package, StringComparison.Ordinal)) ||
                path.StartsWith("Assets/InfinityAssemblyPreflight_", StringComparison.Ordinal) || Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal)) return false;
            return File.Exists(path) && !AssetDatabase.IsValidFolder(path);
        }

        static bool IsYaml(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var header = new byte[5];
                return stream.Read(header, 0, header.Length) == 5 && Encoding.ASCII.GetString(header) == "%YAML";
            }
        }

        static string StableKey(Object value)
        {
            if (value is AssetImporter importer)
            {
                string guid = AssetDatabase.AssetPathToGUID(importer.assetPath);
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("Importer has no source GUID: " + importer.assetPath);
                return "ImporterMeta/" + guid + "/" + importer.GetType().FullName;
            }
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(value);
            if (id.identifierType == 0)
            {
                // Unity built-in resources can expose a GUID/local-file pair without a scene GlobalObjectId.
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId) && !string.IsNullOrEmpty(guid) && localId != 0)
                    return "AssetFile/" + guid + "/" + localId;
                throw new InvalidDataException("Object lacks a persistent global/local identity: " + value.name + " (" + value.GetType().FullName + ")");
            }
            return id.ToString();
        }

        static void AppendObject(Object value, List<string> values, AssetRecord record, bool includeJson, HashSet<string> visited, string ownerGuid, Queue<Object> pending = null)
        {
            string key = StableKey(value);
            if (!visited.Add(key)) return;
            if ((value is MonoBehaviour || value is ScriptableObject) && UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(value))
                throw new InvalidDataException("Missing managed-reference type on " + value.name);
            string type = value.GetType().AssemblyQualifiedName;
            record.objectKeys.Add(key);
            record.types.Add(type);
            RecordTypeMapping(value.GetType(), record, true);
            var metadata = new List<string>();
            using (var serialized = new SerializedObject(value))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    string fieldKey = key + " | " + property.propertyPath;
                    if (property.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        string typename = property.managedReferenceFullTypename ?? string.Empty;
                        if (typename.Length > 0 && property.managedReferenceValue == null)
                            throw new InvalidDataException("Unresolved managed reference " + fieldKey);
                        record.managedReferences.Add(fieldKey + " | " + typename);
                        metadata.Add(property.propertyPath + " | managed | " + typename);
                        RecordIdentity(typename, record);
                    }
                    else if (property.propertyType == SerializedPropertyType.String)
                    {
                        string field = property.stringValue ?? string.Empty;
                        if (property.propertyPath == "m_EditorClassIdentifier") record.classIdentifiers.Add(fieldKey + " | " + field);
                        if (field.Contains(OldAssembly)) RecordIdentity(field, record);
                        // Include parsed strings even when the imported object's bulk payload is not serialized.
                        metadata.Add(property.propertyPath + " | string | " + field);
                    }
                    else if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        Object referenced = property.objectReferenceValue;
                        if (referenced == null && property.objectReferenceEntityIdValue != default(EntityId))
                            throw new InvalidDataException("Unresolved object reference " + fieldKey);
                        string reference = referenced == null ? "null" : StableKey(referenced);
                        metadata.Add(property.propertyPath + " | reference | " + reference);
                        // Include embedded objects owned by this file (for example scene-local Volume profiles),
                        // without recursively loading unrelated external texture/material dependency graphs.
                        if (includeJson && referenced is ScriptableObject && GlobalObjectId.GetGlobalObjectIdSlow(referenced).assetGUID.ToString() == ownerGuid)
                            pending?.Enqueue(referenced);
                        if (property.propertyPath == "m_Script")
                        {
                            record.scriptReferences.Add(fieldKey + " | " + reference);
                            if (referenced is MonoScript script)
                            {
                                Type scriptType = script.GetClass();
                                if (scriptType == null) throw new InvalidDataException("MonoScript has no resolvable class at " + fieldKey);
                                RecordTypeMapping(scriptType, record, true);
                            }
                        }
                    }
                }
            }
            string json = includeJson ? EditorJsonUtility.ToJson(value) : string.Empty;
            json = Regex.Replace(json, "\"instanceID\":(-?\\d+)", match =>
            {
                int instance = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (instance == 0) return "\"object\":null";
                Object referenced = EditorUtility.EntityIdToObject((EntityId)instance);
                if (referenced == null) throw new InvalidDataException("Unresolved JSON object reference on " + key);
                return "\"object\":\"" + StableKey(referenced) + "\"";
            });
            metadata.Sort(StringComparer.Ordinal);
            values.Add(key + " | " + type + " | " + value.name + " | " + json + " | parsed: " + string.Join(";", metadata));
        }

        static void RecordTypeMapping(Type type, AssetRecord record, bool affectsAsset)
        {
            string assembly = type.Assembly.GetName().Name;
            if (!assembly.StartsWith(OldAssembly, StringComparison.Ordinal)) return;
            if (!s_AssemblyMapping.TryGetValue(assembly, out string target))
                throw new InvalidDataException("No approved mapping for " + type.AssemblyQualifiedName);
            record.identityMappings.Add(type.FullName + ", " + assembly + " -> " + type.FullName + ", " + target);
            if (affectsAsset) record.affected = true;
        }

        static void RecordIdentity(string identity, AssetRecord record)
        {
            if (!identity.Contains(OldAssembly)) return;
            Type resolved = Type.GetType(identity, false);
            if (resolved == null)
            {
                string assembly;
                string name;
                int separator = identity.IndexOf("::", StringComparison.Ordinal);
                if (separator >= 0) { assembly = identity.Substring(0, separator); name = identity.Substring(separator + 2); }
                else
                {
                    separator = identity.IndexOf(' ');
                    if (separator < 0) throw new InvalidDataException("Unrecognized persisted assembly identity: " + identity);
                    assembly = identity.Substring(0, separator); name = identity.Substring(separator + 1);
                }
                resolved = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value => value.GetName().Name == assembly)?.GetType(name, false);
            }
            if (resolved == null) throw new InvalidDataException("Unresolved persisted assembly identity: " + identity);
            RecordTypeMapping(resolved, record, true);
        }

        static string HashFile(string path, CancellationToken cancellation)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] buffer = new byte[65536];
                int count;
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    count = stream.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    hash.TransformBlock(buffer, 0, count, null, 0);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Hex(hash.Hash);
            }
        }

        static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return Hex(hash.ComputeHash(stream));
        }

        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
