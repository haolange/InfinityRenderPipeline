using System.Collections.Generic;
using NUnit.Framework;
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
        [Category("Unverified_0_5")]
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
        [Category("Unverified_0_5")]
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
        [Category("Unverified_0_5")]
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
