using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public sealed class ColorOutputGpuTests
    {
        const string k_Root = "Packages/com.infinity.render-pipeline/";

        [TestCase(0f)]
        [TestCase(0.02f)]
        [TestCase(0.18f)]
        [TestCase(1f)]
        public void FilmGrain_PreservesMeanAndBlackWithoutBloomBinding(float signal)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_Root + "Shaders/RenderingFeature/PostProcessing/Compute_PostProcessing.compute");
            Assert.IsNotNull(shader);
            const int width = 257, height = 129;
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var lut = new Texture3D(2, 2, 2, TextureFormat.RGBAFloat, false);
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
            try
            {
                source.SetPixel(0, 0, Color.black); source.Apply(false, false);
                var colors = new Color[8];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(signal, signal, signal, 1);
                lut.SetPixels(colors); lut.Apply(false, false);
                Assert.IsTrue(target.Create());
                int kernel = shader.FindKernel("FinalCombineNoBloom");
                shader.SetTexture(kernel, "SRV_SceneColorTexture", source);
                shader.SetTexture(kernel, "SRV_ExposureEV", source);
                shader.SetTexture(kernel, "SRV_CombineLUT", lut);
                shader.SetTexture(kernel, "UAV_PostProcessTexture", target);
                shader.SetVector("PP_Resolution", new Vector4(width, height, 1f / width, 1f / height));
                shader.SetFloat("PP_ExposureMultiplier", 1); shader.SetFloat("PP_AutoExposure", 0);
                shader.SetFloat("PP_VignetteIntensity", 0); shader.SetFloat("PP_FilmGrainResponse", 0.8f);
                shader.SetFloat("PP_FilmGrainIntensity", 0.6f);
                float[] previous = null;
                for (int frame = 0; frame < 2; frame++)
                {
                    shader.SetInt("PP_FrameIndex", frame);
                    shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
                    float[] values = Read(target, "grain-" + signal + "-frame-" + frame,
                        "Known uniform graded signal; no Bloom resource is bound. PCG grain intensity0.6/response0.8, mean tolerance1%relative, black exact0.");
                    double sum = 0, sumSquares = 0, difference = 0;
                    for (int i = 0; i < width * height; i++)
                    {
                        float value = values[i * 4];
                        Assert.GreaterOrEqual(value, 0); Assert.LessOrEqual(value, signal * 1.6f + 1e-6f);
                        Assert.AreEqual(value, values[i * 4 + 1]); Assert.AreEqual(value, values[i * 4 + 2]);
                        sum += value; sumSquares += value * value;
                        if (previous != null) difference += Math.Abs(value - previous[i * 4]);
                    }
                    double mean = sum / (width * height);
                    Assert.AreEqual(signal, mean, Math.Max(1e-7, signal * 0.01), "Grain must not act as exposure.");
                    if (signal > 0)
                    {
                        Assert.Greater(sumSquares / (width * height) - mean * mean, signal * signal * 0.001);
                        if (previous != null) Assert.Greater(difference / (width * height), signal * 0.01, "Grain must evolve across frames.");
                    }
                    previous = values;
                }
            }
            finally
            {
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(lut);
            }
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void BloomScatter_ControlsWidthWithoutMultiplyingConstantEnergy(float scatter)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_Root + "Shaders/RenderingFeature/PostProcessing/Compute_PostProcessing.compute");
            Assert.IsNotNull(shader);
            var source = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(11, 11, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
            try
            {
                source.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); source.Apply(false, false);
                Assert.IsTrue(target.Create());
                int kernel = shader.FindKernel("BloomUpsample");
                shader.SetTexture(kernel, "SRV_BloomSource", source);
                shader.SetTexture(kernel, "UAV_BloomTarget", target);
                shader.SetVector("BloomMipSize", new Vector4(4, 4, 0.25f, 0.25f));
                shader.SetFloat("PP_BloomScatter", scatter);
                for (int high = 0; high <= 1; high++)
                {
                    using (var commands = new CommandBuffer())
                    {
                        commands.SetRenderTarget(target); commands.ClearRenderTarget(false, true, new Color(high, high, high, 1));
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    shader.Dispatch(kernel, 1, 1, 1);
                    float[] values = Read(target, "bloom-scatter-" + scatter + "-high-" + high,
                        "Low mip constant1; high mip0 or1; active4x4 inside11x11. Scatter0/0.5/1; constant-field energy preserved.");
                    for (int y = 0; y < 11; y++)
                        for (int x = 0; x < 11; x++)
                        {
                            float expected = x < 4 && y < 4 ? Mathf.Lerp(high, 1, scatter) : high;
                            for (int c = 0; c < 3; c++) Assert.AreEqual(expected, values[(y * 11 + x) * 4 + c], 0.0001f);
                        }
                }
            }
            finally
            {
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ProxyShadowLut_ExistingAssetPassMatchesConeIntegral()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(k_Root + "Shaders/Utility/DrawSystemLUT.shader");
            Assert.IsNotNull(shader);
            var material = new Material(shader);
            var target = new RenderTexture(5, 5, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                int pass = material.FindPass("Proxy_Shadow");
                Assert.GreaterOrEqual(pass, 0);
                Assert.IsTrue(target.Create());
                // The isolated EditMode fixture explicitly supplies the existing CRT vertex
                // contract; it does not depend on a Player loop scheduling CRT.Update().
                var properties = new MaterialPropertyBlock();
                properties.SetVectorArray("CustomRenderTextureCenters", new[] { new Vector4(0.5f, 0.5f, 0.5f, 0) });
                properties.SetVectorArray("CustomRenderTextureSizesAndRotations", new[] { new Vector4(1, 1, 1, 0) });
                properties.SetVector("CustomRenderTextureParameters", Vector4.zero);
                properties.SetVector("_CustomRenderTextureInfo", new Vector4(5, 5, 1, 0));
                using (var commands = new CommandBuffer())
                {
                    commands.SetRenderTarget(target);
                    commands.SetViewport(new Rect(0, 0, 5, 5));
                    commands.ClearRenderTarget(false, true, new Color(-7, -7, -7, -7));
                    commands.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 6, 1, properties);
                    Graphics.ExecuteCommandBuffer(commands);
                }
                float[] values = Read(target, "proxy-shadow", "5x5 pixel-center UV. Explicit draw of existing CRT vertex/fragment pass6, cone half-angle0.5rad. Independent256x256 solid-angle quadrature; tolerance0.025. Does not claim automatic CRT update lifecycle.");
                double min = 1, max = 0;
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < 5; x++)
                    {
                        double expected = ProxyReference((x + 0.5) / 5, (y + 0.5) / 5);
                        float actual = values[(y * 5 + x) * 4];
                        Assert.AreEqual(expected, actual, 0.025, $"Proxy LUT ({x},{y})");
                        min = Math.Min(min, actual); max = Math.Max(max, actual);
                    }
                Assert.Greater(max - min, 0.1, "LUT must contain a spatial response.");
            }
            finally
            {
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static double ProxyReference(double u, double v)
        {
            double nx = Math.Sqrt(1 - u * u), ny = Math.Sqrt(1 - v * v);
            const int samples = 256;
            int hits = 0;
            double edge = Math.Cos(0.5);
            for (int zIndex = 0; zIndex < samples; zIndex++)
            {
                double z = edge + (1 - edge) * (zIndex + 0.5) / samples;
                double radius = Math.Sqrt(1 - z * z);
                for (int phiIndex = 0; phiIndex < samples; phiIndex++)
                {
                    double phi = (phiIndex + 0.5) * 2 * Math.PI / samples;
                    if (Math.Cos(phi) * radius * nx + z * ny < ny) hits++;
                }
            }
            return (double)hits / (samples * samples);
        }

        [TestCase(GraphicsFormat.R16G16B16A16_SFloat)]
        [TestCase(GraphicsFormat.R32G32B32A32_SFloat)]
        public void ProductionLut_NeutralDiagonalIsFiniteMonotonicAndNeutral(GraphicsFormat format)
        {
            Assert.IsTrue(SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_Root + "Shaders/ColorGrading/Compute_CombineLUTs.compute");
            Assert.IsNotNull(shader);
            var profile = DefaultVolumeProfileFactory.CreateInMemory();
            var target = new RenderTexture(new RenderTextureDescriptor(32, 32)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = 32,
                graphicsFormat = format,
                depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true
            });
            try
            {
                Assert.IsTrue(target.Create());
                profile.TryGet(out FilmTonemap film);
                profile.TryGet(out ColorGrading grading);
                var descriptor = CombineLutParameterUtility.FromVolumeStack(film, grading);
                descriptor.MappingPolynomial = new float4(0, 1, 0, 0);
                descriptor.InverseGamma = new float4(1, 1, 1, 0);
                // Bind independently by the shader's semantic names, not the pass's property IDs.
                foreach (FieldInfo field in typeof(CombineLutParameterDescriptor).GetFields())
                {
                    string name = field.Name == "ColorShadowTint2" ? "ColorShadow_Tint2" : field.Name;
                    object value = field.GetValue(descriptor);
                    if (value is float scalar) shader.SetFloat(name, scalar);
                    else if (value is int integer) shader.SetInt(name, integer);
                    else if (value is float4 vector) shader.SetVector(name, vector);
                }
                int kernel = shader.FindKernel("MainCS");
                shader.SetTexture(kernel, "CombineLookupTexture", target);
                shader.Dispatch(kernel, 4, 4, 4);
                float[] values = Read(target, "lut-neutral", JsonUtility.ToJson(descriptor));
                float previous = -1;
                for (int i = 0; i < 32; i++)
                {
                    int offset = (i * 32 * 32 + i * 32 + i) * 4;
                    float r = values[offset], g = values[offset + 1], b = values[offset + 2];
                    Assert.GreaterOrEqual(r, previous - 0.0001f, "Neutral LUT diagonal decreased at " + i);
                    Assert.LessOrEqual(Mathf.Max(r, Mathf.Max(g, b)) - Mathf.Min(r, Mathf.Min(g, b)), 0.015f,
                        "Unexpected neutral chroma at " + i);
                    previous = r;
                }
                Assert.Less(values[0], 0.001f);
                Assert.Greater(previous, 0.9f, "LUT did not respond across its input domain.");
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                foreach (VolumeComponent component in profile.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [TestCase(1, 1)]
        [TestCase(17, 13)]
        [TestCase(1919, 1079)]
        public void ProductionOutputTransform_GrayTransferAndBoundary(int width, int height)
        {
            Assert.IsTrue(SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_Root + "Shaders/RenderingFeature/OutputTransform/Compute_OutputTransform.compute");
            Assert.IsNotNull(shader);
            var input = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(width + 7, height + 7, 0, RenderTextureFormat.ARGBFloat)
                { enableRandomWrite = true };
            try
            {
                var pixels = new Color[width * height];
                float[] ramp = { 0, 0.001f, 0.0031308f, 0.18f, 0.5f, 1, 4 };
                for (int i = 0; i < pixels.Length; i++)
                {
                    float value = pixels.Length == 1 ? 0.18f : ramp[i % ramp.Length];
                    pixels[i] = new Color(value, value, value, 1);
                }
                input.SetPixels(pixels); input.Apply(false, false);
                Assert.IsTrue(target.Create());
                int kernel = shader.FindKernel("OutputTransform");
                shader.SetTexture(kernel, "SRV_GradedColor", input);
                shader.SetTexture(kernel, "UAV_DisplayColor", target);
                shader.SetVector("OT_Resolution", new Vector4(width, height, 1f / width, 1f / height));
                shader.SetInt("OT_ApplyRec2020", 0);
                shader.SetFloat("OT_NitsScale", 100);
                for (int policy = 0; policy <= 4; policy++)
                {
                    using (var commands = new CommandBuffer())
                    {
                        commands.SetRenderTarget(target);
                        commands.ClearRenderTarget(false, true, new Color(-7, -7, -7, -7));
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    shader.SetInt("OT_Policy", policy);
                    shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
                    float[] values = Read(target, "output-" + width + "x" + height + "-" + policy,
                        "Known linear Rec709 ramp 0/0.001/0.0031308/0.18/0.5/1/4; 1x1 uses 0.18. Padding sentinel -7. Hardware policy readback is pre-encoding linear, not backbuffer.");
                    for (int y = 0; y < height + 7; y++)
                        for (int x = 0; x < width + 7; x++)
                        {
                            int index = (y * (width + 7) + x) * 4;
                            bool inside = x < width && y < height;
                            double value = inside ? pixels[y * width + x].r : 0;
                            double expected = policy == 0 ? (value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055) :
                                policy == 2 ? Pq(value * 100) : policy == 3 ? (value <= 1.0 / 12 ? Math.Sqrt(3 * value) :
                                    0.17883277 * Math.Log(12 * value - 0.28466892) + 0.55991073) : value;
                            for (int c = 0; c < 3; c++)
                                if (Math.Abs(values[index + c] - (inside ? expected : -7)) > 0.0002)
                                    Assert.Fail($"Policy {policy}, pixel ({x},{y}), channel {c}: {values[index + c]}, expected {(inside ? expected : -7)}.");
                            if (values[index + 3] != (inside ? 1 : -7)) Assert.Fail("Output coverage/padding alpha mismatch.");
                        }
                }
            }
            finally
            {
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(input);
            }
        }

        static double Pq(double nits)
        {
            double p = Math.Pow(nits / 10000, 2610.0 / 16384);
            return Math.Pow((3424.0 / 4096 + 2413.0 / 128 * p) / (1 + 2392.0 / 128 * p), 2523.0 / 32);
        }

        static float[] Read(RenderTexture target, string label, string input)
        {
            var request = AsyncGPUReadback.Request(target, 0);
            request.WaitForCompletion();
            Assert.IsTrue(request.done && !request.hasError, "Raw GPU readback failed.");
            string run = Path.GetFullPath(Path.Combine(Application.dataPath, "../../InfinityRP-Validation",
                "color-gpu-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + label));
            Directory.CreateDirectory(run);
            using var raw = new MemoryStream();
            for (int layer = 0; layer < request.layerCount; layer++)
            {
                byte[] layerBytes = request.GetData<byte>(layer).ToArray();
                raw.Write(layerBytes, 0, layerBytes.Length);
            }
            byte[] bytes = raw.ToArray();
            File.WriteAllBytes(Path.Combine(run, "raw.bin"), bytes);
            File.WriteAllText(Path.Combine(run, "fixture.txt"), $"Unity={Application.unityVersion}\nGPU={SystemInfo.graphicsDeviceName}\nAPI={SystemInfo.graphicsDeviceType}\nFormat={target.graphicsFormat}\nSize={target.width}x{target.height}x{target.volumeDepth}\n{input}\n");
            TestContext.Progress.WriteLine(run);
            int scalarBytes = target.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat ? 2 : 4;
            Assert.AreEqual(target.width * target.height * target.volumeDepth * 4 * scalarBytes, bytes.Length,
                "Readback must include every RGBA voxel, not only the first layer.");
            float[] values = new float[bytes.Length / scalarBytes];
            if (scalarBytes == 4) Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            else for (int i = 0; i < values.Length; i++) values[i] = Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, i * 2));
            for (int i = 0; i < values.Length; i++)
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) Assert.Fail("Raw NaN/Inf at " + i + ": " + run);
            return values;
        }
    }
}
