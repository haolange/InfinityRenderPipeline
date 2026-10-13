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
        [Category("Unverified-0.5")]
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
        [Category("Unverified-0.5")]
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
        [Category("Unverified-0.5")]
        public void HalfImages_DecodeAndFlagNaN()
        {
            byte[] a = Half(0.5f, 0.25f, 1.0f, 1.0f, 0.1f, 0.1f, 0.1f, 1.0f);
            byte[] b = Half(0.5f, 0.25f, 1.0f, 1.0f, float.NaN, 0.1f, 0.1f, 1.0f);
            Assert.AreEqual(0.25f, ImageComparison.Decode(a, 0, "R16G16B16A16_SFloat").y, 1e-3f);
            FImageComparison result = ImageComparison.Compare(a, b, "R16G16B16A16_SFloat", 2, 1, 0.01f);
            Assert.AreEqual(0.5f, result.outlierFraction, 1e-6f, "NaN counts as a full-scale difference.");
        }

        [Test]
        [Category("Unverified-0.5")]
        public void ShortBuffer_IsNotComparable()
        {
            FImageComparison result = ImageComparison.Compare(new byte[4], new byte[8], "R8G8B8A8_UNorm", 2, 1, 0.01f);
            Assert.IsFalse(result.comparable);
            Assert.IsFalse(result.Passes(1, 1));
        }

        [Test]
        [Category("Unverified-0.5")]
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
