using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Editor.Validation;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ImageRegressionRequestTests
    {
        [Test]
        public void RunnerRequest_SatisfiesNormalFrameCaptureContract()
        {
            var fixture = new ImageRegressionRunner.Case
            {
                name = "RequestContract", scenePath = "Assets/RequestContract.unity", camera = "Case Camera",
                width = 64, height = 32, warmupFrames = 7
            };
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../intermediate", Guid.NewGuid().ToString("N")));
            RenderCaptureRequest request = ImageRegressionRunner.MakeCaptureRequest(fixture, directory);
            Assert.DoesNotThrow(() => RenderCaptureSession.Validate(request));
            Assert.AreEqual(new RectInt(0, 0, 64, 32), request.roi);
            Assert.AreEqual(1, request.frameCount);
            Assert.AreEqual(7, request.warmupFrames);
            Assert.AreEqual("Case Camera", request.camera);
            Assert.AreEqual("RequestContract", request.scene);
            CollectionAssert.AreEqual(new[] { "DisplayColor" }, request.buffers);
            Assert.IsFalse(request.includeConfidence);
            Assert.IsFalse(Directory.Exists(directory), "Request construction and validation must not produce files.");
        }
    }
}
