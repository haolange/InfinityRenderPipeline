using System;
using System.Collections.Generic;
using Unity.Collections;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DeferredLightLoopGpuTests
    {
        struct ShadowSlice
        {
            public Vector4 rect, depth, moments, originSize, axisU, axisV;
        }
        struct ProbeRecord
        {
            public Vector4 row0, row1, row2, extents, capture, projection, parameters;
        }
        // The production kernel checks aggregate contribution across words; the exact-index probe and normal multi-tile frames complement it.
        [TestCase(0)] [TestCase(1)] [TestCase(31)] [TestCase(32)]
        [TestCase(33)] [TestCase(63)] [TestCase(64)] [TestCase(65)]
        public void ActualDeferredKernel_AccumulatesLocalLightsAcrossMaskWords(int localCount)
        {
            var actual = Dispatch(new Fixture { localCount = localCount }).color[0][8 * 16 + 8];
            float distanceSquared = 1 + 2 * .0625f * .0625f;
            float window = 1 - Mathf.Pow(Mathf.Sqrt(distanceSquared) / 20, 4);
            float expected = localCount == 0 ? 0 : .5f / Mathf.PI * 30 * window * window / distanceSquared / Mathf.Sqrt(distanceSquared);
            for (int channel = 0; channel < 3; ++channel)
                Assert.That(actual[channel], Is.EqualTo(expected).Within(.015f), "Actual production Deferred kernel, local=" + localCount);
        }

        [Test]
        public void ActualDeferredKernel_WeightsScaleOnlyTheirLightingTerm()
        {
            var results = Dispatch(new Fixture { localCount = 1, weightCases = true }).color;
            float diffuseEnergy = 0, specularEnergy = 0;
            for (int pixel = 0; pixel < 16 * 16; ++pixel)
            {
                for (int channel = 0; channel < 3; ++channel)
                {
                    float ambient = results[0][pixel][channel];
                    float specularTerm = results[2][pixel][channel] - ambient;
                    float diffuseTerm = results[3][pixel][channel] - ambient;
                    diffuseEnergy += diffuseTerm; specularEnergy += specularTerm;
                    var expected = new[] { ambient + diffuseTerm + specularTerm, ambient + .5f * diffuseTerm + specularTerm, ambient + diffuseTerm + .5f * specularTerm };
                    var indices = new[] { 1, 4, 5 };
                    for (int equation = 0; equation < indices.Length; ++equation)
                    {
                        float actual = results[indices[equation]][pixel][channel];
                        Assert.IsFalse(float.IsNaN(actual) || float.IsInfinity(actual));
                        Assert.That(actual, Is.EqualTo(expected[equation]).Within(1e-5f * Mathf.Max(1, Mathf.Abs(expected[equation]))),
                            $"Production RGBA32F Deferred: pixel {pixel}, channel {channel}, weight case {indices[equation]}");
                    }
                }
            }
            Assert.Greater(diffuseEnergy, 0, "Diffuse control must exercise a nonzero term.");
            Assert.Greater(specularEnergy, 0, "Specular control must exercise a nonzero term.");
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void ActualDeferredKernel_OddExtentWritesEveryPixel(bool ao, bool contact)
        {
            var fixture = new Fixture { localCount = 1, size = 19, occlusion = ao ? 1.0f : (float?)null, contact = contact };
            var values = Dispatch(fixture).color[0];
            Assert.AreEqual(19 * 19, values.Length);
            foreach (Color pixel in values)
            {
                Assert.Greater(pixel.r, 0, "Every odd-extent pixel must receive local lighting.");
                for (int channel = 0; channel < 4; ++channel)
                {
                    Assert.IsFalse(float.IsNaN(pixel[channel]) || float.IsInfinity(pixel[channel]));
                }
            }
        }

        // Scene Color 1 with diffuse share 0.5 splits into emission 0.5 and indirect diffuse 0.5. Occlusion scales only the
        // indirect part; with no light and a black environment the result is E + I·ao and the new share I·ao / result.
        [TestCase(1.0f)]
        [TestCase(0.25f)]
        [TestCase(0.0f)]
        public void ActualDeferredKernel_OccludesOnlyTheIndirectShareOfSceneColor(float occlusion)
        {
            var fixture = new Fixture { sceneColor = Color.white, sidebandRatio = 0.5f, occlusion = occlusion };
            Result result = Dispatch(fixture);
            float indirect = 0.5f * occlusion;
            float expected = 0.5f + indirect;
            AssertUniform(result.color[0], expected, 1e-4f, "E + I·ao");
            AssertUniformRatio(result.sideband, indirect / expected, "diffuse share after deferred shading");
        }

        // SSGI replaces the occluded indirect diffuse by its confidence and adds its premultiplied irradiance times the
        // diffuse color (albedo 0.5, metallic 0): I' = I·ao·(1 − a) + 0.5·ssgi.rgb.
        [Test]
        public void ActualDeferredKernel_SSGIReplacesTheOccludedIndirectByConfidence()
        {
            var fixture = new Fixture { sceneColor = Color.white, sidebandRatio = 0.5f, occlusion = 0.25f, ssgi = new Color(0.2f, 0.2f, 0.2f, 0.5f) };
            Result result = Dispatch(fixture);
            float indirect = 0.5f * 0.25f * (1 - 0.5f) + 0.5f * 0.2f;
            float expected = 0.5f + indirect;
            AssertUniform(result.color[0], expected, 1e-4f, "E + SSGI-replaced indirect");
            AssertUniformRatio(result.sideband, indirect / expected, "diffuse share with SSGI");
        }

        // Without the deferred stencil bit the pixel is not deferred: Scene Color and the sideband keep the geometry values.
        [Test]
        public void ActualDeferredKernel_LeavesPixelsWithoutTheDeferredBit()
        {
            var fixture = new Fixture { localCount = 1, sceneColor = new Color(0.3f, 0.6f, 0.9f, 1), sidebandRatio = 0.25f, occlusion = 0.0f, stencil = 4 };
            Result result = Dispatch(fixture);
            foreach (Color pixel in result.color[0])
            {
                Assert.AreEqual(0.3f, pixel.r, 1e-6f);
                Assert.AreEqual(0.6f, pixel.g, 1e-6f);
                Assert.AreEqual(0.9f, pixel.b, 1e-6f);
            }
            AssertUniformRatio(result.sideband, 0.25f, "untouched geometry share");
        }

        static void AssertUniform(Color[] pixels, float expected, float tolerance, string label)
        {
            foreach (Color pixel in pixels)
            {
                for (int channel = 0; channel < 3; ++channel)
                {
                    Assert.That(pixel[channel], Is.EqualTo(expected).Within(tolerance), label);
                }
            }
        }

        // The sideband is RGBA8: the share quantizes once to 1/255; G, B and A pass through untouched.
        static void AssertUniformRatio(Color[] sideband, float expected, string label)
        {
            foreach (Color pixel in sideband)
            {
                Assert.That(pixel.r, Is.EqualTo(expected).Within(0.51f / 255.0f), label);
                Assert.That(pixel.g, Is.EqualTo(1.0f).Within(1e-6f), "first mixed light visibility passes through");
                Assert.That(pixel.b, Is.EqualTo(1.0f).Within(1e-6f), "second mixed light visibility passes through");
                Assert.That(pixel.a, Is.EqualTo(Fixture.SidebandAlpha).Within(1e-6f), "light channels and profile pass through");
            }
        }

        // One deferred dispatch: every pixel shares the same surface, geometry color and sideband.
        sealed class Fixture
        {
            // Light channel 0 (high nibble), no profile.
            internal const float SidebandAlpha = 16.0f / 255.0f;
            internal int localCount;
            internal bool weightCases;
            internal int size = 16;
            internal float? occlusion;
            internal bool contact;
            internal Color sceneColor = Color.clear;
            internal float? sidebandRatio;
            internal Color? ssgi;
            internal uint stencil = 2;
        }

        struct Result
        {
            internal Color[][] color;
            internal Color[] sideband;
        }

        static Result Dispatch(Fixture fixture)
        {
            int localCount = fixture.localCount;
            bool weightCases = fixture.weightCases;
            int size = fixture.size;
            bool ao = fixture.occlusion.HasValue;
            bool contact = fixture.contact;
            bool sideband = fixture.sidebandRatio.HasValue;
            bool ssgi = fixture.ssgi.HasValue;
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A compute device with GPU readback is required.");
            const int directionalCount = 3;
            var owned = new List<UnityEngine.Object>();
            var buffers = new List<GraphicsBuffer>();
            RenderTexture previous = RenderTexture.active;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/Pass/Lighting/Compute_DeferredShading.compute");
                Assert.IsNotNull(source);
                var shader = UnityEngine.Object.Instantiate(source);
                owned.Add(shader);
                shader.shaderKeywords = Array.Empty<string>();
                if (sideband)
                {
                    shader.EnableKeyword("INFINITY_SIDEBAND");
                }
                if (ao)
                {
                    shader.EnableKeyword("DEFERRED_AO");
                }
                if (ssgi)
                {
                    shader.EnableKeyword("DEFERRED_SSGI");
                }
                if (contact)
                {
                    shader.EnableKeyword("DEFERRED_CONTACT_SHADOW");
                }
                int kernel = shader.FindKernel("DeferredShadingCS");
                Texture2D Texture(Color color)
                {
                    var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                    owned.Add(texture);
                    var pixels = new Color[size * size];
                    Array.Fill(pixels, color);
                    texture.SetPixels(pixels); texture.Apply();
                    return texture;
                }
                RenderTexture Target()
                {
                    var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
                    owned.Add(target); target.Create();
                    RenderTexture.active = target; GL.Clear(false, true, Color.clear);
                    RenderTexture.active = previous;
                    return target;
                }
                GraphicsBuffer Buffer<T>(T[] data, int stride) where T : struct
                {
                    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, stride); buffers.Add(buffer); buffer.SetData(data); return buffer;
                }
                var black = Texture(Color.clear);
                var white = Texture(Color.white);
                var normal = Texture(new Color(1f, 1f, 0f, weightCases ? .5f : 0f));
                var albedo = Texture(new Color(.5f, .5f, .5f, weightCases ? .12f : .65f));
                var environment = new Texture2DArray(1, 1, 6, TextureFormat.RGBAFloat, false, true);
                owned.Add(environment);
                for (int i = 0; i < 6; i++) environment.SetPixels(new[] { Color.clear }, i);
                environment.Apply();
                var output = Target();
                // Depth 0 (not far) and the fixture's stencil admission bits, read through both planes of one target.
                var depthStencil = new RenderTexture(new RenderTextureDescriptor(size, size)
                {
                    graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                    depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat_S8_UInt
                });
                owned.Add(depthStencil);
                Assert.IsTrue(depthStencil.Create());
                RenderTexture sidebandTarget = null;
                if (sideband)
                {
                    sidebandTarget = new RenderTexture(new RenderTextureDescriptor(size, size)
                    {
                        graphicsFormat = EffectivePipelineConfiguration.SidebandFormat,
                        depthBufferBits = 0,
                        enableRandomWrite = true
                    });
                    owned.Add(sidebandTarget);
                    Assert.IsTrue(sidebandTarget.Create());
                }
                var records = new LightRecord[directionalCount + Math.Max(1, localCount)];
                for (int i = 0; i < records.Length; i++)
                {
                    records[i].positionRange = new Vector4(0, 0, -1, 20);
                    records[i].forward = new Vector4(0, 0, 1, 0);
                    records[i].right = new Vector4(1, 0, 0, 0);
                    records[i].weights = Vector4.one;
                    records[i].lightLayer = 1;
                    records[i].shadowMatrixIndex = -1;
                    records[i].bakedVisibilitySlot = -1;
                    records[i].cookieIndex = -1;
                    // Normalized total power makes every nonempty count share one analytic expectation.
                    records[i].radiance = i < directionalCount ? Vector4.zero : Vector4.one * (30f / Math.Max(1, localCount));
                }
                int words = Math.Max(1, (localCount + 31) / 32);
                int tileCount = (size + 15) / 16;
                var masks = new uint[words * tileCount * tileCount];
                for (int tile = 0; tile < tileCount * tileCount; ++tile)
                    for (int i = 0; i < localCount; i++) masks[tile * words + i / 32] |= 1u << (i & 31);
                uint packed = localCount == 0 ? 0x0000ffffu : (uint)(localCount - 1) << 16;
                var lightBuffer = Buffer(records, Marshal.SizeOf<LightRecord>());
                shader.SetBuffer(kernel, "g_LightRecordBuffer", lightBuffer);
                shader.SetBuffer(kernel, "SRV_LightTileMasks", Buffer(masks, 4));
                shader.SetBuffer(kernel, "SRV_LightZBins", Buffer(new[] { packed }, 4));
                shader.SetBuffer(kernel, "SRV_LocalShadowMatrices", Buffer(new[] { Matrix4x4.identity }, 64));
                // Replay fixtures bind the actual stage's inactive-resource layout, never a later shader payload.
                bool shadowSlices = System.IO.File.ReadAllText("Packages/com.infinity.render-pipeline/Shaders/Library/ShadowSampling.hlsl").Contains("struct LocalShadowSlice");
                if (shadowSlices)
                {
                    shader.SetBuffer(kernel, "SRV_LocalShadowRects", Buffer(new[] { new ShadowSlice() }, Marshal.SizeOf<LocalShadowSliceGPU>()));
                }
                else shader.SetBuffer(kernel, "SRV_LocalShadowRects", Buffer(new[] { Vector4.zero }, 16));
                if (System.IO.File.Exists("Packages/com.infinity.render-pipeline/Shaders/Library/Lighting/LightCookie.hlsl"))
                {
                    shader.SetBuffer(kernel, "SRV_LightCookies", Buffer(new[] { Matrix4x4.zero }, 64));
                    // The production keywords are global (the pipeline sets them per pass); restored in finally.
                    Shader.EnableKeyword("INFINITY_LIGHT_COOKIES");
                    shader.SetTexture(kernel, "SRV_LightCookieAtlas", black);
                }
                if (System.IO.File.Exists("Packages/com.infinity.render-pipeline/Shaders/Library/Lighting/ReflectionProbes.hlsl"))
                {
                    var probe = new ProbeRecord { row0 = Vector4.zero, row1 = Vector4.zero, row2 = Vector4.zero,
                        extents = Vector4.zero, capture = Vector4.zero, projection = Vector4.zero, parameters = Vector4.zero };
                    shader.SetBuffer(kernel, "SRV_ReflectionProbes", Buffer(new[] { probe }, 112));
                    shader.SetBuffer(kernel, "SRV_ProbeClusterHeaders", Buffer(new[] { Vector2.zero }, 8));
                    shader.SetBuffer(kernel, "SRV_ProbeClusterIndices", Buffer(new[] { 0u }, 4));
                    Shader.EnableKeyword("INFINITY_REFLECTION_PROBES");
                    shader.SetTexture(kernel, "SRV_ReflectionProbeAtlas", environment);
                }
                using var reflection = new UniformBinding<global::InfinityTech.Rendering.Reflection.ReflectionUniform>(default);
                using var lightLoop = new UniformBinding<LightLoopUniform>(new LightLoopUniform
                {
                    tileParameters = new Vector4(size, size, tileCount, words),
                    zBinParameters = new Vector4(1, 0, 0, 0),
                    directionalCount = directionalCount,
                    localCount = localCount
                });
                shader.SetVector("DeferredShading_Resolution", new Vector4(size, size, 1f / size, 1f / size));
                shader.SetInt("DeferredShading_HasRenderingLayers", 1);
                using var view = new UniformBinding<ViewUniform>(new ViewUniform
                {
                    matrix_InvFlipYJitterProj = Matrix4x4.identity,
                    matrix_InvViewFlipYJitterProj = Matrix4x4.identity,
                    matrix_ViewToWorld = Matrix4x4.Translate(new Vector3(0, 0, -2))
                });
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { shadowDistance = 1000 });
                shader.SetFloat("_AtmosphereIBLMaxMip", 0);
                shader.SetTexture(kernel, "SRV_GBufferTextureA", albedo); shader.SetTexture(kernel, "SRV_GBufferTextureB", normal);
                shader.SetTexture(kernel, "SRV_DepthTexture", depthStencil, 0, RenderTextureSubElement.Depth);
                shader.SetTexture(kernel, "SRV_StencilTexture", depthStencil, 0, RenderTextureSubElement.Stencil);
                if (ao)
                {
                    shader.SetTexture(kernel, "SRV_OcclusionTexture", Texture(new Color(fixture.occlusion.Value, 0, 0, 0)));
                }
                if (ssgi)
                {
                    shader.SetTexture(kernel, "SRV_SSGITexture", Texture(fixture.ssgi.Value));
                }
                if (sideband)
                {
                    shader.SetTexture(kernel, "UAV_Sideband", sidebandTarget);
                }
                if (contact)
                {
                    shader.SetTexture(kernel, "SRV_ContactShadowSun", white);
                    var mask = new RenderTexture(new RenderTextureDescriptor(size, size) { graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt, depthBufferBits = 0, enableRandomWrite = true });
                    owned.Add(mask); Assert.IsTrue(mask.Create());
                    RenderTexture.active = mask; GL.Clear(false, true, Color.clear); RenderTexture.active = previous;
                    shader.SetTexture(kernel, "SRV_ContactShadowMask", mask);
                }
                shader.SetTexture(kernel, "SRV_CascadeShadowMap", black); shader.SetTexture(kernel, "SRV_LocalShadowMap", black);
                shader.SetTexture(kernel, "_AtmosphereGGXPrefilter", environment);
                shader.SetTexture(kernel, "UAV_LightingTexture", output);
                var weights = weightCases ? new[] { Vector2.zero, Vector2.one, new Vector2(0, 1), new Vector2(1, 0), new Vector2(.5f, 1), new Vector2(1, .5f) } : new[] { Vector2.one };
                var results = new Color[weights.Length][];
                for (int w = 0; w < weights.Length; ++w)
                {
                    records[directionalCount].weights = new Vector4(weights[w].x, weights[w].y, 1, 1);
                    lightBuffer.SetData(records);
                    // Deferred reads Scene Color and the sideband in place; every invocation starts from the same geometry input.
                    using (var cmd = new CommandBuffer())
                    {
                        cmd.SetRenderTarget(output, depthStencil);
                        cmd.ClearRenderTarget(RTClearFlags.All, fixture.sceneColor, 0.0f, fixture.stencil);
                        if (sideband)
                        {
                            cmd.SetRenderTarget(sidebandTarget);
                            cmd.ClearRenderTarget(RTClearFlags.Color, new Color(fixture.sidebandRatio.Value, 1, 1, Fixture.SidebandAlpha), 1.0f, 0);
                        }
                        Graphics.ExecuteCommandBuffer(cmd);
                    }
                    shader.GetKernelThreadGroupSizes(kernel, out uint threadsX, out uint threadsY, out _);
                    shader.Dispatch(kernel, Mathf.CeilToInt(size / (float)threadsX), Mathf.CeilToInt(size / (float)threadsY), 1);
                    var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
                    Assert.IsFalse(readback.hasError);
                    results[w] = readback.GetData<Color>().ToArray();
                }
                Color[] sidebandValues = null;
                if (sideband)
                {
                    var sidebandReadback = AsyncGPUReadback.Request(sidebandTarget, 0, TextureFormat.RGBAFloat);
                    sidebandReadback.WaitForCompletion();
                    Assert.IsFalse(sidebandReadback.hasError);
                    sidebandValues = sidebandReadback.GetData<Color>().ToArray();
                }
                return new Result { color = results, sideband = sidebandValues };

            }
            finally
            {
                Shader.DisableKeyword("INFINITY_LIGHT_COOKIES");
                Shader.DisableKeyword("INFINITY_REFLECTION_PROBES");
                RenderTexture.active = previous;
                foreach (var buffer in buffers) buffer.Dispose();
                foreach (var item in owned) UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
