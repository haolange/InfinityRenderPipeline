using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public partial class GBufferContractTests
    {
        const int k_Width = 32;
        const int k_Height = 32;
        const float k_AlbedoAbsTol = 2.0f / 255.0f;
        const float k_NormalAngleTolDeg = 1.5f;
        const float k_UNormAbsTol = 0.51f / 255.0f;
        const string k_RasterShaderName = "Hidden/InfinityPipeline/GBufferContractRaster";
        const string k_ComputePath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_GBufferContract.compute";

        internal static int RetainedGpuFixtureCount => GBufferContractGpuRun.RetainedCount;

        // Flat gray, pure red, a red / magenta hue boundary, then silhouettes against cleared pixels (dark diagonal,
        // saturated thin pole, saturated field with a cleared normal target).
        static readonly int[] s_AlbedoFixtures = { 0, 1, 2, 4, 5, 6 };

        [UnityTest]
        public IEnumerator GBufferContract_AlbedoRoundtrip([ValueSource(nameof(s_AlbedoFixtures))] int fixture)
        {
            RequireGpu();
            using (var run = new GBufferContractGpuRun(LoadRasterShader(), LoadComputeShader(), fixture, new Vector3(0.2f, 0.3f, 0.9f).normalized))
            {
                yield return run.Execute();
                run.AssertAlbedo(fixture, "fixture " + fixture);
            }
        }

        [UnityTest]
        public IEnumerator GBufferContract_Oct8x2NormalAngularError()
        {
            RequireGpu();
            Shader raster = LoadRasterShader();
            ComputeShader compute = LoadComputeShader();
            Vector3[] normals =
            {
                new Vector3(0.0f, 0.0f, 1.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(0.0f, 0.0f, -1.0f),
                new Vector3(1.0f, 1.0f, 1.0f).normalized,
                new Vector3(0.2f, 0.3f, 0.9f).normalized,
                new Vector3(-0.5f, 0.2f, -0.8f).normalized
            };

            float maxAngle = 0.0f;
            foreach (Vector3 expected in normals)
            {
                using (var run = new GBufferContractGpuRun(raster, compute, fixtureId: 0, expected))
                {
                    yield return run.Execute();
                    maxAngle = Mathf.Max(maxAngle, run.MaxNormalAngleDeg(expected));
                    run.AssertMaterial();
                }
            }

            Assert.Less(maxAngle, k_NormalAngleTolDeg, $"Oct8x2 decode angular error {maxAngle:F4}° exceeds {k_NormalAngleTolDeg}°.");
        }

        [UnityTest]
        public IEnumerator GBufferContract_SidebandRoundtrip()
        {
            RequireGpu();
            using (var run = new GBufferContractGpuRun(LoadRasterShader(), LoadComputeShader(), fixtureId: 0, Vector3.forward))
            {
                yield return run.Execute();
                run.AssertSideband();
            }
        }

        [UnityTest]
        public IEnumerator GBufferContract_UncoveredSidebandHasNoChannelsOrProfile()
        {
            RequireGpu();
            // The diagonal fixture discards x > y and writes a varying sideband everywhere else.
            using var run = new GBufferContractGpuRun(LoadRasterShader(), LoadComputeShader(), fixtureId: 4, Vector3.forward);
            yield return run.Execute();
            run.AssertSideband(hasDiscardedPixels: true);
        }

        [MenuItem("Window/Infinity/Run GBuffer Contract Tests", false, 65)]
        public static void RunFromMenu()
        {
            string directory = Path.Combine(InfinityTech.Rendering.Tests.ValidationTestRunner.ArtifactRoot,
                "gbuffer-contract-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
            InfinityTech.Rendering.Tests.ValidationTestRunner.RunValidation(directory, nameof(GBufferContractTests));
        }

        static void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("No GPU (graphicsDeviceType is Null).");
            }

            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders not supported.");
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("AsyncGPUReadback not supported.");
            }
        }

        static Shader LoadRasterShader()
        {
            Shader shader = Shader.Find(k_RasterShaderName);
            if (shader == null)
            {
                Assert.Ignore("GBuffer contract raster shader missing.");
            }

            return shader;
        }

        static ComputeShader LoadComputeShader()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_ComputePath);
            if (compute == null)
            {
                Assert.Ignore("GBuffer contract compute shader missing.");
            }

            return compute;
        }

        internal static Vector3 ExpectedAlbedo(int fixtureId, int x, int y, int width, int height)
        {
            if (fixtureId == 6)
            {
                return new Vector3(0.4f, 0.1f, 0.03f);
            }
            if (fixtureId == 4)
            {
                return new Vector3(0.03f, 0.02f, 0.01f);
            }
            if (fixtureId == 5)
            {
                return new Vector3(0.8f, 0.05f, 0.02f);
            }
            if (fixtureId == 0)
            {
                return new Vector3(0.18f, 0.18f, 0.18f);
            }

            if (fixtureId == 1)
            {
                return new Vector3(1.0f, 0.0f, 0.0f);
            }

            return x < width / 2 ? new Vector3(1.0f, 0.0f, 0.0f) : new Vector3(1.0f, 0.0f, 1.0f);
        }

        // Mirrors FixtureSideband of GBufferContractRaster.shader.
        internal static Vector4 ExpectedSideband(int x, int y)
        {
            return new Vector4((x + 0.5f) / 32.0f, (y + 0.5f) / 32.0f, 1.0f - (x + 0.5f) / 32.0f, (y & 15) + (x & 15) * 16);
        }

        sealed class GBufferContractGpuRun : IDisposable
        {
            readonly Material m_Material;
            readonly ComputeShader m_Compute;
            readonly int m_Kernel;
            readonly int m_FixtureId;
            readonly RenderTexture m_GBufferA;
            readonly RenderTexture m_GBufferB;
            readonly RenderTexture m_Sideband;
            readonly Color m_SidebandClearColor;
            readonly RenderTexture m_Depth;
            readonly RenderTexture m_DecodedAlbedo;
            readonly RenderTexture m_DecodedNormal;
            readonly RenderTexture m_DecodedSideband;
            Color[] m_AlbedoPixels;
            Color[] m_NormalPixels;
            Color[] m_SidebandPixels;
            int m_PendingReadbacks;
            string m_ReadbackFailure;
            bool m_DisposeRequested, m_Released;
            // A failed/uncertain submission may never callback. Keep ownership explicit until real callbacks
            // drain, or the owning Editor exits; never clear pending counts to make teardown appear complete.
            static readonly List<GBufferContractGpuRun> s_Retained = new List<GBufferContractGpuRun>();
            internal static int RetainedCount => s_Retained.Count;

            public GBufferContractGpuRun(Shader raster, ComputeShader compute, int fixtureId, Vector3 normal)
            {
                try
                {
                    m_Compute = compute;
                    m_FixtureId = fixtureId;
                    m_Kernel = compute.FindKernel("DecodeGBufferContract");
                    m_Material = new Material(raster)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    m_Material.SetInt("_FixtureId", fixtureId);
                    m_Material.SetVector("_Resolution", new Vector4(k_Width, k_Height, 0.0f, 0.0f));
                    m_Material.SetVector("_FixtureNormal", new Vector4(normal.x, normal.y, normal.z, 0.0f));

                    m_GBufferA = CreateGBuffer("GBufferContractA", EffectivePipelineConfiguration.GBufferAFormat);
                    m_GBufferB = CreateGBuffer("GBufferContractB", EffectivePipelineConfiguration.GBufferBFormat);
                    var sidebandDescriptor = GBufferPassUtilityData.CreateSidebandDescriptor(k_Width, k_Height);
                    m_Sideband = CreateGBuffer("GBufferContractSideband", sidebandDescriptor.colorFormat);
                    m_SidebandClearColor = sidebandDescriptor.clearColor;
                    m_Depth = CreateOwned(new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.Depth)
                    {
                        name = "GBufferContractDepth",
                        hideFlags = HideFlags.HideAndDontSave
                    });

                    m_DecodedAlbedo = CreateDecoded("GBufferContractAlbedo");
                    m_DecodedNormal = CreateDecoded("GBufferContractNormal");
                    m_DecodedSideband = CreateDecoded("GBufferContractDecodedSideband");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public IEnumerator Execute()
            {
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(new RenderTargetIdentifier[] { m_GBufferA, m_GBufferB, m_Sideband }, m_Depth);
                    cmd.ClearRenderTarget(true, true, Color.clear);
                    cmd.SetRenderTarget(m_Sideband);
                    cmd.ClearRenderTarget(false, true, m_SidebandClearColor);
                    cmd.SetRenderTarget(new RenderTargetIdentifier[] { m_GBufferA, m_GBufferB, m_Sideband }, m_Depth);
                    cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 3, 1);
                    if (m_FixtureId == 6)
                    {
                        // RGB zero is a legal packed diagonal normal, not proof of absent geometry.
                        cmd.SetRenderTarget(m_GBufferB); cmd.ClearRenderTarget(false, true, Color.clear);
                    }

                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureA", m_GBufferA);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureB", m_GBufferB);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_Sideband", m_Sideband);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedAlbedo", m_DecodedAlbedo);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedNormal", m_DecodedNormal);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedSideband", m_DecodedSideband);
                    cmd.SetComputeVectorParam(m_Compute, "CBV_Resolution", new Vector4(k_Width, k_Height, 0.0f, 0.0f));
                    cmd.DispatchCompute(m_Compute, m_Kernel, (k_Width + 7) / 8, (k_Height + 7) / 8, 1);

                    // Queue only this fixture's readbacks behind its dispatch. Never stall the Editor on a
                    // process-wide WaitAllRequests: Metal submission and other requests may need its next update.
                    cmd.RequestAsyncReadback(m_DecodedAlbedo, request => CompleteReadback(request, 0));
                    cmd.RequestAsyncReadback(m_DecodedNormal, request => CompleteReadback(request, 1));
                    cmd.RequestAsyncReadback(m_DecodedSideband, request => CompleteReadback(request, 2));
                    m_PendingReadbacks = 3;
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (m_PendingReadbacks > 0 && EditorApplication.timeSinceStartup < deadline)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.AreEqual(0, m_PendingReadbacks, "Owned GBuffer readback timed out; its resources remain retained until callbacks drain.");
                Assert.IsNull(m_ReadbackFailure, m_ReadbackFailure);
                Assert.IsNotNull(m_AlbedoPixels, "Missing albedo readback.");
                Assert.IsNotNull(m_NormalPixels, "Missing normal readback.");
                Assert.IsNotNull(m_SidebandPixels, "Missing sideband readback.");
            }

            // target: 0 albedo, 1 normal, 2 sideband.
            void CompleteReadback(AsyncGPUReadbackRequest request, int target)
            {
                try
                {
                    if (request.hasError)
                    {
                        throw new InvalidOperationException("GBuffer contract readback " + target + " failed.");
                    }
                    // Request data expires after completion; copy it here, before yielding another Editor frame.
                    Color[] pixels = request.GetData<Color>().ToArray();
                    if (pixels.Length != k_Width * k_Height)
                    {
                        throw new InvalidOperationException("Unexpected GBuffer readback size.");
                    }
                    if (target == 0)
                    {
                        m_AlbedoPixels = pixels;
                    }
                    else if (target == 1)
                    {
                        m_NormalPixels = pixels;
                    }
                    else
                    {
                        m_SidebandPixels = pixels;
                    }
                }
                catch (Exception error) { m_ReadbackFailure ??= error.ToString(); }
                finally
                {
                    --m_PendingReadbacks;
                    TryRelease();
                }
            }

            public void AssertAlbedo(int fixtureId, string label)
            {
                float maxAbs = 0.0f;
                int samples = 0;
                for (int y = 1; y < k_Height - 1; ++y)
                {
                    for (int x = 1; x < k_Width - 1; ++x)
                    {
                        if (fixtureId == 2 && Mathf.Abs(x - k_Width / 2) <= 1)
                        {
                            continue;
                        }
                        if (fixtureId == 4 && x > y)
                        {
                            continue;
                        }
                        if (fixtureId == 5 && x != k_Width / 2)
                        {
                            continue;
                        }
                        Vector3 expected = ExpectedAlbedo(fixtureId, x, y, k_Width, k_Height);
                        Color pixel = m_AlbedoPixels[y * k_Width + x];
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(pixel.r - expected.x));
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(pixel.g - expected.y));
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(pixel.b - expected.z));
                        samples++;
                    }
                }

                Assert.Greater(samples, 0, $"No interior samples for {label}.");
                Assert.Less(maxAbs, k_AlbedoAbsTol, $"{label}: max per-channel abs error {maxAbs:F5} exceeds {k_AlbedoAbsTol:F5}.");
            }

            // Roughness 0.5 in GBuffer A alpha and metallic 0.25 in GBuffer B blue survive one UNorm quantization.
            public void AssertMaterial()
            {
                Color albedo = m_AlbedoPixels[(k_Height / 2) * k_Width + k_Width / 2];
                Color normal = m_NormalPixels[(k_Height / 2) * k_Width + k_Width / 2];
                Assert.AreEqual(0.5f, albedo.a, k_UNormAbsTol, "Roughness must survive GBuffer A alpha.");
                Assert.AreEqual(0.25f, normal.a, k_UNormAbsTol, "Metallic must survive GBuffer B blue.");
            }

            // Every light channel mask and profile is exact; the diffuse share and both visibilities quantize once to UNorm.
            public void AssertSideband(bool hasDiscardedPixels = false)
            {
                for (int y = 0; y < k_Height; ++y)
                {
                    for (int x = 0; x < k_Width; ++x)
                    {
                        Color pixel = m_SidebandPixels[y * k_Width + x];
                        if (hasDiscardedPixels && x > y)
                        {
                            Assert.AreEqual(Color.clear, pixel, $"Uncovered sideband at {x},{y}: diffuse share, both visibilities and the packed channel/profile byte must all be zero.");
                            continue;
                        }
                        Vector4 expected = ExpectedSideband(x, y);
                        Assert.AreEqual(expected.x, pixel.r, k_UNormAbsTol, $"Diffuse share at {x},{y}.");
                        Assert.AreEqual(expected.y, pixel.g, k_UNormAbsTol, $"First mixed light visibility at {x},{y}.");
                        Assert.AreEqual(expected.z, pixel.b, k_UNormAbsTol, $"Second mixed light visibility at {x},{y}.");
                        Assert.AreEqual(expected.w, pixel.a, $"Profile and light channels at {x},{y}.");
                    }
                }
            }

            public float MaxNormalAngleDeg(Vector3 expected)
            {
                Vector3 nExpected = expected.normalized;
                float maxAngle = 0.0f;
                for (int y = 1; y < k_Height - 1; ++y)
                {
                    for (int x = 1; x < k_Width - 1; ++x)
                    {
                        Color pixel = m_NormalPixels[y * k_Width + x];
                        Vector3 decoded = new Vector3(pixel.r, pixel.g, pixel.b).normalized;
                        float dot = Mathf.Clamp(Vector3.Dot(decoded, nExpected), -1.0f, 1.0f);
                        maxAngle = Mathf.Max(maxAngle, Mathf.Acos(dot) * Mathf.Rad2Deg);
                    }
                }

                return maxAngle;
            }

            public void Dispose()
            {
                if (m_DisposeRequested) return;
                m_DisposeRequested = true;
                if (m_PendingReadbacks != 0)
                {
                    s_Retained.Add(this);
                }
                TryRelease();
            }

            void TryRelease()
            {
                if (!m_DisposeRequested || m_Released || m_PendingReadbacks != 0) return;
                m_Released = true;
                s_Retained.Remove(this);
                if (m_Material != null)
                {
                    UnityEngine.Object.DestroyImmediate(m_Material);
                }

                Release(m_GBufferA);
                Release(m_GBufferB);
                Release(m_Sideband);
                Release(m_Depth);
                Release(m_DecodedAlbedo);
                Release(m_DecodedNormal);
                Release(m_DecodedSideband);
            }

            static RenderTexture CreateGBuffer(string name, GraphicsFormat format)
            {
                var rt = new RenderTexture(k_Width, k_Height, 0, format)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                return CreateOwned(rt);
            }

            static RenderTexture CreateDecoded(string name)
            {
                var rt = new RenderTexture(k_Width, k_Height, 0, GraphicsFormat.R32G32B32A32_SFloat)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    enableRandomWrite = true,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                return CreateOwned(rt);
            }

            static RenderTexture CreateOwned(RenderTexture texture)
            {
                try
                {
                    if (!texture.Create()) throw new InvalidOperationException("Failed to create " + texture.name);
                    return texture;
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    throw;
                }
            }

            static void Release(RenderTexture rt)
            {
                if (rt == null)
                {
                    return;
                }

                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
