using System;
using System.Collections.Generic;
using InfinityTech.Rendering.PostProcess;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class DefaultVolumeAuthoringTests
    {
        [TestCase(typeof(Exposure))]
        [TestCase(typeof(FilmTonemap))]
        [TestCase(typeof(ColorGrading))]
        [TestCase(typeof(VolumetricFog))]
        public void Complete_AddsOnlyMissingType_PreservesExistingValuesFlagsIdentity_AndUndoes(Type missing)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            VolumeProfile profile = DefaultVolumeProfileFactory.CreateInMemory();
            bool rollbackAttempted = false;
            try
            {
                profile.TryGet(missing, out VolumeComponent removed);
                profile.components.Remove(removed);
                UnityEngine.Object.DestroyImmediate(removed);
                var original = new List<VolumeComponent>(profile.components);
                var json = new Dictionary<VolumeComponent, string>();
                foreach (VolumeComponent component in original)
                {
                    component.active = false;
                    foreach (VolumeParameter parameter in component.parameters) parameter.overrideState = false;
                    if (component is FilmTonemap film) film.slope.value = 0.42f;
                    if (component is ColorGrading grade) grade.Temp.value = 4300;
                    if (component is Exposure exposure) exposure.evCompensation.value = 1.75f;
                    json.Add(component, EditorJsonUtility.ToJson(component));
                }
                Assert.AreEqual(1, DefaultVolumeProfileFactory.ValidateAndComplete(profile));
                Assert.IsTrue(profile.TryGet(missing, out VolumeComponent added));
                foreach (VolumeComponent component in original)
                {
                    Assert.Contains(component, profile.components);
                    Assert.AreEqual(json[component], EditorJsonUtility.ToJson(component), component.GetType().Name);
                }
                Assert.AreEqual(0, DefaultVolumeProfileFactory.ValidateAndComplete(profile));
                Undo.FlushUndoRecordObjects();
                rollbackAttempted = true;
                Undo.RevertAllDownToGroup(group);
                CollectionAssert.AreEqual(original, profile.components);
                foreach (VolumeComponent component in original)
                    Assert.AreEqual(json[component], EditorJsonUtility.ToJson(component));
            }
            finally
            {
                try
                {
                    if (!rollbackAttempted) Undo.RevertAllDownToGroup(group);
                }
                finally
                {
                    foreach (VolumeComponent component in profile.components)
                        UnityEngine.Object.DestroyImmediate(component);
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }

        [Test]
        public void OpeningActualGlobalProfileEditor_DoesNotCompleteOrResetTheProfile()
        {
            VolumeProfile originalDefault = VolumeManager.instance.globalDefaultProfile;
            VolumeProfile profile = DefaultVolumeProfileFactory.CreateInMemory();
            UnityEditor.Editor editor = null;
            try
            {
                profile.TryGet(out Exposure removed);
                profile.components.Remove(removed);
                UnityEngine.Object.DestroyImmediate(removed);
                var snapshots = new Dictionary<VolumeComponent, string>();
                foreach (VolumeComponent component in profile.components)
                {
                    component.active = false;
                    foreach (VolumeParameter parameter in component.parameters) parameter.overrideState = false;
                    if (component is FilmTonemap film) film.slope.value = .42f;
                    snapshots.Add(component, EditorJsonUtility.ToJson(component));
                }
                VolumeManager.instance.SetGlobalDefaultProfile(profile);
                editor = UnityEditor.Editor.CreateEditor(profile);
                Assert.AreEqual("InfinityVolumeProfileEditor", editor.GetType().Name,
                    "Selecting the asset must resolve the lossless editor, not CoreRP's repairing default editor.");
                Assert.AreEqual(snapshots.Count, profile.components.Count);
                foreach (VolumeComponent component in profile.components)
                    Assert.AreEqual(snapshots[component], EditorJsonUtility.ToJson(component));
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                VolumeManager.instance.SetGlobalDefaultProfile(originalDefault);
                foreach (VolumeComponent component in profile.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Complete_NullProfileIsAnExplicitError()
        {
            Assert.Throws<ArgumentNullException>(() => DefaultVolumeProfileFactory.ValidateAndComplete(null));
        }
    }
}
