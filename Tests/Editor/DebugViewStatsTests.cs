using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DebugViewStatsTests
    {
        [Test]
        public void Enum_HasTenNamedValues()
        {
            Assert.AreEqual(10, System.Enum.GetNames(typeof(EDebugView)).Length);
            Assert.AreEqual(0, (int)EDebugView.None);
            Assert.AreEqual(1, (int)EDebugView.Albedo);
            Assert.AreEqual(2, (int)EDebugView.Normal);
            Assert.AreEqual(3, (int)EDebugView.Roughness);
            Assert.AreEqual(4, (int)EDebugView.AO);
            Assert.AreEqual(5, (int)EDebugView.SSR);
            Assert.AreEqual(6, (int)EDebugView.SSGI);
            Assert.AreEqual(7, (int)EDebugView.MotionMagnitude);
            Assert.AreEqual(8, (int)EDebugView.TAAConfidence);
            Assert.AreEqual(9, (int)EDebugView.PreTonemapLuma);
        }

        [Test]
        public void RGB2YCoCg_MatchesHandComputedTuples()
        {
            AssertYCoCg(1.0f, 1.0f, 1.0f, 4.0f, 0.0f, 0.0f);
            AssertYCoCg(1.0f, 0.0f, 0.0f, 1.0f, 2.0f, -1.0f);
            AssertYCoCg(0.0f, 1.0f, 0.0f, 2.0f, 0.0f, 2.0f);
            AssertYCoCg(0.0f, 0.0f, 1.0f, 1.0f, -2.0f, -1.0f);
            AssertYCoCg(0.18f, 0.18f, 0.18f, 0.72f, 0.0f, 0.0f);
        }

        [Test]
        public void ConstantGray18_HasZeroChromaAndHighPass()
        {
            const int width = 16;
            const int height = 16;
            Color[] pixels = new Color[width * height];
            Color gray = new Color(0.18f, 0.18f, 0.18f, 1.0f);
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = gray;
            }

            DebugViewStatResult stats = DebugViewStats.Compute(pixels, width, height);
            Assert.AreEqual(width * height, stats.pixelCount);
            Assert.AreEqual(0.18f, stats.meanR, 1e-5f);
            Assert.AreEqual(0.18f, stats.meanG, 1e-5f);
            Assert.AreEqual(0.18f, stats.meanB, 1e-5f);
            Assert.AreEqual(0.0f, stats.meanAbsCo, 1e-5f);
            Assert.AreEqual(0.0f, stats.meanAbsCg, 1e-5f);
            Assert.AreEqual(0.0f, stats.highPassEnergy, 1e-5f);
            Assert.AreEqual(0.0f, stats.clipPercent, 1e-5f);
            Assert.AreEqual(0.0f, stats.stdR, 1e-5f);
            Assert.AreEqual(0.0f, stats.lumaStddev, 1e-5f);
        }

        [Test]
        public void Checkerboard_HasMoreHighPassThanConstantGray()
        {
            const int width = 16;
            const int height = 16;
            Color[] grayPixels = new Color[width * height];
            Color[] checkerPixels = new Color[width * height];
            Color gray = new Color(0.18f, 0.18f, 0.18f, 1.0f);
            Color black = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            Color white = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    int i = y * width + x;
                    grayPixels[i] = gray;
                    checkerPixels[i] = ((x + y) & 1) == 0 ? black : white;
                }
            }

            DebugViewStatResult grayStats = DebugViewStats.Compute(grayPixels, width, height);
            DebugViewStatResult checkerStats = DebugViewStats.Compute(checkerPixels, width, height);
            Assert.Greater(checkerStats.highPassEnergy, grayStats.highPassEnergy);
        }

        static void AssertYCoCg(float r, float g, float b, float y, float co, float cg)
        {
            DebugViewYCoCg ycocg = DebugViewStats.RGB2YCoCg(r, g, b);
            Assert.AreEqual(y, ycocg.y, 1e-5f);
            Assert.AreEqual(co, ycocg.co, 1e-5f);
            Assert.AreEqual(cg, ycocg.cg, 1e-5f);
        }
    }
}
