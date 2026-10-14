using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
    public sealed class InfinityRenderPipelineRuntimeTextures : IRenderPipelineResources, IRenderPipelineGraphicsSettings
    {
        [SerializeField] int m_Version = 1;
        public int version => m_Version;
        public bool isAvailableInPlayerBuild => true;

        [ResourcePath("Runtime/Resources/Textures/System_LUT/LUT_BestFit.png")]
        public Texture2D bestFitNormalTexture;

        // Blue-noise distributed Sobol sequence (Heitz et al. 2019), 256 samples per pixel over a 128x128 tile.
        // Imported uncompressed single-channel; GetBNDSequenceSample (Montcalo.hlsl) decodes the bytes exactly.
        [ResourcePath("Runtime/Resources/Textures/MonteCarlo/RankingTile256SPP.png")]
        public Texture2D blueNoiseRankingTile;

        [ResourcePath("Runtime/Resources/Textures/MonteCarlo/ScramblingTile256SPP.png")]
        public Texture2D blueNoiseScramblingTile;

        [ResourcePath("Runtime/Resources/Textures/MonteCarlo/ScrambledNoise_Owen.png")]
        public Texture2D blueNoiseOwenScrambledSobol;

        internal bool HasBlueNoise => blueNoiseRankingTile != null && blueNoiseScramblingTile != null && blueNoiseOwenScrambledSobol != null;
    }
}
