using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Runs the production SSGI kernels on one unobstructed plane facing a perspective camera. Cosine rays leave toward
    // the camera and must miss; trace radiance then has zero confidence (the baked indirect diffuse stays), the temporal
    // filter starts a new history, and every output is finite. Classification covers every tile exactly once.
    public class StochasticSSGINumericTests
    {
        const string k_Package = "Packages/com.infinity.render-pipeline/";
        const string k_ShaderPath = k_Package + "Shaders/RenderingFeature/ScreenSpaceIndirectDiffuse/Compute_ScreenSpaceIndirectDiffuse.compute";

        static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute shaders and async readback are required.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device.");
        }

        [TestCase(32, 32)]
        [TestCase(17, 19)]
        public void UnobstructedPlane_TracesMissesAndKeepsBakedDiffuse(int width, int height)
        {
            RequireGpu();
            using (var run = new Run(width, height, sky: false))
            {
                float[] rays = run.Trace();
                for (int i = 0; i < rays.Length; i += 4)
                    Assert.Less(rays[i + 3], 0.0f, "Every trace pixel records a miss: an unobstructed plane does not light itself.");

                foreach (float value in run.TraceRadiance())
                    Assert.AreEqual(0.0f, value, 1e-6f, "Misses carry no radiance and no confidence.");

                float[] meta = run.Temporal();
                for (int i = 0; i < meta.Length; i += 4)
                    Assert.AreEqual(1.0f, meta[i + 3], 1e-6f, "A rejected history restarts at age 1 after this frame.");

                foreach (float value in run.Spatial(Color.clear))
                    Assert.AreEqual(0.0f, value, 1e-6f);
            }
        }

        [Test]
        public void Sky_BinsZeroRays()
        {
            RequireGpu();
            using (var run = new Run(24, 16, sky: true))
            {
                foreach (float value in run.Trace())
                    Assert.AreEqual(0.0f, value, "Skipped tiles clear their ray records.");
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void SparseOddTiles_ContainEachValidLogicalPixelExactlyOnce(int phase)
        {
            RequireGpu();
            using (var run = new Run(35, 39, false)) run.CheckSparseList(phase);
        }

        [Test]
        public void Bilateral_PreservesConstantSignal()
        {
            RequireGpu();
            using (var run = new Run(19, 17, sky: false))
            {
                float[] filtered = run.Spatial(new Color(0.25f, 0.25f, 0.25f, 0.6f));
                for (int i = 0; i < filtered.Length; i += 4)
                {
                    Assert.AreEqual(0.25f, filtered[i], 1e-3f, "Normalized irradiance");
                    Assert.AreEqual(0.6f, filtered[i + 3], 1e-3f, "Normalized confidence");
                }
            }
        }

        [Test]
        public void Samples_AverageMissesToZero()
        {
            RequireGpu();
            using (var run = new Run(16, 16, sky: false))
            {
                run.SetSamples(2);
                run.Trace();
                foreach (float value in run.TraceRadiance())
                    Assert.AreEqual(0.0f, value, 1e-6f, "Two misses average to zero radiance.");
            }
        }

        [Test]
        public void Temporal_ReadsBilateralBeforeHistory()
        {
            RequireGpu();
            using (var run = new Run(16, 16, sky: false))
            {
                float[] temporal = run.TemporalFrom(new Color(0.2f, 0.1f, 0.05f, 0.4f), new Color(0.9f, 0.9f, 0.9f, 0.9f));
                for (int i = 0; i < temporal.Length; i += 4)
                {
                    Assert.AreEqual(0.2f, temporal[i], 1e-4f, "Temporal reads the bilateral buffer, not the trace buffer.");
                    Assert.AreEqual(0.4f, temporal[i + 3], 1e-4f);
                }
            }
        }

        [TestCase(17,19,false)]
        [TestCase(17,19,true)]
        [TestCase(1,1,false)]
        public void BilateralReconstruction_PreservesCoverageAndRejectsOtherSurfaces(int width,int height,bool reject)
        {
            RequireGpu();
            using (var run = new Run(width, height, sky: false))
            {
                float[] result = run.Upsample(reject);
                for (int i=0;i<result.Length;i+=4)
                {
                    Assert.That(result[i],Is.EqualTo(reject?0:.25f).Within(1e-4));
                    Assert.That(result[i+3],Is.EqualTo(reject?0:.6f).Within(1e-4));
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void BilateralReconstruction_TracksRotatingQuadAndOddEdge(int phase)
        {
            RequireGpu();
            using (var run = new Run(17, 19, sky: false))
            {
                float[] result = run.UpsampleRamp(phase);
                int offsetX = phase == 1 || phase == 2 ? 1 : 0;
                for(int y=0;y<19;++y)
                for(int x=0;x<17;++x)
                {
                    float expected = Mathf.Clamp(x,offsetX,16) / 32f;
                    Assert.That(result[(y*17+x)*4], Is.EqualTo(expected).Within(2e-5), $"phase={phase} pixel={x},{y}");
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
            readonly RenderTexture m_RayHit, m_TraceRadiance, m_HitDistance;
            readonly Texture2D m_Depth, m_HiZ, m_GBufferB, m_GBufferC, m_Lighting, m_Motion, m_HistoryRadiance, m_MotionMetadata;
            readonly RenderTexture m_HistoryMeta;

            public Vector2Int TileCount { get; }
            public int TracePixels => m_Trace.x * m_Trace.y;

            public Run(int width, int height, bool sky)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_ShaderPath);
                Assert.IsNotNull(asset, "Missing production compute shader " + k_ShaderPath);
                m_Shader = Track(UnityEngine.Object.Instantiate(asset));
                m_Width = width;
                m_Height = height;
                Unity.Mathematics.int2 trace = ScreenSpaceTraceUtility.TraceSize(new Unity.Mathematics.int2(width, height), true);
                Unity.Mathematics.int2 tiles = ScreenSpaceTraceUtility.TileCount(trace);
                m_Trace = new Vector2Int(trace.x, trace.y);
                TileCount = new Vector2Int(tiles.x, tiles.y);

                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, (float)width / height, 0.1f, 100), false);
                Vector4 planeClip = projection * new Vector4(0, 0, -5, 1);
                float farDepth = SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f;
                float depth = sky ? farDepth : planeClip.z / planeClip.w;

                m_Depth = Solid(width, height, new Color(depth, 0, 0, 0), false);
                m_HiZ = Solid((width + 1) / 2, (height + 1) / 2, new Color(depth, 0, 0, 0), true);
                m_GBufferB = Solid(width, height, new Color(0.5f, 0.5f, 1.0f, 0.5f), false);
                // Default lit, flags = valid surface (4) in the high nibble.
                m_GBufferC = Solid(width, height, new Color((4 << 4) / 255.0f, 0, 0, 0), false);
                m_Lighting = Solid(width, height, new Color(0.8f, 0.4f, 0.2f, 1), true);
                m_Motion = Solid(width, height, Color.clear, false);
                m_HistoryRadiance = Solid(width, height, new Color(5, 5, 5, 1), false);
                m_HistoryMeta = SurfaceMetadataTestUtility.Pack(Solid(width, height, new Color(0, 0, depth, 8), false));
                m_Objects.Add(m_HistoryMeta);
                m_MotionMetadata = Solid(width,height,new Color(depth,depth,1,0),false);

                m_Args = Buffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 4);
                m_TileList = Buffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, m_Trace.x * m_Trace.y));
                m_RayHit = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                m_TraceRadiance = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                m_HitDistance = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32_SFloat);

                ComputeShader s = m_Shader;
                s.SetVector("SSGI_Resolution", new Vector4(width, height, 1.0f / width, 1.0f / height));
                s.SetVector("SSGI_TraceResolution", new Vector4(m_Trace.x, m_Trace.y, 1.0f / m_Trace.x, 1.0f / m_Trace.y));
                s.SetInt("SSGI_TraceScale", 2);
                s.SetInt("SSGI_Samples", 1);
                s.SetInt("SSGI_FrameIndex", 3);
                s.SetInt("SSGI_MaxTiles", TileCount.x * TileCount.y);
                s.SetFloat("SSGI_Intensity", 1);
                s.SetInt("SSGI_HiZSteps", 16);
                s.SetInt("SSGI_TraceFlags", 3);
                s.SetFloat("SSGI_TemporalWeight", 0.93f);
                s.SetFloat("SSGI_TemporalClampScale", 1.25f);
                s.SetFloat("SSGI_HistoryValid", 0);
                s.SetVector("SSGI_CameraPosition", Vector4.zero);
                s.SetVector("SSGI_CameraForward", new Vector4(0, 0, -1, 0));
                s.SetVector("ScreenSpaceDepthParams", new Vector4(0.1f, 100, 0, SystemInfo.usesReversedZBuffer ? 1 : 0));
                s.SetMatrix("Matrix_Proj", projection);
                s.SetMatrix("Matrix_InvProj", projection.inverse);
                s.SetMatrix("Matrix_InvViewProj", projection.inverse);
                s.SetMatrix("Matrix_WorldToView", Matrix4x4.identity);
                s.SetMatrix("Matrix_HistoryViewProj", projection);
                s.SetMatrix("Matrix_MotionViewProj", projection);
                s.SetMatrix("Matrix_LastMotionViewProj", projection);
            }

            public void CheckSparseList(int phase)
            {
                m_Shader.SetInt("SSGI_FrameIndex", phase);
                var pixels = m_GBufferC.GetPixels();
                for (int y = 0; y < m_Height; ++y) for (int x = 0; x < m_Width; ++x)
                    pixels[y * m_Width + x] = (x + y * 3) % 5 == 0 ? new Color((4 << 4) / 255f,0,0,0) : Color.clear;
                m_GBufferC.SetPixels(pixels); m_GBufferC.Apply();
                Trace();
                float[] rays = Read(m_RayHit, 4);
                int[] px = { 0, 1, 1, 0 }, py = { 0, 1, 0, 1 };
                for (int y = 0; y < m_Trace.y; ++y) for (int x = 0; x < m_Trace.x; ++x)
                {
                    int fullX = Math.Min(x * 2 + px[phase], m_Width - 1), fullY = Math.Min(y * 2 + py[phase], m_Height - 1);
                    bool valid = (fullX + fullY * 3) % 5 == 0;
                    float pdf = rays[(y * m_Trace.x + x) * 4 + 3];
                    if (valid) Assert.Less(pdf, 0.0f, "A valid logical pixel records its miss.");
                    else Assert.AreEqual(0.0f, pdf, "An invalid logical pixel stays clear.");
                }
            }

            public float[] Trace()
            {
                int maxTiles = TileCount.x * TileCount.y;
                using (var cmd = new CommandBuffer())
                {
                    int trace = m_Shader.FindKernel("SSGITraceDebug");
                    BindSurface(trace);
                    m_Shader.SetTexture(trace,"SRV_TemporalColor",m_Lighting);
                    m_Shader.SetTexture(trace,"SRV_TemporalColorMips",m_Lighting);
                    m_Shader.SetTexture(trace,"SRV_TemporalDepth",m_Depth);
                    m_Shader.SetTexture(trace,"SRV_HitMotionMetadata",m_MotionMetadata);
                    m_Shader.SetTexture(trace,"SRV_MotionTexture",m_Motion);
                    m_Shader.SetInt("TemporalColorValid",0);
                    m_Shader.SetInt("TemporalColorMipCount",m_Lighting.mipmapCount);
                    m_Shader.SetVector("TemporalColorResolution",new Vector4(m_Width,m_Height,1f/m_Width,1f/m_Height));
                    cmd.SetComputeTextureParam(m_Shader, trace, "SRV_HiZClosestTexture", m_HiZ);
                    cmd.SetComputeTextureParam(m_Shader, trace, "SRV_HiZFurthestTexture", m_HiZ);
                    cmd.SetComputeTextureParam(m_Shader, trace, "_Ranking_Tile", LoadTexture("RankingTile256SPP"));
                    cmd.SetComputeTextureParam(m_Shader, trace, "_Scrambled_Tile", LoadTexture("ScramblingTile256SPP"));
                    cmd.SetComputeTextureParam(m_Shader, trace, "_ScrambledOwen_Noise", LoadTexture("ScrambledNoise_Owen"));
                    cmd.SetComputeTextureParam(m_Shader, trace, "UAV_RayHit", m_RayHit);
                    cmd.SetComputeTextureParam(m_Shader, trace, "UAV_TraceRadiance", m_TraceRadiance);
                    cmd.SetComputeTextureParam(m_Shader, trace, "UAV_HitDistance", m_HitDistance);
                    cmd.DispatchCompute(m_Shader, trace, TileCount.x, TileCount.y, 1);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                return Read(m_RayHit, 4);
            }

            public float[] TraceRadiance() => Read(m_TraceRadiance,4);

            float[] m_LastTemporalMeta;

            public float[] Temporal()
            {
                float[] color = FilterTemporal(m_TraceRadiance, Solid(m_Trace.x, m_Trace.y, new Color(0.9f, 0.9f, 0.9f, 0.9f), false));
                foreach (float value in color)
                    Assert.AreEqual(0.0f, value, 1e-6f, "History is invalid this frame, so the bilateral sample is taken as is.");
                return m_LastTemporalMeta;
            }

            float[] FilterTemporal(Texture bilateral, Texture traceDecoy)
            {
                RenderTexture temporal = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture meta = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32_UInt);
                int kernel = m_Shader.FindKernel("SSGITemporal");
                m_Shader.SetTexture(kernel, "SRV_HitMotionMetadata", m_MotionMetadata);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Current", bilateral);
                m_Shader.SetTexture(kernel, "SRV_TraceRadiance", traceDecoy);
                m_Shader.SetTexture(kernel, "SRV_MotionTexture", m_Motion);
                m_Shader.SetTexture(kernel, "SRV_HistoryRadiance", m_HistoryRadiance);
                m_Shader.SetTexture(kernel, "SRV_HistoryMeta", m_HistoryMeta);
                m_Shader.SetTexture(kernel, "UAV_Temporal", temporal);
                m_Shader.SetTexture(kernel, "UAV_Meta", meta);
                DispatchTrace(kernel);
                m_LastTemporalMeta = SurfaceMetadataTestUtility.Read(meta);
                return Read(temporal, 4);
            }

            public void SetSamples(int samples) => m_Shader.SetInt("SSGI_Samples", samples);

            public float[] Spatial(Color input)
            {
                RenderTexture output = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture meta = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32_UInt);
                int kernel = m_Shader.FindKernel("SSGISpatial");
                BindSurface(kernel);
                m_Shader.SetInt("SSGI_SpatialPass", 0);
                m_Shader.SetInt("SSGI_HasAO", 0);
                m_Shader.SetFloat("SSGI_FrameAngle", 0);
                m_Shader.SetTexture(kernel, "SRV_Temporal", Solid(m_Trace.x, m_Trace.y, input, false));
                m_Shader.SetTexture(kernel, "SRV_Meta", meta);
                m_Shader.SetTexture(kernel, "SRV_Occlusion", m_Depth);
                m_Shader.SetTexture(kernel, "UAV_Output", output);
                DispatchTrace(kernel);
                return Read(output, 4);
            }

            public float[] TemporalFrom(Color bilateral, Color trace)
            {
                return FilterTemporal(Solid(m_Trace.x, m_Trace.y, bilateral, false), Solid(m_Trace.x, m_Trace.y, trace, false));
            }

            public float[] Upsample(bool reject)
            {
                int kernel = m_Shader.FindKernel("SSGIUpsample");
                BindSurface(kernel);
                var output = Target(m_Width,m_Height,GraphicsFormat.R32G32B32A32_SFloat);
                m_Shader.SetTexture(kernel,"SRV_Filtered",Solid(m_Trace.x,m_Trace.y,new Color(.25f,.125f,.0625f,.6f),false));
                var metadata = reject ? SurfaceMetadataTestUtility.Pack(Solid(m_Trace.x,m_Trace.y,new Color(1,1,.5f,8),false)) : m_HistoryMeta;
                if (reject) m_Objects.Add(metadata);
                m_Shader.SetTexture(kernel,"SRV_Meta",metadata);
                m_Shader.SetTexture(kernel,"UAV_Output",output);
                DispatchFull(kernel); return Read(output,4);
            }

            public float[] UpsampleRamp(int phase)
            {
                int kernel=m_Shader.FindKernel("SSGIUpsample");
                m_Shader.SetInt("SSGI_FrameIndex",phase);
                BindSurface(kernel);
                var texture=Solid(m_Trace.x,m_Trace.y,Color.clear,false);
                var colors=new Color[m_Trace.x*m_Trace.y];
                for(int y=0;y<m_Trace.y;++y)
                for(int x=0;x<m_Trace.x;++x)
                    colors[y*m_Trace.x+x]=new Color(Mathf.Min(x*2+((phase==1||phase==2)?1:0),m_Width-1)/32f,0,0,1);
                texture.SetPixels(colors);texture.Apply(false,false);
                var output=Target(m_Width,m_Height,GraphicsFormat.R32G32B32A32_SFloat);
                m_Shader.SetTexture(kernel,"SRV_Filtered",texture);
                m_Shader.SetTexture(kernel,"SRV_Meta",m_HistoryMeta);
                m_Shader.SetTexture(kernel,"UAV_Output",output);
                DispatchFull(kernel);return Read(output,4);
            }

            void BindSurface(int kernel)
            {
                m_Shader.SetTexture(kernel, "SRV_DepthTexture", m_Depth);
                m_Shader.SetTexture(kernel, "SRV_GBufferB", m_GBufferB);
                m_Shader.SetTexture(kernel, "SRV_GBufferC", m_GBufferC);
            }

            void DispatchTrace(int kernel)
            {
                m_Shader.Dispatch(kernel, (m_Trace.x + 7) / 8, (m_Trace.y + 7) / 8, 1);
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
                var target = new RenderTexture(width, height, 0, format) { enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SSGITest-" + format };
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
