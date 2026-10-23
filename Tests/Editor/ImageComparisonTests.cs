using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Editor.Validation;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ImageComparisonTests
    {
        static byte[] Half(params float[] values)
        {
            var bytes = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                ushort h = Mathf.FloatToHalf(values[i]);
                bytes[i * 2] = (byte)(h & 0xFF);
                bytes[i * 2 + 1] = (byte)(h >> 8);
            }
            return bytes;
        }

        [Test]
        public void IdenticalImages_HaveNoError()
        {
            byte[] image = { 10, 20, 30, 255, 40, 50, 60, 255 };
            FImageComparison result = ImageComparison.Compare(image, (byte[])image.Clone(), "R8G8B8A8_UNorm", 2, 1, 1.0f / 255.0f);
            Assert.IsTrue(result.comparable);
            Assert.AreEqual(0.0f, result.rmse);
            Assert.AreEqual(0.0f, result.outlierFraction);
            Assert.IsTrue(result.Passes(0.0f, 0.0f));
        }

        [Test]
        public void OneChangedPixel_IsOneOutlier_AlphaIgnored()
        {
            byte[] a = { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
            byte[] b = (byte[])a.Clone();
            b[4] = 255;  // pixel 1 red
            b[11] = 0;   // pixel 2 alpha only
            FImageComparison result = ImageComparison.Compare(a, b, "R8G8B8A8_SRGB", 4, 1, 2.0f / 255.0f);
            Assert.AreEqual(0.25f, result.outlierFraction, 1e-6f);
            Assert.AreEqual(1.0f, result.maxError, 1e-6f);
            Assert.IsFalse(result.Passes(0.01f, 0.005f));
        }

        [Test]
        public void HalfImages_DecodeAndFlagNaN()
        {
            byte[] a = Half(0.5f, 0.25f, 1.0f, 1.0f, 0.1f, 0.1f, 0.1f, 1.0f);
            byte[] b = Half(0.5f, 0.25f, 1.0f, 1.0f, float.NaN, 0.1f, 0.1f, 1.0f);
            Assert.AreEqual(0.25f, ImageComparison.Decode(a, 0, "R16G16B16A16_SFloat").y, 1e-3f);
            FImageComparison result = ImageComparison.Compare(a, b, "R16G16B16A16_SFloat", 2, 1, 0.01f);
            Assert.AreEqual(0.5f, result.outlierFraction, 1e-6f, "NaN counts as a full-scale difference.");
            Assert.IsFalse(result.comparable);
            Assert.IsFalse(result.Passes(1, 1), "Invalid data cannot pass even with permissive error thresholds.");
        }

        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        [TestCase(float.NegativeInfinity, false)]
        [TestCase(float.NaN, true)]
        [TestCase(float.PositiveInfinity, true)]
        [TestCase(float.NegativeInfinity, true)]
        public void SingleNonFinitePixel_InLargeImage_CannotBeDiluted(float value, bool inBaseline)
        {
            const int width = 640, height = 400;
            var baseline = new byte[width * height * 8];
            var candidate = (byte[])baseline.Clone();
            byte[] invalid = inBaseline ? baseline : candidate;
            ushort half = Mathf.FloatToHalf(value);
            invalid[invalid.Length - 8] = (byte)half;
            invalid[invalid.Length - 7] = (byte)(half >> 8);
            FImageComparison result = ImageComparison.Compare(baseline, candidate, "R16G16B16A16_SFloat", width, height, 4f / 255);
            Assert.AreEqual(1, result.nonFinitePixels);
            Assert.IsFalse(result.comparable, result.reason);
            Assert.IsFalse(result.Passes(0.01f, 0.005f));
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(-1, 1)]
        [TestCase(int.MaxValue, 2)]
        public void InvalidDimensions_AreNotComparable(int width, int height)
        {
            FImageComparison result = ImageComparison.Compare(new byte[8], new byte[8], "R8G8B8A8_UNorm", width, height, 0.01f);
            Assert.IsFalse(result.comparable);
            Assert.IsFalse(result.Passes(1, 1));
        }

        [Test]
        public void TrailingBytes_AreNotSilentlyIgnored()
        {
            FImageComparison result = ImageComparison.Compare(new byte[9], new byte[9], "R8G8B8A8_UNorm", 2, 1, 0.01f);
            Assert.IsFalse(result.comparable);
            Assert.IsFalse(result.Passes(1, 1));
        }

        [Test]
        public void ShortBuffer_IsNotComparable()
        {
            FImageComparison result = ImageComparison.Compare(new byte[4], new byte[8], "R8G8B8A8_UNorm", 2, 1, 0.01f);
            Assert.IsFalse(result.comparable);
            Assert.IsFalse(result.Passes(1, 1));
        }

        [Test]
        public void DefaultCases_HaveCreatorsAndCameras()
        {
            var cases = ImageRegressionRunner.DefaultCases();
            Assert.Greater(cases.Count, 0);
            foreach (ImageRegressionRunner.Case c in cases)
            {
                StringAssert.StartsWith("Assets/Scene/Validation/Validation_", c.scenePath);
                Assert.AreEqual("Camera", c.camera);
            }
        }
    }
}
