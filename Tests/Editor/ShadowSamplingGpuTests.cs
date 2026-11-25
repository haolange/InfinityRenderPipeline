using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowSamplingGpuTests
    {
        [Test]
        public void ReceiverGradient_DegenerateAndSlopedPlanesRemainFinite()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute"));
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16);
            try
            {
                int kernel = shader.FindKernel("ReceiverGradient");
                shader.SetBuffer(kernel, "Results", output);
                shader.Dispatch(kernel, 4, 1, 1);
                var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                Assert.IsFalse(readback.hasError);
                var values = readback.GetData<Vector4>();
                for (int i = 0; i < 4; ++i)
                {
                    Assert.IsFalse(float.IsNaN(values[i].x) || float.IsInfinity(values[i].x));
                    Assert.IsFalse(float.IsNaN(values[i].y) || float.IsInfinity(values[i].y));
                }
                Assert.AreEqual(Vector4.zero, new Vector4(values[0].x, values[0].y, 0, 0));
                Assert.AreEqual(0, values[1].x); Assert.AreEqual(0, values[1].y);
                Assert.AreEqual(0, values[3].x); Assert.AreEqual(0, values[3].y);
                Assert.That(values[2].x, Is.EqualTo(-.8f).Within(1e-5));
                Assert.That(values[2].y, Is.EqualTo(SystemInfo.graphicsUVStartsAtTop ? 1.2f : -1.2f).Within(1e-5));
            }
            finally { Object.DestroyImmediate(shader); }
        }

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
            GraphicsBuffer output = null, matrices = null, slices = null;
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
                var sliceData = new InfinityTech.Rendering.LightPipeline.LocalShadowSliceGPU[6];
                for (int face = 0; face < 6; ++face)
                {
                    // All faces project to their tile center, isolating face selection from projection coverage.
                    var matrix = Matrix4x4.zero; matrix.m23 = .5f; matrix.m33 = 1;
                    matrixData[face] = matrix;
                    sliceData[face].atlasRect = new Vector4(face / 6f, 0, 1 / 6f, 1);
                    sliceData[face].shadowOriginSize = Vector4.zero;
                }
                matrices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, 64); matrices.SetData(matrixData);
                slices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, System.Runtime.InteropServices.Marshal.SizeOf<InfinityTech.Rendering.LightPipeline.LocalShadowSliceGPU>());
                slices.SetData(sliceData);
                output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
                var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                shader.SetVectorArray("TestWorldPositions", new[] { (Vector4)(directions[blockedFace] * 2), (Vector4)(directions[(blockedFace + 1) % 6] * 2) });
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { localMapSize = new Vector4(192, 32, 1 / 192f, 1 / 32f) });
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
            GraphicsBuffer output = null;
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
                output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 16);
                int kernel = shader.FindKernel("SlopedPlane");
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { filterParameters = new Vector4(2, 16, 32, 12) });
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
            GraphicsBuffer output = null;
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
                output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 16);
                int kernel = shader.FindKernel("Penumbra");
                shader.SetInt("TestLocal", local ? 1 : 0);
                shader.SetVector("TestDepthParams", local
                    ? new Vector4(reversed ? 0 : -1, reversed ? 1 : -1, .01f, 0)
                    : new Vector4(reversed ? .1f : -.1f, reversed ? 1 : 0, .1f, 1));
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { filterParameters = new Vector4(1, 64, 128, 20) });
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

        [Test]
        public void ShapePCSS_ElongatedRect_ProducesAnisotropicPenumbraThatRotates()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");

            ComputeShader shader = null;
            Texture2D depth = null;
            GraphicsBuffer output = null;
            try
            {
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute"));
                // Small centered blocker square inside a 128x128 shadow map.
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                bool reversed = SystemInfo.usesReversedZBuffer;
                float blockerDepth = 0.5f;
                float clearDepth = reversed ? 0.0f : 1.0f;
                var values = new float[128 * 128];
                for (int y = 0; y < 128; ++y)
                    for (int x = 0; x < 128; ++x)
                        values[y * 128 + x] = (x >= 62 && x < 66 && y >= 62 && y < 66) ? blockerDepth : clearDepth;
                depth.SetPixelData(values, 0); depth.Apply(false);
                output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 16);

                int kernel = shader.FindKernel("ShapePCSS");
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { filterParameters = new Vector4(1, 64, 64, 24) });
                shader.SetVector("TestRect", new Vector4(0, 0, 1, 1));
                shader.SetVector("TestTexelSize", new Vector2(1f / 128f, 1f / 128f));
                shader.SetVector("TestDepthParams", reversed ? new Vector4(0, 1, 0.01f, 0) : new Vector4(-1, -1, 0.01f, 0));
                shader.SetFloat("TestReceiverDepth", 4.0f);
                shader.SetFloat("TestApexOffset", 0.0f);
                shader.SetTexture(kernel, "TestDepth", depth);
                shader.SetBuffer(kernel, "Results", output);

                int MeasureWidth(Vector4 axisU, Vector4 axisV, int sweepAxis)
                {
                    shader.SetVector("TestEmitterAxisU", axisU);
                    shader.SetVector("TestEmitterAxisV", axisV);
                    shader.SetInt("TestSweepAxis", sweepAxis);
                    shader.SetFloat("TestSweepCoord", 0.5f);
                    shader.Dispatch(kernel, 4, 1, 1);
                    var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                    Assert.IsFalse(readback.hasError);
                    var samples = readback.GetData<Vector4>();
                    int shadowed = 0;
                    for (int i = 0; i < samples.Length; ++i)
                        if (samples[i].x < 0.99f) shadowed++;
                    return shadowed;
                }

                // Wide horizontal Rect: width > height -> X penumbra much wider than Y penumbra.
                Vector4 horizU = new Vector4(1, 0, 0.35f, InfinityTech.Rendering.LightPipeline.ShadowPCSSShapeMode.OrientedRect);
                Vector4 horizV = new Vector4(0, 1, 0.04f, 1);
                int widthX = MeasureWidth(horizU, horizV, 0);
                int widthY = MeasureWidth(horizU, horizV, 1);
                Assert.Greater(widthX, widthY + 12, $"Horizontal Rect penumbra must be wider along X ({widthX}) than Y ({widthY}).");

                // Rotated 90 degrees: X/Y penumbra extents swap.
                Vector4 vertU = new Vector4(0, 1, 0.35f, InfinityTech.Rendering.LightPipeline.ShadowPCSSShapeMode.OrientedRect);
                Vector4 vertV = new Vector4(-1, 0, 0.04f, 1);
                int rotWidthX = MeasureWidth(vertU, vertV, 0);
                int rotWidthY = MeasureWidth(vertU, vertV, 1);
                Assert.Greater(rotWidthY, rotWidthX + 12, $"Rotated Rect penumbra must swap to Y ({rotWidthY}) vs X ({rotWidthX}).");

                // Equal-area isotropic circle behaves symmetrically unlike the elongated Rect.
                float eqRadius = Mathf.Sqrt(0.35f * 0.04f);
                Vector4 diskU = new Vector4(1, 0, eqRadius, InfinityTech.Rendering.LightPipeline.ShadowPCSSShapeMode.IsotropicDisk);
                Vector4 diskV = new Vector4(0, 1, eqRadius, 1);
                int diskX = MeasureWidth(diskU, diskV, 0);
                Assert.Greater(widthX, diskX + 10, $"Elongated Rect X spread ({widthX}) must differ measurably from equal-area Disk ({diskX}).");
            }
            finally { output?.Dispose(); Object.DestroyImmediate(depth); Object.DestroyImmediate(shader); }
        }

        [Test]
        public void ShapePCSS_LineAndTube_RespondToAxisAndRadius()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");

            ComputeShader shader = null;
            Texture2D depth = null;
            GraphicsBuffer output = null;
            try
            {
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute"));
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                bool reversed = SystemInfo.usesReversedZBuffer;
                float blockerDepth = 0.5f;
                float clearDepth = reversed ? 0.0f : 1.0f;
                var values = new float[128 * 128];
                for (int y = 0; y < 128; ++y)
                    for (int x = 0; x < 128; ++x)
                        values[y * 128 + x] = (x >= 62 && x < 66 && y >= 62 && y < 66) ? blockerDepth : clearDepth;
                depth.SetPixelData(values, 0); depth.Apply(false);
                output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 16);

                int kernel = shader.FindKernel("ShapePCSS");
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { filterParameters = new Vector4(1, 64, 64, 24) });
                shader.SetVector("TestRect", new Vector4(0, 0, 1, 1));
                shader.SetVector("TestTexelSize", new Vector2(1f / 128f, 1f / 128f));
                shader.SetVector("TestDepthParams", reversed ? new Vector4(0, 1, 0.01f, 0) : new Vector4(-1, -1, 0.01f, 0));
                shader.SetFloat("TestReceiverDepth", 4.0f);
                shader.SetFloat("TestApexOffset", 0.0f);
                shader.SetTexture(kernel, "TestDepth", depth);
                shader.SetBuffer(kernel, "Results", output);

                int CountSoftSamples(Vector4 axisU, Vector4 axisV, int sweepAxis)
                {
                    shader.SetVector("TestEmitterAxisU", axisU);
                    shader.SetVector("TestEmitterAxisV", axisV);
                    shader.SetInt("TestSweepAxis", sweepAxis);
                    shader.SetFloat("TestSweepCoord", 0.5f);
                    shader.Dispatch(kernel, 4, 1, 1);
                    var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                    Assert.IsFalse(readback.hasError);
                    var samples = readback.GetData<Vector4>();
                    int soft = 0;
                    for (int i = 0; i < samples.Length; ++i)
                        if (samples[i].x > 0.01f && samples[i].x < 0.99f) soft++;
                    return soft;
                }

                // Pure Line (radius = 0, halfLength = 0.4 along X): directional penumbra along X, hard along Y.
                var lineU = new Vector4(1, 0, 0.4f, InfinityTech.Rendering.LightPipeline.ShadowPCSSShapeMode.OrientedCapsule);
                var lineV = new Vector4(0, 1, 0.0f, 1);
                int lineSoftX = CountSoftSamples(lineU, lineV, 0);
                int lineSoftY = CountSoftSamples(lineU, lineV, 1);
                Assert.Greater(lineSoftX, lineSoftY + 10, $"Line length must soften along X ({lineSoftX}) while staying tight along Y ({lineSoftY}).");

                // Tube adds transverse radius -> softens perpendicular Y axis too.
                var tubeV = new Vector4(0, 1, 0.25f, 1);
                int tubeSoftY = CountSoftSamples(lineU, tubeV, 1);
                Assert.Greater(tubeSoftY, lineSoftY + 8, $"Tube transverse radius must soften along Y ({tubeSoftY}) compared to zero-radius Line ({lineSoftY}).");
            }
            finally { output?.Dispose(); Object.DestroyImmediate(depth); Object.DestroyImmediate(shader); }
        }

        [Test]
        public void ShapePCSS_NearEmitterPlanarOccluder_UsesEmitterDepthBaselineAndClampsTile()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");

            ComputeShader shader = null;
            Texture2D depth = null;
            GraphicsBuffer output = null;
            try
            {
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowSamplingContract.compute"));
                // Left half of the atlas has a rogue neighbor-tile blocker at z = very close; right half is tile [0.5, 0, 0.5, 1]
                // with a narrow strip blocker near x = 0.75 (center of right tile).
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                bool reversed = SystemInfo.usesReversedZBuffer;
                // Blocker sits at d_apex = 1.0 (0.3 in front of apexOffset = 0.7), clear = farthest.
                float blockerStored = reversed ? 0.7f : 0.3f;
                float neighborBlocker = reversed ? 1.0f : 0.0f;
                float clearStored = reversed ? 0.0f : 1.0f;
                var values = new float[128 * 128];
                for (int y = 0; y < 128; ++y)
                    for (int x = 0; x < 128; ++x)
                    {
                        if (x < 64) values[y * 128 + x] = neighborBlocker;
                        else values[y * 128 + x] = (x >= 95 && x <= 97) ? blockerStored : clearStored;
                    }
                depth.SetPixelData(values, 0); depth.Apply(false);
                output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 16);

                int kernel = shader.FindKernel("ShapePCSS");
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { filterParameters = new Vector4(1, 64, 64, 24) });
                shader.SetVector("TestRect", new Vector4(0.5f, 0, 0.5f, 1));
                shader.SetVector("TestTexelSize", new Vector2(1f / 128f, 1f / 128f));
                shader.SetVector("TestDepthParams", reversed ? new Vector4(0, 0.7f, 0.01f, 0) : new Vector4(-1, -0.7f, 0.01f, 0));
                shader.SetFloat("TestReceiverDepth", 2.7f);
                shader.SetVector("TestEmitterAxisU", new Vector4(1, 0, 0.3f, InfinityTech.Rendering.LightPipeline.ShadowPCSSShapeMode.IsotropicDisk));
                shader.SetVector("TestEmitterAxisV", new Vector4(0, 1, 0.3f, 1));
                shader.SetInt("TestSweepAxis", 0);
                shader.SetFloat("TestSweepCoord", 0.5f);
                shader.SetTexture(kernel, "TestDepth", depth);
                shader.SetBuffer(kernel, "Results", output);

                int MeasureTransition(float apexOffset, out float borderLit)
                {
                    shader.SetFloat("TestApexOffset", apexOffset);
                    shader.Dispatch(kernel, 4, 1, 1);
                    var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                    Assert.IsFalse(readback.hasError);
                    var samples = readback.GetData<Vector4>();
                    borderLit = samples[0].x;
                    int soft = 0;
                    for (int i = 0; i < samples.Length; ++i)
                        if (samples[i].x > 0.01f && samples[i].x < 0.99f) soft++;
                    return soft;
                }

                int virtualApexTransition = MeasureTransition(0.0f, out float _);
                int emitterBaselineTransition = MeasureTransition(0.7f, out float borderVisibility);
                Assert.Greater(emitterBaselineTransition, virtualApexTransition,
                    $"Emitter-plane baseline ({emitterBaselineTransition}) must yield wider penumbra for near-emitter blocker than virtual apex ({virtualApexTransition}).");
                Assert.AreEqual(1.0f, borderVisibility, 1e-5f, "Tile edge sampling must clamp inside tile [0.5, 1] and never bleed neighbor blocker.");
            }
            finally { output?.Dispose(); Object.DestroyImmediate(depth); Object.DestroyImmediate(shader); }
        }

    }
}
