using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using InfinityTech.Rendering.Pipeline;
using InfinityTech.Rendering.Volume;
using static InfinityTech.Rendering.Pipeline.Tests.VolumeTestUtility;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ScreenSpaceDenoiseTests
    {
        [Test]
        public void IsActive_GatesGTAORequest()
        {
            ScreenSpaceAmbientOcclusion ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            try
            {
                ssao.active = true;
                Assert.IsFalse(VolumeComponentActive(ssao));
                Assert.IsFalse(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));

                ssao.enable.value = true;
                Assert.IsTrue(VolumeComponentActive(ssao));
                Assert.IsTrue(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));

                ssao.active = false;
                Assert.IsFalse(ScreenSpaceModeUtility.ShouldRequestGTAO(ssao));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssao);
            }
        }

        [Test]
        public void ScreenSpaceReflection_ResolveReusesAtLeastTwoRaysByDefault()
        {
            ScreenSpaceReflection ssr = ScriptableObject.CreateInstance<ScreenSpaceReflection>();
            try
            {
                Assert.GreaterOrEqual(ssr.ResolveSamples.value, 2);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ssr);
            }
        }

        // SSR and SSGI sample already-lit color. Deferred and forward shading occlude their own indirect diffuse and
        // environment once, then let the screen-space signal replace that term; nothing occludes the replacement.
        [TestCase("Shaders/Pass/Lighting/Compute_DeferredShading.compute", "indirectDiffuse *= occlusion;", "* environmentBRDF * occlusion;")]
        [TestCase("Shaders/Library/ForwardOutput.hlsl", "diffuseColor * irradiance * occlusion;", "environment * environmentBRDF * occlusion;")]
        public void IndirectComposition_OccludesOnceBeforeScreenSpaceReplacement(string path, string diffuseOcclusion, string specularOcclusion)
        {
            string source = ReadPackageFile(path);
            AssertOccludedOnceBefore(source, diffuseOcclusion, "ReplaceIndirectDiffuse(");
            AssertOccludedOnceBefore(source, specularOcclusion, "ReplaceEnvironmentSpecular(");
            int lastReplacement = source.IndexOf("ReplaceEnvironmentSpecular(", StringComparison.Ordinal);
            Assert.AreEqual(-1, source.IndexOf("occlusion", lastReplacement, StringComparison.Ordinal), "Nothing after the replacement applies occlusion again.");
        }

        [Test]
        public void DeferredShading_ReadsOptionalInputsOnlyWhenTheirFeaturesRecord()
        {
            string source = ReadPackageFile("Runtime/Pipeline/Pass/Lighting/DeferredShadingPass.cs");
            // The feature set decides whether an optional input exists; QueryTexture then fails loudly if the producer did not register it.
            StringAssert.Contains("bool hasOcclusion = context.features.ShouldRecord(EFrameFeature.GTAO) || context.features.ShouldRecord(EFrameFeature.RTAO);", source);
            StringAssert.Contains("bool hasIndirectDiffuse = context.features.ShouldRecord(EFrameFeature.SSGI);", source);
            StringAssert.Contains("bool hasReflection = context.features.ShouldRecord(EFrameFeature.SSR);", source);
            StringAssert.Contains("bool hasContactShadow = context.features.ShouldRecord(EFrameFeature.ContactShadow);", source);
            AssertGatedRead(source, "hasOcclusion", "occlusionTextureHandle", "OcclusionBuffer");
            AssertGatedRead(source, "hasIndirectDiffuse", "ssgiTextureHandle", "SSGIBuffer");
            AssertGatedRead(source, "hasReflection", "ssrTextureHandle", "SSRBuffer");
            AssertGatedRead(source, "hasContactShadow", "contactShadowTextureHandle", "ContactShadowBuffer");
            StringAssert.DoesNotContain("TryQueryTexture(", source);
        }

        static string ReadPackageFile(string path)
        {
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.infinity.render-pipeline", path));
            Assert.IsTrue(File.Exists(fullPath), fullPath);
            return File.ReadAllText(fullPath);
        }

        static void AssertOccludedOnceBefore(string source, string occlusionProduct, string replacement)
        {
            int product = source.IndexOf(occlusionProduct, StringComparison.Ordinal);
            int replaced = source.IndexOf(replacement, StringComparison.Ordinal);
            Assert.GreaterOrEqual(product, 0, occlusionProduct);
            Assert.AreEqual(-1, source.IndexOf(occlusionProduct, product + 1, StringComparison.Ordinal), occlusionProduct + " appears once.");
            Assert.Greater(replaced, product, replacement + " follows " + occlusionProduct);
        }

        // The only query of the buffer sits inside the feature's branch.
        static void AssertGatedRead(string source, string flag, string handle, string buffer)
        {
            string query = "QueryTexture(InfinityShaderIDs." + buffer + ")";
            string pattern = @"if \(" + flag + @"\)\s*\{\s*passData\." + handle + @" = passHandle\.ReadTexture\(m_RGBuilder\.scoper\." + Regex.Escape(query) + @"\);";
            Assert.IsTrue(Regex.IsMatch(source, pattern), flag + " gates the read of " + buffer);
            Assert.AreEqual(1, Regex.Matches(source, Regex.Escape(query)).Count, buffer + " is queried once.");
        }
    }
}
