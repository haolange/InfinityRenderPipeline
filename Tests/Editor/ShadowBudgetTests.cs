using NUnit.Framework;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowBudgetTests
    {
        static int Select(float[] scores, int[] records, int[] faces, int maxLights, int tileBudget, out int[] accepted, out int tiles, out int dropped)
        {
            var buffer = new int[System.Math.Min(maxLights, scores.Length)];
            int count = ShadowAllocator.SelectWithinBudget(scores, records, faces, maxLights, tileBudget, buffer, out tiles, out dropped);
            accepted = new int[count];
            System.Array.Copy(buffer, accepted, count);
            return count;
        }

        [Test]
        [Category("Unverified-0.5")]
        public void HighestScoresWin_WithinTheLightCount()
        {
            Select(new[] { 0.5f, 2.0f, 1.0f }, new[] { 10, 11, 12 }, new[] { 1, 1, 1 }, 2, 16, out int[] accepted, out int tiles, out int dropped);
            CollectionAssert.AreEqual(new[] { 11, 12 }, accepted);
            Assert.AreEqual(2, tiles);
            Assert.AreEqual(1, dropped);
        }

        [Test]
        [Category("Unverified-0.5")]
        public void SixFaceLightThatDoesNotFit_IsSkipped_SmallerOnesStillFit()
        {
            // Budget 16 tiles: two point lights (6 faces each) fit, the third does not, the spot (1 tile) still does.
            Select(new[] { 3.0f, 2.0f, 1.0f, 0.5f }, new[] { 0, 1, 2, 3 }, new[] { 6, 6, 6, 1 }, 8, 16, out int[] accepted, out int tiles, out int dropped);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, accepted);
            Assert.AreEqual(13, tiles);
            Assert.AreEqual(1, dropped);
        }

        [Test]
        [Category("Unverified-0.5")]
        public void EqualScores_KeepRecordOrder()
        {
            Select(new[] { 1.0f, 1.0f, 1.0f }, new[] { 7, 3, 5 }, new[] { 1, 1, 1 }, 2, 16, out int[] accepted, out _, out int dropped);
            CollectionAssert.AreEqual(new[] { 3, 5 }, accepted);
            Assert.AreEqual(1, dropped);
        }
    }
}
