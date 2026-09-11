using System;
using UnityEditor;
using UnityEngine;

namespace InfinityTech.Rendering.Editor
{
    internal static class NativeProbeImport
    {
        [MenuItem("Window/Infinity/Capture/Import Metal Probe")]
        static void Import()
        {
            const string path = "Packages/com.infinity.render-pipeline/Runtime/Plugins/macOS/InfinityCaptureMetal.bundle";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null) throw new InvalidOperationException("The Metal probe is not registered as a native plugin.");
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", "OSX");
            importer.SetEditorData("CPU", "AnyCPU");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
            importer.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "AnyCPU");
            importer.SaveAndReimport();
            Debug.Log("[InfinityRP] Metal probe importer: editor=" + importer.GetCompatibleWithEditor() +
                " player=" + importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX) + " OS=" + importer.GetEditorData("OS") + " CPU=" + importer.GetEditorData("CPU"));
        }
    }
}
