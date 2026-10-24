using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // Deterministic production-kernel controls: distinguish noise reduction from lost energy,
    // delayed lighting changes, invalid reprojection and filtering across another surface.
    public class SSGITemporalResponseGpuTests
    {
        enum Signal { Constant, Noise, NoiseScrambled, SlopeNoise, Step, RejectDepth, RejectNormal, DepthEdge, NormalEdge, ZeroConfidence, LowConfidence, InvalidCenter, MatureNoise, NoisyStep, RadianceNoise, NearDepthEdge, FarDepthEdge, RadianceEdge, SpatialNoise, SpatialSlopeConstant, MovingSurfaceDepth }

        [UnityTest]
        public IEnumerator Constant_PreservesPremultipliedEnergyAndHistoryAge() => Check(Signal.Constant);
        [UnityTest]
        public IEnumerator Reset_PreservesCurrentResolvedSample() => Check(Signal.Noise);
        [UnityTest]
        public IEnumerator LightingStep_RespondsWithoutSeparatingConfidenceFromRadiance() => Check(Signal.Step);
        [UnityTest]
        public IEnumerator MovingSurface_UsesExpectedPreviousObjectDepth() => Check(Signal.MovingSurfaceDepth);
        [UnityTest]
        public IEnumerator ChangedDepth_RejectsHistory() => Check(Signal.RejectDepth);
        [UnityTest]
        public IEnumerator ChangedNormal_RejectsHistory() => Check(Signal.RejectNormal);
        [UnityTest]
        public IEnumerator OtherDepth_DoesNotPolluteCurrentEstimate() => Check(Signal.DepthEdge);
        [UnityTest]
        public IEnumerator OtherNormal_DoesNotPolluteCurrentEstimate() => Check(Signal.NormalEdge);

        [UnityTest]
        public IEnumerator Reset_PreservesIndependentScrambledChannels() => Check(Signal.NoiseScrambled);
        [UnityTest]
        public IEnumerator Reset_PreservesSamplesOnAContinuousSlope() => Check(Signal.SlopeNoise);
        [UnityTest]
        public IEnumerator ZeroConfidence_PreservesZeroPremultipliedEnergy() => Check(Signal.ZeroConfidence);
        [UnityTest]
        public IEnumerator LowConfidence_PreservesPremultipliedEnergy() => Check(Signal.LowConfidence);
        [UnityTest]
        public IEnumerator InvalidCenter_DoesNotBorrowNeighbors() => Check(Signal.InvalidCenter);

        [UnityTest]
        public IEnumerator MatureHistory_FollowsConfiguredEMAForIndependentNoise() => Check(Signal.MatureNoise);
        [UnityTest]
        public IEnumerator NoisyLightingStep_RespondsAcrossOneFourAndSixteenFrames() => Check(Signal.NoisyStep);
        [UnityTest]
        public IEnumerator LowConfidence_PreservesCurrentIndependentRadianceNoise() => Check(Signal.RadianceNoise);
        [UnityTest]
        public IEnumerator NearbyOtherDepth_DoesNotPolluteCurrentEstimate() => Check(Signal.NearDepthEdge);
        [UnityTest]
        public IEnumerator FarOtherDepth_DoesNotPolluteCurrentEstimate() => Check(Signal.FarDepthEdge);
        [UnityTest]
        public IEnumerator Reset_PreservesHardRadianceEdgeAndEnergy() => Check(Signal.RadianceEdge);

        [UnityTest]
        public IEnumerator Spatial_FrontPlaneAttenuatesDeterministicNoise() => Check(Signal.SpatialNoise);
        [UnityTest]
        public IEnumerator Spatial_ContinuousSlopePreservesConstantSignal() => Check(Signal.SpatialSlopeConstant);

        static readonly int[] s_Pattern = { 1, -1, 1, 1, -1, -1, 1, -1, 1, 1, -1, -1, -1, 1, -1, 1 };
        static int Pattern(int x, int y, int phase) => s_Pattern[((y + phase / 4) % 4) * 4 + (x + phase) % 4];

        static bool Spatial(Signal signal) => signal == Signal.SpatialNoise || signal == Signal.SpatialSlopeConstant;

        static bool Noisy(Signal signal) => signal == Signal.Noise || signal == Signal.NoiseScrambled || signal == Signal.SlopeNoise;

        static IEnumerator Check(Signal signal)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Compute/readback device required.");
            using (var run = new Run(signal))
            {
                run.Submit();
                double deadline = EditorApplication.timeSinceStartup + 15;
                while (run.pending != 0 && EditorApplication.timeSinceStartup < deadline)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.AreEqual(0, run.pending, "Readback timed out; owner remains retained until callbacks drain.");
                Assert.IsNull(run.error);
                Assert.AreEqual(0, Run.RetainedOwners, "Every submitted readback owner must drain.");
                foreach (float value in run.color) Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value));
                if (run.meta != null) foreach (float value in run.meta) Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value));
                if (Spatial(signal))
                {
                    if (signal == Signal.SpatialSlopeConstant)
                    {
                        for (int i = 0; i < run.color.Length; i += 4)
                        {
                            Assert.AreEqual(.25f, run.color[i], 1e-5f);
                            Assert.AreEqual(.125f, run.color[i + 1], 1e-5f);
                            Assert.AreEqual(.0625f, run.color[i + 2], 1e-5f);
                            Assert.AreEqual(.5f, run.color[i + 3], 1e-5f);
                        }
                    }
                    else
                    {
                        double squared = 0; int count = 0;
                        for (int y = 4; y <= 6; ++y)
                            for (int x = 4; x <= 8; ++x)
                            {
                                int i = (y * Run.Width + x) * 4;
                                squared += Math.Pow(run.color[i] - .25, 2); count++;
                                Assert.AreEqual(run.color[i] * .5f, run.color[i + 1], 1e-5f);
                                Assert.AreEqual(run.color[i] * .25f, run.color[i + 2], 1e-5f);
                                Assert.AreEqual(.5f, run.color[i + 3], 1e-5f);
                            }
                        Assert.Less(Math.Sqrt(squared / count), .03, "The world-space kernel must retain the established front-plane noise attenuation budget.");
                    }
                }
                else if (signal == Signal.MatureNoise || signal == Signal.NoisyStep)
                {
                    foreach (int frame in new[] { 1, 4, 16 })
                        for (int channel = 0; channel < 4; ++channel)
                        {
                            float[] values = run.sequence[frame];
                            double sum = 0, squared = 0; int count = 0;
                            for (int y = 2; y < 10; ++y)
                                for (int x = 2; x < 10; ++x)
                                {
                                    float scale = channel == 3 ? .5f : 1 << channel;
                                    float value = values[(y * Run.Width + x) * 4 + channel] * scale;
                                    float target = signal == Signal.NoisyStep ? .4f : .25f;
                                    sum += value; squared += (value - target) * (value - target); count++;
                                    if (signal == Signal.MatureNoise)
                                    {
                                        double amplitude = channel == 3 ? .075 : .1;
                                        double expected = .25;
                                        // Every 3x3 chosen sign neighborhood has |mean| <= 5/9.
                                        // Its stddev is >= sqrt(1-(5/9)^2), so the 1.25-sigma box
                                        // contains the known mean +/- this margin. The recurrence
                                        // below stays strictly inside it; no clipping is expected.
                                        double margin = (1.25 * Math.Sqrt(1 - 25.0 / 81) - 5.0 / 9) * amplitude;
                                        for (int t = 0; t < frame; ++t)
                                        {
                                            int signs = 0;
                                            for (int dy = -1; dy <= 1; ++dy)
                                                for (int dx = -1; dx <= 1; ++dx) signs += Pattern(x + dx, y + dy, t + channel);
                                            Assert.LessOrEqual(Math.Abs(signs), 5, "Input construction proves the unclipped-history premise.");
                                            Assert.Less(Math.Abs(expected - .25), margin, "EMA remains inside the proven clip-box margin.");
                                            expected = .93 * expected + .07 * (.25 + amplitude * Pattern(x, y, t + channel));
                                        }
                                        Assert.AreEqual(expected, value, 2e-5, "Configured EMA, not an extra prefilter, frame " + frame + ", channel " + channel);
                                    }
                                }
                            TestContext.WriteLine($"{signal} frame={frame} channel={channel} mean={sum / count:R} rmsFromTarget={Math.Sqrt(squared / count):R}");
                            if (signal == Signal.MatureNoise)
                                Assert.AreEqual(.25, sum / count, .002, "Balanced input must not bias the mature mean.");
                            else
                                Assert.That(sum / count, Is.InRange(frame == 1 ? .26 : frame == 4 ? .29 : .33, .46), "Noisy lighting response at frame " + frame);
                            for (int i = 0; i < values.Length; i += 4)
                            {
                                for (int c = 0; c < 4; ++c) Assert.IsFalse(float.IsNaN(values[i + c]) || float.IsInfinity(values[i + c]));
                                Assert.That(values[i + 3], Is.InRange(0, 1));
                            }
                        }
                }
                else if (signal == Signal.RadianceNoise)
                {
                    for (int y = 0; y < Run.Height; ++y)
                        for (int x = 0; x < Run.Width; ++x)
                        {
                            int i = (y * Run.Width + x) * 4;
                            for (int channel = 0; channel < 3; ++channel)
                                Assert.AreEqual((.00005f + .00002f * Pattern(x, y, channel)) / (1 << channel), run.color[i + channel], 1e-8f);
                            Assert.AreEqual(.0001f, run.color[i + 3], 1e-8f, "Rejected history must not divide or multiply the current radiance by confidence again.");
                            Assert.AreEqual(1, run.meta[i + 3], 1e-5f);
                        }
                }
                else if (signal == Signal.RadianceEdge)
                {
                    double sum = 0;
                    for (int x = 0; x < Run.Width; ++x)
                    {
                        float value = run.color[((Run.Height / 2) * Run.Width + x) * 4]; sum += value;
                        Assert.AreEqual(x < 6 ? .1f : .9f, value, 1e-5f, "Rejected history takes the current pixel without adding a spatial prefilter.");
                        Assert.That(value, Is.InRange(.09999f, .90001f));
                    }
                    Assert.AreEqual(6 * .1 + 7 * .9, sum, 1e-4, "Local radiance edge keeps its integrated energy.");
                }
                else if (Noisy(signal))
                {
                    for (int y = 0; y < Run.Height; ++y)
                        for (int x = 0; x < Run.Width; ++x)
                        {
                            int i = (y * Run.Width + x) * 4;
                            for (int channel = 0; channel < 4; ++channel)
                            {
                                int sign = signal == Signal.NoiseScrambled ? Pattern(x, y, channel) : ((x + y + channel) % 2 == 0 ? 1 : -1);
                                float expected = channel == 3 ? .5f + .15f * sign : (.25f + .1f * sign) / (1 << channel);
                                Assert.AreEqual(expected, run.color[i + channel], 2e-5f, "An invalid history must preserve each current channel.");
                            }
                            Assert.AreEqual(1, run.meta[i + 3], 1e-5f);
                        }
                }

                else
                {
                    int i = ((Run.Height / 2) * Run.Width + Run.Width / 2) * 4;
                    float alpha = signal == Signal.Step ? .8f : signal == Signal.ZeroConfidence ? 0 : signal == Signal.LowConfidence ? .0001f : signal == Signal.InvalidCenter ? 0 : .5f;
                    Assert.AreEqual(alpha * .5f, run.color[i], 2e-5f);
                    Assert.AreEqual(alpha * .25f, run.color[i + 1], 2e-5f);
                    Assert.AreEqual(alpha * .125f, run.color[i + 2], 2e-5f);
                    Assert.AreEqual(alpha, run.color[i + 3], 2e-5f);
                    float age = signal == Signal.InvalidCenter ? 0 : signal == Signal.RejectDepth || signal == Signal.RejectNormal ? 1 : 64;
                    Assert.AreEqual(age, run.meta[i + 3], 1e-5f);
                }
            }
        }

        sealed class Run : IDisposable
        {
            public const int Width = 13, Height = 11;
            const string ShaderPath = "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/ScreenSpaceIndirectDiffuse/Compute_ScreenSpaceIndirectDiffuse.compute";
            static readonly HashSet<Run> s_InFlight = new HashSet<Run>();
            readonly List<Object> m_Objects = new List<Object>();
            readonly CommandBuffer m_Commands = new CommandBuffer();
            readonly RenderTexture m_Color, m_Meta;
            bool m_DisposeRequested, m_Released, m_SubmissionAttempted;
            readonly int m_Frames;
            public readonly Dictionary<int, float[]> sequence = new Dictionary<int, float[]>();
            public static int RetainedOwners => s_InFlight.Count;
            public int pending;
            public string error;
            public float[] color, meta;

            public Run(Signal signal)
            {
                try
                {
                    m_Frames = signal == Signal.MatureNoise || signal == Signal.NoisyStep ? 16 : 1;
                    var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
                    Assert.IsNotNull(asset);
                    var shader = Track(Object.Instantiate(asset));
                    int kernel = shader.FindKernel(Spatial(signal) ? "SSGISpatial" : "SSGITemporal");
                    Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, (float)Width / Height, .1f, 100), false);
                    float distance = signal == Signal.FarDepthEdge ? 50 : 5;
                    Vector4 plane = projection * new Vector4(0, 0, -distance, 1);
                    Vector4 other = projection * new Vector4(0, 0, -2, 1);
                    float depth = plane.z / plane.w;
                    bool edgeDepth = signal == Signal.DepthEdge || signal == Signal.NearDepthEdge || signal == Signal.FarDepthEdge, edgeNormal = signal == Signal.NormalEdge;
                    if (signal == Signal.NearDepthEdge || signal == Signal.FarDepthEdge) other = projection * new Vector4(0, 0, -distance * 1.008f, 1);
                    bool slope = signal == Signal.SlopeNoise;
                    bool slopeY = signal == Signal.SpatialSlopeConstant;
                    float Depth(int x, int y)
                    {
                        if (!slope && !slopeY) return depth;
                        Vector4 ray = projection.inverse * new Vector4((x + .5f) / Width * 2 - 1, (y + .5f) / Height * 2 - 1, depth, 1);
                        Vector3 direction = new Vector3(ray.x, ray.y, ray.z) / ray.w;
                        Vector3 point = direction * (-5 / (direction.z + (slope ? .4f * direction.x : .98f * direction.y)));
                        Vector4 clip = projection * new Vector4(point.x, point.y, point.z, 1);
                        return clip.z / clip.w;
                    }
                    var normal = new Vector3(slope ? .4f : 0, slopeY ? .98f : 0, 1).normalized;
                    Texture2D Texture(Func<int, int, Color> value)
                    {
                        var texture = Track(new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true)
                            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
                        var pixels = new Color[Width * Height];
                        for (int y = 0; y < Height; ++y)
                            for (int x = 0; x < Width; ++x) pixels[y * Width + x] = value(x, y);
                        texture.SetPixels(pixels); texture.Apply(false, false); return texture;
                    }
                    bool Center(int x, int y) => x == Width / 2 && y == Height / 2;
                    void Bind(string name, Texture texture) => m_Commands.SetComputeTextureParam(shader, kernel, name, texture);
                    Bind("SRV_DepthTexture", Texture((x, y) => new Color(signal == Signal.InvalidCenter && Center(x, y) ? (SystemInfo.usesReversedZBuffer ? 0 : 1) : edgeDepth && !Center(x, y) ? other.z / other.w : Depth(x, y), 0, 0, 0)));
                    Bind("SRV_GBufferB", Texture((x, y) => new Color(normal.x * .5f + .5f, normal.y * .5f + .5f, edgeNormal && !Center(x, y) ? 0 : normal.z * .5f + .5f, .5f)));
                    Bind("SRV_GBufferC", Texture((x, y) => new Color(64f / 255, 0, 0, 0)));
                    Bind("SRV_MotionTexture", Texture((x, y) => Color.clear));
                    Texture2D Current(int phase) => Texture((x, y) =>
                    {
                        if ((edgeDepth || edgeNormal) && !Center(x, y)) return new Color(5, 3, 2, .5f);
                        if (Spatial(signal))
                        {
                            float value = .25f + (signal == Signal.SpatialSlopeConstant ? 0 : .1f * Mathf.Sin(Mathf.PI * y / 4));
                            return new Color(value, value * .5f, value * .25f, .5f);
                        }
                        if (signal == Signal.RadianceEdge) return new Color(x < 6 ? .1f : .9f, .2f, .1f, 1);
                        if (signal == Signal.RadianceNoise) return new Color(.00005f + .00002f * Pattern(x, y, 0), .000025f + .00001f * Pattern(x, y, 1), .0000125f + .000005f * Pattern(x, y, 2), .0001f);
                        if (signal == Signal.MatureNoise || signal == Signal.NoisyStep)
                        {
                            float level = signal == Signal.NoisyStep ? .8f : .5f;
                            return new Color(level * .5f + .1f * Pattern(x, y, phase), level * .25f + .05f * Pattern(x, y, phase + 1),
                                level * .125f + .025f * Pattern(x, y, phase + 2), level + .15f * Pattern(x, y, phase + 3));
                        }
                        float alpha = signal == Signal.Step ? .8f : signal == Signal.ZeroConfidence ? 0 : signal == Signal.LowConfidence ? .0001f : .5f;
                        if (Noisy(signal))
                        {
                            int Sign(int channel) => signal == Signal.NoiseScrambled ? Pattern(x, y, channel) : ((x + y + channel) % 2 == 0 ? 1 : -1);
                            return new Color(.25f + .1f * Sign(0), .125f + .05f * Sign(1), .0625f + .025f * Sign(2), .5f + .15f * Sign(3));
                        }
                        return new Color(alpha * .5f, alpha * .25f, alpha * .125f, alpha);
                    });
                    Texture previousColor = Texture((x, y) => signal == Signal.Step || signal == Signal.NoisyStep ? new Color(.05f, .025f, .0125f, .1f) : new Color(.25f, .125f, .0625f, .5f));
                    Texture previousMeta = Texture((x, y) => new Color(signal == Signal.RejectNormal ? 1 : slope ? .4f / 1.4f : 0,
                        signal == Signal.RejectNormal ? 1 : 0, signal == Signal.RejectDepth || signal == Signal.MovingSurfaceDepth ? other.z / other.w : Depth(x, y), 64));
                    var colors = new[] { Target(), Target() };
                    var metas = new[] { Target(), Target() };
                    Bind("SRV_HitMotionMetadata",Texture((x,y)=>new Color(signal==Signal.MovingSurfaceDepth?other.z/other.w:Depth(x,y),Depth(x,y),1,0)));
                    m_Commands.SetComputeVectorParam(shader, "SSGI_Resolution", new Vector4(Width, Height, 1f / Width, 1f / Height));
                    m_Commands.SetComputeVectorParam(shader, "SSGI_TraceResolution", new Vector4(Width, Height, 1f / Width, 1f / Height));
                    m_Commands.SetComputeIntParam(shader, "SSGI_TraceScale", 1);
                    m_Commands.SetComputeMatrixParam(shader, "Matrix_Proj", projection);
                    m_Commands.SetComputeMatrixParam(shader, "Matrix_WorldToView", Matrix4x4.identity);

                    m_Commands.SetComputeFloatParam(shader, "SSGI_HistoryValid", Noisy(signal) || signal == Signal.RadianceNoise || signal == Signal.RadianceEdge ? 0 : 1);
                    m_Commands.SetComputeFloatParam(shader, "SSGI_TemporalWeight", .93f);
                    m_Commands.SetComputeFloatParam(shader, "SSGI_TemporalClampScale", 1.25f);
                    m_Commands.SetComputeVectorParam(shader, "ScreenSpaceDepthParams", new Vector4(.1f, 100, 0, SystemInfo.usesReversedZBuffer ? 1 : 0));
                    foreach (string name in new[] { "Matrix_HistoryViewProj", "Matrix_MotionViewProj", "Matrix_LastMotionViewProj" })
                        m_Commands.SetComputeMatrixParam(shader, name, projection);
                    m_Commands.SetComputeMatrixParam(shader, "Matrix_InvViewProj", projection.inverse);
                    m_Commands.SetComputeMatrixParam(shader, "Matrix_InvProj", projection.inverse);
                    for (int frame = 1; frame <= m_Frames; ++frame)
                    {
                        int target = (frame - 1) % 2;
                        if (Spatial(signal))
                        {
                            Bind("SRV_Temporal", Current(frame - 1)); Bind("UAV_Output", colors[target]);
                            Bind("SRV_HitDistance", Texture((x,y) => new Color(20,0,0,0)));
                        }
                        else
                        {
                            Bind("SRV_TraceRadiance", Current(frame - 1));
                            Bind("SRV_HistoryRadiance", previousColor); Bind("SRV_HistoryMeta", previousMeta);
                            Bind("UAV_Temporal", colors[target]); Bind("UAV_Meta", metas[target]);
                        }
                        m_Commands.DispatchCompute(shader, kernel, (Width + 7) / 8, (Height + 7) / 8, 1);
                        previousColor = colors[target]; previousMeta = metas[target];
                        if (frame == 1 || frame == 4 || frame == m_Frames)
                        {
                            int snapshot = frame;
                            m_Commands.RequestAsyncReadback(colors[target], 0, request => Complete(request, true, snapshot)); pending++;
                            if (!Spatial(signal)) { m_Commands.RequestAsyncReadback(metas[target], 0, request => Complete(request, false, snapshot)); pending++; }
                        }
                    }
                    m_Color = colors[(m_Frames - 1) % 2]; m_Meta = metas[(m_Frames - 1) % 2];
                }
                catch { pending = 0; Release(); throw; }
            }

            public void Submit()
            {
                s_InFlight.Add(this);
                m_SubmissionAttempted = true;
                // Recording failures are handled in the constructor before any submission.
                // Execute failure is uncertain; retain this owner until every real callback drains.
                try { Graphics.ExecuteCommandBuffer(m_Commands); }
                catch { m_DisposeRequested = true; if (pending == 0) Release(); throw; }
            }

            void Complete(AsyncGPUReadbackRequest request, bool isColor, int frame)
            {
                try
                {
                    if (request.hasError) throw new InvalidOperationException("Production temporal readback failed.");
                    float[] values = request.GetData<float>().ToArray();
                    if (values.Length != Width * Height * 4) throw new InvalidOperationException("Unexpected readback dimensions.");
                    if (isColor) { sequence[frame] = values; if (frame == m_Frames) color = values; }
                    else if (frame == m_Frames) meta = values;
                }
                catch (Exception exception) { error = exception.ToString(); }
                finally
                {
                    pending--;
                    if (pending == 0) { s_InFlight.Remove(this); if (m_DisposeRequested) Release(); }
                }
            }

            T Track<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; m_Objects.Add(value); return value; }
            RenderTexture Target()
            {
                var texture = Track(new RenderTexture(Width, Height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat)
                    { enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
                Assert.IsTrue(texture.Create()); return texture;
            }
            public void Dispose() { m_DisposeRequested = true; if (!m_SubmissionAttempted) pending = 0; if (pending == 0) Release(); }
            void Release()
            {
                if (m_Released) return;
                m_Released = true; m_Commands.Dispose();
                foreach (Object value in m_Objects) { if (value is RenderTexture texture) texture.Release(); Object.DestroyImmediate(value); }
            }
        }
    }
}
