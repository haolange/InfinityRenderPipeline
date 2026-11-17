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
            public RGTextureHandle atlasHandle;
        }

        sealed class ReflectionProbeProduction : IRGPassQueueObserver
        {
            public ReflectionProbeAtlas atlas;
            public void OnQueueFailed() { }
            public void OnQueued() => atlas.MarkRecordedProduced();
        }

        // Imports the probe atlas when probes reach this camera's screen, GGX-filters the cubes that are new, changed or
        // realtime (once per frame across cameras), and builds the probe tile masks of the clustered list.
        void ComputeReflectionProbes(in ViewContext context)
        {
            if (!context.features.ShouldRecord(EFrameFeature.ReflectionProbes))
            {
                return;
            }
            context.features.ThrowIfCannotProduce(EFrameFeature.ReflectionProbes);

            LightContext lightContext = context.lights;
            RGTextureHandle atlasHandle = m_RGBuilder.ImportTexture(m_ReflectionProbeAtlas.GetAtlas());
            m_RGBuilder.scoper.RegisterTexture(InfinityShaderIDs.ReflectionProbeAtlas, atlasHandle);

            if (m_ReflectionProbeAtlas.TakeUploads(out ProbeCubeUpload[] uploads, out int uploadCount))
            {
                {
                    RGComputePassHandle<ReflectionProbeFilterPassData> passHandle = m_RGBuilder.AddComputePass<ReflectionProbeFilterPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeReflectionProbes));
                    ref ReflectionProbeFilterPassData passData = ref passHandle.GetPassData();
                    passData.filterCubeToArray = kernels.filterCubeToArray;
                    passData.uploads = uploads;
                    passData.uploadCount = uploadCount;
                    passData.resolution = m_ReflectionProbeAtlas.Resolution;
                    passData.mipCount = m_ReflectionProbeAtlas.MipCount;
                    passData.atlasHandle = passHandle.WriteTexture(atlasHandle);
                    passHandle.EnablePassCulling(false);
                    passHandle.SetQueueObserver(m_ReflectionProbeProduction);
                    passHandle.SetExecuteFunc((in ReflectionProbeFilterPassData passData, in RGComputeEncoder cmdEncoder) =>
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
                                CubeFilterUtilityData.FilterCubeToArray(cmdEncoder, passData.filterCubeToArray, upload.source, 1.0f, passData.atlasHandle,
                                    passData.resolution, mip, CubeFilterUtilityData.MipRoughness(mip, passData.mipCount), upload.slot * 6,
                                    ReflectionProbePassUtilityData.FilterSampleCount);
                            }
                        }
                    });
                }
            }

            RecordTileMaskPass(LightShaderIDs.ProbeTileRectBuffer, InfinityShaderIDs.ProbeTileMaskBuffer, ReflectionProbePassUtilityData.ProbeTileMaskBufferName,
                lightContext.ReflectionProbeCount, lightContext.ProbeTileWordCount, lightContext.TileCount, CustomSamplerId.ComputeProbeTileMask);
            context.features.MarkProduced(EFrameFeature.ReflectionProbes);
        }
    }
}
