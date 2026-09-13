using System.Collections.Generic;
using System.Reflection;
using InfinityTech.Rendering.Editor.Build;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShaderStrippingTests
    {
        static ShaderSnippetData Snippet(string pass)
        {
            object value = default(ShaderSnippetData);
            typeof(ShaderSnippetData).GetField("m_PassName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, pass);
            return (ShaderSnippetData)value;
        }

        [TestCase("ShadowCaster")]
        [TestCase("SRPDefaultUnlit")]
        [TestCase("Meta")]
        [TestCase("TerrainAdd")]
        [TestCase("SystemLUT")]
        public void ActualShaderCallback_PreservesNativeAndSystemPasses(string pass)
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive, "This integration fixture requires the Infinity Editor project.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Shaders/Surface/InfinityUnlit.shader");
            Assert.IsNotNull(shader);
            var variants = new List<ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, Snippet(pass), variants);
            Assert.AreEqual(2, variants.Count);
        }

        [Test]
        public void ActualShaderCallback_StripsOnlyExclusivelyForeignSubShaders()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/ForeignPipeline.shader");
            Assert.IsNotNull(shader);
            var variants = new List<ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, Snippet("Forward"), variants);
            Assert.AreEqual(0, variants.Count);
        }

        [Test]
        public void ActualComputeCallback_KeepsReferencedKernelAndPreflightChecksResources()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            Assert.IsNotNull(shader);
            var variants = new List<ShaderCompilerData> { default };
            new InfinityComputeShaderStripper().OnProcessComputeShader(shader, "MeshDrawPipeline", variants);
            Assert.AreEqual(1, variants.Count);
            Assert.DoesNotThrow(() => new InfinityBuildPreprocessor().OnPreprocessBuild(null));
        }
    }
}
