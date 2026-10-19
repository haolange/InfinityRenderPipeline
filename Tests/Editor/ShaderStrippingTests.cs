using System.Collections.Generic;
using InfinityTech.Rendering.Editor.Build;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ShaderStrippingTests
    {
        static Shader LoadFixture(string lightMode, bool infinityTagged)
        {
            string file = (infinityTagged ? "Infinity" : "Untagged") + (lightMode.Length == 0 ? "Empty" : lightMode);
            string path = "Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/ShaderStripping/" + file + ".shader";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            Assert.IsNotNull(shader, path);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(shader));
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            Assert.AreEqual(1, shader.subshaderCount);
            Assert.AreEqual(1, shader.passCount);
            Assert.AreEqual(infinityTagged ? new ShaderTagId("InfinityRenderPipeline") : ShaderTagId.none,
                shader.FindSubshaderTagValue(0, new ShaderTagId("RenderPipeline")));
            Assert.AreEqual(new ShaderTagId(lightMode), shader.FindPassTagValue(0, 0, new ShaderTagId("LightMode")));
            return shader;
        }

        [Test]
        [Category("Unverified-0.5")]
        public void ActualShaderCallback_StripsUnusedLightModeInInfinitySubShader()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            Shader used = LoadFixture("GBufferPass", true);
            Shader unused = LoadFixture("ForwardBase", true);
            var usedVariants = new List<UnityEditor.Rendering.ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(used, default, usedVariants);
            Assert.AreEqual(2, usedVariants.Count, "GBufferPass is drawn by the pipeline and must be kept.");
            var unusedVariants = new List<UnityEditor.Rendering.ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(unused, default, unusedVariants);
            Assert.AreEqual(0, unusedVariants.Count, "ForwardBase is never drawn by InfinityRP and must be stripped.");
        }

        [TestCase("", true)]
        [TestCase("ShadowCaster", true)]
        [TestCase("SRPDefaultUnlit", true)]
        [TestCase("Meta", true)]
        [TestCase("TerrainAdd", false)]
        [TestCase("SystemLUT", false)]
        public void ActualShaderCallback_PreservesNativeAndSystemPasses(string pass, bool infinityTagged)
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive, "This integration fixture requires the Infinity Editor project.");
            Shader shader = LoadFixture(pass, infinityTagged);
            var variants = new List<UnityEditor.Rendering.ShaderCompilerData> { default, default };
            // A single real pass gives default ShaderSnippetData the correct public 0/0 identity.
            new InfinityShaderStripper().OnProcessShader(shader, default, variants);
            Assert.AreEqual(2, variants.Count);
        }

        [TestCase("ForwardBase")]
        [TestCase("TerrainAdd")]
        [TestCase("SystemLUT")]
        public void ActualShaderCallback_StripsUndrawnLightModesOnlyInInfinitySubShader(string pass)
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            Shader shader = LoadFixture(pass, true);
            var variants = new List<UnityEditor.Rendering.ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, default, variants);
            Assert.AreEqual(0, variants.Count);
        }

        [Test]
        public void ActualShaderCallback_PreservesRealUnlitPassIdentity()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Shaders/Surface/InfinityUnlit.shader");
            Assert.IsNotNull(shader);
            Assert.AreEqual(new ShaderTagId("SRPDefaultUnlit"), shader.FindPassTagValue(0, 0, new ShaderTagId("LightMode")));
            var variants = new List<UnityEditor.Rendering.ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, default, variants);
            Assert.AreEqual(2, variants.Count);
        }

        [Test]
        public void ActualShaderCallback_StripsOnlyExclusivelyForeignSubShaders()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.infinity.render-pipeline/Tests/Editor/Fixtures/ForeignPipeline.shader");
            Assert.IsNotNull(shader);
            var variants = new List<UnityEditor.Rendering.ShaderCompilerData> { default, default };
            new InfinityShaderStripper().OnProcessShader(shader, default, variants);
            Assert.AreEqual(0, variants.Count);
        }

        [Test]
        public void ActualComputeCallback_KeepsReferencedKernelAndPreflightChecksResources()
        {
            Assert.IsTrue(InfinityShaderStripper.IsInfinityActive);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.infinity.render-pipeline/Shaders/RenderingFeature/MeshDrawPipeline/Compute_MeshDrawPipeline.compute");
            Assert.IsNotNull(shader);
            var variants = new List<UnityEditor.Rendering.ShaderCompilerData> { default };
            new InfinityComputeShaderStripper().OnProcessComputeShader(shader, "MeshDrawPipeline", variants);
            Assert.AreEqual(1, variants.Count);
            Assert.DoesNotThrow(() => new InfinityBuildPreprocessor().OnPreprocessBuild(null));
        }
    }
}
