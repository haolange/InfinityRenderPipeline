using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowSamplingGpuTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PCSS_SlopedPlaneUsesActualSampledTexelCenters(bool blocker) => Run(blocker, false, false);

        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void PCSS_LocalAndTileBoundaryRespectPlaneAndGuard(bool blocker, bool local, bool tile) => Run(blocker, local, tile);

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void CachedPointShadow_SelectsTheProducedOriginFace(int blockedFace)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader shader = null;
            Texture2D depth = null;
            ComputeBuffer output = null, matrices = null, slices = null;
            try
            {
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute"));
                depth = new Texture2D(192, 32, TextureFormat.RFloat, false, true);
                var values = new float[192 * 32];
                for (int y = 0; y < 32; ++y)
                    for (int x = 0; x < 192; ++x)
                        values[y * 192 + x] = (x / 32 == blockedFace) == SystemInfo.usesReversedZBuffer ? 1 : 0;
                depth.SetPixelData(values, 0); depth.Apply(false);
                var matrixData = new Matrix4x4[6];
                var sliceData = new InfinityTech.Rendering.LightPipeline.FLocalShadowSliceGPU[6];
                for (int face = 0; face < 6; ++face)
                {
                    // All faces project to their tile center, isolating face selection from projection coverage.
                    var matrix = Matrix4x4.zero; matrix.m23 = .5f; matrix.m33 = 1;
                    matrixData[face] = matrix;
                    sliceData[face].atlasRect = new Vector4(face / 6f, 0, 1 / 6f, 1);
                    sliceData[face].shadowOriginSize = Vector4.zero;
                }
                matrices = new ComputeBuffer(6, 64); matrices.SetData(matrixData);
                slices = new ComputeBuffer(6, System.Runtime.InteropServices.Marshal.SizeOf<InfinityTech.Rendering.LightPipeline.FLocalShadowSliceGPU>());
                slices.SetData(sliceData);
                output = new ComputeBuffer(2, 16);
                var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                shader.SetVectorArray("TestWorldPositions", new[] { (Vector4)(directions[blockedFace] * 2), (Vector4)(directions[(blockedFace + 1) % 6] * 2) });
                shader.SetVector("_LocalShadowMapSize", new Vector4(192, 32, 1 / 192f, 1 / 32f));
                int kernel = shader.FindKernel("CachedPoint");
                shader.SetTexture(kernel, "TestDepth", depth);
                shader.SetBuffer(kernel, "SRV_LocalShadowMatrices", matrices);
                shader.SetBuffer(kernel, "SRV_LocalShadowRects", slices);
                shader.SetBuffer(kernel, "Results", output);
                shader.Dispatch(kernel, 2, 1, 1);
                var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                var result = readback.GetData<Vector4>();
                Assert.AreEqual(1, result[1].x, "Independent other-face receiver must remain lit.");
                Assert.AreEqual(0, result[0].x, "Current light pose must not select the cached depth's cube face.");
            }
            finally
            {
                output?.Dispose(); matrices?.Dispose(); slices?.Dispose();
                Object.DestroyImmediate(depth); Object.DestroyImmediate(shader);
            }
        }

        static void Run(bool blocker, bool local, bool tile)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader shader = null;
            Texture2D depth = null;
            ComputeBuffer output = null;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute");
                Assert.IsNotNull(source);
                shader = Object.Instantiate(source);
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                var values = new float[128 * 128];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        values[y * 128 + x] = .4f + (x + .5f) / 128 * .1f + (y + .5f) / 128 * .15f
                            + (blocker || (tile && (x < 32 || x >= 96 || y < 32 || y >= 96))
                                ? (SystemInfo.usesReversedZBuffer ? .2f : -.2f) : 0);
                depth.SetPixelData(values, 0); depth.Apply(false);
                output = new ComputeBuffer(256, 16);
                int kernel = shader.FindKernel("SlopedPlane");
                shader.SetVector("_ShadowFilterParams", new Vector4(2, 16, 32, 12));
                shader.SetVector("TestRect", tile ? new Vector4(.25f, .25f, .5f, .5f) : new Vector4(0, 0, 1, 1));
                bool reversed = SystemInfo.usesReversedZBuffer;
                shader.SetVector("TestDepthParams", local
                    ? new Vector4(reversed ? .1f : -1.1f, reversed ? 1 : -1, .01f, 0)
                    : new Vector4(reversed ? .1f : -.1f, reversed ? 1 : 0, .01f, 1));
                shader.SetInt("TestLocal", local ? 1 : 0);
                shader.SetTexture(kernel, "TestDepth", depth);
                shader.SetBuffer(kernel, "Results", output);
                shader.Dispatch(kernel, 4, 1, 1);
                var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                var results = readback.GetData<Vector4>();
                for (int i = 0; i < results.Length; i++)
                    Assert.AreEqual(blocker ? 0 : 1, results[i].x, 1e-5,
                        $"sample {i}: a plane must not block itself; a distinct closer plane must block it");
            }
            finally { output?.Dispose(); Object.DestroyImmediate(depth); Object.DestroyImmediate(shader); }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void PCSS_PenumbraWidensWithReceiverSeparationAndSearchCoversLitSide(bool local)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader shader = null;
            Texture2D depth = null;
            ComputeBuffer output = null;
            try
            {
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute"));
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                var values = new float[128 * 128];
                bool reversed = SystemInfo.usesReversedZBuffer;
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++) values[y * 128 + x] = x < 64
                        ? (local ? .5f : (reversed ? .8f : .2f)) : (reversed ? 0 : 1);
                depth.SetPixelData(values, 0); depth.Apply(false);
                output = new ComputeBuffer(256, 16);
                int kernel = shader.FindKernel("Penumbra");
                shader.SetInt("TestLocal", local ? 1 : 0);
                shader.SetVector("TestDepthParams", local
                    ? new Vector4(reversed ? 0 : -1, reversed ? 1 : -1, .01f, 0)
                    : new Vector4(reversed ? .1f : -.1f, reversed ? 1 : 0, .1f, 1));
                shader.SetVector("_ShadowFilterParams", new Vector4(1, 64, 128, 20));
                shader.SetTexture(kernel, "TestDepth", depth); shader.SetBuffer(kernel, "Results", output);
                int[] transition = new int[2];
                for (int separation = 0; separation < 2; separation++)
                {
                    shader.SetFloat("TestReceiverDepth", separation == 0 ? 4 : 8);
                    shader.Dispatch(kernel, 4, 1, 1);
                    var request = AsyncGPUReadback.Request(output); request.WaitForCompletion();
                    Assert.IsFalse(request.hasError);
                    var samples = request.GetData<Vector4>();
                    Assert.AreEqual(0, samples[0].x, 1e-5); Assert.AreEqual(1, samples[255].x, 1e-5);
                    for (int i = 0; i < samples.Length; i++)
                    {
                        Assert.That(samples[i].x, Is.InRange(0f, 1f));
                        if (samples[i].x > .001f && samples[i].x < .999f) transition[separation]++;
                    }
                    // At eight units the analytic filter radius is 6 directional / 7.5 local texels.
                    // A point 4.13 texels into the lit side still sees part of the half-plane blocker.
                    if (separation == 1) Assert.Less(samples[144].x, .99f, "Blocker search must cover the eventual penumbra.");
                }
                Assert.Greater(transition[1], transition[0] + 8, "Far receiver must have a wider partial-visibility band.");
            }
            finally { output?.Dispose(); Object.DestroyImmediate(depth); Object.DestroyImmediate(shader); }
        }

    }
}
