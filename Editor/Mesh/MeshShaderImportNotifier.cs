using System;
using UnityEditor;

namespace InfinityTech.Component.Editor
{
    /// <summary>
    /// A shader import can renumber or remove passes. Mesh pass caches resolve their pass indices again and mesh components
    /// validate their materials again in the next frame; no asset is changed.
    /// </summary>
    sealed class MeshShaderImportNotifier : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                {
                    MeshComponent.NotifyShadersChanged();
                    return;
                }
            }
        }
    }
}
