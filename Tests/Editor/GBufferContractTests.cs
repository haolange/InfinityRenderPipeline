using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class GBufferContractTests
    {
        const int k_Width = 32;
        const int k_Height = 32;
        const float k_AlbedoAbsTol = 2.0f / 255.0f;
        const float k_NormalAngleTolDeg = 1.0f;
        const string k_RasterShaderName = "Hidden/InfinityPipeline/GBufferContractRaster";
        const string k_ComputePath = "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_GBufferContract.compute";
        const string k_BestFitPath = "Packages/com.infinity.render-pipeline/Runtime/Resources/Textures/System_LUT/LUT_BestFit.png";

        [Test]
        public void GBufferContract_RasterComputeRoundtrip_FlatGray18()
        {
            RunAlbedoFixture(fixtureId: 0, "flat gray 0.18");
        }

        [Test]
        public void GBufferContract_RasterComputeRoundtrip_PureRed()
        {
            RunAlbedoFixture(fixtureId: 1, "pure red");
        }

        [Test]
        public void GBufferContract_RasterComputeRoundtrip_CoCgExtremes()
        {
            RunAlbedoFixture(fixtureId: 2, "+Co / -Cg extremes");
        }

        [Test]
        public void GBufferContract_RasterComputeRoundtrip_CheckerboardBoundaryTexels()
        {
            RunAlbedoFixture(fixtureId: 3, "checkerboard boundary texels", requireBothParities: true);
        }

        [Test]
        public void GBufferContract_BestFitNormal_AngularError()
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
                    run.Execute();
                    maxAngle = Mathf.Max(maxAngle, run.MaxNormalAngleDeg(expected));
                }
            }

            Assert.Less(maxAngle, k_NormalAngleTolDeg, $"BestFit decode angular error {maxAngle:F3}° exceeds {k_NormalAngleTolDeg}°.");
        }

        [MenuItem("Infinity/Validation/Run GBuffer Contract Tests", false, 65)]
        public static void RunFromMenu()
        {
            var tests = new GBufferContractTests();
            var log = new StringBuilder();
            int passed = 0;
            int ignored = 0;
            int failed = 0;
            void Run(string name, Action action)
            {
                try
                {
                    action();
                    passed++;
                    log.AppendLine("PASS " + name);
                }
                catch (IgnoreException e)
                {
                    ignored++;
                    log.AppendLine("IGNORE " + name + ": " + e.Message);
                }
                catch (Exception e)
                {
                    failed++;
                    log.AppendLine("FAIL " + name + ": " + e.Message);
                }
            }

            Run(nameof(GBufferContract_RasterComputeRoundtrip_FlatGray18), tests.GBufferContract_RasterComputeRoundtrip_FlatGray18);
            Run(nameof(GBufferContract_RasterComputeRoundtrip_PureRed), tests.GBufferContract_RasterComputeRoundtrip_PureRed);
            Run(nameof(GBufferContract_RasterComputeRoundtrip_CoCgExtremes), tests.GBufferContract_RasterComputeRoundtrip_CoCgExtremes);
            Run(nameof(GBufferContract_RasterComputeRoundtrip_CheckerboardBoundaryTexels), tests.GBufferContract_RasterComputeRoundtrip_CheckerboardBoundaryTexels);
            Run(nameof(GBufferContract_BestFitNormal_AngularError), tests.GBufferContract_BestFitNormal_AngularError);

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string path = Path.Combine(projectRoot, "Logs", "gbuffer-contract-tests.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"passed={passed} ignored={ignored} failed={failed}\n{log}");
            Debug.Log($"GBuffer contract tests: passed={passed} ignored={ignored} failed={failed}\n{log}");
        }

        void RunAlbedoFixture(int fixtureId, string label, bool requireBothParities = false)
        {
            RequireGpu();
            Shader raster = LoadRasterShader();
            ComputeShader compute = LoadComputeShader();
            Texture2D lut = LoadBestFitLut();

            using (var run = new GBufferContractGpuRun(raster, compute, lut, fixtureId, Vector3.forward))
            {
                run.Execute();
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
            if (GraphicsSettings.currentRenderPipeline is InfinityRenderPipelineAsset asset && asset.bestFitNormalTexture != null)
            {
                return asset.bestFitNormalTexture;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(k_BestFitPath);
        }

        internal static Vector3 ExpectedAlbedo(int fixtureId, int x, int y, int width, int height)
        {
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
            readonly Texture m_PreviousLut;
            readonly RenderTexture m_GBufferA;
            readonly RenderTexture m_GBufferB;
            readonly RenderTexture m_GBufferC;
            readonly RenderTexture m_Depth;
            readonly RenderTexture m_DecodedAlbedo;
            readonly RenderTexture m_DecodedNormal;
            Color[] m_AlbedoPixels;
            Color[] m_NormalPixels;

            public GBufferContractGpuRun(Shader raster, ComputeShader compute, Texture2D lut, int fixtureId, Vector3 normal)
            {
                m_Compute = compute;
                m_Kernel = compute.FindKernel("DecodeGBufferContract");
                m_Material = new Material(raster)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                m_Material.SetInt("_FixtureId", fixtureId);
                m_Material.SetVector("_Resolution", new Vector4(k_Width, k_Height, 0.0f, 0.0f));
                m_Material.SetVector("_FixtureNormal", new Vector4(normal.x, normal.y, normal.z, 0.0f));

                m_PreviousLut = Shader.GetGlobalTexture("g_BestFitNormal_LUT");
                if (lut != null)
                {
                    Shader.SetGlobalTexture("g_BestFitNormal_LUT", lut);
                }

                m_GBufferA = CreateGBuffer("GBufferContractA");
                m_GBufferB = CreateGBuffer("GBufferContractB");
                m_GBufferC = CreateGBuffer("GBufferContractC");
                m_Depth = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.Depth)
                {
                    name = "GBufferContractDepth",
                    hideFlags = HideFlags.HideAndDontSave
                };
                m_Depth.Create();
                m_DecodedAlbedo = CreateDecoded("GBufferContractAlbedo");
                m_DecodedNormal = CreateDecoded("GBufferContractNormal");
            }

            public void Execute()
            {
                var cmd = new CommandBuffer { name = string.Empty };
                cmd.SetRenderTarget(new RenderTargetIdentifier[] { m_GBufferA, m_GBufferB, m_GBufferC }, m_Depth);
                cmd.ClearRenderTarget(true, true, Color.clear);
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 3, 1);

                cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureA", m_GBufferA);
                cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureB", m_GBufferB);
                cmd.SetComputeTextureParam(m_Compute, m_Kernel, "SRV_GBufferTextureC", m_GBufferC);
                cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedAlbedo", m_DecodedAlbedo);
                cmd.SetComputeTextureParam(m_Compute, m_Kernel, "UAV_DecodedNormal", m_DecodedNormal);
                cmd.SetComputeVectorParam(m_Compute, "CBV_Resolution", new Vector4(k_Width, k_Height, 0.0f, 0.0f));
                cmd.DispatchCompute(m_Compute, m_Kernel, (k_Width + 7) / 8, (k_Height + 7) / 8, 1);

                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();

                AsyncGPUReadbackRequest albedoRequest = AsyncGPUReadback.Request(m_DecodedAlbedo);
                AsyncGPUReadbackRequest normalRequest = AsyncGPUReadback.Request(m_DecodedNormal);
                AsyncGPUReadback.WaitAllRequests();
                Assert.IsFalse(albedoRequest.hasError, "Albedo GPU readback failed.");
                Assert.IsFalse(normalRequest.hasError, "Normal GPU readback failed.");
                m_AlbedoPixels = albedoRequest.GetData<Color>().ToArray();
                m_NormalPixels = normalRequest.GetData<Color>().ToArray();
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

                        Vector3 expected = ExpectedAlbedo(fixtureId, x, y, k_Width, k_Height);
                        Color pixel = m_AlbedoPixels[y * k_Width + x];
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
                Shader.SetGlobalTexture("g_BestFitNormal_LUT", m_PreviousLut);
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
                rt.Create();
                return rt;
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
                rt.Create();
                return rt;
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
