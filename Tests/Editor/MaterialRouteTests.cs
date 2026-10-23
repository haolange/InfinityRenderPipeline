using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MaterialRouteTests
    {
        [Test]
        public void RayTracingSampler_UsesActualBackendAndRejectsUnknownBackend()
        {
            Assert.AreEqual(CustomSamplerId.ComputeRayTracedOcclusion,
                InfinityRayTracingEnvironment.OcclusionSampler(UnityEngine.Rendering.UnifiedRayTracing.RayTracingBackend.Compute));
            Assert.AreEqual(CustomSamplerId.RayTracingOcclusion,
                InfinityRayTracingEnvironment.OcclusionSampler(UnityEngine.Rendering.UnifiedRayTracing.RayTracingBackend.Hardware));
            Assert.Throws<ArgumentOutOfRangeException>(() => InfinityRayTracingEnvironment.OcclusionSampler(
                (UnityEngine.Rendering.UnifiedRayTracing.RayTracingBackend)255));
            using (var env = new InfinityRayTracingEnvironment())
                Assert.Throws<InvalidOperationException>(() => { var backend = env.Backend; });
        }

        Material m_Material;

        [SetUp]
        public void SetUp()
        {
            Shader shader = Shader.Find("InfinityPipeline/InfinityLit");
            Assert.NotNull(shader);
            m_Material = new Material(shader);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(m_Material);

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(0, 3)]
        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(1, 3)]
        public void ExplicitUpdate_SelectsShadingRouteAndOpaqueSurfacePrepass_AndIsIdempotent(int route, int stage)
        {
            m_Material.SetFloat("_SurfaceRoute", route);
            m_Material.SetFloat("_TranslucentStage", stage);
            MaterialRouteUtility.ApplyPassState(m_Material);
            string expected = stage == 0 ? (route == 0 ? "RasterGBufferPass" : "RasterForwardPass") : $"RasterTranslucentT{stage - 1}Pass";
            int enabled = 0;
            foreach (string pass in new[] { "RasterGBufferPass", "RasterForwardPass", "RasterTranslucentT0Pass", "RasterTranslucentT1Pass", "RasterTranslucentT2Pass" })
            {
                Assert.GreaterOrEqual(m_Material.FindPass(pass), 0);
                Assert.AreEqual(pass == expected || (stage == 0 && pass == "RasterGBufferPass"), m_Material.GetShaderPassEnabled(pass), pass);
                if (m_Material.GetShaderPassEnabled(pass)) ++enabled;
            }
            Assert.AreEqual(stage == 0 && route == 1 ? 2 : 1, enabled);
            Assert.AreEqual(stage == 0, m_Material.GetShaderPassEnabled("RasterDepthPass"));
            Assert.AreEqual(stage != 0, m_Material.GetShaderPassEnabled("RasterTranslucentDepthPass"));
            string before = EditorJsonUtility.ToJson(m_Material);
            MaterialRouteUtility.ApplyPassState(m_Material);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(m_Material));
        }

        [TestCase("_SurfaceRoute", -1f)]
        [TestCase("_SurfaceRoute", 0.5f)]
        [TestCase("_SurfaceRoute", 2f)]
        [TestCase("_TranslucentStage", 4f)]
        [TestCase("_TranslucentStage", float.NaN)]
        [TestCase("_TranslucentStage", float.PositiveInfinity)]
        public void InvalidRoute_IsRejectedBeforePassMutation(string property, float value)
        {
            m_Material.SetFloat(property, value);
            string before = EditorJsonUtility.ToJson(m_Material);
            Assert.Throws<InvalidOperationException>(() => MaterialRouteUtility.ApplyPassState(m_Material));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(m_Material));
        }

        [TestCase(1, 0)]
        [TestCase(0, 1)]
        public void Subsurface_RejectsNonDeferredSurface(int route, int stage)
        {
            m_Material.SetFloat("_SurfaceRoute", route);
            m_Material.SetFloat("_TranslucentStage", stage);
            m_Material.SetFloat("_Subsurface", 1);
            Assert.Throws<InvalidOperationException>(() => MaterialRouteUtility.ApplyPassState(m_Material));
        }

        [Test]
        public void EditorValidation_DoesNotRewriteSerializedMaterialState()
        {
            m_Material.SetFloat("_SurfaceRoute", 1);
            m_Material.SetShaderPassEnabled("RasterGBufferPass", true);
            m_Material.SetShaderPassEnabled("RasterForwardPass", false);
            string before = EditorJsonUtility.ToJson(m_Material);
            new InfinityLitGUI().ValidateMaterial(m_Material);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(m_Material));
        }
    }
}
