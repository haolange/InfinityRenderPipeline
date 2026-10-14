using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Pipeline.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class BakedGITests
    {
        [Test]
        [Category("Unverified-0.5")]
        public void Asset_ProbeVolumeInterface_FollowsLightProbeSystem()
        {
            var asset = ScriptableObject.CreateInstance<InfinityRenderPipelineAsset>();
            try
            {
                Assert.IsFalse(asset.supportProbeVolume, "Light Probe Groups by default");
                Assert.AreEqual(ProbeVolumeSHBands.SphericalHarmonicsL1, asset.maxSHBands);
                asset.lightProbeSystem = ELightProbeSystem.ProbeVolumes;
                asset.probeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL2;
                Assert.IsTrue(asset.supportProbeVolume);
                Assert.AreEqual(ProbeVolumeSHBands.SphericalHarmonicsL2, asset.maxSHBands);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void ProbeVolumeIntegration_DisabledWithoutProbeVolumes()
        {
            using (var integration = new ProbeVolumeIntegration(null))
            {
                Assert.IsFalse(integration.Enabled);
            }
        }

        [Test]
        [Category("Unverified-0.5")]
        public void IESIntensity_LumensSpreadOverTheEmissionSolidAngle()
        {
            var go = new GameObject("IESTest");
            try
            {
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                Assert.AreEqual(1.0f, InfinityIESImporter.CandelaFromIES(light, "Lumens", 4.0f * Mathf.PI), 1e-4f);
                Assert.AreEqual(250.0f, InfinityIESImporter.CandelaFromIES(light, "Candelas", 250.0f), 1e-4f);
                light.type = LightType.Spot;
                light.spotAngle = 60.0f;
                float solidAngle = 2.0f * Mathf.PI * (1.0f - Mathf.Cos(30.0f * Mathf.Deg2Rad));
                Assert.AreEqual(100.0f / solidAngle, InfinityIESImporter.CandelaFromIES(light, "Lumens", 100.0f), 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase("InfinityPipeline/InfinityLit")]
        [TestCase("InfinityPipeline/InfinityLit-Instanced")]
        [TestCase("InfinityPipeline/TerrainLit")]
        [Category("Unverified-0.5")]
        public void SurfaceShaders_HaveMetaAndShadowCasterPasses(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader, shaderName);
            var material = new Material(shader);
            try
            {
                Assert.GreaterOrEqual(material.FindPass("Meta"), 0, "Meta (lightmapper)");
                Assert.GreaterOrEqual(material.FindPass("ShadowCaster"), 0, "ShadowCaster (shadow RendererList)");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
