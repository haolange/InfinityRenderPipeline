using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.LightPipeline;
using InfinityTech.Rendering.MaterialPipeline;

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
        // The production light loop checks aggregate contribution across words; the exact-index probe and normal multi-tile frames complement it.
        static readonly int[] s_LocalCounts = { 0, 1, 31, 32, 33, 63, 64, 65 };
        static readonly float[] s_Occlusions = { 1.0f, 0.25f, 0.0f };

        [UnityTest]
        public IEnumerator EmissionUsesThePixelStencilEvenWithAStalePassEnable([Values(false, true)] bool deferred)
        {
            const float surfaceDepth = 0.5f;
            var initial = new Color(0.125f, 0.25f, 0.375f, 1f);
            // The endpoints survive Unity's material color-space conversion exactly.
            var emission = new Color(1f, 0f, 1f, 0f);
            Shader shader = Shader.Find("InfinityPipeline/InfinityLit");
            Assert.IsNotNull(shader);
            var material = new Material(shader);
            var mesh = new Mesh();
            var target = new RenderTexture(new RenderTextureDescriptor(4, 4)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt
            });
            var view = new UniformBuffer<ViewUniform>();
            AsyncGPUReadbackRequest readback = default;
            bool readbackPending = false;
            try
            {
                Assert.IsTrue(target.Create());
                material.SetFloat("_SurfaceRoute", deferred ? 0f : 1f);
                material.SetColor("_EmissionColor", emission);
                MaterialRouteUtility.ApplyPassState(material);
                string passName = MaterialRouteUtility.EmissivePassName;
                Assert.AreEqual(deferred, material.GetShaderPassEnabled(passName));
                material.SetShaderPassEnabled(passName, true);
                int passIndex = material.FindPass(passName);
                Assert.GreaterOrEqual(passIndex, 0);
                int stencil = (int)material.GetFloat("_StencilRefGBuffer") | StencilBits.UserMask;
                mesh.vertices = new[] { new Vector3(-1, -1, surfaceDepth), new Vector3(-1, 3, surfaceDepth), new Vector3(3, -1, surfaceDepth) };
                mesh.triangles = new[] { 0, 1, 2, 2, 1, 0 };

                using var commands = new CommandBuffer();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(RTClearFlags.All, initial, surfaceDepth, (uint)stencil);
                Graphics.ExecuteCommandBuffer(commands);
                readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                readbackPending = true;
                yield return GpuReadbackTestUtility.Await(readback);
                readbackPending = !readback.done;
                Assert.IsFalse(readbackPending);
                Assert.IsFalse(readback.hasError);
                Color before = readback.GetData<Color>()[10];
                Assert.AreEqual(initial, before);

                commands.Clear();
                commands.SetRenderTarget(target);
                view.Upload(commands, new ViewUniform { matrix_ViewJitterProj = Matrix4x4.identity });
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, passIndex);
                Graphics.ExecuteCommandBuffer(commands);
                readback = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat);
                readbackPending = true;
                yield return GpuReadbackTestUtility.Await(readback);
                readbackPending = !readback.done;
                Assert.IsFalse(readbackPending);
                Assert.IsFalse(readback.hasError);
                Color after = readback.GetData<Color>()[10];
                Assert.AreEqual(deferred ? before + emission : before, after);
                TestContext.Progress.WriteLine($"{(deferred ? "Deferred" : "Forward")}: Scene Color {before} -> {after} with stencil 0x{stencil:X2}.");
            }
            finally
            {
                // Retain the draw inputs until the GPU finishes reading them, including on timeout.
                if (!readbackPending)
                {
                    view.Dispose();
                    UnityEngine.Object.DestroyImmediate(target);
                    UnityEngine.Object.DestroyImmediate(mesh);
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        [UnityTest]
        public IEnumerator RasterDeferred_AccumulatesLocalLightsAcrossMaskWords([ValueSource(nameof(s_LocalCounts))] int localCount)
        {
            Result result = default;
            yield return Dispatch(new Fixture { localCount = localCount }, value => result = value);
            var actual = result.color[0][8 * 16 + 8];
            float normalDistance = 1 + Fixture.SurfaceDepth;
            float distanceSquared = normalDistance * normalDistance + 2 * .0625f * .0625f;
            float window = 1 - Mathf.Pow(Mathf.Sqrt(distanceSquared) / 20, 4);
            float expected = localCount == 0 ? 0 : .5f / Mathf.PI * 30 * window * window / distanceSquared * (normalDistance / Mathf.Sqrt(distanceSquared));
            for (int channel = 0; channel < 3; ++channel)
                Assert.That(actual[channel], Is.EqualTo(expected).Within(.015f), "Raster deferred local light, local=" + localCount);
        }

        [UnityTest]
        public IEnumerator RasterDeferred_WeightsScaleOnlyTheirLightingTerm()
        {
            Result result = default;
            yield return Dispatch(new Fixture { localCount = 1, weightCases = true }, value => result = value);
            var results = result.color;
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

        [UnityTest]
        public IEnumerator RasterDeferred_OddExtentWritesEveryPixel([Values(false, true)] bool ao, [Values(false, true)] bool contact)
        {
            var fixture = new Fixture { localCount = 1, size = 19, occlusion = ao ? 1.0f : (float?)null, contact = contact };
            Result result = default;
            yield return Dispatch(fixture, value => result = value);
            var values = result.color[0];
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

        // Scene Color is the indirect term. Occlusion is the blend factor. The sideband is not rewritten.
        [UnityTest]
        public IEnumerator RasterDeferred_ScalesIndirectByOcclusion([ValueSource(nameof(s_Occlusions))] float occlusion)
        {
            const float geometryShare = 128.0f / 255.0f;
            var fixture = new Fixture { sceneColor = Color.white, sidebandRatio = geometryShare, occlusion = occlusion };
            Result result = default;
            yield return Dispatch(fixture, value => result = value);
            AssertUniform(result.color[0], occlusion, 1e-4f, "I·ao");
            AssertUniformRatio(result.sideband, geometryShare, "sideband is unchanged");
        }

        [UnityTest]
        public IEnumerator RasterDeferred_ScalesPackedIndirectByOcclusion()
        {
            var fixture = new Fixture { sceneColor = Color.white, sidebandRatio = 1.0f, occlusion = 0.25f,
                sceneColorFormat = EffectivePipelineConfiguration.SceneColorFormat };
            Result result = default;
            yield return Dispatch(fixture, value => result = value);
            AssertUniform(result.color[0], 0.25f, 1e-6f, "Packed indirect scaled by occlusion");
            AssertUniformRatio(result.sideband, 1.0f, "sideband is unchanged");
        }

        // Blend One SrcAlpha: rgb adds albedo · ssgi.rgb, alpha is ao · (1 − ssgi.a). Albedo 0.5, metallic 0.
        [UnityTest]
        public IEnumerator RasterDeferred_SSGIReplacesTheOccludedIndirectByConfidence()
        {
            const float geometryShare = 128.0f / 255.0f;
            var fixture = new Fixture { sceneColor = Color.white, sidebandRatio = geometryShare, occlusion = 0.25f, ssgi = new Color(0.2f, 0.2f, 0.2f, 0.5f) };
            Result result = default;
            yield return Dispatch(fixture, value => result = value);
            float expected = 0.5f * 0.2f + 1.0f * (0.25f * (1.0f - 0.5f));
            AssertUniform(result.color[0], expected, 1e-4f, "I·ao·(1−a) + albedo·ssgi");
            AssertUniformRatio(result.sideband, geometryShare, "sideband is unchanged");
        }

        // Without the deferred stencil bit the pixel is not deferred: Scene Color and the sideband keep the geometry values.
        [UnityTest]
        public IEnumerator RasterDeferred_LeavesPixelsWithoutTheDeferredBit()
        {
            var fixture = new Fixture { localCount = 1, sceneColor = new Color(0.3f, 0.6f, 0.9f, 1), sidebandRatio = 0.25f, occlusion = 0.0f, stencil = 4 };
            Result result = default;
            yield return Dispatch(fixture, value => result = value);
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
            // Half depth is preserved by the clear on both forward and reversed Z devices.
            internal const float SurfaceDepth = 0.5f;
            internal int localCount;
            internal bool weightCases;
            internal int size = 16;
            internal float? occlusion;
            internal bool contact;
            internal Color sceneColor = Color.clear;
            internal GraphicsFormat sceneColorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            internal float? sidebandRatio;
            internal Color? ssgi;
            internal uint stencil = 2;
        }

        struct Result
        {
            internal Color[][] color;
            internal Color[] sideband;
        }

        // Records one deferred draw per weight case and hands the readbacks to complete. A readback that does not finish
        // in time keeps the fixture's targets alive (they are not destroyed under a pending GPU copy).
        static IEnumerator Dispatch(Fixture fixture, Action<Result> complete)
        {
            int localCount = fixture.localCount;
            bool weightCases = fixture.weightCases;
            int size = fixture.size;
            bool ao = fixture.occlusion.HasValue;
            bool contact = fixture.contact;
            bool sideband = fixture.sidebandRatio.HasValue;
            bool ssgi = fixture.ssgi.HasValue;
            if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device with GPU readback is required.");
            const int directionalCount = 3;
            var owned = new List<UnityEngine.Object>();
            var buffers = new List<GraphicsBuffer>();
            RenderTexture previous = RenderTexture.active;
            bool readbackPending = false;
            // The open Game view binds the scene light buffer globally between draws and would replace this fixture's records.
            Camera[] cameras = Camera.allCameras;
            var cameraEnabled = new bool[cameras.Length];
            for (int i = 0; i < cameras.Length; ++i)
            {
                cameraEnabled[i] = cameras[i].enabled;
                cameras[i].enabled = false;
            }
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Shaders/Pass/Lighting/DeferredShading.shader");
                Assert.IsNotNull(source);
                var material = new Material(source);
                owned.Add(material);
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
                    var target = new RenderTexture(new RenderTextureDescriptor(size, size, fixture.sceneColorFormat, 0) { enableRandomWrite = true });
                    owned.Add(target);
                    Assert.IsTrue(target.Create());
                    Assert.AreEqual(fixture.sceneColorFormat, target.graphicsFormat);
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
                // The fixture reads both depth and stencil through the planes of one target.
                var depthStencil = new RenderTexture(new RenderTextureDescriptor(size, size)
                {
                    graphicsFormat = GraphicsFormat.None,
                    depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt,
                    stencilFormat = GraphicsFormat.R8_UInt
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
                material.SetBuffer("g_LightRecordBuffer", lightBuffer);
                material.SetBuffer("SRV_LightTileMasks", Buffer(masks, 4));
                material.SetBuffer("SRV_LightZBins", Buffer(new[] { packed }, 4));
                material.SetBuffer("SRV_LocalShadowMatrices", Buffer(new[] { Matrix4x4.identity }, 64));
                // Replay fixtures bind the actual stage's inactive-resource layout, never a later shader payload.
                bool shadowSlices = System.IO.File.ReadAllText("Packages/com.infinity.render-pipeline/Shaders/Library/ShadowSampling.hlsl").Contains("struct LocalShadowSlice");
                if (shadowSlices)
                {
                    material.SetBuffer("SRV_LocalShadowRects", Buffer(new[] { new ShadowSlice() }, Marshal.SizeOf<LocalShadowSliceGPU>()));
                }
                else material.SetBuffer("SRV_LocalShadowRects", Buffer(new[] { Vector4.zero }, 16));
                if (System.IO.File.Exists("Packages/com.infinity.render-pipeline/Shaders/Library/Lighting/LightCookie.hlsl"))
                {
                    material.SetBuffer("SRV_LightCookies", Buffer(new[] { Matrix4x4.zero }, 64));
                    // The production keywords are global (the pipeline sets them per pass); restored in finally.
                    Shader.EnableKeyword("INFINITY_LIGHT_COOKIES");
                    material.SetTexture("SRV_LightCookieAtlas", black);
                }
                if (System.IO.File.Exists("Packages/com.infinity.render-pipeline/Shaders/Library/Lighting/ReflectionProbes.hlsl"))
                {
                    var probe = new ProbeRecord { row0 = Vector4.zero, row1 = Vector4.zero, row2 = Vector4.zero,
                        extents = Vector4.zero, capture = Vector4.zero, projection = Vector4.zero, parameters = Vector4.zero };
                    material.SetBuffer("SRV_ReflectionProbes", Buffer(new[] { probe }, 112));
                    material.SetBuffer("SRV_ProbeClusterHeaders", Buffer(new[] { Vector2.zero }, 8));
                    material.SetBuffer("SRV_ProbeClusterIndices", Buffer(new[] { 0u }, 4));
                    Shader.EnableKeyword("INFINITY_REFLECTION_PROBES");
                    material.SetTexture("SRV_ReflectionProbeAtlas", environment);
                }
                using var reflection = new UniformBinding<global::InfinityTech.Rendering.Reflection.ReflectionUniform>(default);
                using var lightLoop = new UniformBinding<LightLoopUniform>(new LightLoopUniform
                {
                    tileParameters = new Vector4(size, size, tileCount, words),
                    zBinParameters = new Vector4(1, 0, 0, 0),
                    directionalCount = directionalCount,
                    localCount = localCount
                });
                int parameterSize = Marshal.SizeOf<DeferredShadingParameters>();
                using var parameters = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, parameterSize);
                parameters.SetData(new[] { new DeferredShadingParameters { resolution = new Vector4(size, size, 1f / size, 1f / size), hasRenderingLayers = 1 } });
                material.SetConstantBuffer(DeferredShadingPassUtilityData.CBV_DeferredShadingParametersID, parameters, 0, parameterSize);
                using var view = new UniformBinding<ViewUniform>(new ViewUniform
                {
                    matrix_InvFlipYJitterProj = Matrix4x4.identity,
                    matrix_InvViewFlipYJitterProj = Matrix4x4.identity,
                    matrix_ViewToWorld = Matrix4x4.Translate(new Vector3(0, 0, -2))
                });
                using var shadow = new UniformBinding<ShadowUniform>(new ShadowUniform { shadowDistance = 1000 });
                material.SetTexture("SRV_GBufferTextureA", albedo); material.SetTexture("SRV_GBufferTextureB", normal);
                if (ao)
                {
                    material.SetTexture("SRV_OcclusionTexture", Texture(new Color(fixture.occlusion.Value, 0, 0, 0)));
                }
                if (ssgi)
                {
                    material.SetTexture("SRV_SSGITexture", Texture(fixture.ssgi.Value));
                }
                if (sideband)
                {
                    material.SetTexture("SRV_Sideband", sidebandTarget);
                }
                if (contact)
                {
                    material.SetTexture("SRV_ContactShadowSun", white);
                    var mask = new RenderTexture(new RenderTextureDescriptor(size, size) { graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt, depthBufferBits = 0, enableRandomWrite = true });
                    owned.Add(mask); Assert.IsTrue(mask.Create());
                    RenderTexture.active = mask; GL.Clear(false, true, Color.clear); RenderTexture.active = previous;
                    material.SetTexture("SRV_ContactShadowMask", mask);
                }
                material.SetTexture("SRV_CascadeShadowMap", black); material.SetTexture("SRV_LocalShadowMap", black);
                material.SetTexture("_AtmosphereGGXPrefilter", environment);
                var weights = weightCases ? new[] { Vector2.zero, Vector2.one, new Vector2(0, 1), new Vector2(1, 0), new Vector2(.5f, 1), new Vector2(1, .5f) } : new[] { Vector2.one };
                var results = new Color[weights.Length][];
                for (int w = 0; w < weights.Length; ++w)
                {
                    records[directionalCount].weights = new Vector4(weights[w].x, weights[w].y, 1, 1);
                    lightBuffer.SetData(records);
                    // Scene Color starts as the geometry indirect term. The sideband is only read.
                    using (var cmd = new CommandBuffer())
                    {
                        cmd.SetRenderTarget(output, depthStencil);
                        cmd.ClearRenderTarget(RTClearFlags.All, fixture.sceneColor, Fixture.SurfaceDepth, fixture.stencil);
                        if (sideband)
                        {
                            cmd.SetRenderTarget(sidebandTarget);
                            cmd.ClearRenderTarget(RTClearFlags.Color, new Color(fixture.sidebandRatio.Value, 1, 1, Fixture.SidebandAlpha), 1.0f, 0);
                        }
                        cmd.SetRenderTarget(output, depthStencil);
                        cmd.SetGlobalTexture(PassShaderIDs.SRV_DepthTexture, depthStencil, RenderTextureSubElement.Depth);
                        // Global keywords, set in this buffer so a Game view render cannot change them before the draw.
                        cmd.SetKeyword(new GlobalKeyword("INFINITY_SIDEBAND"), sideband);
                        cmd.SetKeyword(new GlobalKeyword("DEFERRED_AO"), ao);
                        cmd.SetKeyword(new GlobalKeyword("DEFERRED_SSGI"), ssgi);
                        cmd.SetKeyword(new GlobalKeyword("DEFERRED_CONTACT_SHADOW"), contact);
                        cmd.SetKeyword(new GlobalKeyword("INFINITY_LIGHT_COOKIES"), true);
                        cmd.SetKeyword(new GlobalKeyword("INFINITY_REFLECTION_PROBES"), true);
                        cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                        Graphics.ExecuteCommandBuffer(cmd);
                    }
                    var readback = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBAFloat);
                    readbackPending = true;
                    yield return GpuReadbackTestUtility.Await(readback);
                    readbackPending = !readback.done;
                    Assert.IsFalse(readbackPending, "Scene Color readback timed out; its targets stay alive until it completes.");
                    Assert.IsFalse(readback.hasError);
                    results[w] = readback.GetData<Color>().ToArray();
                }
                Color[] sidebandValues = null;
                if (sideband)
                {
                    var sidebandReadback = AsyncGPUReadback.Request(sidebandTarget, 0, TextureFormat.RGBAFloat);
                    readbackPending = true;
                    yield return GpuReadbackTestUtility.Await(sidebandReadback);
                    readbackPending = !sidebandReadback.done;
                    Assert.IsFalse(readbackPending, "Sideband readback timed out; its targets stay alive until it completes.");
                    Assert.IsFalse(sidebandReadback.hasError);
                    sidebandValues = sidebandReadback.GetData<Color>().ToArray();
                }
                complete(new Result { color = results, sideband = sidebandValues });

            }
            finally
            {
                for (int i = 0; i < cameras.Length; ++i)
                {
                    if (cameras[i] != null)
                    {
                        cameras[i].enabled = cameraEnabled[i];
                    }
                }
                Shader.DisableKeyword("INFINITY_SIDEBAND");
                Shader.DisableKeyword("DEFERRED_AO");
                Shader.DisableKeyword("DEFERRED_SSGI");
                Shader.DisableKeyword("DEFERRED_CONTACT_SHADOW");
                Shader.DisableKeyword("INFINITY_LIGHT_COOKIES");
                Shader.DisableKeyword("INFINITY_REFLECTION_PROBES");
                RenderTexture.active = previous;
                if (!readbackPending)
                {
                    foreach (var buffer in buffers)
                    {
                        buffer.Dispose();
                    }
                    foreach (var item in owned)
                    {
                        UnityEngine.Object.DestroyImmediate(item);
                    }
                }
            }
        }
    }
}
