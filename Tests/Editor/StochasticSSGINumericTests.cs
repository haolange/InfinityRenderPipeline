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

        [TestCase(32, 32, false)]
        [TestCase(17, 19, true)]
        public void UnobstructedPlane_TracesMissesAndKeepsBakedDiffuse(int width, int height, bool halfResolution)
        {
            RequireGpu();
            using (var run = new Run(width, height, halfResolution, sky: false))
            {
                uint[] args = run.Classify();
                int tiles = run.TileCount.x * run.TileCount.y;
                Assert.AreEqual(tiles, (int)args[0], "Every tile holds the plane and traces.");
                Assert.AreEqual(0, (int)args[3], "Skip tiles");
                for (int i = 0; i < 2; ++i)
                {
                    Assert.AreEqual(1, (int)args[i * 3 + 1]);
                    Assert.AreEqual(1, (int)args[i * 3 + 2]);
                }

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
        public void Sky_SkipsEveryTile()
        {
            RequireGpu();
            using (var run = new Run(24, 16, false, sky: true))
            {
                uint[] args = run.Classify();
                Assert.AreEqual(run.TileCount.x * run.TileCount.y, (int)args[3]);
                Assert.AreEqual(0, (int)args[0]);
                foreach (float value in run.Trace())
                    Assert.AreEqual(0.0f, value, "Skipped tiles clear their ray records.");
            }
        }

        [Test]
        public void Spatial_PreservesConstantSignal()
        {
            RequireGpu();
            using (var run = new Run(19, 17, false, sky: false))
            {
                float[] spatial = run.Spatial(new Color(0.25f, 0.25f, 0.25f, 0.6f));
                for (int i = 0; i < spatial.Length; i += 4)
                {
                    Assert.AreEqual(0.25f, spatial[i], 1e-3f, "Normalized irradiance");
                    Assert.AreEqual(0.6f, spatial[i + 3], 1e-3f, "Normalized confidence");
                }
            }
        }

        [TestCase(17,19,false)]
        [TestCase(17,19,true)]
        [TestCase(1,1,false)]
        public void BilateralReconstruction_PreservesCoverageAndRejectsOtherSurfaces(int width,int height,bool reject)
        {
            RequireGpu();
            using (var run = new Run(width,height,true,sky:false))
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
            using (var run = new Run(17,19,true,sky:false))
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
            readonly Texture2D m_Depth, m_HiZ, m_GBufferB, m_GBufferC, m_Lighting, m_Motion, m_HistoryRadiance, m_HistoryMeta, m_MotionMetadata;

            public Vector2Int TileCount { get; }

            public Run(int width, int height, bool halfResolution, bool sky)
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
                m_HistoryMeta = Solid(width, height, new Color(0, 0, depth, 8), false);
                m_MotionMetadata = Solid(width,height,new Color(depth,depth,1,0),false);

                m_Args = Buffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 6);
                m_TileList = Buffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, 2 * TileCount.x * TileCount.y));
                m_RayHit = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                m_TraceRadiance = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                m_HitDistance = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32_SFloat);

                ComputeShader s = m_Shader;
                s.SetVector("SSGI_Resolution", new Vector4(width, height, 1.0f / width, 1.0f / height));
                s.SetVector("SSGI_TraceResolution", new Vector4(m_Trace.x, m_Trace.y, 1.0f / m_Trace.x, 1.0f / m_Trace.y));
                s.SetInt("SSGI_TraceScale", halfResolution ? 2 : 1);
                s.SetInt("SSGI_FrameIndex", 3);
                s.SetInt("SSGI_MaxTiles", TileCount.x * TileCount.y);
                s.SetFloat("SSGI_MaxDistance", 50);
                s.SetFloat("SSGI_Thickness", 0.1f);
                s.SetFloat("SSGI_Intensity", 1);
                s.SetInt("SSGI_HiZSteps", 16);
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

            public uint[] Classify()
            {
                m_Args.SetData(new uint[] { 0, 1, 1, 0, 1, 1 });
                int kernel = m_Shader.FindKernel(SSGIPassUtilityData.ClassifyKernelName);
                BindSurface(kernel);
                m_Shader.SetBuffer(kernel, "UAV_TileArgs", m_Args);
                m_Shader.SetBuffer(kernel, "UAV_TileList", m_TileList);
                m_Shader.Dispatch(kernel, TileCount.x, TileCount.y, 1);
                var args = new uint[6];
                m_Args.GetData(args);
                return args;
            }

            public float[] Trace()
            {
                int maxTiles = TileCount.x * TileCount.y;
                using (var cmd = new CommandBuffer())
                {
                    int clear = m_Shader.FindKernel(SSGIPassUtilityData.ClearSkippedKernelName);
                    cmd.SetComputeIntParam(m_Shader, "SSGI_TileListOffset", SSGIPassUtilityData.ClassSkip * maxTiles);
                    cmd.SetComputeBufferParam(m_Shader, clear, "SRV_TileList", m_TileList);
                    cmd.SetComputeTextureParam(m_Shader, clear, "UAV_RayHit", m_RayHit);
                    cmd.SetComputeTextureParam(m_Shader, clear, "UAV_TraceRadiance", m_TraceRadiance);
                    cmd.SetComputeTextureParam(m_Shader, clear, "UAV_HitDistance", m_HitDistance);
                    cmd.DispatchCompute(m_Shader, clear, m_Args, (uint)(SSGIPassUtilityData.ClassSkip * ComputeUtility.IndirectDispatchArgsStride));

                    int trace = m_Shader.FindKernel(SSGIPassUtilityData.TraceKernelName);
                    BindSurface(trace);
                    m_Shader.SetTexture(trace,"SRV_TemporalColor",m_Lighting);
                    m_Shader.SetTexture(trace,"SRV_TemporalColorMips",m_Lighting);
                    m_Shader.SetTexture(trace,"SRV_TemporalDepth",m_Depth);
                    m_Shader.SetTexture(trace,"SRV_HitMotionMetadata",m_HistoryMeta);
                    m_Shader.SetTexture(trace,"SRV_MotionTexture",m_Motion);
                    m_Shader.SetInt("TemporalColorValid",0);
                    m_Shader.SetInt("TemporalColorMipCount",m_Lighting.mipmapCount);
                    m_Shader.SetVector("TemporalColorResolution",new Vector4(m_Width,m_Height,1f/m_Width,1f/m_Height));
                    cmd.SetComputeIntParam(m_Shader, "SSGI_TileListOffset", SSGIPassUtilityData.ClassTrace * maxTiles);
                    cmd.SetComputeBufferParam(m_Shader, trace, "SRV_TileList", m_TileList);
                    cmd.SetComputeTextureParam(m_Shader, trace, "SRV_HiZClosestTexture", m_HiZ);
                    cmd.SetComputeTextureParam(m_Shader, trace, "_Ranking_Tile", LoadTexture("RankingTile256SPP"));
                    cmd.SetComputeTextureParam(m_Shader, trace, "_Scrambled_Tile", LoadTexture("ScramblingTile256SPP"));
                    cmd.SetComputeTextureParam(m_Shader, trace, "_ScrambledOwen_Noise", LoadTexture("ScrambledNoise_Owen"));
                    cmd.SetComputeTextureParam(m_Shader, trace, "UAV_RayHit", m_RayHit);
                    cmd.SetComputeTextureParam(m_Shader, trace, "UAV_TraceRadiance", m_TraceRadiance);
                    cmd.SetComputeTextureParam(m_Shader, trace, "UAV_HitDistance", m_HitDistance);
                    cmd.DispatchCompute(m_Shader, trace, m_Args, (uint)(SSGIPassUtilityData.ClassTrace * ComputeUtility.IndirectDispatchArgsStride));
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                return Read(m_RayHit, 4);
            }

            public float[] TraceRadiance() => Read(m_TraceRadiance,4);

            public float[] Temporal()
            {
                RenderTexture temporal = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                RenderTexture meta = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                int kernel = m_Shader.FindKernel(SSGIPassUtilityData.TemporalKernelName);
                m_Shader.SetTexture(kernel,"SRV_HitMotionMetadata",m_MotionMetadata);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_TraceRadiance", m_TraceRadiance);
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
                RenderTexture output = Target(m_Trace.x, m_Trace.y, GraphicsFormat.R32G32B32A32_SFloat);
                int kernel = m_Shader.FindKernel(SSGIPassUtilityData.SpatialKernelName);
                BindSurface(kernel);
                m_Shader.SetTexture(kernel, "SRV_Temporal", Solid(m_Trace.x, m_Trace.y, input, false));
                m_Shader.SetTexture(kernel, "UAV_Output", output);
                m_Shader.SetTexture(kernel, "SRV_HitDistance", Solid(m_Trace.x, m_Trace.y, Color.clear, false));
                DispatchFull(kernel);
                return Read(output, 4);
            }

            public float[] Upsample(bool reject)
            {
                int kernel = m_Shader.FindKernel(SSGIPassUtilityData.UpsampleKernelName);
                BindSurface(kernel);
                var output = Target(m_Width,m_Height,GraphicsFormat.R32G32B32A32_SFloat);
                m_Shader.SetTexture(kernel,"SRV_Filtered",Solid(m_Trace.x,m_Trace.y,new Color(.25f,.125f,.0625f,.6f),false));
                m_Shader.SetTexture(kernel,"SRV_Meta",reject?Solid(m_Trace.x,m_Trace.y,new Color(1,1,.5f,8),false):m_HistoryMeta);
                m_Shader.SetTexture(kernel,"UAV_Output",output);
                DispatchFull(kernel); return Read(output,4);
            }

            public float[] UpsampleRamp(int phase)
            {
                int kernel=m_Shader.FindKernel(SSGIPassUtilityData.UpsampleKernelName);
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
