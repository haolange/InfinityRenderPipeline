namespace InfinityTech.Rendering.Pipeline
{
    public static class TranslucentFeatureUtility
    {
        public static bool ShouldRecordFogComposite(bool fogProduced, bool cloudProduced)
        {
            return fogProduced || cloudProduced;
        }

        public static bool ShouldProduceReactiveMask(bool taaPathActive)
        {
            return taaPathActive;
        }

        public static int ResolveTemporalSceneColorId()
        {
            return InfinityShaderIDs.FoggedSceneColorBuffer;
        }

        public static bool FoggedSceneColorIsDistinctFromOpaque()
        {
            return InfinityShaderIDs.FoggedSceneColorBuffer != InfinityShaderIDs.OpaqueSceneColorBuffer;
        }
    }
}
