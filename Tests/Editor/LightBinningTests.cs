using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class LightBinningTests
    {
        static Matrix4x4 GpuProjection(float fov, float aspect, float near, float far) =>
            GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(fov, aspect, near, far), false);

        [TestCase(false)]
        [TestCase(true)]
        public void TileRect_ContainsPixelsReconstructedByTheActualCameraUniform(bool orthographic)
        {
            var go = new GameObject("TileRectConsumerProjection") { hideFlags = HideFlags.DontSave };
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.orthographic = orthographic;
                camera.orthographicSize = 4;
                camera.fieldOfView = 60;
                camera.aspect = 320f / 180;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 300;
                var screen = new int2(320, 180);
                var uniform = new CameraUniform();
                uniform.UpdateCurrFrameData(camera, screen, true);
                Matrix4x4 consumerProjection = uniform.matrix_InvFlipYJitterProj.inverse;
                Vector4 projectedDepth = consumerProjection * new Vector4(0, 0, -10, 1);
                float depth = projectedDepth.z / projectedDepth.w;
                Matrix4x4 producerProjection = GraphicsUtility.GetComputeGpuProjection(camera);
                int controlsMissing = 0;
                foreach (var pixel in new[] { new int2(40, 35), new int2(230, 35), new int2(40, 145), new int2(230, 145) })
                {
                    Vector4 view = uniform.matrix_InvFlipYJitterProj * new Vector4(
                        (pixel.x + .5f) / screen.x * 2 - 1, (pixel.y + .5f) / screen.y * 2 - 1, depth, 1);
                    Vector3 center = new Vector3(view.x, view.y, view.z) / view.w;
                    int2 tile = pixel / LightBinning.TileSize;
                    Assert.IsTrue(LightBinning.TryComputeTileRect(center, .02f, producerProjection, orthographic, .1f, screen, out int4 rect));
                    Assert.IsTrue(math.all(tile >= rect.xy) && math.all(tile <= rect.zw), $"consumer pixel {pixel} outside producer rect {rect}");
                    bool controlVisible = LightBinning.TryComputeTileRect(center, .02f,
                        GL.GetGPUProjectionMatrix(camera.projectionMatrix, true), orthographic, .1f, screen, out int4 control);
                    if (!controlVisible || math.any(tile < control.xy) || math.any(tile > control.zw)) controlsMissing++;
                }
                if (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) != producerProjection)
                    Assert.Greater(controlsMissing, 0, "The asymmetric oracle must distinguish the old projection convention.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DepthToBin_IsMonotonicAndCoversNearToFar()
        {
            Vector4 bins = LightBinning.ZBinParams(0.1f, 1000, LightBinning.ZBinCount);
            Assert.AreEqual(0, LightBinning.DepthToBin(0.1f, bins));
            Assert.AreEqual(LightBinning.ZBinCount - 1, LightBinning.DepthToBin(999.9f, bins));
            int previous = 0;
            for (float depth = 0.1f; depth < 1000; depth *= 1.07f)
            {
                int bin = LightBinning.DepthToBin(depth, bins);
                Assert.GreaterOrEqual(bin, previous);
                previous = bin;
            }
            // Logarithmic: the bin of 1 m is far from the bin of 10 m even though both are near the camera.
            Assert.Greater(LightBinning.DepthToBin(10, bins) - LightBinning.DepthToBin(1, bins), 20);
        }

        [Test]
        public void ZBins_HoldTheIndexRangeOfOverlappingLights()
        {
            Vector4 parameters = LightBinning.ZBinParams(0.1f, 100, 16);
            var ranges = new NativeArray<float2>(3, Allocator.Temp);
            ranges[0] = new float2(1, 2);
            ranges[1] = new float2(1.5f, 50);
            ranges[2] = new float2(60, 90);
            var bins = new NativeArray<uint>(16, Allocator.Temp);
            try
            {
                LightBinning.BuildZBins(ranges, 3, parameters, bins);
                for (int b = 0; b < 16; ++b)
                {
                    LightBinning.UnpackBin(bins[b], out int min, out int max);
                    for (int i = 0; i < 3; ++i)
                    {
                        bool overlaps = LightBinning.DepthToBin(ranges[i].x, parameters) <= b && b <= LightBinning.DepthToBin(ranges[i].y, parameters);
                        if (overlaps) Assert.IsTrue(min <= i && i <= max, $"bin {b} must include light {i}");
                    }
                }
                Assert.AreEqual(LightBinning.EmptyBin, bins[0], "Nothing reaches the nearest bin.");
            }
            finally
            {
                ranges.Dispose();
                bins.Dispose();
            }
        }

        [Test]
        public void TileRect_ContainsTheProjectionOfEverySurfacePoint()
        {
            int2 screen = new int2(1920, 1080);
            Matrix4x4 projection = GpuProjection(60, 1920f / 1080f, 0.1f, 500);
            var random = new Unity.Mathematics.Random(1234);
            int tested = 0;
            for (int n = 0; n < 400; ++n)
            {
                Vector3 center = new Vector3(random.NextFloat(-40, 40), random.NextFloat(-25, 25), -random.NextFloat(1, 120));
                float radius = random.NextFloat(0.1f, 15);
                bool visible = LightBinning.TryComputeTileRect(center, radius, projection, false, 0.1f, screen, out int4 rect);
                int2 tiles = LightBinning.TileCount(screen);
                for (int s = 0; s < 64; ++s)
                {
                    Vector3 point = center + (Vector3)(random.NextFloat3Direction() * radius);
                    if (-point.z <= 0.1f) continue;
                    Vector4 clip = projection * new Vector4(point.x, point.y, point.z, 1);
                    float2 ndc = new float2(clip.x, clip.y) / clip.w;
                    if (math.any(ndc < -1) || math.any(ndc > 1)) continue;
                    int2 tile = math.clamp((int2)math.floor((ndc * 0.5f + 0.5f) * screen / LightBinning.TileSize), 0, tiles - 1);
                    Assert.IsTrue(visible, $"sphere {center} r={radius} has an on-screen point but was rejected");
                    Assert.IsTrue(math.all(tile >= rect.xy) && math.all(tile <= rect.zw), $"sphere {center} r={radius}: tile {tile} outside {rect}");
                    ++tested;
                }
            }
            Assert.Greater(tested, 1000);
        }

        [Test]
        public void SphereReachingTheNearPlane_CoversEveryTile_AndSphereBehindIsRejected()
        {
            int2 screen = new int2(1280, 720);
            Matrix4x4 projection = GpuProjection(60, 1280f / 720f, 0.1f, 500);
            Assert.IsTrue(LightBinning.TryComputeTileRect(new Vector3(0, 0, -1), 2, projection, false, 0.1f, screen, out int4 rect));
            int2 tiles = LightBinning.TileCount(screen);
            Assert.AreEqual(new int4(0, 0, tiles.x - 1, tiles.y - 1), rect);
            Assert.IsFalse(LightBinning.TryComputeTileRect(new Vector3(0, 0, 10), 2, projection, false, 0.1f, screen, out _), "Behind the camera");
            Assert.IsFalse(LightBinning.TryComputeTileRect(new Vector3(500, 0, -10), 2, projection, false, 0.1f, screen, out _), "Far off screen");
        }
    }
}
