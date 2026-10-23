using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEditor.Rendering;
using UnityEngine;
using InfinityTech.Rendering.Editor;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class MaterialUpgraderTests
    {
        [Test]
        public void Standard_ConvertsToInfinityLit()
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null || Shader.Find(InfinityMaterialUpgraders.LitShader) == null)
                Assert.Ignore("Standard or InfinityLit shader unavailable.");

            var material = new Material(standard);
            Material reference = null;
            try
            {
                reference = new Material(Shader.Find(InfinityMaterialUpgraders.LitShader));
                material.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 1));
                // Unity's native color setter/getter round trip is not bit-identical to the input literal.
                reference.SetColor("_BaseColor", material.GetColor("_Color"));
                material.SetFloat("_Glossiness", 0.8f);
                material.SetFloat("_Metallic", 0.3f);
                material.SetTextureScale("_MainTex", new Vector2(2, 2));
                MaterialUpgrader upgrader = InfinityMaterialUpgraders.Find(material);
                Assert.IsNotNull(upgrader);
                upgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);

                Assert.AreEqual(InfinityMaterialUpgraders.LitShader, material.shader.name);
                Assert.AreEqual(reference.GetColor("_BaseColor"), material.GetColor("_BaseColor"),
                    "Color conversion must match an independent native Material round trip exactly.");
                Assert.AreEqual(0.2f, material.GetFloat("_Roughness"), 1e-5f, "Roughness = 1 - smoothness.");
                Assert.AreEqual(0.3f, material.GetFloat("_Reflectance"), 1e-5f);
                Assert.AreEqual(2.0f, material.GetFloat("_BaseColorTile"), 1e-5f);
                Assert.AreEqual(0.0f, material.GetFloat("_TranslucentStage"));
                Assert.IsNull(InfinityMaterialUpgraders.Find(material), "A converted material has no further upgrader.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void ConversionWizard_SavesOnlyConvertedMaterials_AndUndoRestoresSource()
        {
            string root = "Assets/__InfinityConversionTest_" + Guid.NewGuid().ToString("N");
            InfinityMaterialConversionWizard window = null;
            Material material = null;
            try
            {
                Directory.CreateDirectory(root);
                AssetDatabase.Refresh();
                material = new Material(Shader.Find("Standard"));
                material.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f));
                material.SetFloat("_Glossiness", 0.8f);
                AssetDatabase.CreateAsset(material, root + "/Converted.mat");
                AssetDatabase.SaveAssetIfDirty(material);
                string guid = AssetDatabase.AssetPathToGUID(root + "/Converted.mat");
                string source = EditorJsonUtility.ToJson(material);
                var sentinel = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(sentinel, root + "/Unrelated.asset");
                AssetDatabase.SaveAssetIfDirty(sentinel);
                byte[] stored = File.ReadAllBytes(root + "/Unrelated.asset");
                sentinel.name = "Pending unrelated edit";
                EditorUtility.SetDirty(sentinel);
                window = ScriptableObject.CreateInstance<InfinityMaterialConversionWizard>();
                window.SetEntries(new[] { material });
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                window.ConvertSelected();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Assert.AreEqual(InfinityMaterialUpgraders.LitShader, material.shader.name);
                Assert.IsFalse(EditorUtility.IsDirty(material), "Converted material must be persisted.");
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(root + "/Converted.mat"));
                CollectionAssert.AreEqual(stored, File.ReadAllBytes(root + "/Unrelated.asset"));
                Assert.IsTrue(EditorUtility.IsDirty(sentinel));
                Assert.AreEqual("Pending unrelated edit", sentinel.name);
                string converted = EditorJsonUtility.ToJson(material);
                Undo.PerformUndo();
                Assert.AreEqual(source, EditorJsonUtility.ToJson(material), "Undo must restore the complete source material.");
                Undo.PerformRedo();
                Assert.AreEqual(converted, EditorJsonUtility.ToJson(material), "Redo must restore only the conversion.");
                CollectionAssert.AreEqual(stored, File.ReadAllBytes(root + "/Unrelated.asset"));
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                if (material != null) Undo.ClearUndo(material);
                AssetDatabase.DeleteAsset(root);
            }
        }

        [Test]
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
