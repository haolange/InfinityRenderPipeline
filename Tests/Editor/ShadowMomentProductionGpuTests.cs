using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShadowMomentProductionGpuTests
    {
        static RenderTexture Target(int size = 64)
        {
            var texture = new RenderTexture(size, size, 0, GraphicsFormat.R16G16B16A16_SFloat) { enableRandomWrite = true };
            try { Assert.IsTrue(texture.Create()); return texture; }
            catch { Object.DestroyImmediate(texture); throw; }
        }

        static ushort[] Read(RenderTexture texture)
        {
            var request = AsyncGPUReadback.Request(texture); request.WaitForCompletion();
            Assert.IsFalse(request.hasError); return request.GetData<ushort>().ToArray();
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ProductionKernels_PreserveConstantClearMomentsThroughBothBlurs(int mode) => Constant(mode, false);

        [TestCase(0)]
        [TestCase(1)]
        public void ProductionKernels_PerspectiveClearDepthMatchesFarPlaneAndBothBlurs(int mode) => Constant(mode, true);

        static void Constant(int mode, bool perspective)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader shader = null;
            Texture2D depth = null;
            RenderTexture moments = null, temporary = null;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ShadowMoment/Compute_ShadowMoment.compute");
                Assert.IsNotNull(source); shader = Object.Instantiate(source);
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                var pixels = new float[128 * 128];
                System.Array.Fill(pixels, SystemInfo.usesReversedZBuffer ? 0 : 1);
                depth.SetPixelData(pixels, 0); depth.Apply(false);
                moments = Target(); temporary = Target();
                shader.SetVector("_MomentSliceRect", new Vector4(0, 0, 64, 64));
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(80, 1, .1f, 20), true);
                shader.SetVector("_MomentDepthParams", perspective
                    ? new Vector4(projection.m22, projection.m23, .01f, 0) : new Vector4(.1f, 1, .01f, 1));
                shader.SetVector("_MomentParams", new Vector4(mode, 5.4f, perspective ? .05f : 1, 2));
                int fromDepth = shader.FindKernel("MomentFromDepth");
                shader.SetTexture(fromDepth, "SRV_ShadowDepth", depth);
                shader.SetTexture(fromDepth, "UAV_Moments", moments); shader.Dispatch(fromDepth, 8, 8, 1);
                ushort[] before = Read(moments);
                for (int i = 0; i < before.Length; i++)
                    Assert.AreEqual(before[i % 4], before[i], $"FromDepth constant pixel {i / 4}, channel {i % 4}");
                float e = Mathf.Exp(5.4f), negative = -Mathf.Exp(-5.4f);
                float[] expected = mode == 0 ? new[] { 1f, 1, 0, 0 } : new[] { e, e * e, negative, negative * negative };
                for (int c = 0; c < 4; c++)
                {
                    uint half = math.f32tof16(expected[c]);
                    float step = Mathf.Max(Mathf.Abs(math.f16tof32(half + 1) - expected[c]),
                        Mathf.Abs(expected[c] - math.f16tof32(half == 0 ? 0 : half - 1)));
                    Assert.AreEqual(expected[c], math.f16tof32(before[c]), Mathf.Max(step, 1e-7f), $"FromDepth channel {c}");
                }
                for (int axis = 0; axis < 2; axis++)
                {
                    int kernel = shader.FindKernel(axis == 0 ? "MomentBlurH" : "MomentBlurV");
                    shader.SetTexture(kernel, "SRV_MomentInput", axis == 0 ? moments : temporary);
                    shader.SetTexture(kernel, "UAV_Moments", axis == 0 ? temporary : moments);
                    shader.Dispatch(kernel, 8, 8, 1);
                    ushort[] actual = Read(axis == 0 ? temporary : moments);
                    for (int i = 0; i < actual.Length; i++)
                        Assert.AreEqual(before[i % 4], actual[i],
                            $"Blur axis {axis}, pixel {i / 4}, channel {i % 4}: normalized blur preserves a constant half input");
                }
            }
            finally
            {
                if (moments) { moments.Release(); Object.DestroyImmediate(moments); }
                if (temporary) { temporary.Release(); Object.DestroyImmediate(temporary); }
                Object.DestroyImmediate(depth); Object.DestroyImmediate(shader);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ProductionBlur_NonconstantOffsetTileMatchesGaussianAndPreservesNeighbours(int mode)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader shader = null;
            Texture2D source = null;
            RenderTexture moments = null, temporary = null;
            const int size = 128, minX = 16, minY = 32, width = 64;
            try
            {
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ShadowMoment/Compute_ShadowMoment.compute"));
                source = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
                ushort[] pixels = new ushort[size * size * 4];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float depth = .15f + .7f * ((x - minX) + 2f * (y - minY)) / (3 * (width - 1));
                        depth = Mathf.Clamp01(depth);
                        float positive = Mathf.Exp(5.4f * (2 * depth - 1)), negative = -Mathf.Exp(-5.4f * (2 * depth - 1));
                        float[] values = mode == 0 ? new[] { depth, depth * depth, 0, 0 }
                            : new[] { positive, positive * positive, negative, negative * negative };
                        for (int c = 0; c < 4; c++) pixels[(y * size + x) * 4 + c] = (ushort)math.f32tof16(values[c]);
                    }
                source.SetPixelData(pixels, 0); source.Apply(false);
                moments = Target(size); temporary = Target(size);
                Graphics.CopyTexture(source, moments); Graphics.CopyTexture(source, temporary);
                shader.SetVector("_MomentSliceRect", new Vector4(minX, minY, width, width));
                shader.SetVector("_MomentParams", new Vector4(mode, 5.4f, 1, 2));
                ushort[] before = Read(moments);
                for (int axis = 0; axis < 2; axis++)
                {
                    int kernel = shader.FindKernel(axis == 0 ? "MomentBlurH" : "MomentBlurV");
                    shader.SetTexture(kernel, "SRV_MomentInput", axis == 0 ? moments : temporary);
                    shader.SetTexture(kernel, "UAV_Moments", axis == 0 ? temporary : moments);
                    shader.Dispatch(kernel, size / 8, size / 8, 1);
                    ushort[] actual = Read(axis == 0 ? temporary : moments);
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                            for (int c = 0; c < 4; c++)
                            {
                                int index = (y * size + x) * 4 + c;
                                if (x < minX || x >= minX + width || y < minY || y >= minY + width)
                                {
                                    Assert.AreEqual(pixels[index], actual[index], $"Neighbour {x},{y}, channel {c}");
                                    continue;
                                }
                                // Independent double-precision Gaussian on the actual input to this pass.
                                double sum = 0, weightSum = 0;
                                for (int tap = -2; tap <= 2; tap++)
                                {
                                    double weight = System.Math.Exp(-(tap * tap) / (2 * 1.5 * 1.5));
                                    int sx = Mathf.Clamp(x + (axis == 0 ? tap : 0), minX, minX + width - 1);
                                    int sy = Mathf.Clamp(y + (axis == 1 ? tap : 0), minY, minY + width - 1);
                                    sum += weight * math.f16tof32(before[(sy * size + sx) * 4 + c]); weightSum += weight;
                                }
                                float expected = (float)(sum / weightSum);
                                uint half = math.f32tof16(expected);
                                float rounded = math.f16tof32(half);
                                float step = Mathf.Max(Mathf.Abs(math.f16tof32(half + 1) - rounded),
                                    Mathf.Abs(rounded - math.f16tof32(half == 0 ? 0 : half - 1)));
                                Assert.AreEqual(rounded, math.f16tof32(actual[index]), Mathf.Max(step, 1e-7f),
                                    $"Gaussian axis {axis}, pixel {x},{y}, channel {c}: at most one half ULP");
                            }
                    before = actual;
                }
            }
            finally
            {
                if (moments) { moments.Release(); Object.DestroyImmediate(moments); }
                if (temporary) { temporary.Release(); Object.DestroyImmediate(temporary); }
                Object.DestroyImmediate(source); Object.DestroyImmediate(shader);
            }
        }
        [Test]
        public void VsmPresets_HigherQualityDoesNotIncreaseMultiDepthLightBleeding()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader producer = null, consumer = null;
            Texture2D depth = null;
            RenderTexture moments = null, temporary = null;
            ComputeBuffer output = null;
            try
            {
                producer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ShadowMoment/Compute_ShadowMoment.compute"));
                consumer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowMomentContract.compute"));
                depth = new Texture2D(128, 128, TextureFormat.RFloat, false, true);
                var raw = new float[128 * 128];
                for (int y = 0; y < 128; ++y)
                    for (int x = 0; x < 128; ++x)
                    {
                        float d = x < 64 ? .3f : x < 96 ? .6f : 1;
                        raw[y * 128 + x] = SystemInfo.usesReversedZBuffer ? 1 - d : d;
                    }
                depth.SetPixelData(raw, 0); depth.Apply(false);
                moments = Target(); temporary = Target(); output = new ComputeBuffer(64, 16);
                producer.SetVector("_MomentSliceRect", new Vector4(0, 0, 64, 64));
                producer.SetVector("_MomentDepthParams", new Vector4(.1f, 1, .01f, 1));
                int from = producer.FindKernel("MomentFromDepth"), horizontal = producer.FindKernel("MomentBlurH"), vertical = producer.FindKernel("MomentBlurV");
                producer.SetTexture(from, "SRV_ShadowDepth", depth); producer.SetTexture(from, "UAV_Moments", moments);
                producer.SetTexture(horizontal, "SRV_MomentInput", moments); producer.SetTexture(horizontal, "UAV_Moments", temporary);
                producer.SetTexture(vertical, "SRV_MomentInput", temporary); producer.SetTexture(vertical, "UAV_Moments", moments);
                int visibility = consumer.FindKernel("PresetVisibility");
                consumer.SetTexture(visibility, "StoredMoments", moments); consumer.SetBuffer(visibility, "Results", output);
                float previous = float.PositiveInfinity, low = 0, ultra = 0;
                for (int quality = 0; quality < QualityPresets.ShadowFilter.Length; ++quality)
                {
                    var settings = QualityPresets.ShadowFilter[quality];
                    producer.SetVector("_MomentParams", new Vector4(0, settings.evsmExponent, 1, settings.momentBlurRadius));
                    producer.Dispatch(from, 8, 8, 1); producer.Dispatch(horizontal, 8, 8, 1); producer.Dispatch(vertical, 8, 8, 1);
                    consumer.SetVector("PresetParams", ShadowMomentPassUtilityData.SamplingParams(settings)); consumer.Dispatch(visibility, 1, 1, 1);
                    var request = AsyncGPUReadback.Request(output); request.WaitForCompletion(); Assert.IsFalse(request.hasError);
                    var values = request.GetData<Vector4>();
                    for (int x = 0; x < values.Length; ++x)
                    {
                        Assert.IsFalse(float.IsNaN(values[x].x) || float.IsInfinity(values[x].x), $"Nonfinite visibility at {x}.");
                        Assert.That(values[x].x, Is.InRange(0f, 1f), $"Invalid visibility at {x}.");
                    }
                    Assert.AreEqual(1, values[56].x, 1e-5, "Clear region must stay lit at every quality.");
                    Assert.Less(values[8].x, .001f, "A distinct nearer blocker must remain dark.");
                    float bleeding = 0;
                    for (int x = 4; x < 48; ++x) bleeding += values[x].x;
                    Assert.LessOrEqual(bleeding, previous + 1e-5f,
                        $"Quality {(EQualityLevel)quality} increased integrated visibility behind the two blocker depths.");
                    previous = bleeding;
                    if (quality == 0) low = bleeding;
                    if (quality == 3) ultra = bleeding;
                }
                Assert.Less(ultra, low - .001f, "Ultra must reduce the Low preset's measured light bleeding.");
            }
            finally
            {
                output?.Dispose();
                if (moments) { moments.Release(); Object.DestroyImmediate(moments); }
                if (temporary) { temporary.Release(); Object.DestroyImmediate(temporary); }
                Object.DestroyImmediate(depth); Object.DestroyImmediate(producer); Object.DestroyImmediate(consumer);
            }
        }

        [TestCase(0, .3333f)]
        [TestCase(0, .6f)]
        [TestCase(0, .95f)]
        [TestCase(1, .3333f)]
        [TestCase(1, .6f)]
        [TestCase(1, .95f)]
        public void HalfStoredProductionMoments_DoNotShadowEqualDepthButRejectBehind(int mode, float depthValue)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and async readback required.");
            ComputeShader producer = null, consumer = null;
            Texture2D depth = null;
            RenderTexture moments = null;
            ComputeBuffer results = null;
            try
            {
                producer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ShadowMoment/Compute_ShadowMoment.compute"));
                consumer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/ShadowMomentContract.compute"));
                depth = new Texture2D(2, 2, TextureFormat.RFloat, false, true);
                float raw = SystemInfo.usesReversedZBuffer ? 1 - depthValue : depthValue;
                depth.SetPixelData(new[] { raw, raw, raw, raw }, 0); depth.Apply(false);
                moments = Target(1);
                producer.SetVector("_MomentSliceRect", new Vector4(0, 0, 1, 1));
                producer.SetVector("_MomentDepthParams", new Vector4(.1f, 1, .01f, 1));
                producer.SetVector("_MomentParams", new Vector4(mode, 5.4f, 1, 0));
                int fromDepth = producer.FindKernel("MomentFromDepth");
                producer.SetTexture(fromDepth, "SRV_ShadowDepth", depth);
                producer.SetTexture(fromDepth, "UAV_Moments", moments); producer.Dispatch(fromDepth, 1, 1, 1);
                results = new ComputeBuffer(1, 16);
                int visibility = consumer.FindKernel("StoredMomentVisibility");
                consumer.SetTexture(visibility, "StoredMoments", moments);
                consumer.SetBuffer(visibility, "Results", results); consumer.SetInt("StoredMode", mode);
                var observed = new Vector4[2];
                for (int behind = 0; behind < 2; behind++)
                {
                    consumer.SetFloat("StoredReceiver", behind == 0 ? depthValue : Mathf.Min(1, depthValue + .1f));
                    consumer.Dispatch(visibility, 1, 1, 1);
                    var request = AsyncGPUReadback.Request(results); request.WaitForCompletion();
                    Assert.IsFalse(request.hasError); observed[behind] = request.GetData<Vector4>()[0];
                }
                Assert.Less(observed[1].x, .01f, "Separate receiver behind the stored surface must remain shadowed.");
                Assert.AreEqual(1, observed[0].x, 1e-5,
                    $"Equal-depth surface {depthValue}, stored mean/second {observed[0].y}/{observed[0].z}: storage quantization must not self-shadow.");
            }
            finally
            {
                results?.Dispose();
                if (moments) { moments.Release(); Object.DestroyImmediate(moments); }
                Object.DestroyImmediate(depth); Object.DestroyImmediate(producer); Object.DestroyImmediate(consumer);
            }
        }

    }
}
