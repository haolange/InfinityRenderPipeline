using UnityEngine;

namespace InfinityTech.Rendering.MeshPipeline
{
    // Shader property IDs of the mesh entity interface (Shaders/Library/GPUScene.hlsl).
    internal static class MeshShaderIDs
    {
        public static readonly int InstanceOffset = Shader.PropertyToID("InstanceOffset");
        public static readonly int InstanceToEntity = Shader.PropertyToID("InstanceToEntity");
        public static readonly int InstanceFade = Shader.PropertyToID("InstanceFade");
        public static readonly int EntityTransforms = Shader.PropertyToID("EntityTransforms");
        public static readonly int EntityGenerations = Shader.PropertyToID("EntityGenerations");
        public static readonly int EntityRenderingLayers = Shader.PropertyToID("EntityRenderingLayers");
        public static readonly int PreviousEntityTransforms = Shader.PropertyToID("PreviousEntityTransforms");
        public static readonly int PreviousEntityGenerations = Shader.PropertyToID("PreviousEntityGenerations");
        public static readonly int PreviousEntityCount = Shader.PropertyToID("PreviousEntityCount");
        public static readonly int PreviousEntityValid = Shader.PropertyToID("PreviousEntityValid");
    }
}
