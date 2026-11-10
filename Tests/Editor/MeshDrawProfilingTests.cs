using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using InfinityTech.Rendering;
using InfinityTech.Rendering.MeshPipeline;
using InfinityTech.Rendering.MeshPipeline.Tests;
using InfinityTech.Rendering.Pipeline;

namespace InfinityTech.Rendering.Tests
{
    public class MeshDrawProfilingTests
    {
        [TestCase(EMeshViewKind.Main, "RenderLoop")]
        [TestCase(EMeshViewKind.SceneView, "RenderLoop")]
        [TestCase(EMeshViewKind.Preview, "RenderLoop")]
        [TestCase(EMeshViewKind.CascadeShadow, "Shadows")]
        [TestCase(EMeshViewKind.LocalShadow, "Shadows")]
        public void ViewDomain_SelectsDrawNames(EMeshViewKind kind, string domain)
        {
            Assert.AreEqual(domain + ".DrawMeshDirect", MeshDrawProfiling.Draw(kind, false));
            Assert.AreEqual(domain + ".DrawMeshIndirect", MeshDrawProfiling.Draw(kind, true));
        }

        [Test]
        public void Timings_RecordsEveryMeshPassAndResets()
        {
            var timings = new InfinityDebugDisplaySettingsTimings();
            try
            {
                Assert.AreEqual(3, MeshDrawProfiling.passes.Count);
                CollectionAssert.AreEquivalent(new[] { "ComputeMeshCulling", "ComputeMeshCompaction", "UploadMeshData" },
                    MeshDrawProfiling.passes.Select(s => s.name));
                CollectionAssert.AreEquivalent(new[] { MeshDrawProfiling.culling, MeshDrawProfiling.compaction, MeshDrawProfiling.upload },
                    MeshDrawProfiling.passes);
                Assert.AreEqual(3, MeshDrawProfiling.passes.Distinct().Count());
                timings.recordPassTimings = true;
                Assert.IsTrue(timings.recordPassTimings);
            }
            finally { timings.Reset(); }
            foreach (var sampler in MeshDrawProfiling.passes) Assert.AreEqual("-", timings.Describe(sampler));
        }

        [Test]
        public void Preparation_IsNotWrappedInTheConsumerSampler()
        {
            string source = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/Mesh/Graph/MeshGraphExtension.cs");
            int start = source.IndexOf("void PrepareMeshDrawList", StringComparison.Ordinal);
            int end = source.IndexOf("RGBufferHandle[] RecordPreparation", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0);
            Assert.Greater(end, start);
            string method = source.Substring(start, end - start);
            Assert.IsFalse(method.Contains("BeginProfileRange"));
            Assert.IsFalse(method.Contains("EndProfileRange"));
        }

        [Test]
        public void ConsumerNames_FollowTheViewAndPass()
        {
            MeshView depth = MeshTestUtility.CameraView(1);
            depth.visibilityPhase = EMeshVisibilityPhase.Prediction;
            Assert.AreEqual("DepthPrediction", MeshDrawProfiling.Consumer(depth, "RasterDepthPass"));
            depth.visibilityPhase = EMeshVisibilityPhase.Recovered;
            Assert.AreEqual("DepthRecovery", MeshDrawProfiling.Consumer(depth, "RasterDepthPass"));
            depth.visibilityPhase = EMeshVisibilityPhase.Final;
            Assert.AreEqual("GBuffer", MeshDrawProfiling.Consumer(depth, "RasterGBufferPass"));
            MeshView shadow = MeshView.FromShadow(EMeshViewKind.CascadeShadow, 1, depth.CopyPlanes(), depth.viewPosition, ~0,
                (uint)ERenderingLayer.Everything, 2, ulong.MaxValue, MeshTestUtility.Lod);
            Assert.AreEqual("CascadeShadow.Slice2", MeshDrawProfiling.Consumer(shadow, "RasterShadowPass"));
            Assert.AreEqual("DepthAndShadows", MeshDrawProfiling.FrustumGroup(true, true));
            Assert.AreEqual("Shadows", MeshDrawProfiling.FrustumGroup(false, true));
            Assert.AreEqual("Depth", MeshDrawProfiling.FrustumGroup(true, false));
            Assert.AreEqual("Slice2", MeshDrawProfiling.Slice(EMeshViewKind.CascadeShadow, 2));
        }

        [Test]
        public void RendererListSubmission_DoesNotAddAWrapperScope()
        {
            string source = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/RenderGraph/RGEncoder.cs");
            Assert.IsFalse(source.Contains("RasterRendererList"));
            Assert.IsTrue(source.Contains("m_CommandBuffer.DrawRendererList(rendererList);"));
            Assert.IsTrue(source.Contains("m_RendererLists?.Submit(m_CommandBuffer, rendererList);"));
        }
    }
}
