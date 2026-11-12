using UnityEngine;
using InfinityTech.Core;
using UnityEngine.Rendering;
using InfinityTech.Rendering.RenderGraph;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline
{
    internal static class ReflectionProbePassUtilityData
    {
        internal const string ProbeTileMaskBufferName = "ProbeTileMasks";
        // Importance samples per texel of a probe mip; the source mips carry the low frequencies.
        internal const int FilterSampleCount = 32;
    }

    public partial class InfinityRenderPipeline
    {
        struct ReflectionProbeFilterPassData
        {
            public ComputeKernel filterCubeToArray;
            public ProbeCubeUpload[] uploads;
            public int uploadCount;
            public int resolution;
            public int mipCount;
            public RGTextureHandle atlas;
        }

        sealed class ReflectionProbeProduction : IRGPassQueueObserver
        {
            public ReflectionProbeAtlas atlas;
            public void OnQueueFailed() { }
            public void OnQueued() => atlas.MarkRecordedProduced();
        }

        // Imports the probe atlas when probes reach this camera's screen, GGX-filters the cubes that are new, changed or
        // realtime (once per frame across cameras), and builds the probe tile masks of the clustered list.
        void ComputeReflectionProbes(RenderContext renderContext)
        {
            if (!ShouldRecordFeature(EFrameFeature.ReflectionProbes))
            {
                return;
            }
            ActiveFeatures.ThrowIfCannotProduce(EFrameFeature.ReflectionProbes);

            LightContext context = renderContext.lightContext;
            RGTextureHandle atlas = m_RGBuilder.ImportTexture(m_ReflectionProbeAtlas.GetAtlas());
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.ReflectionProbeAtlas, atlas);

            if (m_ReflectionProbeAtlas.TakeUploads(out ProbeCubeUpload[] uploads, out int uploadCount))
            {
                using (var passRef = m_RGBuilder.AddComputePass<ReflectionProbeFilterPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeReflectionProbes)))
                {
                    ref ReflectionProbeFilterPassData passData = ref passRef.GetPassData();
                    passData.filterCubeToArray = kernels.filterCubeToArray;
                    passData.uploads = uploads;
                    passData.uploadCount = uploadCount;
                    passData.resolution = m_ReflectionProbeAtlas.Resolution;
                    passData.mipCount = m_ReflectionProbeAtlas.MipCount;
                    passData.atlas = passRef.WriteTexture(atlas);
                    passRef.EnablePassCulling(false);
                    passRef.SetQueueObserver(m_ReflectionProbeProduction);
                    passRef.SetExecuteFunc((in ReflectionProbeFilterPassData passData, in RGComputeEncoder cmdEncoder) =>
                    {
                        int count = passData.uploadCount;
                        ProbeCubeUpload[] recorded = passData.uploads;
                        for (int i = 0; i < count; ++i)
                        {
                            ProbeCubeUpload upload = recorded[i];
                            if (upload.source == null)
                            {
                                continue;
                            }
                            // Intensity and HDR decode stay in the record (params.x), so a changed intensity needs no refilter.
                            for (int mip = 0; mip < passData.mipCount; ++mip)
                            {
                                CubeFilterUtilityData.FilterCubeToArray(cmdEncoder, passData.filterCubeToArray, upload.source, 1.0f, passData.atlas,
                                    passData.resolution, mip, CubeFilterUtilityData.MipRoughness(mip, passData.mipCount), upload.slot * 6,
                                    ReflectionProbePassUtilityData.FilterSampleCount);
                            }
                        }
                    });
                }
            }

            RecordTileMaskPass(LightShaderIDs.ProbeTileRectBuffer, InfinityShaderIDs.ProbeTileMaskBuffer, ReflectionProbePassUtilityData.ProbeTileMaskBufferName,
                context.ReflectionProbeCount, context.ProbeTileWordCount, context.TileCount, CustomSamplerId.ComputeProbeTileMask);
            MarkFeatureProduced(EFrameFeature.ReflectionProbes);
        }
    }
}
