using System;
using InfinityTech.Component;
using UnityEditor;

namespace InfinityTech.Rendering.Pipeline.Editor
{
    /// <summary>
    /// A shader import can renumber or remove passes and kernels. The pipeline resolves its kernel and pass tables again,
    /// mesh pass caches resolve their pass indices again and mesh components validate their materials again in the next
    /// frame; no asset is changed.
    /// </summary>
    sealed class ShaderImportNotifier : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool shader = false;
            bool compute = false;
            foreach (string path in importedAssets)
            {
                shader |= path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase);
                compute |= path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase);
            }
            if (shader || compute)
            {
                InfinityRenderPipelineResources.NotifyShadersImported();
            }
            if (shader)
            {
                MeshComponent.NotifyShadersChanged();
            }
        }
    }
}
