using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class HiZGpuTests
    {
        const float Sentinel = -19;
        [TestCase(64, 64, false, false)]
        [TestCase(127, 65, false, false)]
        [TestCase(127, 65, true, false)]
        [TestCase(127, 65, true, true)]
        [TestCase(1, 129, true, false)]
        [TestCase(129, 1, true, false)]
        [TestCase(3, 5, true, false)]
        [TestCase(3, 5, true, true)]
        [TestCase(1638, 1778, false, false)]
        public void EveryMipMatchesQuadExtrema_AndIgnoresPadding(int width, int height, bool padded, bool farOnly)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/PyramidDepth/Compute_PyramidDepth.compute");
            Assert.IsNotNull(shader);
            int bw = (width + 1) / 2, bh = (height + 1) / 2;
            int sw = padded ? width + 13 : width, sh = padded ? height + 9 : height;
            int pw = padded ? (bw + 63) / 64 * 64 : bw, ph = padded ? (bh + 63) / 64 * 64 : bh;
            var source = new Texture2D(sw, sh, TextureFormat.RFloat, false, true);
            var seed = new Texture2D(pw, ph, TextureFormat.RFloat, true, true);
            var closest = Target(pw, ph);
            var furthest = Target(pw, ph);
            using var commands = new CommandBuffer();
            try
            {
                var pixels = new float[sw * sh];
                for (int y = 0; y < sh; ++y) for (int x = 0; x < sw; ++x)
                    pixels[y * sw + x] = (x + y) % 2 == 0 ? -37 : 37;
                var raw = new float[width * height];
                for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x)
                    raw[y * width + x] = ((x * 73L + y * 31L) % 1009) / 1009f;
                raw[raw.Length - 1] = 0;
                if (raw.Length > 1) raw[raw.Length - 2] = 1;
                for (int y = 0; y < height; ++y) Array.Copy(raw, y * width, pixels, y * sw, width);
                source.SetPixelData(pixels, 0); source.Apply(false, false);
                for (int mip = 0; mip < seed.mipmapCount; ++mip)
                {
                    var clear = new float[Math.Max(1, pw >> mip) * Math.Max(1, ph >> mip)];
                    Array.Fill(clear, Sentinel); seed.SetPixelData(clear, mip);
                }
                seed.Apply(false, false);
                Graphics.CopyTexture(seed, closest); Graphics.CopyTexture(seed, furthest);
                int count = PyramidMipBatch.MipCount(bw, bh);
                int kernel = shader.FindKernel(farOnly ? "HiZ_FurthestGeneration" : "HiZ_Generation");
                for (int first = 0; first < count; first += 4)
                {
                    bool fromRaw = first == 0;
                    int srcMip = fromRaw ? 0 : first - 1;
                    int sourceWidth = fromRaw ? width : Math.Max(1, bw >> srcMip);
                    int sourceHeight = fromRaw ? height : Math.Max(1, bh >> srcMip);
                    int dw = Math.Max(1, bw >> first), dh = Math.Max(1, bh >> first);
                    int writes = Math.Min(4, count - first);
                    commands.SetComputeIntParam(shader, "_HiZRawSource", fromRaw ? 1 : 0);
                    commands.SetComputeIntParam(shader, "_HiZSourceMip", srcMip);
                    commands.SetComputeIntParam(shader, "_HiZMipWriteCount", writes);
                    commands.SetComputeVectorParam(shader, "_HiZSourceSize", new Vector4(sourceWidth, sourceHeight, 1f / sourceWidth, 1f / sourceHeight));
                    commands.SetComputeVectorParam(shader, "_HiZDestinationSize", new Vector4(dw, dh, 1f / dw, 1f / dh));
                    commands.SetComputeTextureParam(shader, kernel, "_HiZRawDepth", source);
                    commands.SetComputeTextureParam(shader, kernel, "_HiZSourceFurthest", fromRaw ? (Texture)source : furthest);
                    if (!farOnly) commands.SetComputeTextureParam(shader, kernel, "_HiZSourceClosest", fromRaw ? (Texture)source : closest);
                    for (int i = 0; i < 4; ++i)
                    {
                        int mip = first + Math.Min(i, writes - 1);
                        commands.SetComputeTextureParam(shader, kernel, "_HiZFurthest" + i, furthest, mip);
                        if (!farOnly) commands.SetComputeTextureParam(shader, kernel, "_HiZClosest" + i, closest, mip);
                    }
                    commands.DispatchCompute(shader, kernel, (dw + 7) / 8, (dh + 7) / 8, 1);
                }
                Graphics.ExecuteCommandBuffer(commands);
                var near = raw; var far = raw;
                int ew = width, eh = height;
                for (int mip = 0; mip < count; ++mip)
                {
                    int nw = Math.Max(1, bw >> mip), nh = Math.Max(1, bh >> mip);
                    near = Reduce(near, ew, eh, nw, nh, SystemInfo.usesReversedZBuffer);
                    far = Reduce(far, ew, eh, nw, nh, !SystemInfo.usesReversedZBuffer);
                    if (!farOnly) Check(closest, mip, near, nw, nh);
                    Check(furthest, mip, far, nw, nh);
                    ew = nw; eh = nh;
                }
                for (int mip = count; mip < furthest.mipmapCount; ++mip)
                    Check(furthest, mip, Array.Empty<float>(), 0, 0);
            }
            finally
            {
                closest.Release(); furthest.Release();
                UnityEngine.Object.DestroyImmediate(closest); UnityEngine.Object.DestroyImmediate(furthest);
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(seed);
            }
        }
        static RenderTexture Target(int width, int height)
        {
            var texture = new RenderTexture(width, height, 0, GraphicsFormat.R32_SFloat)
                { enableRandomWrite = true, useMipMap = true, autoGenerateMips = false };
            Assert.IsTrue(texture.Create()); return texture;
        }
        static float[] Reduce(float[] source, int sw, int sh, int dw, int dh, bool maximum)
        {
            var result = new float[dw * dh];
            for (int y = 0; y < dh; ++y) for (int x = 0; x < dw; ++x)
            {
                float value = maximum ? float.NegativeInfinity : float.PositiveInfinity;
                int endX = x == dw - 1 ? sw : Math.Min(sw, x * 2 + 2);
                int endY = y == dh - 1 ? sh : Math.Min(sh, y * 2 + 2);
                for (int sy = Math.Min(y * 2, sh - 1); sy < endY; ++sy)
                    for (int sx = Math.Min(x * 2, sw - 1); sx < endX; ++sx)
                        value = maximum ? Math.Max(value, source[sy * sw + sx]) : Math.Min(value, source[sy * sw + sx]);
                result[y * dw + x] = value;
            }
            return result;
        }
        static void Check(RenderTexture texture, int mip, float[] expected, int width, int height)
        {
            var read = AsyncGPUReadback.Request(texture, mip, GraphicsFormat.R32_SFloat); read.WaitForCompletion();
            Assert.IsFalse(read.hasError);
            var values = read.GetData<float>();
            int pw = Math.Max(1, texture.width >> mip), ph = Math.Max(1, texture.height >> mip);
            Assert.AreEqual(pw * ph, values.Length);
            for (int y = 0; y < ph; ++y) for (int x = 0; x < pw; ++x)
            {
                float value = x < width && y < height ? expected[y * width + x] : Sentinel;
                if (float.IsNaN(values[y * pw + x]) || Math.Abs(value - values[y * pw + x]) > 1e-6f)
                    Assert.Fail($"{texture.name} mip {mip} ({x},{y}): expected {value}, got {values[y * pw + x]}");
            }
        }
    }
}
