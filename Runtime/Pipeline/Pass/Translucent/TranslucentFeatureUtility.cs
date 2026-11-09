using InfinityTech.Rendering.RenderGraph;
namespace InfinityTech.Rendering.Pipeline
{
    public static class TranslucentFeatureUtility
    {
        public static bool ShouldRecordFogComposite(bool fogProduced, bool cloudProduced)
        {
            return fogProduced || cloudProduced;
        }

        public static RGResourceKey ResolveTemporalSceneColorId()
        {
            return InfinityShaderIDs.FoggedSceneColorBuffer;
        }

        public static bool FoggedSceneColorIsDistinctFromOpaque()
        {
            return InfinityShaderIDs.FoggedSceneColorBuffer != InfinityShaderIDs.OpaqueSceneColorBuffer;
        }
    }
}
