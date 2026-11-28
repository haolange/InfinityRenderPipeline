using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class TemporalResolveGpuTests
    {
        const string Path = "Packages/com.infinity.render-pipeline/Shaders/Pass/Temporal/Compute_TemporalAntiAliasing.compute";
        const string ReferencePath = "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/TemporalResolveReference.compute";

        sealed class Fixture : IDisposable
        {
            public readonly int width, height, inputWidth, inputHeight;
            public readonly ComputeShader shader;
            public Texture2D current, depth, history, motion, reactive;
            public RenderTexture result, display, diagnostics, reprojection;
            readonly bool reference;

            public Fixture(int w, int h, bool reference = false, int inputW = 0, int inputH = 0)
            {
                this.reference = reference;
                width = w;
                height = h;
                inputWidth = inputW > 0 ? inputW : w;
                inputHeight = inputH > 0 ? inputH : h;
                shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(reference ? ReferencePath : Path));
                current = Input(inputWidth, inputHeight, new Color(.25f, .125f, .0625f, 1));
                depth = Input(inputWidth, inputHeight, new Color(.5f, 0, 0, 0));
                history = Input(width, height, new Color(.25f, .125f, .0625f, 32));
                motion = Input(inputWidth, inputHeight, Color.clear);
                reactive = Input(inputWidth, inputHeight, Color.clear);
                result = Output(RenderTextureFormat.ARGBHalf);
                display = Output(RenderTextureFormat.ARGBHalf);
                diagnostics = Output(RenderTextureFormat.ARGBFloat);
                reprojection = Output(RenderTextureFormat.ARGBFloat);
            }

            static Texture2D Input(int w, int h, Color color)
            {
                var texture = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                Fill(texture, color);
                return texture;
            }

            public void Fill(Texture2D texture, Color color) => Fill((Texture2D)texture, (object)color);

            static void Fill(Texture2D texture, object colorObj)
            {
                Color color = (Color)colorObj;
                var data = new Color[texture.width * texture.height];
                Array.Fill(data, color);
                texture.SetPixels(data);
                texture.Apply(false, false);
            }

            RenderTexture Output(RenderTextureFormat format)
            {
                var texture = new RenderTexture(width, height, 0, format) { enableRandomWrite = true };
                texture.Create();
                return texture;
            }

            // Diagnostics: x history accepted, y history weight, z reason (0 kept, 1 reset, 2 invalid motion, 3 off screen,
            // 4 footprint without accepted samples, 5 reactive), w sample count. Reprojection: history UV, motion.
            public void Run(float historyValid = 1, bool upscaling = false, Vector2 jitterPixels = default, float sharpness = 0.35f)
            {
                int kernel = shader.FindKernel("MainDebug");
                shader.SetVector("TAA_Resolution", new Vector4(width, height, 1f / width, 1f / height));
                shader.SetVector("TAA_InputResolution", new Vector4(inputWidth, inputHeight, 1f / inputWidth, 1f / inputHeight));
                shader.SetVector("TAA_JitterPixels", new Vector4(jitterPixels.x, jitterPixels.y, 0, 0));
                shader.SetFloat("TAA_ResetBlend", historyValid);
                shader.SetFloat("TAA_Sharpness", sharpness);
                if (!reference)
                {
                    shader.EnableKeyword("TAA_REACTIVE_MASK");
                    if (upscaling)
                    {
                        shader.EnableKeyword("TAA_UPSCALING");
                    }
                    else
                    {
                        shader.DisableKeyword("TAA_UPSCALING");
                    }
                }
                shader.SetTexture(kernel, "SRV_AliasingColorTexture", current);
                shader.SetTexture(kernel, "SRV_DepthTexture", depth);
                shader.SetTexture(kernel, "SRV_HistoryColorTexture", history);
                shader.SetTexture(kernel, "SRV_MotionTexture", motion);
                shader.SetTexture(kernel, "SRV_ReactiveMaskTexture", reactive);
                if (!reference)
                {
                    shader.SetTexture(kernel, "UAV_SharpenedColorTexture", display);
                }
                shader.SetTexture(kernel, "UAV_AccumulateColorTexture", result);
                shader.SetTexture(kernel, "UAV_TAADiagnostics", diagnostics);
                shader.SetTexture(kernel, "UAV_TAAReprojection", reprojection);
                shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
                if (reference)
                {
                    kernel = shader.FindKernel("Sharpen");
                    shader.SetFloat("TAA_Sharpness", sharpness);
                    shader.SetTexture(kernel, "SRV_AliasingColorTexture", result);
                    shader.SetTexture(kernel, "UAV_AccumulateColorTexture", display);
                    shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
                }
            }

            public Color[] Read(RenderTexture texture)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);
                return request.GetData<Color>().ToArray();
            }

            public void Dispose()
            {
                foreach (Texture texture in new Texture[] { current, depth, history, motion, reactive, result, display, diagnostics, reprojection })
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }

        [TestCase(17,13,0f)]
        [TestCase(17,13,1f)]
        [TestCase(1,9,1f)]
        [TestCase(32,24,1f)]
        public void FusedResolve_MatchesTwoDispatchReference(int width,int height,float historyValid)
        {
            using(var fused=new Fixture(width,height))
            using(var reference=new Fixture(width,height,true))
            {
                var pixels=new Color[width*height];
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                    pixels[y*width+x]=new Color((x%7)*.3f,(y%5)*.2f,(x+y)%3==0?8:.01f,1);
                foreach(var f in new[]{fused,reference})
                {
                    f.current.SetPixels(pixels);f.current.Apply(false,false);
                    f.Run(historyValid);
                }
                foreach(var pair in new[]{new[]{fused.result,reference.result},new[]{fused.display,reference.display},new[]{fused.diagnostics,reference.diagnostics}})
                {
                    var actual=fused.Read(pair[0]);var expected=reference.Read(pair[1]);
                    for(int i=0;i<actual.Length;i++) for(int channel=0;channel<4;channel++)
                        Assert.That(actual[i][channel],Is.EqualTo(expected[i][channel]).Within(Mathf.Max(.002f,Mathf.Abs(expected[i][channel])*.001f)),$"pixel {i} channel {channel}");
                }
            }
        }

        [TestCase(17,13)][TestCase(1,9)][TestCase(9,1)]
        public void ResetDoesNotReadPoisonedHistoryAndWritesEveryPixel(int width,int height)
        {
            using(var f=new Fixture(width,height))
            {
                f.Fill(f.history,new Color(float.NaN,float.NaN,float.NaN,float.NaN));f.Run(0);
                foreach(var p in f.Read(f.result))
                { Assert.That(p.r,Is.EqualTo(.25f).Within(.0003));Assert.That(p.g,Is.EqualTo(.125f).Within(.0003));Assert.That(p.b,Is.EqualTo(.0625f).Within(.0003));Assert.AreEqual(1,p.a); }
                foreach(var p in f.Read(f.diagnostics)) { Assert.AreEqual(0,p.g);Assert.AreEqual(1,p.b); }
            }
        }

        [Test]
        public void FootprintWithoutAcceptedSamplesRejectsHistory()
        {
            using (var f = new Fixture(17, 13))
            {
                f.Run();
                Color accepted = f.Read(f.diagnostics)[6 * 17 + 8];
                Assert.AreEqual(1, accepted.r);
                Assert.That(accepted.g, Is.GreaterThan(.9));
                Assert.AreEqual(0, accepted.b);
                // A sample count below 1 marks a texel no resolve accepted.
                f.Fill(f.history, new Color(.25f, .125f, .0625f, 0));
                f.Run();
                Color rejected = f.Read(f.diagnostics)[6 * 17 + 8];
                Assert.AreEqual(0, rejected.r);
                Assert.AreEqual(0, rejected.g);
                Assert.AreEqual(4, rejected.b);
            }
        }

        [Test]
        public void HistoryReprojectionSubtractsMotion()
        {
            using (var f = new Fixture(17, 13))
            {
                f.Fill(f.motion, new Color(1f / 17, -2f / 13, 0, 0));
                f.Run();
                Color p = f.Read(f.reprojection)[6 * 17 + 8];
                Assert.That(p.r * 17, Is.EqualTo(7.5f).Within(.0001));
                Assert.That(p.g * 13, Is.EqualTo(8.5f).Within(.0001));
                Assert.That(p.b * 17, Is.EqualTo(1f).Within(.0001));
                Assert.That(p.a * 13, Is.EqualTo(-2f).Within(.0001));
            }
        }

        [Test]
        public void InvalidMotionOffscreenAndReactiveRejectHistory()
        {
            using (var f = new Fixture(17, 13))
            {
                f.Fill(f.motion, new Color(float.NaN, float.NaN, 0, 0));
                f.Run();
                AssertHistoryUnused(f, 2);
                f.Fill(f.motion, new Color(2, 0, 0, 0));
                f.Run();
                AssertHistoryUnused(f, 3);
                f.Fill(f.motion, Color.clear);
                f.Fill(f.reactive, Color.white);
                f.Run();
                AssertHistoryUnused(f, 5);
            }
        }

        static void AssertHistoryUnused(Fixture f, float reason)
        {
            foreach (Color p in f.Read(f.diagnostics))
            {
                Assert.AreEqual(0, p.g);
                Assert.AreEqual(reason, p.b);
            }
        }

        [Test]
        public void SharpenPreservesFlatHdrColorAndNeverWritesItsSource()
        {
            using(var f=new Fixture(17,13))
            {
                Color color=new Color(8,2,1,1);f.Fill(f.current,color);f.Run(0);
                foreach(var p in f.Read(f.display)) { Assert.That(p.r,Is.EqualTo(8).Within(.01));Assert.That(p.g,Is.EqualTo(2).Within(.003));Assert.That(p.b,Is.EqualTo(1).Within(.002)); }
                foreach(var p in f.current.GetPixels()) Assert.AreEqual(color,p);
            }
        }

        [Test]
        public void JitteredSlantedEdgeConvergesAgainstIndependentAreaReference()
        {
            const int width = 32;
            const int height = 24;
            using (var f = new Fixture(width, height))
            {
                var reference = new float[width * height];
                var mask = new bool[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int at = y * width + x;
                        float edge = width * .47f + .35f * (y + .5f - height * .5f);
                        mask[at] = x > 2 && x < width - 3 && y > 2 && y < height - 3 && Mathf.Abs(x + .5f - edge) < 1.25f;
                        for (int sy = 0; sy < 32; sy++)
                        {
                            for (int sx = 0; sx < 32; sx++)
                            {
                                if (x + (sx + .5f) / 32 >= width * .47f + .35f * (y + (sy + .5f) / 32 - height * .5f))
                                {
                                    reference[at] += 1f / 1024;
                                }
                            }
                        }
                    }
                }
                var mean = new double[width * height];
                var square = new double[width * height];
                double rawError = 0;
                double taaError = 0;
                int observations = 0;
                for (int frame = 0; frame < 128; frame++)
                {
                    // The rasterized edge moves with the jitter; the resolve sees only the jittered color.
                    Vector2 jitter = frame == 0 ? Vector2.zero : new Vector2(HaltonSequence.Get(frame % 8 + 1, 2) - .5f, HaltonSequence.Get(frame % 8 + 1, 3) - .5f) * .75f;
                    var color = new Color[width * height];
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            bool foreground = x + .5f - jitter.x >= width * .47f + .35f * (y + .5f - jitter.y - height * .5f);
                            color[y * width + x] = foreground ? Color.red : Color.blue;
                        }
                    }
                    f.current.SetPixels(color);
                    f.current.Apply(false, false);
                    f.Run(frame == 0 ? 0 : 1);
                    Color[] result = f.Read(f.result);
                    f.history.SetPixels(result);
                    f.history.Apply(false, false);
                    if (frame < 120)
                    {
                        continue;
                    }
                    for (int i = 0; i < result.Length; i++)
                    {
                        if (!mask[i])
                        {
                            continue;
                        }
                        rawError += Math.Abs(color[i].r - reference[i]);
                        taaError += Math.Abs(result[i].r - reference[i]);
                        observations++;
                        mean[i] += result[i].r / 8;
                        square[i] += result[i].r * result[i].r / 8;
                        Assert.That(result[i].g, Is.EqualTo(0).Within(.00001), "No green channel exists in this fixture.");
                    }
                }
                double maxStd = 0;
                for (int i = 0; i < mean.Length; i++)
                {
                    if (mask[i])
                    {
                        maxStd = Math.Max(maxStd, Math.Sqrt(Math.Max(0, square[i] - mean[i] * mean[i])));
                    }
                }
                TestContext.WriteLine($"raw MAE={rawError / observations}, TAA MAE={taaError / observations}, max temporal std={maxStd}");
                Assert.That(taaError / observations, Is.LessThan(.08));
                Assert.That(taaError, Is.LessThan(rawError * .7));
                Assert.That(maxStd, Is.LessThan(.05));
            }
        }

        [Test]
        public void SharpenPreservesChromaticityAcrossLuminanceDetail()
        {
            using(var f=new Fixture(17,13))
            {
                var pixels=new Color[17*13];
                for(int y=0;y<13;y++) for(int x=0;x<17;x++)
                {
                    float intensity = x % 3 == 0 ? 0.1f : x % 3 == 1 ? 0.5f : 0.8f;
                    pixels[y*17+x]=new Color(intensity,intensity*.2f,intensity*.1f,1);
                }
                f.current.SetPixels(pixels);f.current.Apply(false,false);f.Run(0);
                foreach(var color in f.Read(f.display))
                {
                    Assert.That(color.g/color.r,Is.EqualTo(.2f).Within(.001));
                    Assert.That(color.b/color.r,Is.EqualTo(.1f).Within(.001));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClosestDepthMotionDilation_SelectsForegroundVelocity(bool upscaling)
        {
            const int inputWidth = 17, inputHeight = 13;
            int width = upscaling ? inputWidth * 2 : inputWidth;
            int height = upscaling ? inputHeight * 2 : inputHeight;
            using (var f = new Fixture(width, height, inputW: inputWidth, inputH: inputHeight))
            {
                const int cx = 8, cy = 6;
                var depthPixels = new Color[inputWidth * inputHeight];
                Array.Fill(depthPixels, new Color(0.5f, 0, 0, 0));
                float foregroundDepth = SystemInfo.usesReversedZBuffer ? 0.9f : 0.1f;
                depthPixels[cy * inputWidth + (cx + 1)] = new Color(foregroundDepth, 0, 0, 0);
                f.depth.SetPixels(depthPixels);
                f.depth.Apply(false, false);

                var motionPixels = new Color[inputWidth * inputHeight];
                Vector2 expectedVelocity = new Vector2(2f / width, -1f / height);
                motionPixels[cy * inputWidth + (cx + 1)] = new Color(expectedVelocity.x, expectedVelocity.y, 0, 0);
                f.motion.SetPixels(motionPixels);
                f.motion.Apply(false, false);

                f.Run(historyValid: 1f, upscaling: upscaling);
                Color[] reproj = f.Read(f.reprojection);

                int checkX = upscaling ? cx * 2 : cx;
                int checkY = upscaling ? cy * 2 : cy;
                Color centerReproj = reproj[checkY * width + checkX];
                Assert.That(centerReproj.b, Is.EqualTo(expectedVelocity.x).Within(1e-5f));
                Assert.That(centerReproj.a, Is.EqualTo(expectedVelocity.y).Within(1e-5f));

                Color farReproj = reproj[0];
                Assert.That(farReproj.b, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(farReproj.a, Is.EqualTo(0f).Within(1e-5f));
            }
        }

        [Test]
        public void Upscaling_SharpeningPreservesFlatHdrColorAndChromaticity()
        {
            const int inputSize = 8;
            const int displaySize = 16;
            using (var f = new Fixture(displaySize, displaySize, inputW: inputSize, inputH: inputSize))
            {
                Color expected = new Color(2.0f, 0.6f, 0.2f, 1.0f);
                f.Fill(f.current, expected);
                f.Fill(f.history, new Color(float.NaN, float.NaN, float.NaN, 1));
                f.Run(historyValid: 0f, upscaling: true, sharpness: 1.0f);

                Color[] displayPixels = f.Read(f.display);
                Color[] historyPixels = f.Read(f.result);
                for (int i = 0; i < displayPixels.Length; i++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        Assert.That(displayPixels[i][c], Is.EqualTo(expected[c]).Within(0.01f), $"Display pixel {i}, channel {c}");
                        Assert.That(historyPixels[i][c], Is.EqualTo(expected[c]).Within(0.01f), $"History pixel {i}, channel {c}");
                    }
                }

                var isolated = new Color[inputSize * inputSize];
                isolated[inputSize * 4 + 4] = new Color(10f, 2f, 1f, 1f);
                f.current.SetPixels(isolated);
                f.current.Apply(false, false);
                f.Run(historyValid: 0f, upscaling: true, sharpness: 1.0f);

                Color highlight = f.Read(f.display)[displaySize * 8 + 8];
                Assert.IsTrue(float.IsFinite(highlight.r) && float.IsFinite(highlight.g) && float.IsFinite(highlight.b));
                Assert.Greater(highlight.r, 1f);
                Assert.That(highlight.g / highlight.r, Is.EqualTo(0.2f).Within(0.01f));
                Assert.That(highlight.b / highlight.r, Is.EqualTo(0.1f).Within(0.01f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Upscaling_SharpeningDoesNotContaminateHistory(bool reset)
        {
            const int width = 33, height = 19, inputWidth = 17, inputHeight = 11;
            using (var f = new Fixture(width, height, inputW: inputWidth, inputH: inputHeight))
            {
                var sourcePixels = new Color[inputWidth * inputHeight];
                for (int y = 0; y < inputHeight; y++)
                {
                    for (int x = 0; x < inputWidth; x++)
                    {
                        sourcePixels[y * inputWidth + x] = new Color((x % 3 == 0 ? 3f : 0.1f) + 0.03f * y, 0.1f + x * 0.02f, 0.3f, 1f);
                    }
                }
                f.current.SetPixels(sourcePixels);
                f.current.Apply(false, false);
                f.Fill(f.history, reset ? new Color(float.NaN, float.NaN, float.NaN, 1f) : new Color(0.4f, 0.2f, 0.3f, 1f));

                f.Run(historyValid: reset ? 0f : 1f, upscaling: true, sharpness: 0f);
                Color[] historySharp0 = f.Read(f.result);
                Color[] displaySharp0 = f.Read(f.display);

                f.Run(historyValid: reset ? 0f : 1f, upscaling: true, sharpness: 0.5f);
                Color[] historySharpHalf = f.Read(f.result);
                Color[] displaySharpHalf = f.Read(f.display);

                float displayDifference = 0f;
                for (int i = 0; i < historySharp0.Length; i++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        Assert.IsTrue(float.IsFinite(historySharp0[i][c]));
                        Assert.IsTrue(float.IsFinite(historySharpHalf[i][c]));
                    }
                    Assert.GreaterOrEqual(historySharp0[i].a, 1f);
                    Assert.GreaterOrEqual(historySharpHalf[i].a, 1f);
                    Assert.AreEqual(historySharp0[i], historySharpHalf[i], "Sharpening contaminated history.");
                    Assert.That(displaySharp0[i].r, Is.EqualTo(historySharp0[i].r).Within(0.002f));
                    Assert.That(displaySharp0[i].g, Is.EqualTo(historySharp0[i].g).Within(0.002f));
                    Assert.That(displaySharp0[i].b, Is.EqualTo(historySharp0[i].b).Within(0.002f));
                    displayDifference += Math.Abs(displaySharp0[i].r - displaySharpHalf[i].r);
                }
                Assert.Greater(displayDifference, 0.01f, "The sharpness variants must actually exercise different display results.");
            }
        }

        [Test]
        public void Upscaling_JitteredSlantedEdgeConverges()
        {
            const int width = 32, height = 24, inputWidth = 16, inputHeight = 12;
            const float scaleX = width / (float)inputWidth;
            const float scaleY = height / (float)inputHeight;
            using (var f = new Fixture(width, height, inputW: inputWidth, inputH: inputHeight))
            {
                var reference = new float[width * height];
                var mask = new bool[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int at = y * width + x;
                        float edge = width * .47f + .35f * (y + .5f - height * .5f);
                        mask[at] = x > 3 && x < width - 4 && y > 3 && y < height - 4 && Mathf.Abs(x + .5f - edge) < 1.5f;
                        for (int sy = 0; sy < 32; sy++)
                        {
                            for (int sx = 0; sx < 32; sx++)
                            {
                                if (x + (sx + .5f) / 32 >= width * .47f + .35f * (y + (sy + .5f) / 32 - height * .5f))
                                {
                                    reference[at] += 1f / 1024;
                                }
                            }
                        }
                    }
                }

                double initialMae = 0;
                double settledMae = 0;
                int observations = 0;
                var mean = new double[width * height];
                var square = new double[width * height];
                for (int frame = 0; frame < 64; frame++)
                {
                    Vector2 jitter = frame == 0
                        ? Vector2.zero
                        : new Vector2(HaltonSequence.Get(frame % 32 + 1, 2) - .5f, HaltonSequence.Get(frame % 32 + 1, 3) - .5f);
                    var color = new Color[inputWidth * inputHeight];
                    for (int ry = 0; ry < inputHeight; ry++)
                    {
                        for (int rx = 0; rx < inputWidth; rx++)
                        {
                            float dx = (rx + .5f - jitter.x) * scaleX;
                            float dy = (ry + .5f - jitter.y) * scaleY;
                            bool foreground = dx >= width * .47f + .35f * (dy - height * .5f);
                            color[ry * inputWidth + rx] = foreground ? Color.red : Color.blue;
                        }
                    }
                    f.current.SetPixels(color);
                    f.current.Apply(false, false);
                    f.Run(historyValid: frame == 0 ? 0f : 1f, upscaling: true, jitterPixels: jitter, sharpness: 0f);
                    Color[] result = f.Read(f.result);
                    f.history.SetPixels(result);
                    f.history.Apply(false, false);

                    if (frame == 0)
                    {
                        for (int i = 0; i < result.Length; i++)
                        {
                            if (mask[i])
                            {
                                initialMae += Math.Abs(result[i].r - reference[i]);
                            }
                        }
                    }
                    if (frame < 56)
                    {
                        continue;
                    }
                    for (int i = 0; i < result.Length; i++)
                    {
                        if (!mask[i])
                        {
                            continue;
                        }
                        settledMae += Math.Abs(result[i].r - reference[i]);
                        observations++;
                        mean[i] += result[i].r / 8;
                        square[i] += result[i].r * result[i].r / 8;
                        Assert.That(result[i].g, Is.EqualTo(0).Within(.00001), "No green channel exists in this fixture.");
                    }
                }

                double maxStd = 0;
                int maskCount = 0;
                for (int i = 0; i < mean.Length; i++)
                {
                    if (mask[i])
                    {
                        maskCount++;
                        maxStd = Math.Max(maxStd, Math.Sqrt(Math.Max(0, square[i] - mean[i] * mean[i])));
                    }
                }
                double initialAvg = initialMae / maskCount;
                double settledAvg = settledMae / observations;
                TestContext.WriteLine($"TAAU frame-0 MAE={initialAvg}, settled MAE={settledAvg}, max temporal std={maxStd}");
                Assert.That(settledAvg, Is.LessThan(.08));
                Assert.That(settledAvg, Is.LessThan(initialAvg * .7));
                Assert.That(maxStd, Is.LessThan(.05));
            }
        }
    }
}
