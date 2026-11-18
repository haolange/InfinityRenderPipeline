using System;
using NUnit.Framework;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class PipelineCapabilitiesTests
    {
        static readonly MeshPassDesc[] s_MeshPasses = { new MeshPassDesc("RasterShadowPass", "Shadow", 0, 2999, EMeshEntityFlags.CastShadow) };
        static readonly MeshPassId s_ShadowPass = new MeshPassId(0);

        [Test]
        public void RequiredProducerOutsideFeatures_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(
                new[] { EFrameFeature.Depth, EFrameFeature.Display },
                new[] { EFrameFeature.Depth, EFrameFeature.GBuffer },
                false, s_MeshPasses, s_ShadowPass));
        }

        [Test]
        public void EmptyLists_Throw()
        {
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(Array.Empty<EFrameFeature>(), new[] { EFrameFeature.Depth }, false, s_MeshPasses, s_ShadowPass));
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(new[] { EFrameFeature.Depth }, null, false, s_MeshPasses, s_ShadowPass));
        }

        [Test]
        public void ShadowCasterPassOutsideTheMeshPasses_Throws()
        {
            EFrameFeature[] features = { EFrameFeature.Depth };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineCapabilities(features, features, false, s_MeshPasses, new MeshPassId(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineCapabilities(features, features, false, s_MeshPasses, new MeshPassId(-1)));
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(features, features, false, null, s_ShadowPass));
            Assert.Throws<ArgumentException>(() => new PipelineCapabilities(features, features, false, new MeshPassDesc[0], s_ShadowPass));
        }

        [Test]
        public void Records_ReflectsTheFeatureList()
        {
            var capabilities = new PipelineCapabilities(
                new[] { EFrameFeature.Depth, EFrameFeature.Display },
                new[] { EFrameFeature.Display },
                true, s_MeshPasses, s_ShadowPass);
            foreach (EFrameFeature feature in Enum.GetValues(typeof(EFrameFeature)))
            {
                bool listed = feature == EFrameFeature.Depth || feature == EFrameFeature.Display;
                Assert.AreEqual(listed, capabilities.Records(feature), feature.ToString());
            }
            Assert.IsTrue(capabilities.asyncCompute);
            CollectionAssert.AreEqual(new[] { EFrameFeature.Display }, capabilities.requiredProducers);
            Assert.AreEqual(1, capabilities.meshPasses.Length);
            Assert.AreNotSame(s_MeshPasses, capabilities.meshPasses, "The capabilities own their copy of the mesh pass table.");
            Assert.AreEqual(0, capabilities.shadowCasterPass.index);
        }

        [Test]
        public void DesktopPipeline_RecordsEveryFeature_AndEveryMeshPass()
        {
            foreach (EFrameFeature feature in Enum.GetValues(typeof(EFrameFeature)))
            {
                Assert.IsTrue(InfinityRenderPipeline.s_Capabilities.Records(feature), feature.ToString());
            }
            MeshPassDesc[] passes = InfinityRenderPipeline.s_Capabilities.meshPasses;
            Assert.AreEqual(InfinityMeshPasses.all.Length, passes.Length);
            Assert.AreEqual("RasterShadowPass", passes[InfinityRenderPipeline.s_Capabilities.shadowCasterPass.index].lightMode);
        }
    }
}
