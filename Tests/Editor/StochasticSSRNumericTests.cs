using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Runs the production stochastic SSR kernels on one unobstructed plane facing a perspective camera. Reflected rays
    // leave toward the camera and must miss; the resolve then has zero confidence, the temporal filter starts a new
    // history, and every output is finite. Classification counts must cover every tile exactly once.
    public class StochasticSSRNumericTests
    {
        const string k_Package = "Packages/com.infinity.render-pipeline/";
        const string k_ShaderPath = k_Package + "Shaders/RenderingFeature/ScreenSpaceReflection/Compute_ScreenSpaceReflection.compute";

        static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders and async readback are required.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device.");
        }

        [TestCase(32, 32, false, 0.25f)]
        [TestCase(17, 19, true, 0.25f)]
        [TestCase(17, 19, false, 0.4f)]
        [Category("Unverified-0.5")]
        public void UnobstructedPlane_ClassifiesTracesMissesAndResolvesToZero(int width, int height, bool halfResolution, float roughness)
        {
            RequireGpu();
            using (var run = new Run(width, height, halfResolution, roughness))
            {
                uint[] args = run.Classify();
                int tiles = run.TileCount.x * run.TileCount.y;
                Assert.AreEqual(tiles, (int)(args[0] + args[3] + args[6]), "Every tile is classified exactly once.");
                // Linear roughness is 0.3: a 0.25 plane traces Hi-Z, a 0.4 plane traces linear. Max roughness is 0.5.
                Assert.AreEqual(roughness < 0.3f ? tiles : 0, (int)args[0], "Hi-Z tiles");
                Assert.AreEqual(roughness < 0.3f ? 0 : tiles, (int)args[3], "Linear tiles");
                Assert.AreEqual(0, (int)args[6], "Skip tiles");
                for (int i = 0; i < 3; ++i)
                {
                    Assert.AreEqual(1, (int)args[i * 3 + 1]);
                    Assert.AreEqual(1, (int)args[i * 3 + 2]);
                }

                float[] rays = run.Trace();
                for (int i = 0; i < rays.Length; i += 4)
                {
                    Assert.IsFalse(float.IsNaN(rays[i + 3]) || float.IsInfinity(rays[i + 3]), "Ray record must be finite.");
                    Assert.LessOrEqual(rays[i + 3], 0.0f, "An unobstructed plane must not reflect itself.");
                }

                float[] resolved = run.Resolve();
                foreach (float value in resolved)
                    Assert.AreEqual(0.0f, value, 1e-6f, "Misses carry no radiance and no confidence.");

                float[] meta = run.Temporal();
                for (int i = 0; i < meta.Length; i += 4)
                    Assert.AreEqual(1.0f, meta[i + 3], 1e-6f, "A rejected history restarts at age 1 after this frame.");

                float[] spatial = run.Spatial(Color.clear);
                foreach (float value in spatial)
                    Assert.AreEqual(0.0f, value, 1e-6f);
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void RoughPlane_SkipsEveryTile()
        {
            RequireGpu();
            using (var run = new Run(24, 16, false, 0.9f))
            {
                uint[] args = run.Classify();
                Assert.AreEqual(run.TileCount.x * run.TileCount.y, (int)args[6]);
                Assert.AreEqual(0, (int)(args[0] + args[3]));
                float[] rays = run.Trace();
                foreach (float value in rays)
                    Assert.AreEqual(0.0f, value, "Skipped tiles clear their ray records.");
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void Spatial_PreservesConstantSignal()
        {
            RequireGpu();
            using (var run = new Run(19, 17, false, 0.4f))
            {
                float[] spatial = run.Spatial(new Color(0.18f, 0.18f, 0.18f, 0.7f));
                for (int i = 0; i < spatial.Length; i += 4)
                {
                    Assert.AreEqual(0.18f, spatial[i], 1e-3f, "Normalized radiance");
                    Assert.AreEqual(0.7f, spatial[i + 3], 1e-3f, "Normalized confidence");
                }
            }
        }

        sealed class Run : IDisposable
        {
            readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
            readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
            readonly ComputeShader m_Shader;
            readonly int m_Width, m_Height;
            readonly Vector2Int m_Trace;
            readonly GraphicsBuffer m_Args, m_TileList;
            readonly RenderTexture m_TileClass, m_RayHit, m_Resolved, m_HitDistance;
            readonly Texture2D m_Depth, m_HiZ, m_GBufferA, m_GBufferB, m_GBufferC, m_Lighting, m_Motion, m_HistoryRadiance, m_HistoryMeta;

            public Vector2Int TileCount { get; }

            public Run(int width, int height, bool halfResolution, float roughness)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_ShaderPath);
                Assert.IsNotNull(asset, "Missing production compute shader " + k_ShaderPath);
                m_Shader = Track(UnityEngine.Object.Instantiate(asset));
                m_Width = width;
                m_Height = height;
                Unity.Mathematics.int2 trace = SSRPassUtilityData.TraceSize(new Unity.Mathematics.int2(width, height), halfResolution);
                Unity.Mathematics.int2 tiles = SSRPassUtilityData.TileCount(trace);
                m_Trace = new Vector2Int(trace.x, trace.y);
                TileCount = new Vector2Int(tiles.x, tiles.y);

                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, (float)width / height, 0.1f, 100), false);
                Vector4 planeClip = projection * new Vector4(0, 0, -5, 1);
                float planeDepth = planeClip.z / planeClip.w;

                m_Depth = Solid(width, height, new Color(planeDepth, 0, 0, 0), false);
                m_HiZ = Solid(width, height, new Color(planeDepth, 0, 0, 0), true);
                m_GBufferA = Solid(width, height, new Color(0, 0, roughness, 0), false);
                m_GBufferB = Solid(width, height, new Color(0.5f, 0.5f, 1.0f, 0.5f), false);
                // Default lit, flags = valid surface (4) in the high nibble.
                m_GBufferC = Solid(width, height, new Color((4 << 4) / 255.0f, 0, 0, 0), false);
                m_Lighting = Solid(width, height, new Color(0.8f, 0.4f, 0.2f, 1), true);
                m_Motion = Solid(width, height, Color.clear, false);
                m_HistoryRadiance = Solid(width, height, new Color(5, 5, 5, 1), false);
                m_HistoryMeta = Solid(width, height, new Color(0, 0, planeDepth, 8), false);

                m_Args = Buffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 9);
                m_TileList = Buffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, 3 * TileCount.x * TileCount.y));
                m_TileClass = Target(TileCount.x, TileCount.y, GraphicsFormat.R32_UInt);
                m_RayHit = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                m_Resolved = Target(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                m_HitDistance = Target(width, height, GraphicsFormat.R32_SFloat);

                ComputeShader s = m_Shader;
                s.SetVector("SSR_Resolution", new Vector4(width, height, 1.0f / width, 1.0f / height));
                s.SetVector("SSR_TraceResolution", new Vector4(m_Trace.x, m_Trace.y, 1.0f / m_Trace.x, 1.0f / m_Trace.y));
                s.SetInt("SSR_TraceScale", halfResolution ? 2 : 1);
                s.SetInt("SSR_FrameIndex", 3);
                s.SetInt("SSR_MaxTiles", TileCount.x * TileCount.y);
                s.SetFloat("SSR_MaxDistance", 50);
                s.SetFloat("SSR_Thickness", 0.1f);
                s.SetFloat("SSR_MaxRoughness", 0.5f);
                s.SetFloat("SSR_EdgeFade", 0.05f);
                s.SetFloat("SSR_MirrorRoughness", 0.05f);
                s.SetFloat("SSR_LinearRoughness", 0.3f);
                s.SetFloat("SSR_BRDFBias", 0.7f);
                s.SetInt("SSR_HiZSteps", 16);
                s.SetInt("SSR_LinearSteps", 16);
                s.SetInt("SSR_ResolveSamples", 4);
                s.SetFloat("SSR_ResolveRadius", 2);
                s.SetFloat("SSR_TemporalWeight", 0.93f);
                s.SetFloat("SSR_TemporalClampScale", 1.25f);
                s.SetFloat("SSR_HistoryValid", 0);
                s.SetInt("SSR_DebugMode", 0);
                s.SetVector("SSR_CameraPosition", Vector4.zero);
                s.SetVector("SSR_CameraForward", new Vector4(0, 0, -1, 0));
                s.SetVector("ScreenSpaceDepthParams", new Vector4(0.1f, 100, 0, SystemInfo.usesReversedZBuffer ? 1 : 0));
                s.SetMatrix("Matrix_Proj", projection);
                s.SetMatrix("Matrix_InvProj", projection.inverse);
                s.SetMatrix("Matrix_InvViewProj", projection.inverse);
                s.SetMatrix("Matrix_WorldToView", Matrix4x4.identity);
                s.SetMatrix("Matrix_HistoryViewProj", projection);
                s.SetMatrix("Matrix_MotionViewProj", projection);
                s.SetMatrix("Matrix_LastMotionViewProj", projection);
            }

            public uint[] Classify()
            {
                m_Args.SetData(new uint[] { 0, 1, 1, 0, 1, 1, 0, 1, 1 });
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.ClassifyKernelName);
                BindSurface(kernel);
                m_Shader.SetBuffer(kernel, "UAV_TileArgs", m_Args);
                m_Shader.SetBuffer(kernel, "UAV_TileList", m_TileList);
                m_Shader.SetTexture(kernel, "UAV_TileClass", m_TileClass);
                m_Shader.Dispatch(kernel, TileCount.x, TileCount.y, 1);
                var args = new uint[9];
                m_Args.GetData(args);
                return args;
            }

            public float[] Trace()
            {
                int maxTiles = TileCount.x * TileCount.y;
                using (var cmd = new CommandBuffer())
                {
                    int clear = m_Shader.FindKernel(SSRPassUtilityData.ClearSkippedKernelName);
                    cmd.SetComputeIntParam(m_Shader, "SSR_TileListOffset", SSRPassUtilityData.ClassSkip * maxTiles);
                    cmd.SetComputeBufferParam(m_Shader, clear, "SRV_TileList", m_TileList);
                    cmd.SetComputeTextureParam(m_Shader, clear, "UAV_RayHit", m_RayHit);
                    cmd.DispatchCompute(m_Shader, clear, m_Args, (uint)(SSRPassUtilityData.ClassSkip * ComputeUtility.IndirectDispatchArgsStride));
                    TraceClass(cmd, SSRPassUtilityData.TraceHiZKernelName, SSRPassUtilityData.ClassHiZ, maxTiles);
                    TraceClass(cmd, SSRPassUtilityData.TraceLinearKernelName, SSRPassUtilityData.ClassLinear, maxTiles);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                return Read(m_RayHit, 4);
            }

            void TraceClass(CommandBuffer cmd, string kernelName, int tileClass, int maxTiles)
            {
                int kernel = m_Shader.FindKernel(kernelName);
                BindSurface(kernel);
                cmd.SetComputeIntParam(m_Shader, "SSR_TileListOffset", tileClass * maxTiles);
                cmd.SetComputeBufferParam(m_Shader, kernel, "SRV_TileList", m_TileList);
                cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_HiZTexture", m_HiZ);
                cmd.SetComputeTextureParam(m_Shader, kernel, "_Ranking_Tile", LoadTexture("RankingTile256SPP"));
                cmd.SetComputeTextureParam(m_Shader, kernel, "_Scrambled_Tile", LoadTexture("ScramblingTile256SPP"));
                cmd.SetComputeTextureParam(m_Shader, kernel, "_ScrambledOwen_Noise", LoadTexture("ScrambledNoise_Owen"));
                cmd.SetComputeTextureParam(m_Shader, kernel, "UAV_RayHit", m_RayHit);
                cmd.DispatchCompute(m_Shader, kernel, m_Args, (uint)(tileClass * ComputeUtility.IndirectDispatchArgsStride));
            }

            public float[] Resolve()
            {
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.ResolveKernelName);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_RayHit", m_RayHit);
                m_Shader.SetTexture(kernel, "SRV_LightingPyramid", m_Lighting);
                m_Shader.SetTexture(kernel, "UAV_Resolved", m_Resolved);
                m_Shader.SetTexture(kernel, "UAV_HitDistance", m_HitDistance);
                DispatchFull(kernel);
                float[] hitDistance = Read(m_HitDistance, 1);
                foreach (float value in hitDistance) Assert.AreEqual(0.0f, value, "No hit, no hit distance.");
                return Read(m_Resolved, 4);
            }

            public float[] Temporal()
            {
                RenderTexture temporal = Target(m_Width, m_Height, GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture meta = Target(m_Width, m_Height, GraphicsFormat.R32G32B32A32_SFloat);
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.TemporalKernelName);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Resolved", m_Resolved);
                m_Shader.SetTexture(kernel, "SRV_HitDistance", m_HitDistance);
                m_Shader.SetTexture(kernel, "SRV_MotionTexture", m_Motion);
                m_Shader.SetTexture(kernel, "SRV_HistoryRadiance", m_HistoryRadiance);
                m_Shader.SetTexture(kernel, "SRV_HistoryMeta", m_HistoryMeta);
                m_Shader.SetTexture(kernel, "UAV_Temporal", temporal);
                m_Shader.SetTexture(kernel, "UAV_Meta", meta);
                DispatchFull(kernel);
                foreach (float value in Read(temporal, 4))
                    Assert.AreEqual(0.0f, value, 1e-6f, "History is invalid this frame, so the current sample is taken as is.");
                return Read(meta, 4);
            }

            public float[] Spatial(Color input)
            {
                RenderTexture output = Target(m_Width, m_Height, GraphicsFormat.R32G32B32A32_SFloat);
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.SpatialKernelName);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Temporal", Solid(m_Width, m_Height, input, false));
                m_Shader.SetTexture(kernel, "UAV_Output", output);
                DispatchFull(kernel);
                return Read(output, 4);
            }

            void BindSurface(int kernel)
            {
                m_Shader.SetTexture(kernel, "SRV_DepthTexture", m_Depth);
                m_Shader.SetTexture(kernel, "SRV_GBufferA", m_GBufferA);
                m_Shader.SetTexture(kernel, "SRV_GBufferB", m_GBufferB);
                m_Shader.SetTexture(kernel, "SRV_GBufferC", m_GBufferC);
            }

            void DispatchFull(int kernel)
            {
                m_Shader.Dispatch(kernel, (m_Width + 7) / 8, (m_Height + 7) / 8, 1);
            }

            static Texture2D LoadTexture(string name)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_Package + "Runtime/Resources/Textures/MonteCarlo/" + name + ".png");
                Assert.IsNotNull(texture, "Missing blue-noise texture " + name);
                return texture;
            }

            static float[] Read(RenderTexture target, int channels)
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, target.name + " readback error");
                float[] values = request.GetData<float>().ToArray();
                Assert.AreEqual(target.width * target.height * channels, values.Length, target.name + " readback size");
                foreach (float value in values)
                    Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), target.name + " must be finite.");
                return values;
            }

            T Track<T>(T obj) where T : UnityEngine.Object
            {
                obj.hideFlags = HideFlags.HideAndDontSave;
                m_Objects.Add(obj);
                return obj;
            }

            GraphicsBuffer Buffer(GraphicsBuffer.Target target, int count)
            {
                var buffer = new GraphicsBuffer(target, count, sizeof(uint));
                m_Buffers.Add(buffer);
                return buffer;
            }

            RenderTexture Target(int width, int height, GraphicsFormat format)
            {
                var target = new RenderTexture(width, height, 0, format) { enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SSRTest-" + format };
                Assert.IsTrue(target.Create(), "Cannot create " + format);
                return Track(target);
            }

            Texture2D Solid(int width, int height, Color value, bool mips)
            {
                var texture = Track(new Texture2D(width, height, TextureFormat.RGBAFloat, mips, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
                for (int mip = 0; mip < texture.mipmapCount; ++mip)
                {
                    int count = Mathf.Max(1, width >> mip) * Mathf.Max(1, height >> mip);
                    var colors = new Color[count];
                    for (int i = 0; i < count; ++i) colors[i] = value;
                    texture.SetPixels(colors, mip);
                }
                texture.Apply(false, false);
                return texture;
            }

            public void Dispose()
            {
                foreach (GraphicsBuffer buffer in m_Buffers) buffer.Release();
                foreach (UnityEngine.Object obj in m_Objects)
                {
                    if (obj is RenderTexture target) target.Release();
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
        }
    }
}
