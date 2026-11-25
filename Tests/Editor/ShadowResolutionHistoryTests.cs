using System;
using NUnit.Framework;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowResolutionHistoryTests
    {
        [Test]
        public void ProjectedPixels_MapToPowerOfTwoTiers_AndCapSixFaceCubes()
        {
            Assert.AreEqual(128, ShadowResolutionHistory.TargetTierFromProjectedPixels(90f, 128, 1024));
            Assert.AreEqual(256, ShadowResolutionHistory.TargetTierFromProjectedPixels(260f, 128, 1024));
            Assert.AreEqual(512, ShadowResolutionHistory.TargetTierFromProjectedPixels(520f, 128, 1024));
            Assert.AreEqual(1024, ShadowResolutionHistory.TargetTierFromProjectedPixels(1200f, 128, 1024));

            var history = new ShadowResolutionHistory();
            int spotRes = history.Resolve(1, ulong.MaxValue, 1200f, 1, 128, 1024, 2048, frame: 1);
            int pointRes = history.Resolve(2, ulong.MaxValue, 1200f, 6, 128, 1024, 2048, frame: 1);
            Assert.AreEqual(1024, spotRes);
            Assert.AreEqual(512, pointRes, "Six-face cube lights are capped to atlasRes/4 so one point light cannot exhaust the atlas.");
        }

        [Test]
        public void HysteresisAndCooldown_PreventResolutionThrashingOnCameraJitter()
        {
            var history = new ShadowResolutionHistory();
            // Start at 256 (projected = 260px).
            int tier = history.Resolve(10, 1, 260f, 1, 128, 1024, 2048, frame: 1, cooldownFrames: 8);
            Assert.AreEqual(256, tier);

            // Jitter just above naive 384px threshold within upgrade deadband (< 256*1.5*1.25 = 480) after cooldown.
            tier = history.Resolve(10, 1, 410f, 1, 128, 1024, 2048, frame: 20, cooldownFrames: 8);
            Assert.AreEqual(256, tier, "Upgrade hysteresis deadband must hold 256 until projectedPixels >= 480.");

            // Cross upgrade hysteresis threshold well after cooldown -> jumps to 512.
            tier = history.Resolve(10, 1, 500f, 1, 128, 1024, 2048, frame: 30, cooldownFrames: 8);
            Assert.AreEqual(512, tier);

            // Immediately drop sharply on frame 32 (within cooldown = 8) -> cooldown holds 512.
            tier = history.Resolve(10, 1, 140f, 1, 128, 1024, 2048, frame: 32, cooldownFrames: 8);
            Assert.AreEqual(512, tier, "Cooldown frames must prevent immediate oscillation.");

            // After cooldown elapsed, stay in downgrade deadband (320 > 512*0.75*0.75 = 288) -> still 512.
            tier = history.Resolve(10, 1, 320f, 1, 128, 1024, 2048, frame: 40, cooldownFrames: 8);
            Assert.AreEqual(512, tier, "Downgrade hysteresis deadband must hold 512 until projectedPixels <= 288.");

            // Cross below 288 -> cleanly downgrades to 256.
            tier = history.Resolve(10, 1, 270f, 1, 128, 1024, 2048, frame: 41, cooldownFrames: 8);
            Assert.AreEqual(256, tier);
        }

        [Test]
        public void PrunesStaleEntriesAndAllocatesZeroManagedBytesAtSteadyState()
        {
            var history = new ShadowResolutionHistory();
            for (int id = 1; id <= 8; ++id)
                history.Resolve((ulong)id, 1, 300f, 1, 128, 1024, 2048, frame: 1);
            Assert.AreEqual(8, history.EntryCount);

            // Keep only lights 1..2 active up to frame 200; entries 3..8 unseen > 120 frames prune cleanly.
            for (int f = 100; f <= 260; f += 20)
            {
                history.Resolve(1, 1, 300f, 1, 128, 1024, 2048, frame: f);
                history.Resolve(2, 1, 300f, 1, 128, 1024, 2048, frame: f);
            }
            Assert.AreEqual(2, history.EntryCount);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 261; f < 600; ++f)
            {
                history.Resolve(1, 1, 300f, 1, 128, 1024, 2048, frame: f);
                history.Resolve(2, 1, 520f, 1, 128, 1024, 2048, frame: f);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, after - before, "Steady-state ShadowResolutionHistory.Resolve/Prune must allocate 0 managed bytes.");
        }
    }
}
