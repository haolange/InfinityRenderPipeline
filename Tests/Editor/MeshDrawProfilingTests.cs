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
        [Test]
        public void PassDomain_SelectsDrawNames()
        {
            foreach (MeshPassDesc pass in MeshPassTable.all)
            {
                string domain = pass.lightMode == "RasterShadowPass" ? "Shadows" : "RenderLoop";
                Assert.AreEqual(domain + ".DrawMeshDirect", MeshDrawProfiling.DrawSampler(pass, false).name, pass.lightMode);
                Assert.AreEqual(domain + ".DrawMeshIndirect", MeshDrawProfiling.DrawSampler(pass, true).name, pass.lightMode);
            }
        }

        [Test]
        public void Timings_RecordsEveryMeshPassAndResets()
        {
            var timings = new InfinityDebugDisplaySettingsTimings();
            try
            {
                CollectionAssert.AreEquivalent(new[] { "ComputeMeshDrawList", "UploadMeshData" }, MeshDrawProfiling.passes.Select(s => s.name));
                CollectionAssert.AreEquivalent(new[] { MeshDrawProfiling.production, MeshDrawProfiling.upload }, MeshDrawProfiling.passes);
                Assert.AreEqual(2, MeshDrawProfiling.passes.Distinct().Count());
                timings.recordPassTimings = true;
                Assert.IsTrue(timings.recordPassTimings);
            }
            finally { timings.Reset(); }
            foreach (var sampler in MeshDrawProfiling.passes) Assert.AreEqual("-", timings.Describe(sampler));
        }

        [Test]
        public void Production_IsNotWrappedInTheConsumerSampler()
        {
            string source = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/Mesh/Graph/MeshGraphExtension.cs");
            int start = source.IndexOf("internal RGMeshDrawListHandle Create(", StringComparison.Ordinal);
            int end = source.IndexOf("internal void UseDrawList", StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0);
            Assert.Greater(end, start);
            string method = source.Substring(start, end - start);
            Assert.IsFalse(method.Contains("BeginProfileRange"));
            Assert.IsFalse(method.Contains("EndProfileRange"));
        }

        [Test]
        public void ConsumerNames_FollowThePass()
        {
            Assert.AreEqual("Depth", MeshDrawProfiling.PassSampler(MeshPassTable.Depth).name);
            Assert.AreEqual("GBuffer", MeshDrawProfiling.PassSampler(MeshPassTable.GBuffer).name);
            Assert.AreEqual("Shadow", MeshDrawProfiling.PassSampler(MeshPassTable.Shadow).name);
            Assert.AreEqual("TranslucentT1", MeshDrawProfiling.PassSampler(MeshPassTable.TranslucentT1).name);
            Assert.AreEqual("Slice2", MeshDrawProfiling.SliceSampler(2).name);
        }

        [Test]
        public void RendererListSubmission_DoesNotAddAWrapperScope()
        {
            string source = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/RenderGraph/RGEncoder.cs");
            Assert.IsFalse(source.Contains("RasterRendererList"));
            Assert.IsTrue(source.Contains("m_Core.commands.DrawRendererList(rendererList);"));
            Assert.IsTrue(source.Contains(".Submit(m_Core.commands, rendererList);"));
        }
    }
}
