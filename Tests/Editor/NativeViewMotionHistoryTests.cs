using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace InfinityTech.Rendering.Tests
{
    public class NativeViewMotionHistoryTests
    {
        [Test]
        public void NativeViewMotionHistory_IsRetired()
        {
            string path = Path.GetFullPath("Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/Context/NativeViewMotionHistory.cs");
            Assert.IsFalse(File.Exists(path), path);
        }

        [Test]
        public void NativeMotion_UsesUnityPreviousMatrix_WithoutInstancedMpb()
        {
            string path = Path.GetFullPath("Packages/com.infinity.render-pipeline/Shaders/ShaderLibrary/NativeMotion.hlsl");
            Assert.IsTrue(File.Exists(path), path);
            string source = File.ReadAllText(path);
            Assert.IsTrue(source.Contains("unity_MatrixPreviousM"));
            Assert.IsFalse(source.Contains("InfinityPreviousObjectToWorld"));
            Assert.IsFalse(source.Contains("InfinityPreviousVertexOffset"));
            Assert.IsFalse(source.Contains("UNITY_INSTANCING_BUFFER_START"));
            Assert.IsFalse(source.Contains("SRV_NativePreviousVertices"));
        }

        [Test]
        public void MotionAndTranslucent_RequestUnityMotionVectors_WithoutNativeVertexUpload()
        {
            string motion = File.ReadAllText(Path.GetFullPath(
                "Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/Pass/MotionPass.cs"));
            Assert.IsTrue(motion.Contains("PerObjectData.MotionVectors"));
            Assert.IsFalse(motion.Contains("preferNativeMotionVectors"));
            Assert.IsFalse(motion.Contains("NativePreviousVertices"));
            Assert.IsFalse(motion.Contains("UploadNativeMotionVertices"));

            string translucent = File.ReadAllText(Path.GetFullPath(
                "Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/Pass/TranslucentPass.cs"));
            Assert.IsTrue(translucent.Contains("PerObjectData.MotionVectors"));
            Assert.IsFalse(translucent.Contains("NativePreviousVertices"));

            string pipeline = File.ReadAllText(Path.GetFullPath(
                "Packages/com.infinity.render-pipeline/Runtime/RenderPipeline/InfinityRenderPipeline.cs"));
            Assert.IsFalse(pipeline.Contains("SetPropertyBlock"));
            Assert.IsFalse(pipeline.Contains("nativeMotionHistory"));

            string lit = File.ReadAllText(Path.GetFullPath(
                "Packages/com.infinity.render-pipeline/Shaders/Surface/InfinityLit.shader"));
            Assert.IsTrue(lit.Contains("InfinityLitMaterial.hlsl"));
            Assert.IsFalse(lit.Contains("CBUFFER_START(UnityPerMaterial)"));
        }
    }
}
