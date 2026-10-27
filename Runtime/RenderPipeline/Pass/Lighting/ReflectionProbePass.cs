using System.Collections.Generic;
using UnityEngine;
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
            public ComputeShader shader;
            public FProbeCubeUpload[] uploads;
            public int resolution;
            public int mipCount;
            public RGTextureHandle atlas;
        }

        sealed class ReflectionProbeProduction : IRGPassQueueObserver
        {
            public ReflectionProbeAtlas atlas;
            public FProbeCubeKey[] keys;
            public void OnQueueFailed() { }
            public void OnQueued() => atlas.MarkProduced(keys, keys.Length);
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
            m_RGScoper.RegisterTexture(InfinityShaderIDs.ReflectionProbeAtlas, atlas);

            List<FProbeCubeUpload> pending = m_ReflectionProbeAtlas.Uploads;
            if (pending.Count > 0)
            {
                FProbeCubeUpload[] uploads = pending.ToArray();
                pending.Clear();
                var keys = new FProbeCubeKey[uploads.Length];
                for (int i = 0; i < uploads.Length; ++i) keys[i] = uploads[i].key;

                using (RGComputePassHandle passRef = m_RGBuilder.AddComputePass<ReflectionProbeFilterPassData>(ProfilingSampler.Get(CustomSamplerId.ComputeReflectionProbes)))
                {
                    ref ReflectionProbeFilterPassData passData = ref passRef.GetPassData<ReflectionProbeFilterPassData>();
                    passData.shader = shaders.cubeFilterShader;
                    passData.uploads = uploads;
                    passData.resolution = m_ReflectionProbeAtlas.Resolution;
                    passData.mipCount = m_ReflectionProbeAtlas.MipCount;
                    passData.atlas = passRef.WriteTexture(atlas);
                    passRef.EnablePassCulling(false);
                    passRef.SetQueueObserver(new ReflectionProbeProduction { atlas = m_ReflectionProbeAtlas, keys = keys });
                    passRef.SetExecuteFunc((in ReflectionProbeFilterPassData passData, in RGComputeEncoder cmdEncoder) =>
                    {
                        foreach (FProbeCubeUpload upload in passData.uploads)
                        {
                            if (upload.source == null) continue;
                            // Intensity and HDR decode stay in the record (params.x), so a changed intensity needs no refilter.
                            for (int mip = 0; mip < passData.mipCount; ++mip)
                            {
                                CubeFilterUtilityData.FilterCubeToArray(cmdEncoder, passData.shader, upload.source, 1.0f, passData.atlas,
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
