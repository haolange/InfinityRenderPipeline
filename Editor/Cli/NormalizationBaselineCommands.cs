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
            VolumeProfile profile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("task=U02");
            lines.AppendLine("status=UNVERIFIED");
            lines.AppendLine("package=" + UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(InfinityRenderPipeline).Assembly)?.version);
            lines.AppendLine("unity=" + Application.unityVersion);
            lines.AppendLine("profile=" + (profile != null ? profile.name : "missing"));
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components)
                {
                    if (component == null) continue;
                    lines.AppendLine(component.GetType().Name + ".active=" + component.active + ",IsActive=" + GraphicsUtility.VolumeComponentActive(component));
                }
            }

            string path = Path.Combine(outputDirectory, "u02-baseline.txt");
            File.WriteAllText(path, lines.ToString());
            Debug.Log("[InfinityRP] U02 baseline receipt: " + path);
            return path;
        }
    }
}
