using UnityEditor.ShaderGraph;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // Surface inputs of InfinityLit that Shader Graph's standard blocks do not name: roughness (not smoothness) and the
    // Unreal-style specular level (0.5 = 4% reflectance at normal incidence).
    static class InfinityBlockFields
    {
        [GenerateBlocks("Infinity Render Pipeline")]
        public struct SurfaceDescription
        {
            public static string name = "SurfaceDescription";
            public static BlockFieldDescriptor Roughness = new BlockFieldDescriptor(SurfaceDescription.name, "Roughness", "Roughness", "SURFACEDESCRIPTION_ROUGHNESS",
                new FloatControl(0.5f), ShaderStage.Fragment);
            public static BlockFieldDescriptor SpecularLevel = new BlockFieldDescriptor(SurfaceDescription.name, "SpecularLevel", "Specular Level", "SURFACEDESCRIPTION_SPECULARLEVEL",
                new FloatControl(0.5f), ShaderStage.Fragment);
        }
    }
}
