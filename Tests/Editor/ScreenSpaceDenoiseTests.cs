using System.IO;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ScreenSpaceDenoiseTests
    {
        [Test]
        public void VolumeHasOverrides_GatesGTAORequest()
        {
            ScreenSpaceAmbientOcclusion ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            try
            {
                ssao.active = true;
                Assert.IsFalse(GraphicsUtility.VolumeHasOverrides(ssao));
                Assert.IsFalse(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));

                ssao.Intensity.overrideState = true;
                Assert.IsTrue(GraphicsUtility.VolumeHasOverrides(ssao));
                Assert.IsTrue(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));

                ssao.active = false;
                Assert.IsFalse(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));
            }
            finally
            {
                Object.DestroyImmediate(ssao);
            }
        }

        [Test]
        public void ScreenSpaceReflection_NumRaysDefaultIsAtLeastTwo()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            try
            {
                Assert.GreaterOrEqual(ssr.NumRays.value, 2);
            }
            finally
            {
                Object.DestroyImmediate(ssr);
            }
        }

        [Test]
        public void CompositeShader_DoesNotMultiplyAOOnSSROrSSGI()
        {
            string shaderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ScreenSpaceComposite/Compute_ScreenSpaceComposite.compute"));
            Assert.IsTrue(File.Exists(shaderPath), shaderPath);
            string source = File.ReadAllText(shaderPath);
            Assert.IsTrue(source.Contains("AO is applied once in DeferredShading on IBL"));
            Assert.IsFalse(source.Contains("* ao"));
            Assert.IsFalse(source.Contains("COMPOSITE_AO"));
            Assert.IsTrue(source.Contains("lighting += microfaceCtx.AlbedoColor * ssgiColor.rgb;"));
            Assert.IsTrue(source.Contains("lighting += (microfaceCtx.SpecularColor * envBRDF.x + envBRDF.y) * indirectSpecular;"));
        }

        [Test]
        public void CompositePass_DoesNotBindCOMPOSITE_AO()
        {
            string passPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/Pass/ScreenSpaceCompositePass.cs"));
            Assert.IsTrue(File.Exists(passPath), passPath);
            string source = File.ReadAllText(passPath);
            Assert.IsFalse(source.Contains("COMPOSITE_AO"));
            Assert.IsFalse(source.Contains("hasAO"));
        }

        [Test]
        public void DeferredShading_QueriesOcclusionOnlyWhenRegistered()
        {
            string passPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/Pass/DeferredShadingPass.cs"));
            Assert.IsTrue(File.Exists(passPath), passPath);
            string source = File.ReadAllText(passPath);
            Assert.IsTrue(source.Contains("TryQueryTexture(InfinityShaderIDs.OcclusionBuffer"));
            Assert.IsFalse(source.Contains("QueryTexture(InfinityShaderIDs.OcclusionBuffer)"));
        }

        [Test]
        public void RampTemporalWeight_RampsFromZeroAfterReset()
        {
            int validFrames = 12;
            float configured = 0.93f;
            float first = ScreenSpaceHistoryUtility.RampTemporalWeight(configured, ref validFrames, resetHistory: true);
            Assert.AreEqual(0.0f, first);
            Assert.AreEqual(1, validFrames);

            float second = ScreenSpaceHistoryUtility.RampTemporalWeight(configured, ref validFrames, resetHistory: false);
            Assert.Greater(second, 0.0f);
            Assert.Less(second, configured);

            for (int i = 0; i < ScreenSpaceHistoryUtility.TemporalResetRampFrames; ++i)
            {
                ScreenSpaceHistoryUtility.RampTemporalWeight(configured, ref validFrames, resetHistory: false);
            }

            float settled = ScreenSpaceHistoryUtility.RampTemporalWeight(configured, ref validFrames, resetHistory: false);
            Assert.AreEqual(configured, settled);
            Assert.AreEqual(ScreenSpaceHistoryUtility.TemporalResetRampFrames, validFrames);
        }
    }
}
