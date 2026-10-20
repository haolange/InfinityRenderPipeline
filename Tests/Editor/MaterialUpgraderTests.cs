using NUnit.Framework;
using UnityEditor.Rendering;
using UnityEngine;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MaterialUpgraderTests
    {
        [Test]
        [Category("Unverified_0_5")]
        public void Standard_ConvertsToInfinityLit()
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null || Shader.Find(InfinityMaterialUpgraders.LitShader) == null)
                Assert.Ignore("Standard or InfinityLit shader unavailable.");

            var material = new Material(standard);
            try
            {
                material.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1));
                material.SetFloat("_Glossiness", 0.8f);
                material.SetFloat("_Metallic", 0.3f);
                material.SetTextureScale("_MainTex", new Vector2(2, 2));
                MaterialUpgrader upgrader = InfinityMaterialUpgraders.Find(material);
                Assert.IsNotNull(upgrader);
                upgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);

                Assert.AreEqual(InfinityMaterialUpgraders.LitShader, material.shader.name);
                Assert.AreEqual(new Color(0.2f, 0.4f, 0.6f, 1), material.GetColor("_BaseColor"));
                Assert.AreEqual(0.2f, material.GetFloat("_Roughness"), 1e-5f, "Roughness = 1 - smoothness.");
                Assert.AreEqual(0.3f, material.GetFloat("_Reflectance"), 1e-5f);
                Assert.AreEqual(2.0f, material.GetFloat("_BaseColorTile"), 1e-5f);
                Assert.AreEqual(0.0f, material.GetFloat("_TranslucentStage"));
                Assert.IsNull(InfinityMaterialUpgraders.Find(material), "A converted material has no further upgrader.");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        [Category("Unverified_0_5")]
        public void Upgraders_TargetInfinityShaders_AndSourcesAreUnique()
        {
            var sources = new System.Collections.Generic.HashSet<string>();
            foreach (MaterialUpgrader upgrader in InfinityMaterialUpgraders.All)
            {
                Assert.IsTrue(sources.Add(upgrader.OldShaderPath), upgrader.OldShaderPath + " has two upgraders.");
                Assert.IsTrue(upgrader.NewShaderPath == InfinityMaterialUpgraders.LitShader || upgrader.NewShaderPath == InfinityMaterialUpgraders.UnlitShader);
            }
        }
    }
}
