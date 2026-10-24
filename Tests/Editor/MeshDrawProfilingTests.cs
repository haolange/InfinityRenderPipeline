using System.IO;
using System.Linq;
using NUnit.Framework;
using InfinityTech.Rendering.MeshPipeline;
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
        public void RendererListSubmission_DoesNotAddAWrapperScope()
        {
            string source = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/RendererCore/RenderGraph/RGEncoder.cs");
            Assert.IsFalse(source.Contains("RasterRendererList"));
            Assert.IsTrue(source.Contains("m_CommandBuffer.DrawRendererList(rendererList);"));
            Assert.IsTrue(source.Contains("m_RendererLists?.Submit(m_CommandBuffer, rendererList);"));
        }
    }
}
