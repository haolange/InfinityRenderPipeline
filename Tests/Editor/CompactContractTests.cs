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
        public void PrepareBatch_DoesNotDispatchCullFrustum()
        {
            string path = Path.GetFullPath("Packages/com.infinity.render-pipeline/Runtime/RendererCore/PrimitivePipeline/MeshPipeline/MeshDrawGPUBackend.cs");
            Assert.IsTrue(File.Exists(path), path);
            string source = File.ReadAllText(path);
            Assert.IsTrue(source.Contains("BeginSample(\"ComputeMeshGpuClearCounts\")"));
            Assert.IsTrue(source.Contains("BeginSample(\"ComputeMeshGpuCompactIndices\")"));
            Assert.IsTrue(source.Contains("BeginSample(\"ComputeMeshGpuBuildArgs\")"));
            Assert.IsTrue(source.Contains("BeginSample(\"RenderLoop.DrawMesh\")"));
            Assert.IsTrue(source.Contains("DispatchCullFrustum"));
            int prepareIndirect = source.IndexOf("internal static bool PrepareIndirect");
            Assert.Greater(prepareIndirect, 0);
            string prepareIndirectBody = source.Substring(prepareIndirect);
            int drawIndirect = prepareIndirectBody.IndexOf("internal static void DrawIndirect");
            Assert.Greater(drawIndirect, 0);
            prepareIndirectBody = prepareIndirectBody.Substring(0, drawIndirect);
            Assert.IsFalse(prepareIndirectBody.Contains("BeginSample(\"ComputeMeshGpuCompact\")"));
            string builder = File.ReadAllText(Path.GetFullPath(
                "Packages/com.infinity.render-pipeline/Runtime/RendererCore/RenderGraph/RGBuilder.cs"));
            Assert.IsTrue(builder.Contains("BeginSample(\"ComputeMeshGpuCompact\")"));
            string drawLists = File.ReadAllText(Path.GetFullPath(
                "Packages/com.infinity.render-pipeline/Runtime/RendererCore/RenderGraph/RGDrawList.cs"));
            Assert.IsTrue(drawLists.Contains("CascadeSlice"));
            Assert.IsTrue(drawLists.Contains("LocalShadowSlice"));
            int prepareBatch = source.IndexOf("private static bool PrepareBatch");
            Assert.Greater(prepareBatch, 0);
            string prepareBatchBody = source.Substring(prepareBatch);
            int nextMethod = prepareBatchBody.IndexOf("private static void DrawBatch");
            Assert.Greater(nextMethod, 0);
            prepareBatchBody = prepareBatchBody.Substring(0, nextMethod);
            Assert.IsTrue(prepareBatchBody.Contains("BeginSample(\"ComputeMeshGpuClearCounts\")"));
            Assert.IsTrue(prepareBatchBody.Contains("BeginSample(\"ComputeMeshGpuCompactIndices\")"));
            Assert.IsTrue(prepareBatchBody.Contains("BeginSample(\"ComputeMeshGpuBuildArgs\")"));
            Assert.IsFalse(prepareBatchBody.Contains("BeginSample(\"ComputeMeshGpuCompact\")"));
            Assert.IsFalse(prepareBatchBody.Contains("s_KernelCull"));
            Assert.IsFalse(prepareBatchBody.Contains("DispatchCompute(s_Shader, s_KernelCull"));
            int drawIndirectMethod = source.IndexOf("internal static void DrawIndirect");
            Assert.Greater(drawIndirectMethod, 0);
            string drawIndirectBody = source.Substring(drawIndirectMethod);
            int submitIndirect = drawIndirectBody.IndexOf("internal static bool SubmitIndirect");
            Assert.Greater(submitIndirect, 0);
            drawIndirectBody = drawIndirectBody.Substring(0, submitIndirect);
            Assert.IsTrue(drawIndirectBody.Contains("BeginSample(\"RenderLoop.DrawMesh\")"));
        }
    }
}
