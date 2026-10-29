using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Synthetic depth, closest and furthest pyramids. Hi-Z and the 2D march must agree, and the trace flags must
    // change the result. Rays end at the near/far planes and the screen edge.
    public class ScreenSpaceTraceContractGpuTests
    {
        const int Size = 64;
        const string ShaderPath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ScreenSpaceTraceContract.compute";

        [Test]
        public void Plane_HiZAndLinearAgreeWithinOneTexel()
        {
            float surface = ProjectedDepth(new Vector3(0, 0, -5));
            float[] depth = Fill(surface);
            Vector4[] hit = Trace(depth, new Vector3(0, 0, -2), new Vector3(0, 0, -1), 3);
            AssertHit(hit[0], surface);
            AssertHit(hit[1], surface);
            Assert.That(hit[1].x, Is.EqualTo(hit[0].x).Within(1f / Size));
            Assert.That(hit[1].y, Is.EqualTo(hit[0].y).Within(1f / Size));
        }

        [Test]
        public void Occluder_TraceBehindPassesItAndOtherwiseMisses()
        {
            float near = ProjectedDepth(new Vector3(0, 0, -2));
            float farWall = ProjectedDepth(new Vector3(0, 0, -6));
            float[] blocked = new float[Size * Size];
            for (int i = 0; i < blocked.Length; ++i) blocked[i] = i % Size < Size / 2 ? near : farWall;

            Vector3 origin = new Vector3(-0.35f, 0, -4);
            Vector3 direction = new Vector3(0.45f, 0, -1);
            Vector4[] closed = Trace(blocked, origin, direction, 0);
            Assert.AreEqual(0, closed[0].w, "Hi-Z stops behind the occluder when trace-behind is off.");
            Assert.AreEqual(0, closed[1].w, "Linear stops behind the occluder when trace-behind is off.");
            Vector4[] passed = Trace(blocked, origin, direction, 3);
            AssertHit(passed[0], farWall);
            AssertHit(passed[1], farWall);
            Assert.Greater(passed[0].x, 0.5f, "The hit is on the far wall, past the occluder.");
        }

        [Test]
        public void Silhouette_TraceBehindHitsTheFarWallNotTheEdge()
        {
            float sky = SystemInfo.usesReversedZBuffer ? 0 : 1;
            float bar = ProjectedDepth(new Vector3(0, 0, -2));
            float wall = ProjectedDepth(new Vector3(0, 0, -6));
            float[] depth = Fill(sky);
            for (int y = 0; y < Size; ++y)
            {
                for (int x = 30; x <= 33; ++x) depth[y * Size + x] = bar;
                for (int x = 36; x < Size; ++x) depth[y * Size + x] = wall;
            }
            Vector3 origin = new Vector3(-0.35f, 0, -4);
            Vector3 direction = new Vector3(0.45f, 0, -1).normalized;
            AssertSilhouette(depth, origin, direction, false);
            AssertSilhouette(depth, origin, direction, true);
        }

        static void AssertSilhouette(float[] depth, Vector3 origin, Vector3 direction, bool relative)
        {
            float wall = ProjectedDepth(new Vector3(0, 0, -6));
            Vector4[] hit = Trace(depth, origin, direction, 3, relative);
            string mode = relative ? "relative " : "planar ";
            Assert.AreEqual(1, hit[0].w, mode + "Hi-Z " + hit[0] + " linear " + hit[1]);
            AssertHit(hit[0], wall);
            Assert.Greater(hit[0].x, 36f / Size, mode + "Hi-Z passes the foreground edge.");
            AssertHit(hit[1], wall);
            Assert.Greater(hit[1].x, 36f / Size, mode + "Linear does not accept the silhouette as a hit.");
        }

        [Test]
        public void GrazingPlane_BothModesHitTheRecedingSurface()
        {
            float nearRaw = ProjectedDepth(new Vector3(0, 0, -2.5f));
            float farRaw = ProjectedDepth(new Vector3(0, 0, -8f));
            float[] depth = new float[Size * Size];
            for (int x = 0; x < Size; ++x)
            {
                float raw = Mathf.Lerp(nearRaw, farRaw, x / (float)(Size - 1));
                for (int y = 0; y < Size; ++y) depth[y * Size + x] = raw;
            }
            Vector3 origin = ViewAtRaw(6, RawCloser(Mathf.Lerp(nearRaw, farRaw, 6f / (Size - 1)), 0.97f));
            Vector3 target = ViewAtRaw(58, Mathf.Lerp(nearRaw, farRaw, 58f / (Size - 1)));
            Vector3 direction = (target - origin).normalized;
            foreach (bool relative in new[] { false, true })
            {
                Vector4[] hit = Trace(depth, origin, direction, 3, relative);
                string mode = relative ? "relative" : "planar";
                AssertHit(hit[0], depth[RampIndex(hit[0])]);
                AssertHit(hit[1], depth[RampIndex(hit[1])]);
                Assert.Greater(hit[0].x * Size, 8f, mode + " Hi-Z " + hit[0]);
                Assert.Greater(hit[1].x * Size, 8f, mode + " linear " + hit[1]);
            }
        }

        [Test]
        public void ThinRod_PlanarModeDoesNotCatchTheEdge()
        {
            float sky = SystemInfo.usesReversedZBuffer ? 0 : 1;
            float bar = ProjectedDepth(new Vector3(0, 0, -2));
            float[] depth = Fill(sky);
            for (int y = 0; y < Size; ++y) depth[y * Size + 32] = bar;
            Vector3 origin = new Vector3(-0.35f, 0, -4);
            Vector3 direction = new Vector3(0.45f, 0, -1).normalized;
            Vector4[] planar = Trace(depth, origin, direction, 3, false);
            AssertNotOnRod(planar[0], bar, "Planar Hi-Z");
            AssertNotOnRod(planar[1], bar, "Planar linear");
            Vector4[] relative = Trace(depth, origin, direction, 3, true);
            Assert.IsFalse(float.IsNaN(relative[0].w) || float.IsNaN(relative[1].w));
        }

        static void AssertNotOnRod(Vector4 hit, float bar, string label)
        {
            if (hit.w <= 0) return;
            Assert.That(Mathf.Abs(hit.z - bar), Is.GreaterThan(1e-3f), label + " thickened the rod " + hit);
            Assert.AreNotEqual(32, Mathf.RoundToInt(hit.x * Size - 0.5f), label + " landed on the rod texel " + hit);
        }

        [Test]
        public void TowardRay_RequiresItsFlag()
        {
            float surface = ProjectedDepth(new Vector3(0, 0, -2.5f));
            Vector3 origin = new Vector3(0, 0, -4);
            Vector3 direction = new Vector3(0.2f, 0, 1);
            Vector4[] closed = Trace(Fill(surface), origin, direction, 1);
            Assert.AreEqual(0, closed[0].w);
            Assert.AreEqual(0, closed[1].w);
            Vector4[] open = Trace(Fill(surface), origin, direction, 3);
            AssertHit(open[0], surface);
            AssertHit(open[1], surface);
        }

        [Test]
        public void Ray_ReachesTheScreenEdge()
        {
            float far = SystemInfo.usesReversedZBuffer ? 0 : 1;
            float[] depth = Fill(far);
            Vector3 origin = new Vector3(-0.4f, 0, -3);
            Vector3 direction = new Vector3(1, 0, -0.05f).normalized;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false);
            float edgeDepth = 0;
            bool placed = false;
            for (int step = 1; step < 800 && !placed; ++step)
            {
                Vector3 point = origin + direction * (step * 0.02f);
                Vector4 clip = projection * new Vector4(point.x, point.y, point.z, 1);
                if (clip.w <= 0.01f) break;
                float u = clip.x / clip.w * 0.5f + 0.5f;
                float v = clip.y / clip.w * 0.5f + 0.5f;
                if (u < (Size - 4f) / Size || u >= 1 || v < 0 || v >= 1) continue;
                edgeDepth = clip.z / clip.w;
                int y = Mathf.Clamp((int)(v * Size), 0, Size - 1);
                for (int x = Size - 4; x < Size; ++x)
                    depth[y * Size + x] = edgeDepth;
                placed = true;
            }
            Assert.IsTrue(placed, "The ray must cross the screen edge inside the frustum.");
            Vector4[] hit = Trace(depth, origin, direction, 3);
            AssertHit(hit[0], edgeDepth);
            Assert.Greater(hit[0].x, 0.9f, "Hi-Z reaches the screen edge.");
            AssertHit(hit[1], edgeDepth);
            Assert.Greater(hit[1].x, 0.9f, "Linear reaches the screen edge.");
        }

        static float ProjectedDepth(Vector3 view)
        {
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false);
            Vector4 clip = projection * new Vector4(view.x, view.y, view.z, 1);
            return clip.z / clip.w;
        }

        static float[] Fill(float value)
        {
            var depth = new float[Size * Size];
            Array.Fill(depth, value);
            return depth;
        }

        static void AssertHit(Vector4 hit, float depth)
        {
            Assert.AreEqual(1, hit.w);
            Assert.That(hit.z, Is.EqualTo(depth).Within(1e-4f));
            Assert.That(hit.x * Size - 0.5f, Is.EqualTo(Mathf.Round(hit.x * Size - 0.5f)).Within(1e-4f));
            Assert.That(hit.y * Size - 0.5f, Is.EqualTo(Mathf.Round(hit.y * Size - 0.5f)).Within(1e-4f));
        }

        static float EyeDepth(float raw)
        {
            const float near = 0.1f, far = 100f;
            float denominator = SystemInfo.usesReversedZBuffer ? near + raw * (far - near) : far - raw * (far - near);
            return near * far / Mathf.Max(denominator, 1e-8f);
        }

        static float RawCloser(float raw, float eyeScale)
        {
            const float near = 0.1f, far = 100f;
            float eye = EyeDepth(raw) * eyeScale;
            float denominator = near * far / eye;
            return SystemInfo.usesReversedZBuffer ? (denominator - near) / (far - near) : (far - denominator) / (far - near);
        }

        static Vector3 ViewAtRaw(int x, float raw)
        {
            float u = ((x + 0.5f) / Size) * 2f - 1f;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false);
            Vector4 view = projection.inverse * new Vector4(u, 0f, raw, 1f);
            view /= view.w;
            return new Vector3(view.x, view.y, view.z);
        }

        static int RampIndex(Vector4 hit)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(hit.x * Size - 0.5f), 0, Size - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(hit.y * Size - 0.5f), 0, Size - 1);
            return y * Size + x;
        }

        static Vector4[] Trace(float[] depth, Vector3 origin, Vector3 direction, int flags, bool relative = false)
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A compute device is required.");
            bool reversed = SystemInfo.usesReversedZBuffer;
            var surface = new Texture2D(Size, Size, TextureFormat.RFloat, false, true);
            var closest = new Texture2D(Size / 2, Size / 2, TextureFormat.RFloat, true, true);
            var furthest = new Texture2D(Size / 2, Size / 2, TextureFormat.RFloat, true, true);
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            try
            {
                surface.SetPixelData(depth, 0);
                surface.Apply(false, false);
                BuildPyramid(depth, Size, reversed, true, closest);
                BuildPyramid(depth, Size, reversed, false, furthest);
                int kernel = shader.FindKernel("TracePair");
                shader.SetMatrix("Projection", GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false));
                shader.SetVector("ScreenSpaceDepthParams", new Vector4(0.1f, 100, 0, reversed ? 1 : 0));
                shader.SetVector("Origin", origin);
                shader.SetVector("Direction", direction.normalized);
                shader.SetInt("Budget", 32);
                shader.SetInt("Steps", 48);
                shader.SetInt("BinarySteps", 6);
                shader.SetInt("Flags", flags);
                if (relative) shader.EnableKeyword("SCREEN_TRACE_RELATIVE_THICKNESS");
                else shader.DisableKeyword("SCREEN_TRACE_RELATIVE_THICKNESS");
                shader.SetTexture(kernel, "SurfaceDepth", surface);
                shader.SetTexture(kernel, "ClosestPyramid", closest);
                shader.SetTexture(kernel, "FurthestPyramid", furthest);
                using (var output = new ComputeBuffer(2, 16))
                {
                    shader.SetBuffer(kernel, "Results", output);
                    shader.Dispatch(kernel, 1, 1, 1);
                    var request = AsyncGPUReadback.Request(output);
                    request.WaitForCompletion();
                    Assert.IsFalse(request.hasError);
                    return request.GetData<Vector4>().ToArray();
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shader);
                UnityEngine.Object.DestroyImmediate(surface);
                UnityEngine.Object.DestroyImmediate(closest);
                UnityEngine.Object.DestroyImmediate(furthest);
            }
        }

        static void BuildPyramid(float[] full, int size, bool reversed, bool closestPyramid, Texture2D texture)
        {
            float[] source = full;
            int sourceSize = size;
            int mip = 0;
            while (sourceSize > 1)
            {
                int destSize = Math.Max(1, sourceSize / 2);
                var dest = new float[destSize * destSize];
                for (int y = 0; y < destSize; ++y)
                for (int x = 0; x < destSize; ++x)
                {
                    float value = closestPyramid ? (reversed ? 0 : 1) : (reversed ? 1 : 0);
                    for (int oy = 0; oy < 2; ++oy)
                    for (int ox = 0; ox < 2; ++ox)
                    {
                        int sx = Math.Min(sourceSize - 1, x * 2 + ox);
                        int sy = Math.Min(sourceSize - 1, y * 2 + oy);
                        float sample = source[sy * sourceSize + sx];
                        if (closestPyramid) value = reversed ? Math.Max(value, sample) : Math.Min(value, sample);
                        else value = reversed ? Math.Min(value, sample) : Math.Max(value, sample);
                    }
                    dest[y * destSize + x] = value;
                }
                texture.SetPixelData(dest, mip);
                source = dest;
                sourceSize = destSize;
                mip++;
                if (mip >= texture.mipmapCount) break;
            }
            texture.Apply(false, false);
        }
    }
}
