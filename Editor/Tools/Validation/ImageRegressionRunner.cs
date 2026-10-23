using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfinityTech.Component;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    // Captures normal Game frames. State survives Play-mode reloads; restoration follows capture retirement.
    [InitializeOnLoad]
    internal static class ImageRegressionRunner
    {
        [Serializable]
        internal sealed class Case
        {
            public string name, scenePath;
            public string camera = "Camera";
            public int width = 640, height = 400, warmupFrames = 90;
            public float pixelThreshold = 4.0f / 255.0f;
            public float maxRmse = 0.01f, maxOutlierFraction = 0.005f;
        }

        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class MarkerPose { public string path; public Quaternion rotation; }
        [Serializable]
        sealed class State
        {
            public bool record, stop;
            public string runDirectory, baselineDirectory, captureDirectory, persistenceError, phase = "Open", outcome = "Running";
            public long phaseStarted;
            public int index;
            public List<Case> cases = new List<Case>();
            public List<CaseResult> results = new List<CaseResult>();
            public SavedScene[] originalScenes;
            public string[] selection;
            public List<MarkerPose> markers = new List<MarkerPose>();
        }

        [Serializable]
        internal sealed class CaseResult
        {
            public string name, status, detail;
            public FImageComparison comparison;
        }

        [Serializable]
        sealed class CaptureBuffer
        {
            public string semantic, file, format, error, colorSpace, transferEncoding, sha256;
            public int width, height;
        }
        [Serializable]
        sealed class CaptureEvidence
        {
            public string status, error;
            public int capturedFrames, outstanding;
            public List<CaptureBuffer> buffers = new List<CaptureBuffer>();
        }

        const string MenuRoot = "Window/Infinity/Image Regression/";
        const string Semantic = "DisplayColor";
        const string StateKey = "InfinityRP.ImageRegression.StatePath";
        const string RecoveryKey = "InfinityRP.ImageRegression.Recovery";
        const string ReportKey = "InfinityRP.ImageRegression.LastReport";
        static string StatePath => SessionState.GetString(StateKey, "");
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        internal static string BaselineDirectory => Path.Combine(ProjectRoot, "ImageBaselines", SystemInfo.graphicsDeviceType.ToString());
        static readonly string[] s_Names = { "AreaLights", "LocalLights", "Decal", "Translucent", "Shadows", "Cookies", "ReflectionProbes", "ScreenSpace" };
        internal static List<Case> DefaultCases() => s_Names.Select(name => new Case
        {
            name = name, scenePath = "Assets/Scene/Validation/Validation_" + name + ".unity"
        }).ToList();

        static State s_State;
        static bool s_ReloadLocked;
        static RenderCaptureSession s_OwnedCapture;

        static ImageRegressionRunner()
        {
            string path = StatePath;
            string recovery = SessionState.GetString(RecoveryKey, "");
            if (string.IsNullOrEmpty(recovery) && (string.IsNullOrEmpty(path) || !File.Exists(path))) return;
            try
            {
                s_State = JsonUtility.FromJson<State>(string.IsNullOrEmpty(recovery) ? File.ReadAllText(path) : recovery);
                if (s_State == null || s_State.originalScenes == null || s_State.cases == null)
                    throw new InvalidDataException("Incomplete regression recovery state.");
                EditorApplication.update += Tick;
            }
            catch (Exception error)
            {
                // Keep the recovery intent and original bytes for explicit inspection.
                Debug.LogError("[InfinityRP][ImageRegression] Cannot resume preserved state " + path + ": " + error.Message);
            }
        }

        [MenuItem(MenuRoot + "Record Baselines", false, 80)]
        static void RecordBaselines() => Start(true);
        [MenuItem(MenuRoot + "Run Comparison", false, 81)]
        static void RunComparison() => Start(false);
        [MenuItem(MenuRoot + "Cancel", false, 82)]
        internal static void Cancel()
        {
            if (s_State == null) return;
            s_State.stop = true;
            s_State.outcome = "Cancelled";
            OwnedCapture()?.Cancel();
            Phase("Drain");
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        [MenuItem(MenuRoot + "Open Baseline Folder", false, 83)]
        static void OpenBaselines()
        {
            Directory.CreateDirectory(BaselineDirectory);
            EditorUtility.RevealInFinder(BaselineDirectory);
        }

        static void Start(bool record) => Start(record, DefaultCases(),
            Path.Combine(ProjectRoot, "intermediate", "image-regression", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")), BaselineDirectory);

        // The same runner serves explicit fixture/output ownership in validation and the normal menu entry points.
        internal static void Start(bool record, List<Case> cases, string runDirectory, string baselineDirectory)
        {
            if (s_State != null || (!string.IsNullOrEmpty(StatePath) && File.Exists(StatePath)))
                throw new InvalidOperationException("A regression run or preserved recovery state already exists.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || UnityEngine.FrameDebugger.enabled)
                throw new InvalidOperationException("Leave Play mode, compilation and Frame Debugger before starting.");
            if (RenderCaptureService.current != null && !RenderCaptureService.current.Finished)
                throw new InvalidOperationException("The existing capture must retire before regression starts.");
            if (InfinityDebugDisplaySettings.current.debugView != EDebugView.None)
                throw new InvalidOperationException("Normal image regression requires Debug View None.");
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            if (setup.Length == 0 || setup.Any(s => string.IsNullOrEmpty(s.path)) ||
                Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save the original scenes before running image regression; no scene is saved automatically.");
            ValidateCases(cases);
            runDirectory = OwnedRunDirectory(runDirectory);
            baselineDirectory = Path.GetFullPath(baselineDirectory);
            string relative = Path.GetRelativePath(ProjectRoot, baselineDirectory).Replace('\\', '/');
            if (!(relative.StartsWith("ImageBaselines/", StringComparison.Ordinal) || relative.StartsWith("intermediate/", StringComparison.Ordinal)))
                throw new ArgumentException("Baselines must be inside the project's ImageBaselines or intermediate directory.");
            if (Directory.Exists(runDirectory)) throw new ArgumentException("Use a new regression run directory.");
            var state = new State
            {
                record = record, runDirectory = runDirectory, baselineDirectory = baselineDirectory,
                cases = cases.Select(c => JsonUtility.FromJson<Case>(JsonUtility.ToJson(c))).ToList(),
                originalScenes = setup.Select(s => new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray(),
                selection = Selection.objects.Select(o => GlobalObjectId.GetGlobalObjectIdSlow(o).ToString()).ToArray(),
                phaseStarted = DateTime.UtcNow.Ticks
            };
            Directory.CreateDirectory(runDirectory);
            s_State = state;
            SessionState.SetString(StateKey, Path.Combine(runDirectory, "state.json"));
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            try { SaveRequired(); }
            catch
            {
                // No scene or GPU producer has started; leave no orphan active run.
                s_State = null;
                SessionState.EraseString(StateKey);
                SessionState.EraseString(RecoveryKey);
                EditorApplication.update -= Tick;
                throw;
            }
        }

        internal static void ValidateCases(List<Case> cases)
        {
            if (cases == null || cases.Count == 0) throw new ArgumentException("At least one explicit regression case is required.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Case c in cases)
            {
                if (c == null || !System.Text.RegularExpressions.Regex.IsMatch(c.name ?? "", "^[A-Za-z0-9_-]+$") || !names.Add(c.name))
                    throw new ArgumentException("Case names must be unique safe file names.");
                if (string.IsNullOrEmpty(c.scenePath) || !c.scenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !c.scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || c.scenePath.Contains("..") || c.scenePath.Contains('\\') ||
                    !File.Exists(Path.Combine(ProjectRoot, c.scenePath)))
                    throw new ArgumentException("Create the fixture explicitly before regression: " + c?.scenePath);
                if (string.IsNullOrEmpty(c.camera) || c.width < 1 || c.height < 1 || (long)c.width * c.height > int.MaxValue || c.warmupFrames < 0 ||
                    !Finite(c.pixelThreshold) || c.pixelThreshold < 0 || !Finite(c.maxRmse) || c.maxRmse < 0 ||
                    !Finite(c.maxOutlierFraction) || c.maxOutlierFraction < 0 || c.maxOutlierFraction > 1)
                    throw new ArgumentException("Invalid capture dimensions, camera or comparison thresholds: " + c.name);
            }
        }

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static string OwnedRunDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) throw new ArgumentException("An absolute run directory is required.");
            string full = Path.GetFullPath(path);
            string parent = Path.GetFullPath(Path.Combine(ProjectRoot, "intermediate")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.Ordinal)) throw new ArgumentException("Run outputs belong inside the project's intermediate directory.");
            return full;
        }

        internal static object Status() => new
        {
            active = s_State != null, phase = s_State?.phase, outcome = s_State?.outcome,
            index = s_State?.index, total = s_State?.cases.Count, report = SessionState.GetString(ReportKey, ""),
            persistenceError = s_State?.persistenceError,
            outstanding = OwnedCapture()?.OutstandingCount ?? 0
        };

        static void Tick()
        {
            if (s_State == null) { EditorApplication.update -= Tick; return; }
            try
            {
                if (s_State.phase == "NeedsAttention") return;
                if (s_State.phase != "Restore" && s_State.phase != "Drain" && DateTime.UtcNow.Ticks - s_State.phaseStarted > TimeSpan.FromMinutes(3).Ticks)
                    throw new TimeoutException("Regression phase timed out: " + s_State.phase);
                Step();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (s_State.phase == "Restore" || s_State.phase == "ExitPlay")
                {
                    s_State.outcome = "NeedsAttention";
                    s_State.phase = "NeedsAttention";
                    Save();
                    EditorApplication.update -= Tick;
                    return;
                }
                Record("Error", error.Message, default);
                s_State.stop = true;
                OwnedCapture()?.Cancel();
                Phase("Drain");
            }
        }

        static void Step()
        {
            if (s_State.phase == "Drain")
            {
                RenderCaptureSession owned = OwnedCapture();
                owned?.Pump();
                RepaintGame();
                if (owned != null && !owned.Finished) return;
                UnlockReload();
                // A later independent capture may have replaced the public slot. Never stop its Play session.
                if (RenderCaptureService.current != null && RenderCaptureService.current != owned && !RenderCaptureService.current.Finished) return;
                Phase("ExitPlay");
                EditorApplication.isPlaying = false;
                return;
            }
            if (s_State.phase == "ExitPlay")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                s_State.index++;
                Phase(s_State.stop || s_State.index >= s_State.cases.Count ? "Restore" : "Open");
                return;
            }
            if (s_State.phase == "Restore")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                    throw new InvalidOperationException("Regression recovery preserves unexpected unsaved scene edits.");
                EditorSceneManager.RestoreSceneManagerSetup(s_State.originalScenes.Select(s => new SceneSetup
                { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                Selection.objects = s_State.selection.Select(id => GlobalObjectId.TryParse(id, out var parsed)
                    ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) : null).Where(o => o != null).ToArray();
                Finish();
                return;
            }
            Case current = s_State.cases[s_State.index];
            switch (s_State.phase)
            {
                case "Open":
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                        throw new InvalidOperationException("Regression will not discard unsaved scene edits.");
                    Scene scene = EditorSceneManager.OpenScene(current.scenePath, OpenSceneMode.Single);
                    s_State.markers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LivenessMarker>(true))
                        .Select(m => new MarkerPose { path = TransformPath(m.transform), rotation = m.transform.rotation }).ToList();
                    Phase("EnterPlay");
                    if (s_State.phase != "EnterPlay") return;
                    EditorApplication.isPlaying = true;
                    return;
                case "EnterPlay":
                    if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                    if (RenderCaptureService.current != null && !RenderCaptureService.current.Finished) return;
                    Camera camera = Camera.allCameras.Single(c => c.name == current.camera && c.gameObject.scene.path == current.scenePath);
                    camera.pixelRect = new Rect(0, 0, current.width, current.height);
                    foreach (LivenessMarker marker in camera.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LivenessMarker>(true)))
                    {
                        MarkerPose pose = s_State.markers.Single(p => p.path == TransformPath(marker.transform));
                        marker.enabled = false;
                        marker.transform.rotation = pose.rotation;
                        LivenessMarkerUtility.PlaceInCameraNearCorner(marker.transform, camera);
                    }
                    // Prevent a script reload from dropping queued readbacks. Play exit happens only after retirement.
                    EditorApplication.LockReloadAssemblies();
                    s_ReloadLocked = true;
                    s_State.captureDirectory = Path.Combine(s_State.runDirectory, current.name);
                    s_OwnedCapture = null;
                    try
                    {
                        RenderCaptureService.Start(MakeCaptureRequest(current, s_State.runDirectory));
                        s_OwnedCapture = RenderCaptureService.current;
                    }
                    catch { UnlockReload(); throw; }
                    Phase("Capture");
                    return;
                case "Capture":
                    if (OwnedCapture() == null) throw new InvalidOperationException("The owned capture was lost during regression; no foreign capture will be cancelled.");
                    RepaintGame();
                    if (!OwnedCapture().Finished) return;
                    Evaluate(current);
                    Phase("Drain");
                    return;
                default: throw new InvalidDataException("Unknown regression phase: " + s_State.phase);
            }
        }

        static void RepaintGame()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.titleContent.text == "Game") window.Repaint();
        }
        static string TransformPath(Transform t) => t.parent == null ? t.GetSiblingIndex().ToString() : TransformPath(t.parent) + "/" + t.GetSiblingIndex();
        internal static RenderCaptureRequest MakeCaptureRequest(Case current, string runDirectory) => new RenderCaptureRequest
        {
            outputDirectory = Path.Combine(runDirectory, current.name), fixture = current.name,
            scene = Path.GetFileNameWithoutExtension(current.scenePath), camera = current.camera, cameraType = CameraType.Game,
            width = current.width, height = current.height, roi = new RectInt(0, 0, current.width, current.height),
            warmupFrames = current.warmupFrames, frameCount = 1, includeConfidence = false, requestRepaint = true,
            buffers = new[] { Semantic }
        };

        static void Evaluate(Case current)
        {
            string directory = Path.Combine(s_State.runDirectory, current.name);
            string capturePath = Path.Combine(directory, "capture.json");
            var evidence = JsonUtility.FromJson<CaptureEvidence>(File.ReadAllText(capturePath));
            CaptureBuffer buffer = evidence.buffers?.SingleOrDefault(b => b.semantic == Semantic && string.IsNullOrEmpty(b.error) && !string.IsNullOrEmpty(b.file));
            if (evidence.status != "Completed" || evidence.capturedFrames != 1 || evidence.outstanding != 0 || buffer == null)
            { Record("Error", $"capture {evidence.status}: {evidence.error}", default); return; }
            byte[] captured = File.ReadAllBytes(Path.Combine(directory, buffer.file));
            FImageComparison valid = ImageComparison.Compare(captured, captured, buffer.format, buffer.width, buffer.height, current.pixelThreshold);
            if (!valid.comparable) { Record("Error", "Invalid captured image: " + valid.reason, valid); return; }
            string baselineJson = Path.Combine(s_State.baselineDirectory, current.name + ".json");
            if (s_State.record)
            {
                Directory.CreateDirectory(s_State.baselineDirectory);
                buffer.sha256 = Hash(captured);
                // Immutable content-addressed payload first; the metadata replacement is the commit point.
                buffer.file = current.name + "-" + buffer.sha256 + ".bin";
                string payload = Path.Combine(s_State.baselineDirectory, buffer.file);
                if (!File.Exists(payload)) AtomicBytes(payload, captured);
                if (Hash(File.ReadAllBytes(payload)) != buffer.sha256) throw new InvalidDataException("Baseline payload verification failed.");
                AtomicText(baselineJson, JsonUtility.ToJson(buffer, true));
                Record("Recorded", $"{buffer.width}x{buffer.height} {buffer.format}", default);
                return;
            }
            if (!File.Exists(baselineJson))
            { Record("NoBaseline", baselineJson, default); return; }
            var baseline = JsonUtility.FromJson<CaptureBuffer>(File.ReadAllText(baselineJson));
            if (baseline == null || baseline.width != buffer.width || baseline.height != buffer.height || baseline.format != buffer.format ||
                baseline.colorSpace != buffer.colorSpace || baseline.transferEncoding != buffer.transferEncoding)
            { Record("Fail", "Baseline dimensions, format or color semantics differ.", default); return; }
            if (string.IsNullOrEmpty(baseline.file) || Path.GetFileName(baseline.file) != baseline.file)
            { Record("Error", "Baseline payload must be a local file name.", default); return; }
            string baselineBin = Path.Combine(s_State.baselineDirectory, baseline.file);
            if (!File.Exists(baselineBin)) { Record("NoBaseline", baselineBin, default); return; }
            byte[] stored = File.ReadAllBytes(baselineBin);
            if (string.IsNullOrEmpty(baseline.sha256) || baseline.sha256 != Hash(stored))
            { Record("Error", "Baseline data does not match its recorded SHA256.", default); return; }
            FImageComparison comparison = ImageComparison.Compare(stored, captured, buffer.format, buffer.width, buffer.height, current.pixelThreshold);
            bool pass = comparison.Passes(current.maxRmse, current.maxOutlierFraction);
            Record(pass ? "Pass" : "Fail", $"rmse {comparison.rmse:F6}, max {comparison.maxError:F6}, outliers {comparison.outlierFraction:P4}; {comparison.reason}", comparison);
        }

        static string Hash(byte[] data) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        static void Record(string status, string detail, FImageComparison comparison)
        {
            string name = s_State.index < s_State.cases.Count ? s_State.cases[s_State.index].name : null;
            s_State.results.Add(new CaseResult { name = name, status = status, detail = detail, comparison = comparison });
            Debug.Log($"[InfinityRP][ImageRegression] {name}: {status} ({detail})");
            Save();
        }
        static void Phase(string phase) { s_State.phase = phase; s_State.phaseStarted = DateTime.UtcNow.Ticks; Save(); }
        static void UnlockReload() { if (!s_ReloadLocked) return; EditorApplication.UnlockReloadAssemblies(); s_ReloadLocked = false; }
        static void Finish()
        {
            UnlockReload();
            int failed = s_State.results.Count(r => r.status != "Pass" && r.status != "Recorded");
            if (s_State.outcome != "Cancelled") s_State.outcome = string.IsNullOrEmpty(s_State.persistenceError) && failed == 0 && s_State.results.Count == s_State.cases.Count ? "Passed" : "Failed";
            string report = Path.Combine(s_State.runDirectory, "report.json");
            AtomicText(report, JsonUtility.ToJson(s_State, true));
            SessionState.SetString(ReportKey, report);
            Debug.Log($"[InfinityRP][ImageRegression] {s_State.outcome}: {s_State.results.Count} case(s), {failed} not passing. Report: {report}");
            string statePath = StatePath;
            if (File.Exists(statePath)) File.Delete(statePath);
            SessionState.EraseString(StateKey);
            SessionState.EraseString(RecoveryKey);
            s_State = null;
            s_OwnedCapture = null;
            EditorApplication.update -= Tick;
        }
        static void AtomicBytes(string path, byte[] data)
        {
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, data);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        static void AtomicText(string path, string value) => AtomicBytes(path, System.Text.Encoding.UTF8.GetBytes(value));
        static RenderCaptureSession OwnedCapture()
        {
            if (s_OwnedCapture != null) return s_OwnedCapture;
            RenderCaptureSession candidate = RenderCaptureService.current;
            if (s_State != null && !string.IsNullOrEmpty(s_State.captureDirectory) && candidate != null &&
                Path.GetFullPath(candidate.request.outputDirectory) == Path.GetFullPath(s_State.captureDirectory))
                s_OwnedCapture = candidate;
            return s_OwnedCapture;
        }
        static void SaveRequired()
        {
            string json = JsonUtility.ToJson(s_State, true);
            SessionState.SetString(RecoveryKey, json);
            AtomicText(StatePath, json);
        }
        static void Save()
        {
            try { SaveRequired(); }
            catch (Exception error)
            {
                // Persistence must never prevent cancellation, GPU retirement or scene recovery.
                if (string.IsNullOrEmpty(s_State.persistenceError)) Debug.LogError("[InfinityRP][ImageRegression] State write failed: " + error.Message);
                s_State.persistenceError = error.ToString();
                s_State.stop = true;
                s_State.outcome = "Failed";
                if (s_State.phase != "Restore" && s_State.phase != "ExitPlay" && s_State.phase != "NeedsAttention")
                {
                    s_State.phase = "Drain";
                    s_State.phaseStarted = DateTime.UtcNow.Ticks;
                    OwnedCapture()?.Cancel();
                }
                SessionState.SetString(RecoveryKey, JsonUtility.ToJson(s_State, true));
            }
        }
    }
}
