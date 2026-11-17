using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InfinityTech.Rendering.Editor
{
    internal static class PlayerBuildValidation
    {
        [Serializable] sealed class BuildEvidence
        {
            public string startedUtc;
            public string finishedUtc;
            public string unity;
            public string target;
            public bool development;
            public string output;
            public string result;
            public string[] scenes;
            public string[] graphicsApis;
            public int errors;
            public int warnings;
            public ulong bytes;
            public double seconds;
            public string[] messages;
        }

        [MenuItem("Window/Infinity/Build macOS Player")]
        public static void BuildMacOSPlayer() => BuildMacOSPlayer(new[] { "Assets/Scene/Spazon/Scene_Spazon.unity" }, true);

        [MenuItem("Window/Infinity/Build macOS Release Player")]
        public static void BuildMacOSReleasePlayer() => BuildMacOSPlayer(new[] { "Assets/Scene/Spazon/Scene_Spazon.unity" }, false);

        [MenuItem("Window/Infinity/Build macOS Fault Fixture Player")]
        static void BuildFaultFixturePlayer() => BuildMacOSPlayer(new[] {
            "Assets/Scene/Validation/Validation_LocalLights.unity", "Assets/Scene/Spazon/Scene_Spazon.unity" }, true);

        internal static string BuildMacOSPlayer(string[] scenes, bool development, string runDirectory = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Player validation build requires an idle EditMode editor.");
            foreach (string scene in scenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                    throw new FileNotFoundException("The reference scene is required for Player acceptance.", scene);
            // The performance package writes these reserved paths even on ordinary builds.
            // Refuse existing files so its preprocess/postprocess hooks cannot overwrite user data.
            string[] generatedPaths = {
                "Assets/Resources/PerformanceTestRunInfo.json",
                "Assets/Resources/PerformanceTestRunSettings.json"
            };
            foreach (string path in generatedPaths)
                if (File.Exists(path) || File.Exists(path + ".meta"))
                    throw new InvalidOperationException("Build-generated path already exists; archive and verify its ownership before building: " + path);
            string run;
            string outputApp;
            if (!string.IsNullOrEmpty(runDirectory))
            {
                run = Path.GetFullPath(runDirectory);
                Directory.CreateDirectory(run);
                outputApp = Path.Combine(run, "InfinityRP.app");
            }
            else
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../intermediate/player-build"));
                run = Path.Combine(root, "player-build-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(run);
                File.WriteAllText(Path.Combine(root, "player-build-latest.txt"), run);
                outputApp = Path.Combine(run, "InfinityRP.app");
            }
            var evidence = new BuildEvidence
            {
                startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                target = BuildTarget.StandaloneOSX.ToString(),
                development = development,
                output = outputApp,
                scenes = scenes, result = "Started",
                graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneOSX).Select(api => api.ToString()).ToArray()
            };
            File.WriteAllText(Path.Combine(run, "build-start.json"), JsonUtility.ToJson(evidence, true));
            try
            {
                // Supply this build's scene list directly; never change the user's EditorBuildSettings.
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = evidence.scenes, locationPathName = evidence.output,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    target = BuildTarget.StandaloneOSX,
                    options = development ? (BuildOptions.Development | BuildOptions.AllowDebugging) : BuildOptions.None
                });
                evidence.result = report.summary.result.ToString();
                evidence.errors = report.summary.totalErrors;
                evidence.warnings = report.summary.totalWarnings;
                evidence.bytes = report.summary.totalSize;
                evidence.seconds = report.summary.totalTime.TotalSeconds;
                evidence.messages = report.steps.SelectMany(step => step.messages.Select(message =>
                    step.name + " | " + message.type + " | " + message.content)).ToArray();
            }
            catch (Exception error)
            {
                evidence.result = "Exception";
                evidence.messages = new[] { error.ToString() };
                throw;
            }
            finally
            {
                evidence.finishedUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(Path.Combine(run, "build-report.json"), JsonUtility.ToJson(evidence, true));
                Debug.Log("[InfinityRP] macOS Player build " + evidence.result + ": " + run);
                // Failed builds do not invoke the package's postprocess cleanup.
                // Only files proven absent before this synchronous build are eligible.
                bool removedGeneratedFiles = false;
                foreach (string path in generatedPaths)
                {
                    foreach (string generated in new[] { path, path + ".meta" })
                    {
                        if (!File.Exists(generated))
                        {
                            continue;
                        }
                        string archive = Path.Combine(run, "generated-build-assets", Path.GetFileName(generated));
                        Directory.CreateDirectory(Path.GetDirectoryName(archive));
                        File.Copy(generated, archive, false);
                        File.Delete(generated);
                        removedGeneratedFiles = true;
                    }
                }
                if (removedGeneratedFiles)
                {
                    AssetDatabase.Refresh();
                }
            }
            if (evidence.result != BuildResult.Succeeded.ToString())
                throw new InvalidOperationException("macOS Player build failed. See " + Path.Combine(run, "build-report.json"));
            return evidence.output;
        }
    }
}
