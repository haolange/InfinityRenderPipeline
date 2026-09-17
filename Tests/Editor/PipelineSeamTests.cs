using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class PipelineSeamTests
    {
        static readonly string[] GeometryPassFiles =
        {
            "DepthPass.cs",
            "GBufferPass.cs",
            "ForwardPass.cs",
            "MotionPass.cs",
            "CascadeShadowPass.cs",
            "LocalShadowPass.cs"
        };

        static readonly string[] ForbiddenTokens =
        {
            "MeshDrawRequest",
            "BuiltinMeshesPasses",
            "MeshVisibilityHandle",
            "EMeshBackendPolicy",
            "scriptableRenderContext.CreateRendererList",
            "scriptableRenderContext.CreateShadowRendererList"
        };

        [Test]
        public void MeshDrawPipeline_DoesNotWrapPrepareAndDrawWithLegacySampler()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../Packages/com.infinity.render-pipeline/Runtime/RendererCore/PrimitivePipeline/MeshPipeline/MeshDrawPipeline.cs"));
            Assert.IsTrue(File.Exists(path), path);
            string text = File.ReadAllText(path);
            Assert.IsTrue(text.Contains("BeginSample(\"RenderLoop.DrawMesh\")"));
            Assert.IsFalse(text.Contains("RenderLoop.DrawMeshPipeline"));
        }

        [Test]
        public void GeometryPassFiles_DoNotReferenceRetiredSeamTypes()
        {
            string passDir = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/Pass"));
            Assert.IsTrue(Directory.Exists(passDir), passDir);

            foreach (string file in GeometryPassFiles)
            {
                string path = Path.Combine(passDir, file);
                Assert.IsTrue(File.Exists(path), path);
                string text = File.ReadAllText(path);
                foreach (string token in ForbiddenTokens)
                {
                    Assert.IsFalse(text.Contains(token), $"{file} still references {token}");
                }
            }
        }
    }
}
