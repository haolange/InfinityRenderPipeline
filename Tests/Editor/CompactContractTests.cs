using System.IO;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.MeshPipeline;

namespace InfinityTech.Rendering.MeshPipeline.Tests
{
    public class CompactContractTests
    {
        [Test]
        public void ComputeShader_HasOneCompactKernel_AndDropsScanScatter()
        {
            string path = Path.GetFullPath("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            Assert.IsTrue(File.Exists(path), path);
            string source = File.ReadAllText(path);
            Assert.IsTrue(source.Contains("#pragma kernel CullFrustum"));
            Assert.IsTrue(source.Contains("#pragma kernel ClearCounts"));
            Assert.IsTrue(source.Contains("#pragma kernel Compact"));
            Assert.IsTrue(source.Contains("#pragma kernel BuildIndirectArgs"));
            Assert.IsFalse(source.Contains("CompactCommandInstances"));
            Assert.IsFalse(source.Contains("PrefixSumCommands"));
            Assert.IsFalse(source.Contains("ScatterVisibleInstances"));
            Assert.IsTrue(source.Contains("_CandidateTable"));
            Assert.IsTrue(source.Contains("_ShadingIndices"));
        }

        [Test]
        public void RequiredKernels_MatchBackendContract()
        {
            MeshDrawGPUBackend.SetShader(null);
            Assert.IsFalse(MeshDrawGPUBackend.SupportsIndirect);
        }

        [Test]
        public void RasterSubmissionContainsNoImplicitComputePreparation()
        {
            string backend=File.ReadAllText(Path.GetFullPath("Packages/com.infinity.render-pipeline/Runtime/RendererCore/PrimitivePipeline/MeshPipeline/MeshDrawGPUBackend.cs"));
            int start=backend.IndexOf("internal static void DrawIndirect");
            int end=backend.IndexOf("private static bool CanPrepareIndirect",start);
            string draw=backend.Substring(start,end-start);
            Assert.IsFalse(draw.Contains("DispatchCompute("));
            Assert.IsFalse(draw.Contains("SetBufferData("));
            Assert.IsFalse(draw.Contains("EnsureCapacity("));
            Assert.IsTrue(draw.Contains("MeshDrawProfiling.Draw(viewKind, true)"));
            string builder=File.ReadAllText(Path.GetFullPath("Packages/com.infinity.render-pipeline/Runtime/RendererCore/RenderGraph/RGBuilder.cs"));
            Assert.IsTrue(builder.Contains("InsertDrawListPreparation"));
            Assert.IsTrue(builder.Contains("MeshDrawProfiling.upload"));
            Assert.IsTrue(builder.Contains("MeshDrawProfiling.compaction"));
            Assert.IsFalse(builder.Contains("PrepareSubmit("));
        }
    }
}
