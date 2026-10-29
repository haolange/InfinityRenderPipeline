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
        public void UnobstructedPlane_ClassifiesTracesMissesAndResolvesToZero(int width, int height, bool halfResolution, float roughness)
        {
            RequireGpu();
            using (var run = new Run(width, height, halfResolution, roughness))
            {
                uint[] args = run.Classify();
                int tiles = run.TileCount.x * run.TileCount.y;
                Assert.AreEqual(tiles, (int)args[0], "Every reflective tile is one indirect group.");
                Assert.AreEqual(1, (int)args[1]);
                Assert.AreEqual(1, (int)args[2]);
                // Linear roughness is 0.3: a 0.25 plane traces Hi-Z, a 0.4 plane traces linear. The class lives on the tile texture.
                uint expectedClass = roughness < 0.3f ? (uint)SSRPassUtilityData.ClassHiZ : (uint)SSRPassUtilityData.ClassLinear;
                foreach (uint tileClass in run.TileClasses())
                    Assert.AreEqual(expectedClass, tileClass);

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
        public void Resolve_HitEdgeFadeIsNotDilutedByAMiss()
        {
            RequireGpu();
            using (var run = new Run(8, 8, false, 0.25f))
            {
                var hit = new ReflectionSample { radiance = new Vector4(1, 0, 0, 1), dirT = new Vector4(0, 0, 2, 1) };
                var empty = new ReflectionSample { radiance = new Vector4(0, 9, 0, 1), dirT = Vector4.zero };
                var otherHit = new ReflectionSample { radiance = new Vector4(0, 1, 0, 1), dirT = new Vector4(1, 0, 1, 1) };
                var miss = new ReflectionSample { radiance = Vector4.zero, dirT = new Vector4(0, 0, 1, -1) };

                Color kept = run.ResolveCenter(hit, empty);
                Assert.Greater(kept.r, 0.99f, "A facing-failed or invalid sample is empty and must not enter the estimator.");
                Assert.Less(kept.g, 0.01f);
                Assert.Greater(kept.a, 0.99f);

                Color withMiss = run.ResolveCenter(hit, miss);
                Assert.AreEqual(kept.a, withMiss.a, 0.02f, "A miss does not dilute the hit edge fade.");
                Assert.Greater(withMiss.r, 0.99f);

                Color split = run.ResolveCenter(hit, otherHit);
                Color sameDirection = run.ResolveCenter(hit, new ReflectionSample { radiance = otherHit.radiance, dirT = hit.dirT });
                Assert.AreEqual(split.r, sameDirection.r, 0.05f, "Hit colors are averaged. Direction accepts the sample and does not reweight it.");
                Assert.AreEqual(0.5f, split.r, 0.05f);
            }
        }

        [Test]
        public void Resolve_FarRootKeepsItsDirection()
        {
            RequireGpu();
            using (var run = new Run(8, 8, false, 0.25f))
            {
                var empty = new ReflectionSample();
                Color farRoot = run.ResolveCenter(new ReflectionSample { radiance = new Vector4(1, 0, 0, 1), dirT = new Vector4(0, 0, 3, 1) }, empty);
                Assert.Greater(farRoot.r, 0.99f, "Dir * HitT along the normal is the far root and must stay a hit.");
                Assert.Greater(farRoot.a, 0.99f);
                Color nearRoot = run.ResolveCenter(new ReflectionSample { radiance = new Vector4(1, 0, 0, 1), dirT = new Vector4(0, 0, -3, 1) }, empty);
                Assert.Less(nearRoot.a, 0.01f, "The opposite root points into the surface and must not enter the estimator.");
            }
        }

        [Test]
        public void Resolve_NeighborHitUsesThatPixelsOrigin()
        {
            RequireGpu();
            using (var run = new Run(8, 8, false, 0.25f))
            {
                const int center = 4;
                Vector2Int neighbor = NeighborOffset(new Vector2(center + 0.5f, center + 0.5f), 3, 2, 2f);
                Assert.AreNotEqual(Vector2Int.zero, neighbor, "The reuse offset must leave the center pixel.");
                int nx = Mathf.Clamp(center + neighbor.x, 0, 7);
                int ny = Mathf.Clamp(center + neighbor.y, 0, 7);
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false);
                float planeDepth = PlaneDepth(projection, new Vector3(0, 0, -5));
                float nearDepth = PlaneDepth(projection, new Vector3(0, 0, -2));
                run.SetDepth(nx, ny, nearDepth);
                Vector3 receiver = WorldAt(center, center, 8, planeDepth, projection.inverse);
                Vector3 origin = WorldAt(nx, ny, 8, nearDepth, projection.inverse);
                Vector3 dirT = receiver + new Vector3(0, 0, 2) - origin;
                var records = new ReflectionSample[8 * 8 * 4];
                int centerIndex = (center * 8 + center) * 4;
                records[centerIndex] = new ReflectionSample { radiance = new Vector4(0, 1, 0, 1), dirT = new Vector4(0, 0, 2, 1) };
                records[(ny * 8 + nx) * 4] = new ReflectionSample { radiance = new Vector4(1, 0, 0, 1), dirT = new Vector4(dirT.x, dirT.y, dirT.z, 1) };
                Color mixed = run.ResolveRecords(records, 2, 2);
                Assert.Greater(mixed.r, 0.15f, "The neighbor hit must be rebuilt from that pixel's origin.");
                Assert.Greater(mixed.g, 0.15f, "The center hit must remain in the estimate.");
            }
        }

        [Test]
        public void Temporal_ClampsConfidenceAsWellAsColor()
        {
            RequireGpu();
            using (var run = new Run(8, 8, false, 0.25f))
            {
                Color mean = new Color(0.8f, 0.4f, 0.2f, 0.35f);
                Color dev = new Color(0.05f, 0.05f, 0.05f, 0.05f);
                Color history = new Color(0.8f, 0.4f, 0.2f, 1f);
                Color current = new Color(0f, 0f, 0f, 0.05f);
                Color output = run.TemporalClamp(history, current, mean, dev);
                Assert.AreEqual(current.a, output.a, 1e-3f, "Output alpha is this frame's edge fade.");
                Assert.GreaterOrEqual(output.r, current.r - 1e-4f, "A kept history must not go below this frame.");
                Assert.GreaterOrEqual(output.g, current.g - 1e-4f);
                Assert.GreaterOrEqual(output.b, current.b - 1e-4f);
            }
        }

        [Test]
        public void Temporal_DeadHistoryDoesNotBlackOutTheResolve()
        {
            RequireGpu();
            using (var run = new Run(8, 8, false, 0.25f))
            {
                Color mean = new Color(0.8f, 0.4f, 0.2f, 0.9f);
                Color dev = new Color(0.05f, 0.05f, 0.05f, 0.05f);
                Color history = new Color(0f, 0f, 0f, 0f);
                Color current = new Color(0.8f, 0.4f, 0.2f, 1f);
                Color output = run.TemporalClamp(history, current, mean, dev);
                Assert.AreEqual(current.r, output.r, 1e-3f, "A dead history must not cover this frame's hit.");
                Assert.AreEqual(current.g, output.g, 1e-3f);
                Assert.AreEqual(current.b, output.b, 1e-3f);
                Assert.AreEqual(current.a, output.a, 1e-3f);
            }
        }

        [Test]
        public void RoughPlane_SkipsEveryTile()
        {
            RequireGpu();
            using (var run = new Run(24, 16, false, 0.9f))
            {
                uint[] args = run.Classify();
                Assert.AreEqual(0, (int)args[0], "A tile rougher than the cutoff is not traced.");
                foreach (uint tileClass in run.TileClasses())
                    Assert.AreEqual((uint)SSRPassUtilityData.ClassSkip, tileClass);
                foreach (float value in run.Rays())
                    Assert.AreEqual(0.0f, value, "Skipped tiles clear their ray records.");
            }
        }

        [Test]
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

        static float Frac(float value) => value - Mathf.Floor(value);

        static float PlaneDepth(Matrix4x4 projection, Vector3 view)
        {
            Vector4 clip = projection * new Vector4(view.x, view.y, view.z, 1);
            return clip.z / clip.w;
        }

        static Vector3 WorldAt(int x, int y, int size, float rawDepth, Matrix4x4 inverseViewProj)
        {
            Vector3 ndc = new Vector3((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f, rawDepth);
            Vector4 world = inverseViewProj * new Vector4(ndc.x, ndc.y, ndc.z, 1);
            return new Vector3(world.x, world.y, world.z) / world.w;
        }

        static Vector2Int NeighborOffset(Vector2 pixel, int frame, int sampleCount, float radius)
        {
            Vector2 uv = pixel + frame * new Vector2(47, 17) * 0.695f;
            Vector2 magic = new Vector2(0.06711056f, 0.00583715f);
            float noise = Frac(52.9829189f * Frac(Vector2.Dot(uv, magic)));
            float rotation = noise * Mathf.PI * 2f;
            float r = Mathf.Sqrt(1.5f / sampleCount) * radius;
            float theta = 2.39996323f + rotation;
            return new Vector2Int(Mathf.RoundToInt(Mathf.Cos(theta) * r), Mathf.RoundToInt(Mathf.Sin(theta) * r));
        }

        struct ReflectionSample
        {
            public Vector4 radiance;
            public Vector4 dirT;
        }

        sealed class Run : IDisposable
        {
            readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
            readonly List<GraphicsBuffer> m_Buffers = new List<GraphicsBuffer>();
            readonly ComputeShader m_Shader;
            readonly int m_Width, m_Height;
            readonly Vector2Int m_Trace;
            readonly GraphicsBuffer m_Args, m_TileList, m_Samples;
            readonly RenderTexture m_TileClass, m_RayHit, m_Resolved, m_HitDistance;
            readonly Texture2D m_Depth, m_HiZ, m_GBufferA, m_GBufferB, m_GBufferC, m_Lighting, m_Motion, m_HistoryRadiance, m_MotionMetadata;
            readonly RenderTexture m_HistoryMeta;

            public Vector2Int TileCount { get; }

            public Run(int width, int height, bool halfResolution, float roughness)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_ShaderPath);
                Assert.IsNotNull(asset, "Missing production compute shader " + k_ShaderPath);
                m_Shader = Track(UnityEngine.Object.Instantiate(asset));
                m_Width = width;
                m_Height = height;
                Unity.Mathematics.int2 trace = ScreenSpaceTraceUtility.TraceSize(new Unity.Mathematics.int2(width, height), halfResolution);
                Unity.Mathematics.int2 tiles = ScreenSpaceTraceUtility.TileCount(trace);
                m_Trace = new Vector2Int(trace.x, trace.y);
                TileCount = new Vector2Int(tiles.x, tiles.y);

                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, (float)width / height, 0.1f, 100), false);
                Vector4 planeClip = projection * new Vector4(0, 0, -5, 1);
                float planeDepth = planeClip.z / planeClip.w;

                m_Depth = Solid(width, height, new Color(planeDepth, 0, 0, 0), false);
                m_HiZ = Solid((width + 1) / 2, (height + 1) / 2, new Color(planeDepth, 0, 0, 0), true);
                m_GBufferA = Solid(width, height, new Color(0, 0, roughness, 0), false);
                m_GBufferB = Solid(width, height, new Color(0.5f, 0.5f, 1.0f, 0.5f), false);
                // Default lit, flags = valid surface (4) in the high nibble.
                m_GBufferC = Solid(width, height, new Color((4 << 4) / 255.0f, 0, 0, 0), false);
                m_Lighting = Solid(width, height, new Color(0.8f, 0.4f, 0.2f, 1), true);
                m_Motion = Solid(width, height, Color.clear, false);
                m_HistoryRadiance = Solid(width, height, new Color(5, 5, 5, 1), false);
                m_HistoryMeta = SurfaceMetadataTestUtility.Pack(Solid(width, height, new Color(0, 0, planeDepth, 8), false));
                m_Objects.Add(m_HistoryMeta);
                m_MotionMetadata = Solid(width,height,new Color(planeDepth,planeDepth,1,0),false);

                m_Args = Buffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 3);
                m_TileList = Buffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, TileCount.x * TileCount.y));
                m_TileClass = Target(TileCount.x, TileCount.y, GraphicsFormat.R32_UInt);
                m_RayHit = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                m_Samples = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_Trace.x * m_Trace.y * 4, 32);
                m_Buffers.Add(m_Samples);
                m_Resolved = Target(width, height, GraphicsFormat.R32G32B32A32_SFloat);
                m_HitDistance = Target(width, height, GraphicsFormat.R32_SFloat);

                ComputeShader s = m_Shader;
                s.SetVector("SSR_Resolution", new Vector4(width, height, 1.0f / width, 1.0f / height));
                s.SetVector("SSR_TraceResolution", new Vector4(m_Trace.x, m_Trace.y, 1.0f / m_Trace.x, 1.0f / m_Trace.y));
                s.SetInt("SSR_TraceScale", halfResolution ? 2 : 1);
                s.SetInt("SSR_FrameIndex", 3);
                s.SetFloat("SSR_MaxRoughness", 0.5f);
                s.SetFloat("SSR_EdgeFade", 0.05f);
                s.SetFloat("SSR_MirrorRoughness", 0.05f);
                s.SetFloat("SSR_LinearRoughness", 0.3f);
                s.SetFloat("SSR_BRDFBias", 0.7f);
                s.SetInt("SSR_HiZSteps", 16);
                s.SetInt("SSR_LinearSteps", 16);
                s.SetInt("SSR_BinarySteps", 4);
                s.SetInt("SSR_TraceFlags", 3);
                s.SetInt("SSR_ResolveSamples", 4);
                s.SetInt("SSR_Samples", 2);
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
                m_Args.SetData(new uint[] { 0, 1, 1 });
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.ClassifyKernelName);
                BindSurface(kernel);
                m_Shader.SetBuffer(kernel, "UAV_TileArgs", m_Args);
                m_Shader.SetBuffer(kernel, "UAV_TileList", m_TileList);
                m_Shader.SetTexture(kernel, "UAV_TileClass", m_TileClass);
                m_Shader.SetTexture(kernel, "UAV_RayHit", m_RayHit);
                m_Shader.SetBuffer(kernel, "UAV_Samples", m_Samples);
                m_Shader.Dispatch(kernel, TileCount.x, TileCount.y, 1);
                var args = new uint[3];
                m_Args.GetData(args);
                return args;
            }

            public uint[] TileClasses()
            {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(m_TileClass);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, "Tile class readback error");
                uint[] values = request.GetData<uint>().ToArray();
                Assert.AreEqual(TileCount.x * TileCount.y, values.Length);
                return values;
            }

            public float[] Rays() => Read(m_RayHit, 4);

            public float[] Trace()
            {
                using (var cmd = new CommandBuffer())
                {
                    int kernel = m_Shader.FindKernel(SSRPassUtilityData.TraceKernelName);
                    BindSurface(kernel);
                    cmd.SetComputeBufferParam(m_Shader, kernel, "SRV_TileList", m_TileList);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_TileClass", m_TileClass);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_HiZClosestTexture", m_HiZ);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_HiZFurthestTexture", m_HiZ);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "_Ranking_Tile", LoadTexture("RankingTile256SPP"));
                    cmd.SetComputeTextureParam(m_Shader, kernel, "_Scrambled_Tile", LoadTexture("ScramblingTile256SPP"));
                    cmd.SetComputeTextureParam(m_Shader, kernel, "_ScrambledOwen_Noise", LoadTexture("ScrambledNoise_Owen"));
                    cmd.SetComputeTextureParam(m_Shader, kernel, "UAV_RayHit", m_RayHit);
                    cmd.SetComputeBufferParam(m_Shader, kernel, "UAV_Samples", m_Samples);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_TemporalColor", m_Lighting);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_TemporalColorMips", m_Lighting);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_TemporalDepth", m_Depth);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_HitMotionMetadata", m_MotionMetadata);
                    cmd.SetComputeTextureParam(m_Shader, kernel, "SRV_MotionTexture", m_Motion);
                    m_Shader.SetInt("TemporalColorValid", 0);
                    m_Shader.SetInt("TemporalColorMipCount", m_Lighting.mipmapCount);
                    m_Shader.SetVector("TemporalColorResolution", new Vector4(m_Width, m_Height, 1f / m_Width, 1f / m_Height));
                    cmd.DispatchCompute(m_Shader, kernel, m_Args, 0);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                return Rays();
            }

            public float[] Resolve()
            {
                DispatchResolve();
                float[] hitDistance = Read(m_HitDistance, 1);
                foreach (float value in hitDistance) Assert.AreEqual(0.0f, value, "No hit, no hit distance.");
                return Read(m_Resolved, 4);
            }

            public void SetDepth(int x, int y, float rawDepth)
            {
                m_Depth.SetPixel(x, y, new Color(rawDepth, 0, 0, 0));
                m_Depth.Apply(false, false);
            }

            public Color ResolveCenter(ReflectionSample first, ReflectionSample second)
            {
                var records = new ReflectionSample[m_Trace.x * m_Trace.y * 4];
                int center = (m_Height / 2) * m_Trace.x + m_Width / 2;
                records[center * 4] = first;
                records[center * 4 + 1] = second;
                return ResolveRecords(records, 1, 2);
            }

            public Color ResolveRecords(ReflectionSample[] records, int resolveSamples, int samples)
            {
                m_Shader.SetInt("SSR_ResolveSamples", resolveSamples);
                m_Shader.SetInt("SSR_Samples", samples);
                m_Samples.SetData(records);
                DispatchResolve();
                float[] resolved = Read(m_Resolved, 4);
                int center = ((m_Height / 2) * m_Width + m_Width / 2) * 4;
                return new Color(resolved[center], resolved[center + 1], resolved[center + 2], resolved[center + 3]);
            }

            void DispatchResolve()
            {
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.ResolveKernelName);
                BindSurface(kernel);
                m_Shader.SetBuffer(kernel, "SRV_Samples", m_Samples);
                m_Shader.SetTexture(kernel, "UAV_Resolved", m_Resolved);
                m_Shader.SetTexture(kernel, "UAV_HitMean", Target(m_Width, m_Height, GraphicsFormat.R16G16B16A16_SFloat));
                m_Shader.SetTexture(kernel, "UAV_HitDev", Target(m_Width, m_Height, GraphicsFormat.R16G16B16A16_SFloat));
                m_Shader.SetTexture(kernel, "UAV_HitDistance", m_HitDistance);
                DispatchFull(kernel);
            }

            public Color TemporalClamp(Color history, Color current, Color mean, Color deviation)
            {
                m_Shader.SetFloat("SSR_HistoryValid", 1);
                RenderTexture temporal = Target(m_Width, m_Height, GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture meta = Target(m_Width, m_Height, GraphicsFormat.R32G32_UInt);
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.TemporalKernelName);
                m_Shader.SetTexture(kernel, "SRV_HitMotionMetadata", m_MotionMetadata);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Resolved", Solid(m_Width, m_Height, current, false));
                m_Shader.SetTexture(kernel, "SRV_HitMean", Solid(m_Width, m_Height, mean, false));
                m_Shader.SetTexture(kernel, "SRV_HitDev", Solid(m_Width, m_Height, deviation, false));
                m_Shader.SetTexture(kernel, "SRV_HitDistance", m_HitDistance);
                m_Shader.SetTexture(kernel, "SRV_HistoryVariance", Solid(m_Width, m_Height, Color.clear, false));
                m_Shader.SetTexture(kernel, "UAV_Variance", Target(m_Width, m_Height, GraphicsFormat.R16_SFloat));
                m_Shader.SetTexture(kernel, "SRV_MotionTexture", m_Motion);
                m_Shader.SetTexture(kernel, "SRV_HistoryRadiance", Solid(m_Width, m_Height, history, false));
                m_Shader.SetTexture(kernel, "SRV_HistoryMeta", m_HistoryMeta);
                m_Shader.SetTexture(kernel, "UAV_Temporal", temporal);
                m_Shader.SetTexture(kernel, "UAV_Meta", meta);
                DispatchFull(kernel);
                float[] values = Read(temporal, 4);
                int center = ((m_Height / 2) * m_Width + m_Width / 2) * 4;
                return new Color(values[center], values[center + 1], values[center + 2], values[center + 3]);
            }

            public float[] Temporal()
            {
                RenderTexture temporal = Target(m_Width, m_Height, GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture meta = Target(m_Width, m_Height, GraphicsFormat.R32G32_UInt);
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.TemporalKernelName);
                m_Shader.SetTexture(kernel,"SRV_HitMotionMetadata",m_MotionMetadata);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Resolved", m_Resolved);
                m_Shader.SetTexture(kernel, "SRV_HitMean", Solid(m_Width, m_Height, Color.clear, false));
                m_Shader.SetTexture(kernel, "SRV_HitDev", Solid(m_Width, m_Height, new Color(1, 1, 1, 0), false));
                m_Shader.SetTexture(kernel, "SRV_HitDistance", m_HitDistance);
                m_Shader.SetTexture(kernel, "SRV_HistoryVariance", Solid(m_Width, m_Height, Color.clear, false));
                m_Shader.SetTexture(kernel, "UAV_Variance", Target(m_Width, m_Height, GraphicsFormat.R16_SFloat));
                m_Shader.SetTexture(kernel, "SRV_MotionTexture", m_Motion);
                m_Shader.SetTexture(kernel, "SRV_HistoryRadiance", m_HistoryRadiance);
                m_Shader.SetTexture(kernel, "SRV_HistoryMeta", m_HistoryMeta);
                m_Shader.SetTexture(kernel, "UAV_Temporal", temporal);
                m_Shader.SetTexture(kernel, "UAV_Meta", meta);
                DispatchFull(kernel);
                foreach (float value in Read(temporal, 4))
                    Assert.AreEqual(0.0f, value, 1e-6f, "History is invalid this frame, so the current sample is taken as is.");
                return SurfaceMetadataTestUtility.Read(meta);
            }

            public float[] Spatial(Color input)
            {
                RenderTexture output = Target(m_Width, m_Height, GraphicsFormat.R32G32B32A32_SFloat);
                int kernel = m_Shader.FindKernel(SSRPassUtilityData.BilateralKernelName);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Temporal", Solid(m_Width, m_Height, input, false));
                m_Shader.SetTexture(kernel, "SRV_Variance", Solid(m_Width, m_Height, Color.clear, false));
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
