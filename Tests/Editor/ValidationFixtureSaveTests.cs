using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InfinityTech.Rendering.Editor.Validation;
using InfinityTech.Rendering.PostProcess;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class ValidationFixtureSaveTests
    {
        [Test]
        public void Save_RejectsUnrelatedDirtyAssets_ThenPersistsOwnedProfileAndScene()
        {
            Assert.AreEqual(GetType().FullName + "." + nameof(Save_RejectsUnrelatedDirtyAssets_ThenPersistsOwnedProfileAndScene),
                TestContext.CurrentContext.Test.FullName, "This integration test requires UTF's isolated EditMode bootstrap scene.");
            string root = "Assets/__InfinityFixtureSaveTest_" + Guid.NewGuid().ToString("N");
            string sentinelPath = root + "/Unrelated.asset";
            try
            {
                Directory.CreateDirectory(root);
                AssetDatabase.Refresh();
                var sentinel = ScriptableObject.CreateInstance<VolumeProfile>();
                sentinel.name = "Unrelated";
                AssetDatabase.CreateAsset(sentinel, sentinelPath);
                AssetDatabase.SaveAssetIfDirty(sentinel);
                byte[] stored = File.ReadAllBytes(sentinelPath);
                sentinel.name = "Pending unrelated edit";
                EditorUtility.SetDirty(sentinel);

                var fixture = new ValidationFixture("SaveBoundary", root + "/Fixtures");
                CollectionAssert.AreEqual(stored, File.ReadAllBytes(sentinelPath), "Fixture construction must not save unrelated assets.");
                VolumeProfile profile = fixture.GlobalVolume(p => ValidationFixture.AddComponent<Exposure>(p));
                CollectionAssert.AreEqual(stored, File.ReadAllBytes(sentinelPath), "Owned profile creation must not save unrelated assets.");
                string profilePath = AssetDatabase.GetAssetPath(profile);
                string profileGuid = AssetDatabase.AssetPathToGUID(profilePath);
                var rejected = Assert.Throws<InvalidOperationException>(() => fixture.Save());
                StringAssert.Contains(sentinelPath, rejected.Message);
                Assert.IsFalse(File.Exists(fixture.ScenePath), "Rejection must precede native scene saving.");

                CollectionAssert.AreEqual(stored, File.ReadAllBytes(sentinelPath), "Fixture save must not save unrelated assets.");
                Assert.IsTrue(EditorUtility.IsDirty(sentinel), "Pending edits must remain pending.");
                Assert.AreEqual("Pending unrelated edit", sentinel.name);
                // Resolve only this test's sentinel; the rejected pending edit was proved intact above.
                sentinel.name = "Unrelated";
                AssetDatabase.SaveAssetIfDirty(sentinel);
                CollectionAssert.AreEqual(stored, File.ReadAllBytes(sentinelPath));
                fixture.Save();
                Assert.IsTrue(File.Exists(fixture.ScenePath));
                AssetDatabase.ImportAsset(profilePath, ImportAssetOptions.ForceUpdate);
                var persisted = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
                Assert.AreEqual(profileGuid, AssetDatabase.AssetPathToGUID(profilePath));
                Assert.IsTrue(persisted.TryGet<Exposure>(out var exposure));
                Assert.IsNotNull(exposure);
                Assert.AreEqual(profilePath, AssetDatabase.GetAssetPath(exposure), "Owned subobjects must persist with the profile.");
            }
            finally
            {
                try
                {
                    // UTF owns this bootstrap; UTF restores the user's outer SceneSetup after the run.
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                }
                finally
                {
                    AssetDatabase.DeleteAsset(root);
                }
            }
        }
        [TestCase("AreaLights")]
        [TestCase("LocalLights")]
        [TestCase("Decal")]
        [TestCase("Translucent")]
        public void EarlyCreators_RejectDirtyForeignAssetInsideMaterialDirectory(string kind)
        {
            string root = "Assets/__InfinityFixtureOwnershipTest_" + Guid.NewGuid().ToString("N");
            string directory = root + "/" + kind + "Materials";
            string sentinelPath = directory + "/UserOwned.asset";
            try
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
                var sentinel = ScriptableObject.CreateInstance<VolumeProfile>();
                sentinel.name = "User owned";
                AssetDatabase.CreateAsset(sentinel, sentinelPath);
                AssetDatabase.SaveAssetIfDirty(sentinel);
                byte[] stored = File.ReadAllBytes(sentinelPath);
                sentinel.name = "Pending user edit";
                EditorUtility.SetDirty(sentinel);
                Action create = kind switch
                {
                    "AreaLights" => () => CreateAreaLightsValidationScene.CreateInDirectory(root),
                    "LocalLights" => () => CreateLocalLightsValidationScene.CreateInDirectory(root),
                    "Decal" => () => CreateDecalValidationScene.CreateInDirectory(root),
                    _ => () => CreateTranslucentValidationScene.CreateInDirectory(root)
                };
                var error = Assert.Throws<InvalidOperationException>(() => create());
                StringAssert.Contains(sentinelPath, error.Message);
                Assert.IsFalse(File.Exists(root + "/Validation_" + kind + ".unity"));
                CollectionAssert.AreEqual(stored, File.ReadAllBytes(sentinelPath));
                Assert.IsTrue(EditorUtility.IsDirty(sentinel));
                Assert.AreEqual("Pending user edit", sentinel.name);
            }
            finally
            {
                try { EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single); }
                finally { AssetDatabase.DeleteAsset(root); }
            }
        }

        [Test]
        public void Lit_PositiveEmissionEnablesBakedGI_ZeroEmissionRemainsBlack()
        {
            Assert.AreEqual(GetType().FullName + "." + nameof(Lit_PositiveEmissionEnablesBakedGI_ZeroEmissionRemainsBlack),
                TestContext.CurrentContext.Test.FullName, "This integration test requires UTF's isolated EditMode bootstrap scene.");
            string root = "Assets/__InfinityFixtureEmissionTest_" + Guid.NewGuid().ToString("N");
            try
            {
                var fixture = new ValidationFixture("Emission", root);
                Material emitter = fixture.Lit("Emitter", Color.black, 0.5f, emission: new Color(4, 3.2f, 2));
                Material dark = fixture.Lit("Dark", Color.black, 0.5f);
                Assert.AreEqual(MaterialGlobalIlluminationFlags.BakedEmissive, emitter.globalIlluminationFlags);
                Assert.AreEqual(MaterialGlobalIlluminationFlags.EmissiveIsBlack, dark.globalIlluminationFlags);
                Assert.Greater(emitter.GetColor("_EmissionColor").maxColorComponent, 0);
                Assert.AreEqual(0, dark.GetColor("_EmissionColor").maxColorComponent);
            }
            finally
            {
                try { EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single); }
                finally { AssetDatabase.DeleteAsset(root); }
            }
        }
    }
}
