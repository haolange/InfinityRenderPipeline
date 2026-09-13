using System;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Editor.Cli
{
    public static class NormalizationBaselineCommands
    {
        [CliCommand("infinity_normalization_baseline", "Write a U02 protective baseline receipt (EditMode XML path, Volume snapshot, LUT descriptor, log mark). Does not capture GPU frames.")]
        public static string WriteBaseline(string outputDirectory)
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new ArgumentException("outputDirectory is required.");
            }

            Directory.CreateDirectory(outputDirectory);
            InfinityRenderPipelineGlobalSettings settings = InfinityRenderPipelineGlobalSettings.Require();
            VolumeProfile profile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string editorLog = Path.Combine(projectRoot, "Logs", "Editor.log");
            long logMark = File.Exists(editorLog) ? new FileInfo(editorLog).Length : -1;
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("task=U02");
            lines.AppendLine("status=UNVERIFIED");
            lines.AppendLine("package=" + UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(InfinityRenderPipeline).Assembly)?.version);
            lines.AppendLine("unity=" + Application.unityVersion);
            lines.AppendLine("pid=" + System.Diagnostics.Process.GetCurrentProcess().Id);
            lines.AppendLine("globalSettings=" + AssetDatabase.GetAssetPath(settings));
            lines.AppendLine("bound=" + (GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>() == settings));
            lines.AppendLine("profile=" + (profile != null ? profile.name : "missing"));
            lines.AppendLine("editorLog=" + editorLog);
            lines.AppendLine("logMark=" + logMark);
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components)
                {
                    if (component == null) continue;
                    lines.AppendLine(component.GetType().Name + ".active=" + component.active + ",IsActive=" + GraphicsUtility.VolumeComponentActive(component));
                }

                profile.TryGet(out InfinityTech.Rendering.PostProcess.FilmTonemap film);
                profile.TryGet(out InfinityTech.Rendering.PostProcess.ColorGrading grading);
                string lutKey = string.Join("|",
                    film != null ? film.mode.value.ToString() : "nofilm",
                    film != null ? film.slope.value.ToString("R") : "0",
                    film != null ? film.toe.value.ToString("R") : "0",
                    film != null ? film.shoulder.value.ToString("R") : "0",
                    film != null ? film.blackClip.value.ToString("R") : "0",
                    film != null ? film.whiteClip.value.ToString("R") : "0",
                    grading != null ? grading.Temp.value.ToString("R") : "0",
                    grading != null ? grading.Tint.value.ToString("R") : "0",
                    grading != null ? grading.ExpandGamut.value.ToString("R") : "0",
                    grading != null ? grading.BlueCorrection.value.ToString("R") : "0");
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(lutKey));
                    lines.AppendLine("lutKey=" + lutKey);
                    lines.AppendLine("lutHash=" + System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
                }
            }

            string path = Path.Combine(outputDirectory, "u02-baseline.txt");
            File.WriteAllText(path, lines.ToString());
            Debug.Log("[InfinityRP] U02 baseline receipt: " + path);
            return path;
        }
    }
}
