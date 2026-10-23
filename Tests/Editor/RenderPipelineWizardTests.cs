using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using InfinityTech.Rendering.PostProcess;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.Editor;
using InfinityTech.Rendering.LightPipeline;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    // The wizard's checks only read state; the helpers behind its fixes are exact.
    public class RenderPipelineWizardTests
    {
        [Test]
        public void Checks_AreDescribed_AndEvaluateWithoutSideEffects()
        {
            var titles = new HashSet<string>();
            bool batching = GraphicsSettings.useScriptableRenderPipelineBatching;
            ColorSpace colorSpace = UnityEditor.PlayerSettings.colorSpace;
            foreach (InfinityWizardCheck check in InfinityWizardChecks.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(check.title));
                Assert.IsFalse(string.IsNullOrEmpty(check.description), check.title);
                Assert.IsTrue(titles.Add(check.title), "Duplicate check " + check.title);
                Assert.DoesNotThrow(() => check.isSatisfied(), check.title);
            }
            Assert.AreEqual(batching, GraphicsSettings.useScriptableRenderPipelineBatching);
            Assert.AreEqual(colorSpace, UnityEditor.PlayerSettings.colorSpace);
        }

        [TestCase(EInfinityLightType.Point)]
        [TestCase(EInfinityLightType.Spot)]
        [TestCase(EInfinityLightType.Rect)]
        [TestCase(EInfinityLightType.Disk)]
        [TestCase(EInfinityLightType.Directional)]
        public void LightShape_InSyncAfterApply_OutOfSyncAfterNativeEdit(EInfinityLightType type)
        {
            var go = new GameObject("WizardLight");
            try
            {
                Light light = go.AddComponent<Light>();
                FLightShape shape = FLightShape.Default(type);
                LightShapeUtility.ApplyToUnityLight(light, shape);
                Assert.IsTrue(LightShapeUtility.IsInSync(light, shape));
                Assert.IsFalse(LightShapeUtility.ApplyToUnityLight(light, shape), "Applying a synced shape changes nothing.");
                light.type = type == EInfinityLightType.Directional ? LightType.Point : LightType.Directional;
                Assert.IsFalse(LightShapeUtility.IsInSync(light, shape));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DefaultVolumeProfile_PersistentCompletionUndoRedo_PreservesConcreteTypes()
        {
            string root = "Assets/__InfinityWizardProfileTest_" + System.Guid.NewGuid().ToString("N");
            VolumeProfile profile = null;
            try
            {
                System.IO.Directory.CreateDirectory(root);
                AssetDatabase.Refresh();
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                Exposure existing = profile.Add<Exposure>();
                existing.evCompensation.value = 1.25f;
                AssetDatabase.CreateAsset(profile, root + "/Profile.asset");
                AssetDatabase.AddObjectToAsset(existing, profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                string existingJson = EditorJsonUtility.ToJson(existing);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing, out string existingGuid, out long existingId);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Assert.Greater(DefaultVolumeProfileFactory.ValidateAndComplete(profile), 0);
                AssetDatabase.SaveAssetIfDirty(profile);
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Assert.AreEqual(0, DefaultVolumeProfileFactory.CountMissing(profile));
                Assert.IsTrue(profile.TryGet<ProbeVolumesOptions>(out var createdOptions));
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(createdOptions, out string createdGuid, out long createdId));
                Undo.PerformUndo();
                Assert.AreEqual(1, profile.components.Count, "Undo retains only the pre-existing component.");
                Assert.AreSame(existing, profile.components[0]);
                Assert.AreEqual(existingJson, EditorJsonUtility.ToJson(existing));
                Undo.PerformRedo();
                Assert.IsTrue(profile.TryGet<ProbeVolumesOptions>(out var options),
                    "Redo must restore the concrete APV component, not its VolumeComponent base class.");
                Assert.AreEqual(typeof(ProbeVolumesOptions), options.GetType());
                Assert.IsTrue(EditorUtility.IsPersistent(options), "Redo must restore the component as a persistent subasset.");
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(options, out string restoredGuid, out long restoredId));
                Assert.AreEqual(createdGuid, restoredGuid);
                Assert.AreEqual(createdId, restoredId);
                Assert.AreEqual(0.05f, options.normalBias.value);
                Assert.IsFalse(options.normalBias.overrideState);
                Assert.AreEqual(1.0f, options.intensityMultiplier.value);
                Assert.AreEqual(0, DefaultVolumeProfileFactory.CountMissing(profile));
                Assert.AreEqual(profile.components.Count, new HashSet<System.Type>(profile.components.Select(c => c.GetType())).Count);
                Assert.AreEqual(existingJson, EditorJsonUtility.ToJson(existing));
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing, out string redoGuid, out long redoId));
                Assert.AreEqual(existingGuid, redoGuid);
                Assert.AreEqual(existingId, redoId);
                AssetDatabase.SaveAssetIfDirty(profile);
                AssetDatabase.ImportAsset(root + "/Profile.asset", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(root + "/Profile.asset");
                Assert.IsTrue(profile.TryGet<ProbeVolumesOptions>(out options));
                Assert.AreEqual(typeof(ProbeVolumesOptions), options.GetType());
                System.IO.File.Copy(root + "/Profile.asset", root + "/DiskProbe.asset");
                AssetDatabase.ImportAsset(root + "/DiskProbe.asset", ImportAssetOptions.ForceSynchronousImport);
                var fromDisk = AssetDatabase.LoadAssetAtPath<VolumeProfile>(root + "/DiskProbe.asset");
                Assert.IsTrue(fromDisk.TryGet<ProbeVolumesOptions>(out _), "Independent disk import must retain the APV subasset.");
                Assert.AreEqual(0, DefaultVolumeProfileFactory.CountMissing(fromDisk));
                byte[] stored = System.IO.File.ReadAllBytes(root + "/Profile.asset");
                Assert.AreEqual(0, DefaultVolumeProfileFactory.ValidateAndComplete(profile), "A second completion must be a no-op.");
                Assert.IsFalse(EditorUtility.IsDirty(profile));
                CollectionAssert.AreEqual(stored, System.IO.File.ReadAllBytes(root + "/Profile.asset"));
            }
            finally
            {
                var ownedObjects = new HashSet<Object>(AssetDatabase.LoadAllAssetsAtPath(root + "/Profile.asset"));
                if (profile != null)
                    foreach (VolumeComponent component in profile.components) ownedObjects.Add(component);
                foreach (Object owned in ownedObjects)
                {
                    if (owned == null) continue;
                    Undo.ClearUndo(owned);
                    if (!EditorUtility.IsPersistent(owned)) Object.DestroyImmediate(owned);
                }
                if (profile != null) Undo.ClearUndo(profile);
                AssetDatabase.DeleteAsset(root);
            }
        }

        [Test]
        public void DefaultVolumeProfile_CountMissing_MatchesValidateAndComplete()
        {
            VolumeProfile defaults = DefaultVolumeProfileFactory.CreateInMemory();
            VolumeProfile empty = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                int expected = defaults.components.Count;
                Assert.AreEqual(expected, DefaultVolumeProfileFactory.CountMissing(empty));
                Assert.AreEqual(0, DefaultVolumeProfileFactory.CountMissing(defaults));
                Assert.AreEqual(expected, DefaultVolumeProfileFactory.ValidateAndComplete(empty));
                Assert.AreEqual(0, DefaultVolumeProfileFactory.CountMissing(empty));
            }
            finally
            {
                foreach (VolumeComponent c in defaults.components) Object.DestroyImmediate(c);
                foreach (VolumeComponent c in empty.components) Object.DestroyImmediate(c);
                Object.DestroyImmediate(defaults);
                Object.DestroyImmediate(empty);
            }
        }
    }
}
