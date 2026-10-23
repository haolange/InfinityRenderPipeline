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
    public class DBufferCompositeGpuTests
    {
        [UnityTest]
        public IEnumerator PremultipliedCoverage_PreservesZeroFractionalAndOverlappingDecals()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Compute and asynchronous readback required.");
            const int count = 5;
            var a = new Color[count]; var b = new Color[count]; var c = new Color[count];
            var expected = new Vector4[count * 2];
            var baseColor = new Vector3(.18f, .4f, .7f);
            var firstColor = new Vector3(.8f, .1f, .3f);
            var secondColor = new Vector3(.1f, .7f, .2f);
            var firstNormal = new Vector3(.6f, .8f, 0);
            var secondNormal = new Vector3(0, .8f, .6f);
            for (int i = 0; i < count; ++i)
            {
                // Straight attributes accumulated with the producer's independent over equation.
                float coverage = new[] { 0, .5f, 1, .25f, .5f }[i];
                Vector3 color = firstColor * coverage;
                Vector3 encoded = (firstNormal * .5f + Vector3.one * .5f) * coverage;
                float rough = .2f * coverage, materialG = .7f * coverage;
                if (i == 4)
                {
                    const float topAlpha = .4f;
                    color = secondColor * topAlpha + color * (1 - topAlpha);
                    encoded = (secondNormal * .5f + Vector3.one * .5f) * topAlpha + encoded * (1 - topAlpha);
                    rough = .6f * topAlpha + rough * (1 - topAlpha);
                    materialG = .3f * topAlpha + materialG * (1 - topAlpha);
                    coverage = topAlpha + coverage * (1 - topAlpha);
                }
                a[i] = new Color(color.x, color.y, color.z, coverage);
                b[i] = new Color(encoded.x, encoded.y, encoded.z, coverage);
                c[i] = new Color(rough, materialG, 0, coverage);
                Vector3 outColor = color + baseColor * (1 - coverage);
                Vector3 outNormal = Vector3.up;
                if (coverage > 0)
                {
                    Vector3 decalNormal = (encoded / coverage * 2 - Vector3.one).normalized;
                    outNormal = ((1 - coverage) * Vector3.up + coverage * decalNormal).normalized;
                }
                expected[i * 2] = new Vector4(outColor.x, outColor.y, outColor.z, rough + .8f * (1 - coverage));
                expected[i * 2 + 1] = new Vector4(outNormal.x, outNormal.y, outNormal.z, materialG + .15f * (1 - coverage));
            }
            using (var fixture = new CompositeGpuFixture(a, b, c))
            {
                yield return fixture.Execute();
                for (int i = 0; i < expected.Length; ++i)
                    for (int channel = 0; channel < 4; ++channel)
                        Assert.AreEqual(expected[i][channel], fixture.values[i][channel], 2e-5f,
                            $"case {i / 2}, output {i % 2}, channel {channel}");
            }
        }

        // Same queue/readback ownership pattern as ColorOutputGpuTests.PresentGpuFixture.
        sealed class CompositeGpuFixture : IDisposable
        {
            static readonly HashSet<CompositeGpuFixture> s_Retained = new HashSet<CompositeGpuFixture>();
            ComputeShader m_Shader;
            ComputeBuffer m_Output;
            Texture2D m_A, m_B, m_C;
            bool m_Pending, m_SubmissionAttempted, m_DisposeRequested, m_Released;
            string m_Error;
            public Vector4[] values { get; private set; }

            public CompositeGpuFixture(Color[] a, Color[] b, Color[] c)
            {
                try
                {
                    var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                        "Packages/com.infinity.render-pipeline/Tests/Editor/Shaders/Compute_DBufferContract.compute");
                    Assert.IsNotNull(source);
                    m_Shader = Object.Instantiate(source);
                    m_A = Texture(a); m_B = Texture(b); m_C = Texture(c);
                    m_Output = new ComputeBuffer(a.Length * 2, 16);
                }
                catch { Dispose(); throw; }
            }

            public IEnumerator Execute()
            {
                using (var commands = new CommandBuffer())
                {
                    int kernel = m_Shader.FindKernel("Composite");
                    commands.SetComputeIntParam(m_Shader, "CaseCount", m_A.width);
                    commands.SetComputeTextureParam(m_Shader, kernel, "_DBufferTextureA", m_A);
                    commands.SetComputeTextureParam(m_Shader, kernel, "_DBufferTextureB", m_B);
                    commands.SetComputeTextureParam(m_Shader, kernel, "_DBufferTextureC", m_C);
                    commands.SetComputeBufferParam(m_Shader, kernel, "Results", m_Output);
                    commands.DispatchCompute(m_Shader, kernel, m_A.width, 1, 1);
                    // Recording failure has no queued work; an uncertain submission remains owned.
                    commands.RequestAsyncReadback(m_Output, request =>
                    {
                        try
                        {
                            if (request.hasError) throw new InvalidOperationException("DBuffer GPU readback failed.");
                            values = request.GetData<Vector4>().ToArray();
                        }
                        catch (Exception error) { m_Error = error.ToString(); }
                        finally { m_Pending = false; TryRelease(); }
                    });
                    m_Pending = true;
                    m_SubmissionAttempted = true;
                    Graphics.ExecuteCommandBuffer(commands);
                }
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (m_Pending && EditorApplication.timeSinceStartup < deadline)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.IsFalse(m_Pending, "DBuffer readback timed out; its owner remains retained until callback drain.");
                Assert.IsNull(m_Error, m_Error);
                Assert.IsNotNull(values);
                Assert.AreEqual(m_A.width * 2, values.Length);
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
                m_Output?.Dispose();
                Object.DestroyImmediate(m_A); Object.DestroyImmediate(m_B); Object.DestroyImmediate(m_C);
                Object.DestroyImmediate(m_Shader);
            }
        }

        static Texture2D Texture(Color[] colors)
        {
            var texture = new Texture2D(colors.Length, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                texture.SetPixels(colors); texture.Apply(false); return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
        }
    }
}
