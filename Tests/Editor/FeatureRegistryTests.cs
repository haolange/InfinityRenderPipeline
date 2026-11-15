using System;
using NUnit.Framework;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class FeatureRegistryTests
    {
        static FeatureSupportContext NoShaders(bool temporal) => new FeatureSupportContext
        {
            resourceReasons = FeatureRegistry.ResolveResourceSupport(new PipelineKernels(new InfinityRenderPipelineRuntimeShaders()), new InfinityRenderPipelineRuntimeTextures()),
            temporal = temporal,
            rayTracedOcclusionReady = false,
            rayTracedOcclusionReason = "test",
        };

        [Test]
        public void EveryFeature_HasDescriptor()
        {
            foreach (EFrameFeature feature in Enum.GetValues(typeof(EFrameFeature)))
            {
                Assert.IsNotNull(FeatureRegistry.Get(feature), feature.ToString());
                Assert.AreEqual(feature, FeatureRegistry.Get(feature).id);
            }
        }

        [Test]
        public void FeatureWithoutRequirements_Records()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.Request(EFrameFeature.Display);
            FeatureRegistry.Resolve(features, NoShaders(true));
            Assert.IsTrue(features.ShouldRecord(EFrameFeature.Depth));
            Assert.IsTrue(features.ShouldRecord(EFrameFeature.Display));
            Assert.IsFalse(features.IsRequested(EFrameFeature.GBuffer), "Resolve must not request what nobody asked for.");
        }

        [Test]
        public void MissingShader_BlocksWithReason()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.GTAO);
            FeatureRegistry.Resolve(features, NoShaders(true));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.GTAO));
            StringAssert.Contains("ssaoShader", features.GetUnsupportedReason(EFrameFeature.GTAO));
            Assert.IsFalse(features.IsRequested(EFrameFeature.HalfResDownsample), "Dependencies of a blocked feature are not requested.");
        }

        [Test]
        public void MissingUngatedKernel_FailsResolution()
        {
            var kernels = new PipelineKernels(new InfinityRenderPipelineRuntimeShaders());
            var failure = Assert.Throws<InvalidOperationException>(kernels.RequireUngatedKernels);
            StringAssert.Contains("cubeFilterShader", failure.Message);
        }

        [Test]
        public void NonTemporalCamera_BlocksHistoryFeatures()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.TAA);
            features.Request(EFrameFeature.SSR);
            FeatureRegistry.Resolve(features, NoShaders(false));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.TAA));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.SSR));
            StringAssert.Contains("without history", features.GetUnsupportedReason(EFrameFeature.TAA));
            Assert.IsFalse(features.IsRequested(EFrameFeature.HiZ));
        }

        [Test]
        public void CameraSupport_ReportsItsReason()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.RTAO);
            FeatureRegistry.Resolve(features, NoShaders(true));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.RTAO));
            Assert.AreEqual("test", features.GetUnsupportedReason(EFrameFeature.RTAO));
        }
    }
}
