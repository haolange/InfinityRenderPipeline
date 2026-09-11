using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class QualityLifecycleTests
    {
        [Test]
        public void CameraDimensionDescriptor_EqualsByFields()
        {
            var a = new CameraDimensionDescriptor(new Unity.Mathematics.int2(1920, 1080), 0.75f, true);
            var b = new CameraDimensionDescriptor(new Unity.Mathematics.int2(1920, 1080), 0.75f, true);
            Assert.AreEqual(a.internalSize, b.internalSize);
            Assert.IsTrue(a.superResolution);
            Assert.AreEqual(1440, a.internalSize.x);
            Assert.AreEqual(810, a.internalSize.y);
        }

        [Test]
        public void GlobalSettings_ResolveDefaultProfileOrThrow()
        {
            try
            {
                VolumeProfile profile = InfinityRenderPipelineGlobalSettings.ResolveDefaultVolumeProfile();
                Assert.IsNotNull(profile);
            }
            catch (System.InvalidOperationException)
            {
                Assert.Pass("GlobalSettings profile is required and correctly fails closed when missing.");
            }
        }
    }
}
