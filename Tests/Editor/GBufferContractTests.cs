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
        const float k_NormalAngleTolDeg = 1.0f;
        const string k_RasterShaderName = "Hidden/InfinityPipeline/GBufferContractRaster";
        const string k_ComputePath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_GBufferContract.compute";
        const string k_BestFitPath = "Packages/com.infinity.render-pipeline/Runtime/Resources/Textures/System_LUT/LUT_BestFit.png";

        [UnityTest]
        public IEnumerator GBufferContract_RasterComputeRoundtrip_FlatGray18()
        {
            return RunAlbedoFixture(fixtureId: 0, "flat gray 0.18");
        }

        [UnityTest]
        public IEnumerator GBufferContract_RasterComputeRoundtrip_PureRed()
        {
            return RunAlbedoFixture(fixtureId: 1, "pure red");
        }

        [UnityTest]
        public IEnumerator GBufferContract_RasterComputeRoundtrip_CoCgExtremes()
        {
            return RunAlbedoFixture(fixtureId: 2, "+Co / -Cg extremes");
        }

        [UnityTest]
        public IEnumerator GBufferContract_RasterComputeRoundtrip_CheckerboardBoundaryTexels()
        {
            return RunAlbedoFixture(fixtureId: 3, "checkerboard boundary texels", requireBothParities: true);
        }

        internal static int RetainedGpuFixtureCount => GBufferContractGpuRun.RetainedCount;

        static readonly int[] s_SilhouetteFixtures = { 4, 5, 6 };

        [UnityTest]
        public IEnumerator GBufferContract_SilhouetteDoesNotImportClearChroma([ValueSource(nameof(s_SilhouetteFixtures))] int fixture)
        {
            return RunAlbedoFixture(fixture, "dark diagonal / saturated thin pole against clear pixels", requireBothParities: true);
        }

        [UnityTest]
        public IEnumerator GBufferContract_BestFitNormal_AngularError()
        {
            RequireGpu();
            Shader raster = LoadRasterShader();
            ComputeShader compute = LoadComputeShader();
            Texture2D lut = LoadBestFitLut();
            if (lut == null)
            {
                Assert.Ignore("BestFit LUT missing.");
            }

            Vector3[] normals =
            {
                new Vector3(0.0f, 0.0f, 1.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(1.0f, 1.0f, 1.0f).normalized,
                new Vector3(0.2f, 0.3f, 0.9f).normalized,
                new Vector3(-0.5f, 0.2f, 0.8f).normalized
            };

            float maxAngle = 0.0f;
            foreach (Vector3 expected in normals)
            {
                using (var run = new GBufferContractGpuRun(raster, compute, lut, fixtureId: 0, expected))
                {
                    yield return run.Execute();
                    maxAngle = Mathf.Max(maxAngle, run.MaxNormalAngleDeg(expected));
                }
            }

            Assert.Less(maxAngle, k_NormalAngleTolDeg, $"BestFit decode angular error {maxAngle:F3}° exceeds {k_NormalAngleTolDeg}°.");
        }

        [MenuItem("Window/Infinity/Run GBuffer Contract Tests", false, 65)]
        public static void RunFromMenu()
        {
            string directory = Path.Combine(InfinityTech.Rendering.Tests.ValidationTestRunner.ArtifactRoot,
                "gbuffer-contract-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N"));
            InfinityTech.Rendering.Tests.ValidationTestRunner.RunValidation(directory, nameof(GBufferContractTests));
        }

        IEnumerator RunAlbedoFixture(int fixtureId, string label, bool requireBothParities = false)
        {
            RequireGpu();
            Shader raster = LoadRasterShader();
            ComputeShader compute = LoadComputeShader();
            Texture2D lut = LoadBestFitLut();

            using (var run = new GBufferContractGpuRun(raster, compute, lut, fixtureId, Vector3.forward))
            {
                yield return run.Execute();
                run.AssertAlbedo(fixtureId, label, requireBothParities);
            }
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

        static Texture2D LoadBestFitLut()
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings(out InfinityRenderPipelineRuntimeTextures textures) &&
                textures != null && textures.bestFitNormalTexture != null)
            {
                return textures.bestFitNormalTexture;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(k_BestFitPath);
        }

        internal static Vector3 ExpectedAlbedo(int fixtureId, int x, int y, int width, int height)
        {
            if (fixtureId == 6) return new Vector3(0.4f, 0.1f, 0.03f);
            if (fixtureId == 4) return new Vector3(0.03f, 0.02f, 0.01f);
            if (fixtureId == 5) return new Vector3(0.8f, 0.05f, 0.02f);
            if (fixtureId == 0)
            {
                return new Vector3(0.18f, 0.18f, 0.18f);
            }

            if (fixtureId == 1 || fixtureId == 3)
            {
                return new Vector3(1.0f, 0.0f, 0.0f);
            }

            return x < width / 2 ? new Vector3(1.0f, 0.0f, 0.0f) : new Vector3(1.0f, 0.0f, 1.0f);
        }

        sealed class GBufferContractGpuRun : IDisposable
        {
            readonly Material m_Material;
            readonly ComputeShader m_Compute;
            readonly int m_Kernel;
            readonly int m_FixtureId;
            readonly Texture m_PreviousLut;
            readonly RenderTexture m_GBufferA;
            readonly RenderTexture m_GBufferB;
            readonly RenderTexture m_GBufferC;
            readonly RenderTexture m_Depth;
            readonly RenderTexture m_DecodedAlbedo;
            readonly RenderTexture m_DecodedNormal;
            Color[] m_AlbedoPixels;
            Color[] m_NormalPixels;
            int m_PendingReadbacks;
            string m_ReadbackFailure;
            bool m_DisposeRequested, m_Released;
            // A failed/uncertain submission may never callback. Keep ownership explicit until real callbacks
            // drain, or the owning Editor exits; never clear pending counts to make teardown appear complete.
            static readonly List<GBufferContractGpuRun> s_Retained = new List<GBufferContractGpuRun>();
            internal static int RetainedCount => s_Retained.Count;

            public GBufferContractGpuRun(Shader raster, ComputeShader compute, Texture2D lut, int fixtureId, Vector3 normal)
            {
                m_PreviousLut = Shader.GetGlobalTexture("g_BestFitNormal_LUT");
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

                    if (lut != null)
                    {
                        Shader.SetGlobalTexture("g_BestFitNormal_LUT", lut);
                    }

                    m_GBufferA = CreateGBuffer("GBufferContractA");
                    m_GBufferB = CreateGBuffer("GBufferContractB");
                    m_GBufferC = CreateGBuffer("GBufferContractC");
                    m_Depth = CreateOwned(new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.Depth)
                    {
                        name = "GBufferContractDepth",
                        hideFlags = HideFlags.HideAndDontSave
                    });

                    m_DecodedAlbedo = CreateDecoded("GBufferContractAlbedo");
                    m_DecodedNormal = CreateDecoded("GBufferContractNormal");
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
                    cmd.SetRenderTarget(new RenderTargetIdentifier[] { m_GBufferA, m_GBufferB, m_GBufferC }, m_Depth);
                    cmd.ClearRenderTarget(true, true, Color.clear);
                    cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 3, 1);
                    if (m_FixtureId == 6)
                    {
                        // RGB zero is a legal packed diagonal normal, not proof of absent geometry.
                        cmd.SetRenderTarget(m_GBufferB); cmd.ClearRenderTarget(false, true, Color.clear);
                    }

                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureA", m_GBufferA);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureB", m_GBufferB);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureC", m_GBufferC);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedAlbedo", m_DecodedAlbedo);
                    cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedNormal", m_DecodedNormal);
                    cmd.SetComputeVectorParam(m_Compute, "CBV_Resolution", new Vector4(k_Width, k_Height, 0.0f, 0.0f));
                    cmd.DispatchCompute(m_Compute, m_Kernel, (k_Width + 7) / 8, (k_Height + 7) / 8, 1);

                    // Queue only this fixture's readbacks behind its dispatch. Never stall the Editor on a
                    // process-wide WaitAllRequests: Metal submission and other requests may need its next update.
                    cmd.RequestAsyncReadback(m_DecodedAlbedo, request => CompleteReadback(request, true));
                    cmd.RequestAsyncReadback(m_DecodedNormal, request => CompleteReadback(request, false));
                    m_PendingReadbacks = 2;
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
            }

            void CompleteReadback(AsyncGPUReadbackRequest request, bool albedo)
            {
                try
                {
                    if (request.hasError) throw new InvalidOperationException((albedo ? "Albedo" : "Normal") + " GPU readback failed.");
                    // Request data expires after completion; copy it here, before yielding another Editor frame.
                    Color[] pixels = request.GetData<Color>().ToArray();
                    if (pixels.Length != k_Width * k_Height) throw new InvalidOperationException("Unexpected GBuffer readback size.");
                    if (albedo) m_AlbedoPixels = pixels;
                    else m_NormalPixels = pixels;
                }
                catch (Exception error) { m_ReadbackFailure ??= error.ToString(); }
                finally
                {
                    --m_PendingReadbacks;
                    TryRelease();
                }
            }

            public void AssertAlbedo(int fixtureId, string label, bool requireBothParities)
            {
                float maxAbs = 0.0f;
                int samples = 0;
                int evenParity = 0;
                int oddParity = 0;

                for (int y = 1; y < k_Height - 1; ++y)
                {
                    for (int x = 1; x < k_Width - 1; ++x)
                    {
                        if (fixtureId == 2 && Mathf.Abs(x - k_Width / 2) <= 1)
                        {
                            continue;
                        }

                        if (fixtureId == 4 && x > y) continue;
                        if (fixtureId == 5 && x != k_Width / 2) continue;
                        Vector3 expected = ExpectedAlbedo(fixtureId, x, y, k_Width, k_Height);
                        Color pixel = m_AlbedoPixels[y * k_Width + x];
                        int expectedLayer = (x & 3) == 0 ? 1 : ((x & 3) == 1 ? 2 : ((x & 3) == 2 ? 128 : 255));
                        Assert.AreEqual(expectedLayer, pixel.a, "Rendering layer must survive actual GBuffer UNorm encode/decode.");
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(pixel.r - expected.x));
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(pixel.g - expected.y));
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(pixel.b - expected.z));
                        samples++;
                        if (((x & 1) == (y & 1)))
                        {
                            evenParity++;
                        }
                        else
                        {
                            oddParity++;
                        }
                    }
                }

                Assert.Greater(samples, 0, $"No interior samples for {label}.");
                if (requireBothParities)
                {
                    Assert.Greater(evenParity, 0, "Missing even-parity checkerboard texels.");
                    Assert.Greater(oddParity, 0, "Missing odd-parity checkerboard texels.");
                }

                Assert.Less(maxAbs, k_AlbedoAbsTol, $"{label}: max per-channel abs error {maxAbs:F5} exceeds {k_AlbedoAbsTol:F5}.");
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
                if (m_PendingReadbacks != 0) s_Retained.Add(this);
                Shader.SetGlobalTexture("g_BestFitNormal_LUT", m_PreviousLut);
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
                Release(m_GBufferC);
                Release(m_Depth);
                Release(m_DecodedAlbedo);
                Release(m_DecodedNormal);
            }

            static RenderTexture CreateGBuffer(string name)
            {
                var rt = new RenderTexture(k_Width, k_Height, 0, GraphicsFormat.R8G8B8A8_UNorm)
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
