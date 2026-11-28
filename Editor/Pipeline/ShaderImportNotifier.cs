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
            ClassifyShaders(importedAssets, ref shader, ref compute);
            ClassifyShaders(deletedAssets, ref shader, ref compute);
            ClassifyShaders(movedAssets, ref shader, ref compute);
            ClassifyShaders(movedFromAssetPaths, ref shader, ref compute);
            if (shader || compute)
            {
                InfinityRenderPipelineResources.NotifyShadersImported();
            }
            if (shader)
            {
                MeshComponent.NotifyShadersChanged();
            }
        }

        static void ClassifyShaders(string[] paths, ref bool shader, ref bool compute)
        {
            foreach (string path in paths)
            {
                shader |= path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase);
                compute |= path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
