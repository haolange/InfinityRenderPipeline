using System;
using UnityEditor.ShaderGraph;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // Assets/Create/Shader Graph/Infinity: new graphs with the Infinity target and the sub-target's blocks.
    static class InfinityShaderGraphMenus
    {
        const int kPriority = CoreUtils.Sections.section1 + CoreUtils.Priorities.assetsCreateShaderMenuPriority;

        [MenuItem("Assets/Create/Shader Graph/Infinity/Lit Shader Graph", priority = kPriority)]
        static void CreateLitGraph()
        {
            Create(typeof(InfinityLitSubTarget), new[]
            {
                BlockFields.VertexDescription.Position, BlockFields.VertexDescription.Normal, BlockFields.VertexDescription.Tangent,
                BlockFields.SurfaceDescription.BaseColor, BlockFields.SurfaceDescription.NormalTS, BlockFields.SurfaceDescription.Metallic,
                InfinityBlockFields.SurfaceDescription.Roughness, InfinityBlockFields.SurfaceDescription.SpecularLevel, BlockFields.SurfaceDescription.Emission,
            });
        }

        [MenuItem("Assets/Create/Shader Graph/Infinity/Unlit Shader Graph", priority = kPriority + 1)]
        static void CreateUnlitGraph()
        {
            Create(typeof(InfinityUnlitSubTarget), new[]
            {
                BlockFields.VertexDescription.Position, BlockFields.VertexDescription.Normal, BlockFields.VertexDescription.Tangent,
                BlockFields.SurfaceDescription.BaseColor, BlockFields.SurfaceDescription.Emission, BlockFields.SurfaceDescription.Alpha,
            });
        }

        [MenuItem("Assets/Create/Shader Graph/Infinity/Decal Shader Graph", priority = kPriority + 2)]
        static void CreateDecalGraph()
        {
            Create(typeof(InfinityDecalSubTarget), new[]
            {
                BlockFields.VertexDescription.Position, BlockFields.VertexDescription.Normal, BlockFields.VertexDescription.Tangent,
                BlockFields.SurfaceDescription.BaseColor, BlockFields.SurfaceDescription.NormalTS, BlockFields.SurfaceDescription.Metallic,
                InfinityBlockFields.SurfaceDescription.Roughness, BlockFields.SurfaceDescription.Occlusion, BlockFields.SurfaceDescription.Alpha,
            });
        }

        static void Create(Type subTarget, BlockFieldDescriptor[] blocks)
        {
            var target = (InfinityTarget)Activator.CreateInstance(typeof(InfinityTarget));
            target.TrySetActiveSubTarget(subTarget);
            GraphUtil.CreateNewGraphWithOutputs(new Target[] { target }, blocks);
        }
    }
}
