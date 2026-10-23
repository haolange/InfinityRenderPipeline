using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class CubemapArraySamplingGpuTests
    {
        const float DirectionStep = 1e-5f;
        const int CasesPerMip = 110;

        [UnityTest]
        public IEnumerator ArraySampling_PreservesEdgesCornersMipBlendAndProbeAtlasIdentity()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and asynchronous readback required.");
            var inputs = new List<Vector4>();
            foreach (float mip in new[] { 0f, 2.5f, 4f })
            {
                for (int axis = 0; axis < 3; ++axis)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        var v = Vector3.zero; v[axis] = sign;
                        inputs.Add(new Vector4(v.x, v.y, v.z, mip));
                    }
                for (int zero = 0; zero < 3; ++zero)
                    for (int a = -1; a <= 1; a += 2)
                        for (int b = -1; b <= 1; b += 2)
                            foreach (float along in new[] { -.37f, 0f, .37f })
                                for (int side = -1; side <= 1; side += 2)
                            {
                                var v = Vector3.zero; v[zero] = along;
                                v[(zero + 1) % 3] = a * (1 + side * DirectionStep);
                                v[(zero + 2) % 3] = b; v.Normalize();
                                inputs.Add(new Vector4(v.x, v.y, v.z, mip));
                            }
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            for (int axis = 0; axis < 3; ++axis)
                            {
                                var v = new Vector3(x, y, z); v[axis] *= 1 + DirectionStep; v.Normalize();
                                inputs.Add(new Vector4(v.x, v.y, v.z, mip));
                            }
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            var v = new Vector3(x, y, z).normalized;
                            inputs.Add(new Vector4(v.x, v.y, v.z, mip));
                        }
            }
            foreach (bool constant in new[] { true, false })
                using (var fixture = new SamplingGpuFixture(inputs.ToArray(), constant))
                {
                    yield return fixture.Execute();
                    for (int i = 0; i < inputs.Count; ++i)
                    {
                        Vector4 sky = fixture.values[i * 3], probe = fixture.values[i * 3 + 1];
                        Vector4 hardware = fixture.values[i * 3 + 2];
                        int local = i % CasesPerMip;
                        for (int channel = 0; channel < 3; ++channel)
                        {
                            Assert.IsFalse(float.IsNaN(sky[channel]) || float.IsInfinity(sky[channel]));
                            Assert.AreEqual(sky[channel] * .37f, probe[channel], 2e-5f,
                                $"Probe base slice/intensity, sample {i}, channel {channel}");
                            // Metal's native cube uses an asymmetric vertex convention. The canonical array corner
                            // averages three faces; compare native away from corners and check corner continuity below.
                            // At the 1x1 level a noncentral edge footprint already includes a cube vertex.
                            bool nativeCornerFootprint = HasCornerFootprint(inputs[i]);
                            if (!nativeCornerFootprint || constant)
                                Assert.AreEqual(hardware[channel], sky[channel], 2e-3f,
                                    $"Native cube edge/axis, sample {i}, channel {channel}");
                            if (constant) Assert.AreEqual(new[] { .25f, .5f, .75f }[channel], sky[channel], 2e-6f);
                        }
                    }
                    if (!constant)
                        for (int i = 0; i < inputs.Count; ++i)
                            if (inputs[i].w == 4)
                            {
                                Vector3 expected = OneTexelBilinear(inputs[i]);
                                for (int channel = 0; channel < 3; ++channel)
                                    Assert.AreEqual(expected[channel], fixture.values[i * 3][channel], 2e-5f, "Complete 1x1 footprint.");
                            }
                    if (!constant)
                        for (int i = 0; i < inputs.Count; ++i)
                            if (i % CasesPerMip >= 102)
                            {
                                var direction = new Vector3(inputs[i].x, inputs[i].y, inputs[i].z);
                                int low = Mathf.FloorToInt(inputs[i].w), high = Mathf.CeilToInt(inputs[i].w);
                                Vector3 expected = Vector3.Lerp(CornerMean(direction, low), CornerMean(direction, high), inputs[i].w - low);
                                for (int channel = 0; channel < 3; ++channel)
                                    Assert.AreEqual(expected[channel], fixture.values[i * 3][channel], 2e-5f, "Symmetric three-face vertex mean.");
                            }
                    for (int mip = 0; mip < 3; ++mip)
                    {
                        int offset = mip * CasesPerMip;
                        for (int edge = 6; edge < 78; edge += 2)
                            AssertContinuous(fixture.values[(offset + edge) * 3], fixture.values[(offset + edge + 1) * 3]);
                        for (int corner = 78; corner < 102; corner += 3)
                        {
                            AssertContinuous(fixture.values[(offset + corner) * 3], fixture.values[(offset + corner + 1) * 3]);
                            AssertContinuous(fixture.values[(offset + corner) * 3], fixture.values[(offset + corner + 2) * 3]);
                        }
                    }
                }
        }

        [UnityTest]
        public IEnumerator ArrayPrefilter_MatchesNativeCubeWithTheSameMipZeroInput()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and asynchronous readback required.");
            foreach (bool constant in new[] { true, false })
                foreach (float roughness in new[] { 0f, .35f, 1f })
                    using (var fixture = new SamplingGpuFixture(new[] { Vector4.one }, constant))
                    {
                        yield return fixture.ExecuteFilter(roughness);
                        Assert.AreEqual(fixture.filteredCube.Length, fixture.filteredArray.Length);
                        for (int i = 0; i < fixture.filteredArray.Length; ++i)
                            for (int channel = 0; channel < 3; ++channel)
                            {
                                float value = fixture.filteredArray[i][channel];
                                Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value));
                                Assert.AreEqual(fixture.filteredCube[i][channel], value, 2e-3f,
                                    $"Actual array prefilter: roughness {roughness}, texel {i}, channel {channel}");
                                if (constant) Assert.AreEqual(new[] { .25f, .5f, .75f }[channel], value, 2e-6f);
                            }
                    }
        }

        static void FaceUv(Vector3 d, out int face, out Vector2 uv)
        {
            Vector3 a = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            Vector2 st;
            if (a.x >= a.y && a.x >= a.z)
            {
                face = d.x > 0 ? 0 : 1;
                st = new Vector2(d.x > 0 ? -d.z : d.z, -d.y) / a.x;
            }
            else if (a.y >= a.z)
            {
                face = d.y > 0 ? 2 : 3;
                st = new Vector2(d.x, d.y > 0 ? d.z : -d.z) / a.y;
            }
            else
            {
                face = d.z > 0 ? 4 : 5;
                st = new Vector2(d.z > 0 ? d.x : -d.x, -d.y) / a.z;
            }
            uv = st * .5f + Vector2.one * .5f;
        }

        static bool HasCornerFootprint(Vector4 sample)
        {
            FaceUv(new Vector3(sample.x, sample.y, sample.z), out _, out Vector2 uv);
            int low = Mathf.FloorToInt(sample.w), high = Mathf.CeilToInt(sample.w);
            for (int mip = low; mip <= high; ++mip)
            {
                int size = Math.Max(1, 16 >> mip);
                Vector2 pos = uv * size - Vector2.one * .5f;
                int x = Mathf.FloorToInt(pos.x), y = Mathf.FloorToInt(pos.y);
                float u = pos.x - x, v = pos.y - y;
                for (int dy = 0; dy < 2; ++dy)
                    for (int dx = 0; dx < 2; ++dx)
                        if ((dx == 0 ? 1 - u : u) * (dy == 0 ? 1 - v : v) > 0
                            && (x + dx < 0 || x + dx >= size) && (y + dy < 0 || y + dy >= size)) return true;
            }
            return false;
        }

        static Vector3 OneTexelBilinear(Vector4 sample)
        {
            Vector3 d = new Vector3(sample.x, sample.y, sample.z);
            FaceUv(d, out int face, out Vector2 uv);
            int horizontal = face < 2 ? (d.z > 0 ? 4 : 5) : (d.x > 0 ? 0 : 1);
            int vertical = face >= 2 && face < 4 ? (d.z > 0 ? 4 : 5) : (d.y > 0 ? 2 : 3);
            Func<int, Vector3> color = f => new Vector3(.09f * (f + 1) + .065f + .08f,
                .07f * (6 - f) + .085f, .03f * f + .055f);
            Vector3 a = color(face), b = color(horizontal), c = color(vertical);
            float u = Mathf.Abs(uv.x - .5f), v = Mathf.Abs(uv.y - .5f);
            return a * ((1 - u) * (1 - v)) + b * (u * (1 - v)) + c * ((1 - u) * v)
                + (a + b + c) / 3 * (u * v);
        }

        static Vector3 CornerMean(Vector3 direction, int mip)
        {
            int[] faces = { direction.x > 0 ? 0 : 1, direction.y > 0 ? 2 : 3, direction.z > 0 ? 4 : 5 };
            Vector3 result = Vector3.zero;
            float halfTexel = .5f / Math.Max(1, 16 >> mip);
            foreach (int face in faces)
            {
                Vector2 st;
                switch (face)
                {
                    case 0: st = new Vector2(-direction.z, -direction.y); break;
                    case 1: st = new Vector2(direction.z, -direction.y); break;
                    case 2: st = new Vector2(direction.x, direction.z); break;
                    case 3: st = new Vector2(direction.x, -direction.z); break;
                    case 4: st = new Vector2(direction.x, -direction.y); break;
                    default: st = new Vector2(-direction.x, -direction.y); break;
                }
                float u = st.x > 0 ? 1 - halfTexel : halfTexel;
                float v = st.y > 0 ? 1 - halfTexel : halfTexel;
                result += new Vector3(.09f * (face + 1) + .13f * u + mip * .02f,
                    .07f * (6 - face) + .17f * v, .03f * face + .11f * (u + 2 * v) / 3);
            }
            return result / 3;
        }

        static void AssertContinuous(Vector4 a, Vector4 b)
        {
            for (int channel = 0; channel < 3; ++channel)
                Assert.AreEqual(a[channel], b[channel], 5e-4f, "Opposite face limits must converge.");
        }

        // Queued readback owns every resource until its terminal callback, including on timeout or uncertain submit.
        sealed class SamplingGpuFixture : IDisposable
        {
            static readonly HashSet<SamplingGpuFixture> s_Retained = new HashSet<SamplingGpuFixture>();
            ComputeShader m_Shader, m_FilterShader;
            RenderTexture m_FilterCube, m_FilterArray;
            int m_FilterPending;
            public Vector4[] filteredCube { get; private set; }
            public Vector4[] filteredArray { get; private set; }
            ComputeBuffer m_Samples, m_Output;
            Cubemap m_Cube;
            Texture2DArray m_Array, m_Atlas;
            readonly int m_Count;
            bool m_Pending, m_SubmissionAttempted, m_DisposeRequested, m_Released;
            string m_Error;
            public Vector4[] values { get; private set; }

            public SamplingGpuFixture(Vector4[] samples, bool constant)
            {
                m_Count = samples.Length;
                try
                {
                    m_Shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                        "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_CubemapArraySamplingContract.compute"));
                    m_Cube = new Cubemap(16, TextureFormat.RGBAFloat, true) { filterMode = FilterMode.Trilinear };
                    m_Array = new Texture2DArray(16, 16, 6, TextureFormat.RGBAFloat, true, true);
                    m_Atlas = new Texture2DArray(16, 16, 12, TextureFormat.RGBAFloat, true, true);
                    m_Array.filterMode = m_Atlas.filterMode = FilterMode.Trilinear;
                    m_Cube.wrapMode = m_Array.wrapMode = m_Atlas.wrapMode = TextureWrapMode.Clamp;
                    for (int mip = 0; mip < 5; ++mip)
                        for (int face = 0; face < 6; ++face)
                        {
                            int size = Math.Max(1, 16 >> mip);
                            var colors = new Color[size * size];
                            for (int y = 0; y < size; ++y)
                                for (int x = 0; x < size; ++x)
                                    colors[y * size + x] = constant ? new Color(.25f, .5f, .75f, 1) : new Color(
                                        .09f * (face + 1) + .13f * (x + .5f) / size + mip * .02f,
                                        .07f * (6 - face) + .17f * (y + .5f) / size,
                                        .03f * face + .11f * (x + 2 * y + 1.5f) / (3 * size), 1);
                            m_Cube.SetPixels(colors, (CubemapFace)face, mip);
                            m_Array.SetPixels(colors, face, mip);
                            m_Atlas.SetPixels(colors, face + 6, mip);
                            m_Atlas.SetPixels(Enumerable.Repeat(new Color(8, 4, 2, 1), size * size).ToArray(), face, mip);
                        }
                    m_Cube.Apply(false); m_Array.Apply(false); m_Atlas.Apply(false);
                    m_Samples = new ComputeBuffer(m_Count, 16); m_Samples.SetData(samples);
                    m_Output = new ComputeBuffer(m_Count * 3, 16);
                }
                catch { Dispose(); throw; }
            }

            public IEnumerator Execute()
            {
                using (var commands = new CommandBuffer())
                {
                    int kernel = m_Shader.FindKernel("Compare");
                    commands.SetComputeIntParam(m_Shader, "CaseCount", m_Count);
                    commands.SetComputeTextureParam(m_Shader, kernel, "TestArray", m_Array);
                    commands.SetComputeTextureParam(m_Shader, kernel, "TestCube", m_Cube);
                    commands.SetComputeTextureParam(m_Shader, kernel, "SRV_ReflectionProbeAtlas", m_Atlas);
                    commands.SetComputeBufferParam(m_Shader, kernel, "Samples", m_Samples);
                    commands.SetComputeBufferParam(m_Shader, kernel, "Results", m_Output);
                    commands.DispatchCompute(m_Shader, kernel, m_Count, 1, 1);
                    commands.RequestAsyncReadback(m_Output, request =>
                    {
                        try
                        {
                            if (request.hasError) throw new InvalidOperationException("Cubemap sampling readback failed.");
                            values = request.GetData<Vector4>().ToArray();
                        }
                        catch (Exception error) { m_Error = error.ToString(); }
                        finally { m_Pending = false; TryRelease(); }
                    });
                    m_Pending = true; m_SubmissionAttempted = true;
                    Graphics.ExecuteCommandBuffer(commands);
                }
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (m_Pending && EditorApplication.timeSinceStartup < deadline)
                {
                    EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                }
                Assert.IsFalse(m_Pending, "Cubemap readback timed out; owner retained until callback drain.");
                Assert.IsNull(m_Error, m_Error);
                Assert.IsNotNull(values);
                Assert.AreEqual(m_Count * 3, values.Length);
            }

            public IEnumerator ExecuteFilter(float roughness)
            {
                m_FilterShader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/CubeFilter/Compute_CubeFilter.compute"));
                var descriptor = new RenderTextureDescriptor(8, 8,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat, 0)
                {
                    dimension = TextureDimension.Tex2DArray, volumeDepth = 6, mipCount = 1, enableRandomWrite = true
                };
                m_FilterCube = new RenderTexture(descriptor);
                if (!m_FilterCube.Create()) throw new InvalidOperationException("Cube prefilter target creation failed.");
                m_FilterArray = new RenderTexture(descriptor);
                if (!m_FilterArray.Create()) throw new InvalidOperationException("Array prefilter target creation failed.");
                using (var commands = new CommandBuffer())
                {
                    commands.SetComputeVectorParam(m_FilterShader, "CubeFilter_Params", new Vector4(8, 0, roughness, 0));
                    // Both kernels see exactly source mip zero, so their only difference is the sampling surface.
                    commands.SetComputeVectorParam(m_FilterShader, "CubeFilter_Source", new Vector4(16, 1, 1, 64));
                    int cubeKernel = m_FilterShader.FindKernel("FilterCubeToArray");
                    int arrayKernel = m_FilterShader.FindKernel("FilterArrayToArray");
                    commands.SetComputeTextureParam(m_FilterShader, cubeKernel, "SRV_CubeSource", m_Cube);
                    commands.SetComputeTextureParam(m_FilterShader, cubeKernel, "UAV_CubeArray", m_FilterCube);
                    commands.DispatchCompute(m_FilterShader, cubeKernel, 1, 1, 6);
                    commands.SetComputeTextureParam(m_FilterShader, arrayKernel, "SRV_CubeArraySource", m_Array);
                    commands.SetComputeTextureParam(m_FilterShader, arrayKernel, "UAV_CubeArray", m_FilterArray);
                    commands.DispatchCompute(m_FilterShader, arrayKernel, 1, 1, 6);
                    commands.RequestAsyncReadback(m_FilterCube, 0, r => CompleteFilter(r, true));
                    commands.RequestAsyncReadback(m_FilterArray, 0, r => CompleteFilter(r, false));
                    m_FilterPending = 2; m_Pending = true; m_SubmissionAttempted = true;
                    Graphics.ExecuteCommandBuffer(commands);
                }
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (m_Pending && EditorApplication.timeSinceStartup < deadline)
                {
                    EditorApplication.QueuePlayerLoopUpdate(); yield return null;
                }
                Assert.IsFalse(m_Pending, "Prefilter readback timed out; owner retained until both callbacks drain.");
                Assert.IsNull(m_Error, m_Error);
                Assert.IsNotNull(filteredCube); Assert.IsNotNull(filteredArray);
                Assert.AreEqual(8 * 8 * 6, filteredArray.Length);
            }

            void CompleteFilter(AsyncGPUReadbackRequest request, bool cube)
            {
                try
                {
                    if (request.hasError) throw new InvalidOperationException("Prefilter readback failed.");
                    if (request.layerCount != 6) throw new InvalidOperationException("Prefilter must return all six faces.");
                    var faces = new Vector4[8 * 8 * 6];
                    for (int layer = 0; layer < request.layerCount; ++layer)
                    {
                        var data = request.GetData<Vector4>(layer);
                        if (data.Length != 8 * 8) throw new InvalidOperationException("Unexpected prefilter face size.");
                        for (int i = 0; i < data.Length; ++i) faces[layer * 8 * 8 + i] = data[i];
                    }
                    if (cube) filteredCube = faces;
                    else filteredArray = faces;
                }
                catch (Exception error) { m_Error = error.ToString(); }
                finally { m_Pending = --m_FilterPending > 0; TryRelease(); }
            }

            public void Dispose()
            {
                m_DisposeRequested = true;
                if (m_Pending && !m_SubmissionAttempted) m_Pending = false;
                if (m_Pending) s_Retained.Add(this);
                TryRelease();
            }

            void TryRelease()
            {
                if (!m_DisposeRequested || m_Pending || m_Released) return;
                m_Released = true; s_Retained.Remove(this);
                m_Output?.Dispose(); m_Samples?.Dispose();
                if (m_FilterCube) { m_FilterCube.Release(); Object.DestroyImmediate(m_FilterCube); }
                if (m_FilterArray) { m_FilterArray.Release(); Object.DestroyImmediate(m_FilterArray); }
                Object.DestroyImmediate(m_FilterShader);
                Object.DestroyImmediate(m_Cube); Object.DestroyImmediate(m_Array); Object.DestroyImmediate(m_Atlas);
                Object.DestroyImmediate(m_Shader);
            }
        }
    }
}
