using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            foreach (MeshPassDesc pass in InfinityMeshPasses.all)
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
                CollectionAssert.AreEquivalent(new[] { "ComputeMeshView", "ComputeMeshDrawList", "UploadMeshData" }, MeshDrawProfiling.passes.Select(s => s.name));
                CollectionAssert.AreEquivalent(new[] { MeshDrawProfiling.viewProduction, MeshDrawProfiling.production, MeshDrawProfiling.upload }, MeshDrawProfiling.passes);
                Assert.AreEqual(3, MeshDrawProfiling.passes.Distinct().Count());
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
            int start = source.IndexOf("internal RGMeshDrawListHandle CreateDrawList(", StringComparison.Ordinal);
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
            Assert.AreEqual("Depth", MeshDrawProfiling.PassSampler(InfinityMeshPasses.all[InfinityMeshPasses.Depth.index]).name);
            Assert.AreEqual("GBuffer", MeshDrawProfiling.PassSampler(InfinityMeshPasses.all[InfinityMeshPasses.GBuffer.index]).name);
            Assert.AreEqual("Shadow", MeshDrawProfiling.PassSampler(InfinityMeshPasses.all[InfinityMeshPasses.Shadow.index]).name);
            Assert.AreEqual("TranslucentT1", MeshDrawProfiling.PassSampler(InfinityMeshPasses.all[InfinityMeshPasses.TranslucentT1.index]).name);
            Assert.AreEqual("Slice2", MeshDrawProfiling.SliceSampler(2).name);
        }

        [Test]
        public void ProducerScopes_NameTheirKernels()
        {
            // The kernels come from the compute shader's own declarations and the backend's resolution, not from the samplers.
            string shader = File.ReadAllText("Packages/com.infinity.render-pipeline/Shaders/Mesh/Compute_MeshDrawPipeline.compute");
            string backend = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/Mesh/Backend/MeshDrawGPUBackend.cs");
            string[] kernels = Regex.Matches(shader, @"^\s*#pragma\s+kernel\s+(\w+)", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            foreach (var scope in new[] { MeshDrawProfiling.initializeIndirectArgs, MeshDrawProfiling.buildDrawList })
            {
                CollectionAssert.Contains(kernels, scope.name, "A list producer's in-pass scope names the kernel it dispatches.");
                StringAssert.Contains($"ComputeKernel.Resolve(shader, field, \"{scope.name}\")", backend);
            }
            // The view producer's scope names the family of view kernels: each kernel the view can choose starts with it.
            string[] viewKernels = kernels.Where(kernel => kernel.StartsWith(MeshDrawProfiling.cullMeshView.name, StringComparison.Ordinal)).ToArray();
            CollectionAssert.Contains(viewKernels, MeshDrawProfiling.cullMeshView.name);
            string[] resolvedViewKernels = Regex.Matches(backend, @"m_CullView\w*Kernel = ComputeKernel\.Resolve\(shader, field, ""(\w+)""\)")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            CollectionAssert.AreEquivalent(viewKernels, resolvedViewKernels, "The backend resolves exactly the view kernels of the view scope's family.");
        }

        [Test]
        public void RendererListSubmission_DoesNotAddAWrapperScope()
        {
            string source = File.ReadAllText("Packages/com.infinity.render-pipeline/Runtime/RenderGraph/RGEncoder.cs");
            Assert.IsFalse(source.Contains("RasterRendererList"));
            Assert.IsTrue(source.Contains("m_Core.commands.DrawRendererList(rendererList);"));
            Assert.IsTrue(source.Contains(".Submit(m_Core.commands, rendererListHandle);"));
        }
    }
}
