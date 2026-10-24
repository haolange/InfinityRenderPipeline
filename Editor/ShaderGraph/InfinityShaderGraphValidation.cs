using System;
using System.IO;
using System.Linq;
using UnityEditor.ShaderGraph;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // Explicit validation entry point. Shader Graph internals stay inside this asmref boundary.
    internal static class InfinityShaderGraphValidation
    {
        public static string[] CreateGraphs(string directory)
        {
            string fullDirectory = Path.GetFullPath(directory);
            string assetRoot = Path.GetFullPath(UnityEngine.Application.dataPath) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith("Assets/", StringComparison.Ordinal) || !fullDirectory.StartsWith(assetRoot, StringComparison.Ordinal))
                throw new ArgumentException("Graph validation output must be an explicit directory inside this project's Assets.", nameof(directory));
            var names = new[] { "Lit", "Unlit", "Decal" };
            var types = new[] { typeof(InfinityLitSubTarget), typeof(InfinityUnlitSubTarget), typeof(InfinityDecalSubTarget) };
            var blocks = new[] { InfinityPasses.LitBlocks, InfinityPasses.ColorBlocks, InfinityPasses.DecalBlocks };
            var paths = new string[names.Length];
            for (int i = 0; i < paths.Length; ++i)
            {
                paths[i] = directory.TrimEnd('/') + "/" + names[i] + ".shadergraph";
                if (File.Exists(paths[i])) throw new IOException("Validation must not overwrite an existing graph: " + paths[i]);
            }
            Directory.CreateDirectory(directory);
            for (int i = 0; i < paths.Length; ++i)
            {
                var target = new InfinityTarget();
                if (!target.TrySetActiveSubTarget(types[i])) throw new InvalidOperationException("Infinity sub-target is unavailable: " + names[i]);
                var graph = new GraphData();
                graph.AddContexts();
                graph.InitializeOutputs(new Target[] { target }, InfinityPasses.VertexBlocks.Concat(blocks[i]).ToArray());
                graph.AddCategory(CategoryData.DefaultCategory());
                graph.path = "Infinity Validation";
                FileUtilities.WriteShaderGraphToDisk(paths[i], graph);
                AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(paths[i]) == null)
                    throw new InvalidOperationException("Generated graph did not import: " + paths[i]);
            }
            return paths;
        }
    }
}
