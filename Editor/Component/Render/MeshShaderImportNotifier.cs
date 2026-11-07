using System;
using UnityEditor;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Component.Editor
{
    /// <summary>
    /// A shader import can renumber passes. Mesh pass caches resolve their pass indices again before their next draw list;
    /// no asset is changed.
    /// </summary>
    sealed class MeshShaderImportNotifier : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                {
                    MeshPassShaderUtility.NotifyShadersChanged();
                    return;
                }
            }
        }
    }
}
