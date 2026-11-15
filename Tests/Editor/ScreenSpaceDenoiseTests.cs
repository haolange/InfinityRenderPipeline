using System.IO;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.Volume;
using static InfinityTech.Rendering.Pipeline.Tests.VolumeTestUtility;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ScreenSpaceDenoiseTests
    {
        [Test]
        public void IsActive_GatesGTAORequest()
        {
            ScreenSpaceAmbientOcclusion ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            try
            {
                ssao.active = true;
                Assert.IsFalse(VolumeComponentActive(ssao));
                Assert.IsFalse(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));

                ssao.enable.value = true;
                Assert.IsTrue(VolumeComponentActive(ssao));
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
        public void ScreenSpaceReflection_ResolveReusesAtLeastTwoRaysByDefault()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            try
            {
                Assert.GreaterOrEqual(ssr.ResolveSamples.value, 2);
            }
            finally
            {
                Object.DestroyImmediate(ssr);
            }
        }

        [Test]
        public void CompositeShader_DoesNotMultiplyAOOnSSROrSSGI()
        {
            string shaderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Shaders/Pass/ScreenSpace/Compute_ScreenSpaceComposite.compute"));
            Assert.IsTrue(File.Exists(shaderPath), shaderPath);
            string source = File.ReadAllText(shaderPath);
            Assert.IsTrue(source.Contains("AO is applied once in DeferredShading on IBL"));
            Assert.IsFalse(source.Contains("* ao"));
            Assert.IsFalse(source.Contains("COMPOSITE_AO"));
        }

        [Test]
        public void CompositePass_DoesNotBindCOMPOSITE_AO()
        {
            string passPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Runtime/Pipeline/Pass/ScreenSpace/ScreenSpaceCompositePass.cs"));
            Assert.IsTrue(File.Exists(passPath), passPath);
            string source = File.ReadAllText(passPath);
            Assert.IsFalse(source.Contains("COMPOSITE_AO"));
            Assert.IsFalse(source.Contains("hasAO"));
        }

        [Test]
        public void DeferredShading_ReadsOcclusionAndContactShadowOnlyWhenTheirFeaturesRecord()
        {
            string passPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline/Runtime/Pipeline/Pass/Lighting/DeferredShadingPass.cs"));
            Assert.IsTrue(File.Exists(passPath), passPath);
            string source = File.ReadAllText(passPath);
            // The feature set decides whether the optional input exists; QueryTexture then fails loudly if the producer did not register it.
            Assert.IsTrue(source.Contains("bool hasAO = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);"));
            Assert.IsTrue(source.Contains("hasAO ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.OcclusionBuffer) : default"));
            Assert.IsTrue(source.Contains("bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);"));
            Assert.IsTrue(source.Contains("hasContactShadow ? m_RGBuilder.scoper.QueryTexture(InfinityShaderIDs.ContactShadowBuffer) : default"));
            Assert.IsFalse(source.Contains("TryQueryTexture(InfinityShaderIDs.OcclusionBuffer"));
            Assert.IsFalse(source.Contains("TryQueryTexture(InfinityShaderIDs.ContactShadowBuffer"));
        }


    }
}
