using System.Collections.Generic;
using System.Reflection;
using InfinityTech.Rendering.Editor.Build;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShaderStrippingTests
    {
        static ShaderSnippetData Snippet(string pass, uint subshaderIndex = 0, uint passIndex = 0)
        {
            object value = default(ShaderSnippetData);
            typeof(ShaderSnippetData).GetField("m_PassName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, pass);
            typeof(ShaderSnippetData).GetField("m_PassIdentifier", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, new PassIdentifier(subshaderIndex, passIndex));
            return (ShaderSnippetData)value;
        }

        [Test]
        [Category("Unverified-0.5")]
        public void ActualShaderCallback_StripsUnusedLightModeInInfinitySubShader()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/UnusedLightMode.shader");
            Assert.IsNotNull(shader);

            var used = new List<ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, Snippet("GBuffer", 0, 0), used);
            Assert.AreEqual(2, used.Count, "GBufferPass is drawn by the pipeline and must be kept.");

            var unused = new List<ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, Snippet("LegacyForward", 0, 1), unused);
            Assert.AreEqual(0, unused.Count, "ForwardBase is never drawn by InfinityRP and must be stripped.");
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
