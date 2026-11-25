using NUnit.Framework;
using Unity.Collections;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowBudgetTests
    {
        static int Select(float[] scores, int[] records, int[] faces, int maxLights, int tileBudget, out int[] accepted, out int tiles, out int dropped)
        {
            using var scoreArray = new NativeArray<float>(scores, Allocator.Temp);
            using var recordArray = new NativeArray<int>(records, Allocator.Temp);
            using var faceArray = new NativeArray<int>(faces, Allocator.Temp);
            using var buffer = new NativeArray<int>(System.Math.Max(1, System.Math.Min(maxLights, scores.Length)), Allocator.Temp);
            int count = ShadowAllocator.SelectWithinBudget(scoreArray, recordArray, faceArray, maxLights, tileBudget, buffer, scores.Length, out tiles, out dropped);
            accepted = new int[count];
            NativeArray<int>.Copy(buffer, accepted, count);
            return count;
        }

        [Test]
        public void HighestScoresWin_WithinTheLightCount()
        {
            Select(new[] { 0.5f, 2.0f, 1.0f }, new[] { 10, 11, 12 }, new[] { 1, 1, 1 }, 2, 16, out int[] accepted, out int tiles, out int dropped);
            CollectionAssert.AreEqual(new[] { 11, 12 }, accepted);
            Assert.AreEqual(2, tiles);
            Assert.AreEqual(1, dropped);
        }

        [Test]
        public void SixFaceLightThatDoesNotFit_IsSkipped_SmallerOnesStillFit()
        {
            // Budget 16 tiles: two point lights (6 faces each) fit, the third does not, the spot (1 tile) still does.
            Select(new[] { 3.0f, 2.0f, 1.0f, 0.5f }, new[] { 0, 1, 2, 3 }, new[] { 6, 6, 6, 1 }, 8, 16, out int[] accepted, out int tiles, out int dropped);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, accepted);
            Assert.AreEqual(13, tiles);
            Assert.AreEqual(1, dropped);
        }

        [Test]
        public void EqualScores_KeepRecordOrder()
        {
            Select(new[] { 1.0f, 1.0f, 1.0f }, new[] { 7, 3, 5 }, new[] { 1, 1, 1 }, 2, 16, out int[] accepted, out _, out int dropped);
            CollectionAssert.AreEqual(new[] { 3, 5 }, accepted);
            Assert.AreEqual(1, dropped);
        }

        [Test]
        public void VariableBudget_DowngradesLowerPriorityLightsBeforeDropping()
        {
            // 1024x1024 atlas = 1,048,576 texels, min tile = 128.
            // Light 0 (score 10, 1 face, desired 1024 -> 1,048,576? Wait: test 5 lights wanting 512 each in 1024x1024:
            // Light 0..2 take 3 * 512^2 = 786,432; Light 3 wants 512 (262,144 -> leaves 0) -> wait: test 4 * 512 desired + 256 desired:
            // Let's take total 1,048,576 texels:
            //   Candidate 0: score 10, 1 face, desired 512 (262,144) -> accepted at 512, rem 786,432
            //   Candidate 1: score  9, 1 face, desired 512 (262,144) -> accepted at 512, rem 524,288
            //   Candidate 2: score  8, 1 face, desired 512 (262,144) -> accepted at 512, rem 262,144
            //   Candidate 3: score  7, 6 faces, desired 512 (6*262k=1.5M) -> steps to 256 (6*65536=393,216 > 262k) -> steps to 128 (6*16384=98,304 <= 262k): downgraded! rem 163,840
            //   Candidate 4: score  6, 1 face, desired 512 (262k) -> steps to 256 (65,536 <= 163k): downgraded! rem 98,304
            //   Candidate 5: score  5, 6 faces, desired 256 -> even at 128 needs 98,304 <= 98,304 -> accepted at 128! rem 0
            //   Candidate 6: score  4, 1 face, desired 128 -> dropped!
            long totalTexels = 1024L * 1024L;
            using var scores = new NativeArray<float>(new[] { 10f, 9f, 8f, 7f, 6f, 5f, 4f }, Allocator.Temp);
            using var records = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6 }, Allocator.Temp);
            using var faces = new NativeArray<int>(new[] { 1, 1, 1, 6, 1, 6, 1 }, Allocator.Temp);
            using var desired = new NativeArray<int>(new[] { 512, 512, 512, 512, 512, 256, 128 }, Allocator.Temp);
            using var acceptedRecords = new NativeArray<int>(16, Allocator.Temp);
            using var acceptedResolutions = new NativeArray<int>(16, Allocator.Temp);

            int count = ShadowAllocator.SelectWithinVariableBudget(
                scores, records, faces, desired, 16, totalTexels, 128,
                acceptedRecords, acceptedResolutions, 7,
                out long texelsUsed, out int downgraded, out int dropped);

            Assert.AreEqual(6, count);
            Assert.AreEqual(totalTexels, texelsUsed);
            Assert.AreEqual(3, downgraded);
            Assert.AreEqual(1, dropped);
            Assert.AreEqual(512, acceptedResolutions[0]);
            Assert.AreEqual(512, acceptedResolutions[1]);
            Assert.AreEqual(512, acceptedResolutions[2]);
            Assert.AreEqual(128, acceptedResolutions[3]);
            Assert.AreEqual(256, acceptedResolutions[4]);
            Assert.AreEqual(128, acceptedResolutions[5]);
            Assert.AreEqual(5, acceptedRecords[5]);
        }
    }
}
