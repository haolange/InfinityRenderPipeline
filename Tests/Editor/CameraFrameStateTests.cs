using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class CameraFrameStateTests
    {
        [Test]
        public void FrameFeatureSet_RequestProduceReset_Works()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            Assert.IsTrue(features.ShouldRecord(EFrameFeature.Depth));
            Assert.IsFalse(features.IsProduced(EFrameFeature.Depth));

            features.MarkProduced(EFrameFeature.Depth);
            Assert.IsTrue(features.IsProduced(EFrameFeature.Depth));

            features.Reset();
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.Depth));
            Assert.IsFalse(features.IsProduced(EFrameFeature.Depth));
        }

        [Test]
        public void FrameFeatureSet_DBufferIsOptional()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            features.MarkProduced(EFrameFeature.Depth);
            features.Request(EFrameFeature.GBuffer);
            features.MarkSupported(EFrameFeature.GBuffer);
            features.MarkProduced(EFrameFeature.GBuffer);
            features.Request(EFrameFeature.DeferredShading);
            features.MarkSupported(EFrameFeature.DeferredShading);
            features.MarkProduced(EFrameFeature.DeferredShading);
            features.Request(EFrameFeature.TAA);
            features.MarkSupported(EFrameFeature.TAA);
            features.MarkProduced(EFrameFeature.TAA);
            features.Request(EFrameFeature.Display);
            features.MarkSupported(EFrameFeature.Display);
            features.MarkProduced(EFrameFeature.Display);

            Assert.DoesNotThrow(() => features.EnsureRequiredProducers(superResolutionEnabled: false));
            Assert.IsFalse(features.ShouldRecord(EFrameFeature.DBuffer));
        }

        [Test]
        public void FrameFeatureSet_EnsureRequiredProducers_ThrowsWhenMissing()
        {
            var features = new FrameFeatureSet();
            features.Request(EFrameFeature.Depth);
            features.MarkSupported(EFrameFeature.Depth);
            features.MarkProduced(EFrameFeature.Depth);
            Assert.Throws<InvalidOperationException>(() => features.EnsureRequiredProducers(superResolutionEnabled: false));
        }

        [Test]
        public void CameraFrameState_CreateDispose_DestroysVolumeStack()
        {
            VolumeManager.instance.Initialize(null, null);
            CameraFrameState frameState = null;
            try
            {
                frameState = new CameraFrameState(42);
                Assert.AreEqual(42, frameState.cameraId);
                Assert.IsNotNull(frameState.volumeStack);
                Assert.IsNotNull(frameState.historyCache);
                Assert.IsNotNull(frameState.features);
                Assert.IsNotNull(frameState.cameraUniform);
                Assert.AreEqual(0.0f, frameState.exposureState.evCompensation);
            }
            finally
            {
                frameState?.Dispose();
                VolumeManager.instance.Deinitialize();
            }
        }
    }
}
