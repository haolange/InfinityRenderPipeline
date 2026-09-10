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
        [TestCase(64, 64)]
        [TestCase(127, 65)]
        [TestCase(1, 129)]
        [TestCase(129, 1)]
        [TestCase(3, 5)]
        [TestCase(1638, 1778)]
        public void EveryMipMatchesClosestDepthReference(int width, int height)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/PyramidDepth/Compute_PyramidDepth.compute");
            Assert.IsNotNull(shader);
            var source = new Texture2D(width, height, TextureFormat.RFloat, false, true);
            var target = new RenderTexture(width, height, 0, GraphicsFormat.R32_SFloat)
                { enableRandomWrite = true, useMipMap = true, autoGenerateMips = false };
            var cmd = new CommandBuffer();
            try
            {
                var expected = new float[width * height];
                for (int i = 0; i < expected.Length; i++) expected[i] = ((i * 73L + i / width * 31L) % 1009) / 1009f;
                // The trailing odd corner must remain visible at the root mip.
                expected[expected.Length - 1] = SystemInfo.usesReversedZBuffer ? 1 : 0;
                source.SetPixelData(expected, 0); source.Apply(false, false);
                Assert.IsTrue(target.Create());
                int count = PyramidMipBatch.MipCount(width, height), kernel = shader.FindKernel("HiZ_Generation");
                for (int first = 0; first < count; first += 4)
                {
                    bool copy = first == 0;
                    int srcMip = copy ? 0 : first - 1;
                    int sw = PyramidMipBatch.MipSize(width, srcMip), sh = PyramidMipBatch.MipSize(height, srcMip);
                    int dw = PyramidMipBatch.MipSize(width, first), dh = PyramidMipBatch.MipSize(height, first);
                    int writes = Math.Min(4, count - first);
                    cmd.SetComputeIntParam(shader, "_CopySource", copy ? 1 : 0);
                    cmd.SetComputeIntParam(shader, "_SrcMip", srcMip);
                    cmd.SetComputeIntParam(shader, "_MipWriteCount", writes);
                    cmd.SetComputeVectorParam(shader, "_SrcSize", new Vector4(sw, sh, 1f / sw, 1f / sh));
                    cmd.SetComputeVectorParam(shader, "_DstSize", new Vector4(dw, dh, 1f / dw, 1f / dh));
                    cmd.SetComputeTextureParam(shader, kernel, "_PrevMipDepth", copy ? (Texture)source : target);
                    for (int i = 0; i < 4; i++)
                        cmd.SetComputeTextureParam(shader, kernel, "_HierarchicalDepth" + i, target, first + Math.Min(i, writes - 1));
                    cmd.DispatchCompute(shader, kernel, (dw + 7) / 8, (dh + 7) / 8, 1);
                }
                Graphics.ExecuteCommandBuffer(cmd);
                int ew = width, eh = height;
                for (int mip = 0; mip < count; mip++)
                {
                    var read = AsyncGPUReadback.Request(target, mip, GraphicsFormat.R32_SFloat);
                    read.WaitForCompletion();
                    Assert.IsFalse(read.hasError, "Mip " + mip);
                    var values = read.GetData<float>();
                    Assert.AreEqual(expected.Length, values.Length);
                    for (int i = 0; i < values.Length; i++)
                        if (Math.Abs(expected[i] - values[i]) > 1e-6f || float.IsNaN(values[i]))
                            Assert.Fail($"Mip {mip} pixel {i}: expected {expected[i]}, got {values[i]}");
                    int nw = Math.Max(1, ew / 2), nh = Math.Max(1, eh / 2);
                    var next = new float[nw * nh];
                    for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++)
                    {
                        float value = expected[Math.Min(y * 2, eh - 1) * ew + Math.Min(x * 2, ew - 1)];
                        int endX = x == nw - 1 ? ew : x * 2 + 2, endY = y == nh - 1 ? eh : y * 2 + 2;
                        for (int sy = y * 2; sy < endY; sy++) for (int sx = x * 2; sx < endX; sx++)
                            value = SystemInfo.usesReversedZBuffer ? Math.Max(value, expected[sy * ew + sx]) : Math.Min(value, expected[sy * ew + sx]);
                        next[y * nw + x] = value;
                    }
                    expected = next; ew = nw; eh = nh;
                }
            }
            finally { cmd.Dispose(); target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
