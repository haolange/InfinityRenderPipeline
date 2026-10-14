using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Validation
{
    /// <summary>
    /// Image regression over the validation fixtures, built on RenderCaptureService. For each case the runner opens the
    /// fixture scene (creating it when missing), enters Play mode, captures DisplayColor of the fixture's Game camera
    /// after a warm-up, leaves Play mode and either records the capture as the baseline or compares it with the
    /// baseline. Baselines live outside Assets in ImageBaselines/&lt;graphics API&gt;/ (one .bin + .json per case) and are
    /// produced on the user's machine with Record Baselines; Run Comparison writes Library/InfinityImageRegression/
    /// &lt;run&gt;/report.json and logs one line per case. State survives the Play mode domain reload (state.json).
    /// </summary>
    [InitializeOnLoad]
    internal static class ImageRegressionRunner
    {
        [Serializable]
        internal sealed class Case
        {
            public string name;
            public string scenePath;
            public string camera = "Camera";
            public int warmupFrames = 90;
            public float pixelThreshold = 4.0f / 255.0f;
            public float maxRmse = 0.01f;
            public float maxOutlierFraction = 0.005f;
        }

        [Serializable]
        sealed class State
        {
            public bool record;
            public string runDirectory;
            public int index;
            public string phase = "Open";
            public List<Case> cases = new List<Case>();
            public List<CaseResult> results = new List<CaseResult>();
        }

        [Serializable]
        internal sealed class CaseResult
        {
            public string name;
            public string status;
            public string detail;
            public FImageComparison comparison;
        }

        // Mirrors the parts of RenderCaptureSession's capture.json the runner reads.
        [Serializable] sealed class CaptureBuffer { public string semantic, file, format, error; public int width, height; }
        [Serializable] sealed class CaptureEvidence { public string status, error; public List<CaptureBuffer> buffers = new List<CaptureBuffer>(); }

        const string MenuRoot = "Window/Infinity/Image Regression/";
        const string Semantic = "DisplayColor";
        static string StatePath => Path.Combine(ProjectRoot, "Library", "InfinityImageRegression", "state.json");
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        internal static string BaselineDirectory => Path.Combine(ProjectRoot, "ImageBaselines", SystemInfo.graphicsDeviceType.ToString());

        static readonly Dictionary<string, Action> s_Creators = new Dictionary<string, Action>
        {
            { "AreaLights", CreateAreaLightsValidationScene.Create },
            { "LocalLights", CreateLocalLightsValidationScene.Create },
            { "Decal", CreateDecalValidationScene.Create },
            { "Translucent", CreateTranslucentValidationScene.Create },
            { "Shadows", CreateShadowsValidationScene.Create },
            { "Cookies", CreateCookiesValidationScene.Create },
            { "ReflectionProbes", CreateReflectionProbesValidationScene.Create },
            { "ScreenSpace", CreateScreenSpaceValidationScene.Create },
        };

        internal static List<Case> DefaultCases()
        {
            return s_Creators.Keys.Select(name => new Case { name = name, scenePath = "Assets/Scene/Validation/Validation_" + name + ".unity" }).ToList();
        }

        static State s_State;

        static ImageRegressionRunner()
        {
            s_State = Load();
            if (s_State == null) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem(MenuRoot + "Record Baselines", false, 80)]
        static void RecordBaselines() => Start(true);

        [MenuItem(MenuRoot + "Run Comparison", false, 81)]
        static void RunComparison() => Start(false);

        [MenuItem(MenuRoot + "Cancel", false, 82)]
        static void Cancel()
        {
            if (s_State == null) return;
            Finish("Cancelled");
        }

        [MenuItem(MenuRoot + "Open Baseline Folder", false, 83)]
        static void OpenBaselines()
        {
            Directory.CreateDirectory(BaselineDirectory);
            EditorUtility.RevealInFinder(BaselineDirectory);
        }

        static void Start(bool record)
        {
            if (s_State != null) throw new InvalidOperationException("An image regression run is active; cancel it first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode before starting a run.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            s_State = new State
            {
                record = record,
                runDirectory = Path.Combine(ProjectRoot, "Library", "InfinityImageRegression", DateTime.Now.ToString("yyyyMMdd-HHmmss")),
                cases = DefaultCases(),
            };
            Directory.CreateDirectory(s_State.runDirectory);
            Save();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (s_State == null)
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                Step();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Record("Error", error.Message, default);
                Advance();
            }
        }

        static void Step()
        {
            if (s_State.index >= s_State.cases.Count)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish("Completed");
                return;
            }
            Case current = s_State.cases[s_State.index];
            switch (s_State.phase)
            {
                case "Open":
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (!File.Exists(Path.Combine(ProjectRoot, current.scenePath)))
                    {
                        if (!s_Creators.TryGetValue(current.name, out Action create)) throw new FileNotFoundException(current.scenePath);
                        create();
                    }
                    EditorSceneManager.OpenScene(current.scenePath, OpenSceneMode.Single);
                    s_State.phase = "EnterPlay";
                    Save();
                    EditorApplication.isPlaying = true;
                    return;
                case "EnterPlay":
                    if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                    if (RenderCaptureService.current != null && !RenderCaptureService.current.Finished) return;
                    RenderCaptureService.Start(new RenderCaptureRequest
                    {
                        outputDirectory = Path.Combine(s_State.runDirectory, current.name),
                        fixture = current.name,
                        scene = Path.GetFileNameWithoutExtension(current.scenePath),
                        camera = current.camera,
                        cameraType = CameraType.Game,
                        warmupFrames = current.warmupFrames,
                        frameCount = 1,
                        includeConfidence = false,
                        buffers = new[] { Semantic },
                    });
                    s_State.phase = "Capture";
                    Save();
                    return;
                case "Capture":
                    if (RenderCaptureService.current == null || !RenderCaptureService.current.Finished) return;
                    Evaluate(current);
                    s_State.phase = "ExitPlay";
                    Save();
                    EditorApplication.isPlaying = false;
                    return;
                case "ExitPlay":
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    Advance();
                    return;
            }
        }

        static void Evaluate(Case current)
        {
            string directory = Path.Combine(s_State.runDirectory, current.name);
            string capturePath = Path.Combine(directory, "capture.json");
            if (!File.Exists(capturePath))
            {
                Record("Error", "capture.json missing", default);
                return;
            }
            var evidence = JsonUtility.FromJson<CaptureEvidence>(File.ReadAllText(capturePath));
            CaptureBuffer buffer = evidence.buffers?.LastOrDefault(b => b.semantic == Semantic && string.IsNullOrEmpty(b.error) && !string.IsNullOrEmpty(b.file));
            if (evidence.status != "Completed" || buffer == null)
            {
                Record("Error", $"capture {evidence.status}: {evidence.error}", default);
                return;
            }
            string capturedFile = Path.Combine(directory, buffer.file);
            string baselineBin = Path.Combine(BaselineDirectory, current.name + ".bin");
            string baselineJson = Path.Combine(BaselineDirectory, current.name + ".json");
            if (s_State.record)
            {
                Directory.CreateDirectory(BaselineDirectory);
                File.Copy(capturedFile, baselineBin, true);
                File.WriteAllText(baselineJson, JsonUtility.ToJson(buffer, true));
                Record("Recorded", $"{buffer.width}x{buffer.height} {buffer.format}", default);
                return;
            }
            if (!File.Exists(baselineBin) || !File.Exists(baselineJson))
            {
                Record("NoBaseline", baselineBin, default);
                return;
            }
            var baseline = JsonUtility.FromJson<CaptureBuffer>(File.ReadAllText(baselineJson));
            if (baseline.width != buffer.width || baseline.height != buffer.height || baseline.format != buffer.format)
            {
                Record("Fail", $"baseline {baseline.width}x{baseline.height} {baseline.format}, capture {buffer.width}x{buffer.height} {buffer.format}", default);
                return;
            }
            FImageComparison comparison = ImageComparison.Compare(File.ReadAllBytes(baselineBin), File.ReadAllBytes(capturedFile), buffer.format,
                buffer.width, buffer.height, current.pixelThreshold);
            bool pass = comparison.Passes(current.maxRmse, current.maxOutlierFraction);
            Record(pass ? "Pass" : "Fail", $"rmse {comparison.rmse:F4}, max {comparison.maxError:F3}, outliers {comparison.outlierFraction:P2}", comparison);
        }

        static void Record(string status, string detail, FImageComparison comparison)
        {
            Case current = s_State.index < s_State.cases.Count ? s_State.cases[s_State.index] : null;
            s_State.results.Add(new CaseResult { name = current?.name, status = status, detail = detail, comparison = comparison });
            Debug.Log($"[InfinityRP][ImageRegression] {current?.name}: {status} ({detail})");
            Save();
        }

        static void Advance()
        {
            if (EditorApplication.isPlaying)
            {
                s_State.phase = "ExitPlay";
                EditorApplication.isPlaying = false;
                Save();
                return;
            }
            s_State.index++;
            s_State.phase = "Open";
            Save();
        }

        static void Finish(string outcome)
        {
            string report = Path.Combine(s_State.runDirectory, "report.json");
            File.WriteAllText(report, JsonUtility.ToJson(s_State, true));
            int failed = s_State.results.Count(r => r.status != "Pass" && r.status != "Recorded");
            Debug.Log($"[InfinityRP][ImageRegression] {outcome}: {s_State.results.Count} case(s), {failed} not passing. Report: {report}");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            s_State = null;
            if (File.Exists(StatePath)) File.Delete(StatePath);
            EditorApplication.update -= Tick;
        }

        static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            File.WriteAllText(StatePath, JsonUtility.ToJson(s_State, true));
        }

        static State Load()
        {
            if (!File.Exists(StatePath)) return null;
            try
            {
                return JsonUtility.FromJson<State>(File.ReadAllText(StatePath));
            }
            catch (Exception error)
            {
                Debug.LogWarning("[InfinityRP][ImageRegression] Discarding unreadable run state: " + error.Message);
                File.Delete(StatePath);
                return null;
            }
        }
    }
}
