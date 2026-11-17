using System;
using NUnit.Framework;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class PipelineCapabilitiesTests
    {
        [Test]
        public void RequiredProducerOutsideFeatures_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(
                new[] { EFrameFeature.Depth, EFrameFeature.Display },
                new[] { EFrameFeature.Depth, EFrameFeature.GBuffer },
                false));
        }

        [Test]
        public void EmptyLists_Throw()
        {
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(Array.Empty<EFrameFeature>(), new[] { EFrameFeature.Depth }, false));
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(new[] { EFrameFeature.Depth }, null, false));
        }

        [Test]
        public void Records_ReflectsTheFeatureList()
        {
            var capabilities = new PipelineCapabilities(
                new[] { EFrameFeature.Depth, EFrameFeature.Display },
                new[] { EFrameFeature.Display },
                true);
            foreach (EFrameFeature feature in Enum.GetValues(typeof(EFrameFeature)))
            {
                bool listed = feature == EFrameFeature.Depth || feature == EFrameFeature.Display;
                Assert.AreEqual(listed, capabilities.Records(feature), feature.ToString());
            }
            Assert.IsTrue(capabilities.asyncCompute);
            CollectionAssert.AreEqual(new[] { EFrameFeature.Display }, capabilities.requiredProducers);
        }

        [Test]
        public void DesktopPipeline_RecordsEveryFeature()
        {
            foreach (EFrameFeature feature in Enum.GetValues(typeof(EFrameFeature)))
            {
                Assert.IsTrue(InfinityRenderPipeline.s_Capabilities.Records(feature), feature.ToString());
            }
        }
    }
}
